using ChecklistPlantao.Domain.Scheduling;

namespace ChecklistPlantao.Client.Core.Notifications;

/// <summary>Um alerta a ser agendado no sistema operacional.</summary>
public sealed record ScheduledNotification(
    string Id,
    DateTimeOffset FireAt,
    string Title,
    string Body,
    Guid SectorId,
    Guid TemplateId,
    Guid ColumnId,
    DateOnly ServiceDate,
    NotificationOccurrenceKind Kind,
    int RepeatIndex,
    bool AllowSnooze,
    int SnoozeMinutes)
{
    /// <summary>
    /// Identificador estável do alerta. Reagendar recalcula o mesmo Id, então o sistema
    /// substitui em vez de acumular alertas duplicados.
    /// </summary>
    public static string BuildId(Guid columnId, DateOnly serviceDate, NotificationOccurrenceKind kind, int repeatIndex) =>
        $"{columnId:N}:{serviceDate:yyyyMMdd}:{kind}:{repeatIndex}";

    /// <summary>Rota do deep link, para o toque abrir exatamente o checklist certo.</summary>
    public string DeepLink => $"/checklist/{TemplateId}/{ColumnId}";
}

/// <summary>
/// Agendamento de notificações locais.
///
/// Local e não remoto de propósito: o alerta principal do plantão não pode depender de internet
/// nem de serviço de push. Cada plataforma implementa com o mecanismo nativo dela.
/// </summary>
public interface ILocalNotificationScheduler
{
    /// <summary>
    /// Verdadeiro quando os alertas só funcionam com o aplicativo em execução.
    /// No Windows sem empacotamento MSIX isso é verdade — e a interface precisa dizer isso ao
    /// usuário em vez de prometer o que não entrega. Ver docs/NOTIFICATIONS.md.
    /// </summary>
    bool RequiresAppRunning { get; }

    /// <summary>Verdadeiro quando a plataforma consegue disparar no horário exato.</summary>
    bool SupportsExactTiming { get; }

    Task ScheduleAsync(IReadOnlyList<ScheduledNotification> notifications, CancellationToken cancellationToken = default);

    Task CancelAsync(IEnumerable<string> notificationIds, CancellationToken cancellationToken = default);

    Task CancelAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Dispara agora, para o botão "Testar notificação".</summary>
    Task ShowNowAsync(string title, string body, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetScheduledIdsAsync(CancellationToken cancellationToken = default);
}

public sealed record NotificationPermissions(
    bool NotificationsGranted,
    bool? ExactAlarmGranted,
    bool BatteryOptimizationIgnored);

public interface INotificationPermissionService
{
    Task<NotificationPermissions> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Pede a permissão ao sistema. Devolve o estado resultante.</summary>
    Task<NotificationPermissions> RequestAsync(CancellationToken cancellationToken = default);

    /// <summary>Abre a tela do sistema onde o usuário concede o que falta.</summary>
    Task OpenSettingsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Avalia se as notificações realmente funcionam neste aparelho.
///
/// Existe para impedir a mentira mais fácil de cometer: exibir "notificações ativas" quando uma
/// permissão essencial está faltando.
/// </summary>
public interface INotificationHealthService
{
    Task<IReadOnlyList<string>> DiagnoseAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reagenda os alertas após o dispositivo reiniciar, o fuso mudar ou a configuração ser sincronizada.</summary>
public interface IDeviceStartupRescheduler
{
    Task RescheduleAsync(CancellationToken cancellationToken = default);
}

/// <summary>Som próprio do alerta, quando a plataforma permitir.</summary>
public interface INotificationSoundService
{
    Task<bool> IsSoundEnabledAsync(CancellationToken cancellationToken = default);

    Task PlayTestSoundAsync(CancellationToken cancellationToken = default);
}
