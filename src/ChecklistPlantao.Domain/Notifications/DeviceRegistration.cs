using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Notifications;

public enum DevicePlatform
{
    Unknown = 0,
    Android = 1,
    Windows = 2,
    Ios = 3,
}

/// <summary>
/// Estado das notificações relatado pelo próprio dispositivo. O servidor só conhece o último
/// valor sincronizado — nunca o estado atual de um aparelho offline.
/// </summary>
public enum NotificationHealth
{
    /// <summary>Nunca foi avaliado neste dispositivo.</summary>
    Unknown = 0,

    /// <summary>Permissões concedidas e agendamento funcionando.</summary>
    Healthy = 1,

    /// <summary>Funciona, mas com ressalva — por exemplo sem permissão de alarme exato.</summary>
    Degraded = 2,

    /// <summary>Permissão essencial ausente: os alertas não vão chegar.</summary>
    Unhealthy = 3,
}

/// <summary>
/// Dispositivo conhecido pelo servidor. Serve ao painel administrativo e ao diagnóstico —
/// não guarda nada sobre o que foi marcado nem por quem.
/// </summary>
public sealed class DeviceRegistration : ISyncVersioned
{
    private DeviceRegistration()
    {
        DeviceName = string.Empty;
        AppVersion = string.Empty;
    }

    public DeviceRegistration(Guid id, string deviceName, DevicePlatform platform, string appVersion, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(deviceName, "nome do dispositivo");

        Id = id;
        DeviceName = deviceName.Trim();
        Platform = platform;
        AppVersion = appVersion?.Trim() ?? string.Empty;
        LastSeenAtUtc = nowUtc;
        NotificationHealth = NotificationHealth.Unknown;
        IsActive = true;
        Version = 1;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string DeviceName { get; private set; }

    public DevicePlatform Platform { get; private set; }

    public string AppVersion { get; private set; }

    public DateTime LastSeenAtUtc { get; private set; }

    public DateTime? LastSyncAtUtc { get; private set; }

    public bool NotificationsPermissionGranted { get; private set; }

    /// <summary>Só se aplica ao Android. Nulo nas demais plataformas.</summary>
    public bool? ExactAlarmPermissionGranted { get; private set; }

    public bool BatteryOptimizationIgnored { get; private set; }

    public NotificationHealth NotificationHealth { get; private set; }

    public DateTime? LastNotificationTestAtUtc { get; private set; }

    public Guid? CurrentUserId { get; private set; }

    public bool IsActive { get; private set; }

    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Heartbeat(
        string appVersion,
        Guid? currentUserId,
        bool notificationsPermissionGranted,
        bool? exactAlarmPermissionGranted,
        bool batteryOptimizationIgnored,
        NotificationHealth health,
        DateTime? lastNotificationTestAtUtc,
        DateTime nowUtc)
    {
        AppVersion = appVersion?.Trim() ?? AppVersion;
        CurrentUserId = currentUserId;
        NotificationsPermissionGranted = notificationsPermissionGranted;
        ExactAlarmPermissionGranted = exactAlarmPermissionGranted;
        BatteryOptimizationIgnored = batteryOptimizationIgnored;
        NotificationHealth = health;
        LastNotificationTestAtUtc = lastNotificationTestAtUtc ?? LastNotificationTestAtUtc;
        LastSeenAtUtc = nowUtc;
        Version++;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkSynchronized(DateTime nowUtc)
    {
        LastSyncAtUtc = nowUtc;
        LastSeenAtUtc = nowUtc;
        Version++;
        UpdatedAtUtc = nowUtc;
    }

    public void Rename(string deviceName, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(deviceName, "nome do dispositivo");

        DeviceName = deviceName.Trim();
        Version++;
        UpdatedAtUtc = nowUtc;
    }

    public void SetActive(bool isActive, DateTime nowUtc)
    {
        if (IsActive == isActive)
        {
            return;
        }

        IsActive = isActive;
        Version++;
        UpdatedAtUtc = nowUtc;
    }
}
