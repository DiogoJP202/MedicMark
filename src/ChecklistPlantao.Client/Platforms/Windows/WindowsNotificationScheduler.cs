using System.Collections.Concurrent;
using ChecklistPlantao.Client.Core.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace ChecklistPlantao.Client.Platforms.Windows;

/// <summary>
/// Notificações no Windows.
///
/// LIMITAÇÃO CONHECIDA E DELIBERADA (D-010): sem empacotamento MSIX não há agendamento no
/// sistema operacional, então os alertas são disparados por um temporizador dentro do próprio
/// processo. Consequência: eles funcionam com o aplicativo aberto — inclusive minimizado — e
/// NÃO funcionam com ele fechado.
///
/// Isso não é escondido do usuário: <see cref="RequiresAppRunning"/> é verdadeiro, a tela
/// "Estado do dispositivo" mostra a ressalva e a faixa de saúde permanece visível.
/// Para alertas com o app fechado seria necessário MSIX + Windows 10 SDK; ver docs/NOTIFICATIONS.md.
/// </summary>
public sealed class WindowsNotificationScheduler(ILogger<WindowsNotificationScheduler> logger)
    : ILocalNotificationScheduler, IDisposable
{
    private readonly ConcurrentDictionary<string, (ScheduledNotification Notification, Timer Timer)> _agendados = new();

    public bool RequiresAppRunning => true;

    public bool SupportsExactTiming => true;

    public Task ScheduleAsync(IReadOnlyList<ScheduledNotification> notifications, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        foreach (var notificacao in notifications)
        {
            var espera = notificacao.FireAt - DateTimeOffset.Now;

            if (espera <= TimeSpan.Zero)
            {
                continue;
            }

            // Timer aceita no máximo ~24,8 dias; o horizonte aqui é de horas, então cabe.
            var timer = new Timer(_ => Dispatch(notificacao.Id), null, espera, Timeout.InfiniteTimeSpan);

            _agendados.AddOrUpdate(
                notificacao.Id,
                (notificacao, timer),
                (_, anterior) =>
                {
                    anterior.Timer.Dispose();
                    return (notificacao, timer);
                });
        }

        return Task.CompletedTask;
    }

    public Task CancelAsync(IEnumerable<string> notificationIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notificationIds);

        foreach (var id in notificationIds)
        {
            if (_agendados.TryRemove(id, out var entrada))
            {
                entrada.Timer.Dispose();
            }
        }

        return Task.CompletedTask;
    }

    public Task CancelAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var chave in _agendados.Keys)
        {
            if (_agendados.TryRemove(chave, out var entrada))
            {
                entrada.Timer.Dispose();
            }
        }

        return Task.CompletedTask;
    }

    public Task ShowNowAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        Show(title, body, deepLink: null);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetScheduledIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([.. _agendados.Keys]);

    private void Dispatch(string id)
    {
        if (!_agendados.TryRemove(id, out var entrada))
        {
            return;
        }

        entrada.Timer.Dispose();
        Show(entrada.Notification.Title, entrada.Notification.Body, entrada.Notification.DeepLink);
    }

    private void Show(string title, string body, string? deepLink)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body);

            if (deepLink is not null)
            {
                // O argumento volta no clique e o App usa para navegar até a coluna certa.
                builder.AddArgument("rota", deepLink);
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Não foi possível exibir a notificação no Windows.");
        }
    }

    public void Dispose() => CancelAllAsync().GetAwaiter().GetResult();
}

/// <summary>
/// Permissões no Windows. O sistema não pede permissão em tempo de execução: o usuário controla
/// pelas Configurações. O que se pode verificar é se o registro do canal funcionou.
/// </summary>
public sealed class WindowsNotificationPermissionService(ILogger<WindowsNotificationPermissionService> logger)
    : INotificationPermissionService
{
    public Task<NotificationPermissions> GetAsync(CancellationToken cancellationToken = default)
    {
        var registrado = false;

        try
        {
            registrado = AppNotificationManager.Default is not null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gerenciador de notificações do Windows indisponível.");
        }

        // ExactAlarm não existe no Windows: nulo comunica "não se aplica", e não "negado".
        return Task.FromResult(new NotificationPermissions(registrado, null, BatteryOptimizationIgnored: true));
    }

    public Task<NotificationPermissions> RequestAsync(CancellationToken cancellationToken = default) => GetAsync(cancellationToken);

    public Task OpenSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível abrir as configurações de notificação do Windows.");
        }

        return Task.CompletedTask;
    }
}
