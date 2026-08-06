namespace ChecklistPlantao.Contracts.Devices;

public sealed record RegisterDeviceRequest(
    Guid DeviceId,
    string DeviceName,
    string Platform,
    string AppVersion);

/// <summary>
/// O que o dispositivo relata periodicamente. Não há nada operacional aqui: só saúde técnica.
/// O servidor conhece apenas o último valor sincronizado, nunca o estado atual de um aparelho offline.
/// </summary>
public sealed record DeviceHeartbeatRequest(
    Guid DeviceId,
    string AppVersion,
    bool NotificationsPermissionGranted,
    bool? ExactAlarmPermissionGranted,
    bool BatteryOptimizationIgnored,
    string NotificationHealth,
    DateTime? LastNotificationTestAtUtc);

public sealed record DeviceDto(
    Guid Id,
    string DeviceName,
    string Platform,
    string AppVersion,
    DateTime LastSeenAtUtc,
    DateTime? LastSyncAtUtc,
    bool NotificationsPermissionGranted,
    bool? ExactAlarmPermissionGranted,
    bool BatteryOptimizationIgnored,
    string NotificationHealth,
    DateTime? LastNotificationTestAtUtc,
    Guid? CurrentUserId,
    bool IsActive);

/// <summary>Resposta do teste de conexão da tela de configuração inicial.</summary>
public sealed record ServerProbeResponse(
    string Application,
    string Version,
    string Environment,
    DateTime ServerTimeUtc,
    string TimeZoneId);
