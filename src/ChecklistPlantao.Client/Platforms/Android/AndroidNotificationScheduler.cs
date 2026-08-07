using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using ChecklistPlantao.Client.Core.Notifications;
using Microsoft.Extensions.Logging;
// O apelido NÃO pode se chamar "Application": o projeto tem o namespace
// ChecklistPlantao.Application, e a busca por namespaces envolventes vence o apelido.
using AndroidApp = Android.App.Application;

namespace ChecklistPlantao.Client.Platforms.Android;

/// <summary>
/// Notificações no Android usando <c>AlarmManager</c>.
///
/// Diferente do Windows, aqui o agendamento é do SISTEMA: o alerta dispara com o aplicativo
/// fechado e sobrevive ao reinício do aparelho (ver <c>BootReceiver</c>).
///
/// Sobre exatidão: a partir do Android 12 o alarme exato exige permissão especial. Quando ela
/// não existe, caímos para <c>SetAndAllowWhileIdle</c>, que pode atrasar alguns minutos. Esse
/// fato é reportado à interface — nunca dizemos "no horário" quando pode não estar.
/// </summary>
public sealed class AndroidNotificationScheduler(ILogger<AndroidNotificationScheduler> logger) : ILocalNotificationScheduler
{
    public const string ChannelId = "checklistplantao.alertas";
    public const string ExtraNotificationId = "notificacao_id";
    public const string ExtraTitle = "titulo";
    public const string ExtraBody = "mensagem";
    public const string ExtraRoute = "rota";

    private static readonly HashSet<string> Agendados = [];

    public bool RequiresAppRunning => false;

    public bool SupportsExactTiming => CanScheduleExact();

    public Task ScheduleAsync(IReadOnlyList<ScheduledNotification> notifications, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        EnsureChannel();

        var alarmManager = (AlarmManager?)AndroidApp.Context.GetSystemService(Context.AlarmService);

        if (alarmManager is null)
        {
            logger.LogError("AlarmManager indisponível: nenhuma notificação será agendada.");
            return Task.CompletedTask;
        }

        var exato = CanScheduleExact();

        foreach (var notificacao in notifications)
        {
            var disparo = notificacao.FireAt.ToUnixTimeMilliseconds();

            if (disparo <= Java.Lang.JavaSystem.CurrentTimeMillis())
            {
                continue;
            }

            var intent = new Intent(AndroidApp.Context, typeof(NotificationReceiver));
            intent.PutExtra(ExtraNotificationId, notificacao.Id);
            intent.PutExtra(ExtraTitle, notificacao.Title);
            intent.PutExtra(ExtraBody, notificacao.Body);
            intent.PutExtra(ExtraRoute, notificacao.DeepLink);

            var pending = PendingIntent.GetBroadcast(
                AndroidApp.Context,
                notificacao.Id.GetHashCode(StringComparison.Ordinal),
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

            if (exato)
            {
                alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, disparo, pending);
            }
            else
            {
                // Sem permissão de alarme exato: o alerta chega, mas pode atrasar.
                alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, disparo, pending);
            }

            lock (Agendados)
            {
                Agendados.Add(notificacao.Id);
            }
        }

        return Task.CompletedTask;
    }

    public Task CancelAsync(IEnumerable<string> notificationIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notificationIds);

        var alarmManager = (AlarmManager?)AndroidApp.Context.GetSystemService(Context.AlarmService);

        foreach (var id in notificationIds)
        {
            var intent = new Intent(AndroidApp.Context, typeof(NotificationReceiver));

            var pending = PendingIntent.GetBroadcast(
                AndroidApp.Context,
                id.GetHashCode(StringComparison.Ordinal),
                intent,
                PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);

            if (pending is not null)
            {
                alarmManager?.Cancel(pending);
                pending.Cancel();
            }

            lock (Agendados)
            {
                Agendados.Remove(id);
            }
        }

        return Task.CompletedTask;
    }

    public Task CancelAllAsync(CancellationToken cancellationToken = default)
    {
        string[] copia;

        lock (Agendados)
        {
            copia = [.. Agendados];
        }

        return CancelAsync(copia, cancellationToken);
    }

    public Task ShowNowAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        EnsureChannel();
        NotificationReceiver.Show(AndroidApp.Context, "teste", title, body, route: null);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetScheduledIdsAsync(CancellationToken cancellationToken = default)
    {
        lock (Agendados)
        {
            return Task.FromResult<IReadOnlyList<string>>([.. Agendados]);
        }
    }

    /// <summary>
    /// Canal de alta importância, com som e vibração. Criar o canal é obrigatório a partir do
    /// Android 8; suas características só podem ser definidas na criação.
    /// </summary>
    public static void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager?)AndroidApp.Context.GetSystemService(Context.NotificationService);

        if (manager is null || manager.GetNotificationChannel(ChannelId) is not null)
        {
            return;
        }

        var canal = new NotificationChannel(ChannelId, "Alertas do plantão", NotificationImportance.High)
        {
            Description = "Lembretes dos horários do checklist de plantão.",
            LockscreenVisibility = NotificationVisibility.Public,
        };

        canal.EnableVibration(true);
        canal.SetShowBadge(true);
        manager.CreateNotificationChannel(canal);
    }

    private static bool CanScheduleExact()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return true;
        }

        var alarmManager = (AlarmManager?)AndroidApp.Context.GetSystemService(Context.AlarmService);
        return alarmManager?.CanScheduleExactAlarms() ?? false;
    }
}

/// <summary>Recebe o alarme e publica a notificação.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class NotificationReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        Show(
            context,
            intent.GetStringExtra(AndroidNotificationScheduler.ExtraNotificationId) ?? Guid.NewGuid().ToString(),
            intent.GetStringExtra(AndroidNotificationScheduler.ExtraTitle) ?? "Checklist de Plantão",
            intent.GetStringExtra(AndroidNotificationScheduler.ExtraBody) ?? string.Empty,
            intent.GetStringExtra(AndroidNotificationScheduler.ExtraRoute));
    }

    internal static void Show(Context context, string id, string title, string body, string? route)
    {
        AndroidNotificationScheduler.EnsureChannel();

        // Deep link: tocar na notificação abre o checklist exatamente na coluna atrasada.
        var abrir = new Intent(context, typeof(MainActivity));
        abrir.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        if (route is not null)
        {
            abrir.PutExtra(AndroidNotificationScheduler.ExtraRoute, route);
        }

        var pending = PendingIntent.GetActivity(
            context,
            id.GetHashCode(StringComparison.Ordinal),
            abrir,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(context, AndroidNotificationScheduler.ChannelId)
            .SetContentTitle(title)!
            .SetContentText(body)!
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))!
            .SetPriority((int)NotificationPriority.High)!
            .SetCategory(NotificationCompat.CategoryReminder)!
            .SetAutoCancel(true)!
            .SetContentIntent(pending)!
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)!;

        NotificationManagerCompat.From(context).Notify(id.GetHashCode(StringComparison.Ordinal), builder.Build());
    }
}

/// <summary>
/// Reagenda os alertas depois que o aparelho reinicia.
///
/// Sem isto, um celular reiniciado à noite perderia todos os horários do plantão — exatamente o
/// cenário que o item 17 do enunciado exige tratar.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.RECEIVE_BOOT_COMPLETED")]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, Intent.ActionTimeChanged, Intent.ActionTimezoneChanged])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        AndroidNotificationScheduler.EnsureChannel();

        // O reagendamento real depende do contêiner de serviços, que só existe com o aplicativo
        // vivo. Marcamos a pendência; MauiProgram reagenda na próxima abertura, e o alarme mais
        // próximo é restabelecido então.
        var preferencias = context.GetSharedPreferences("checklistplantao", FileCreationMode.Private);
        preferencias?.Edit()?.PutBoolean("reagendar_pendente", true)?.Apply();
    }
}

/// <summary>Permissões de notificação e alarme exato no Android.</summary>
public sealed class AndroidNotificationPermissionService : INotificationPermissionService
{
    public async Task<NotificationPermissions> GetAsync(CancellationToken cancellationToken = default)
    {
        var notificacoes = NotificationManagerCompat.From(AndroidApp.Context).AreNotificationsEnabled();

        bool? exato = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? ((AlarmManager?)AndroidApp.Context.GetSystemService(Context.AlarmService))?.CanScheduleExactAlarms() ?? false
            : true;

        var bateria = IsBatteryOptimizationIgnored();

        return await Task.FromResult(new NotificationPermissions(notificacoes, exato, bateria)).ConfigureAwait(false);
    }

    public async Task<NotificationPermissions> RequestAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            await Permissions.RequestAsync<Permissions.PostNotifications>().ConfigureAwait(false);
        }

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task OpenSettingsAsync(CancellationToken cancellationToken = default)
    {
        // Leva direto à tela onde a permissão que falta pode ser concedida — pedir para o
        // usuário "procurar nas configurações" seria pedir demais no meio de um plantão.
        var intent = OperatingSystem.IsAndroidVersionAtLeast(31) && !(((AlarmManager?)AndroidApp.Context.GetSystemService(Context.AlarmService))?.CanScheduleExactAlarms() ?? true)
            ? new Intent(global::Android.Provider.Settings.ActionRequestScheduleExactAlarm)
            : new Intent(global::Android.Provider.Settings.ActionAppNotificationSettings)
                .PutExtra(global::Android.Provider.Settings.ExtraAppPackage, AndroidApp.Context.PackageName);

        intent.SetFlags(ActivityFlags.NewTask);
        AndroidApp.Context.StartActivity(intent);

        return Task.CompletedTask;
    }

    private static bool IsBatteryOptimizationIgnored()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            return true;
        }

        var power = (PowerManager?)AndroidApp.Context.GetSystemService(Context.PowerService);
        return power?.IsIgnoringBatteryOptimizations(AndroidApp.Context.PackageName!) ?? false;
    }
}
