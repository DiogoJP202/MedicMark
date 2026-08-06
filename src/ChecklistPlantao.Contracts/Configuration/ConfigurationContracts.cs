namespace ChecklistPlantao.Contracts.Configuration;

public sealed record SectorDto(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    TimeOnly? ShiftStart,
    TimeOnly? ShiftEnd,
    int Version);

public sealed record BedDto(
    Guid Id,
    Guid SectorId,
    string Code,
    string? Description,
    int SortOrder,
    bool IsActive,
    int Version);

public sealed record ChecklistColumnDto(
    Guid Id,
    Guid ChecklistTemplateId,
    string DisplayName,
    TimeOnly? TriggerTime,
    int SortOrder,
    bool IsActive,
    bool NotificationEnabled,
    int LeadTimeMinutes,
    int GracePeriodMinutes,
    int RepeatIntervalMinutes,
    int MaximumRepeats,
    bool AllowSnooze,
    int SnoozeMinutes,
    int Version);

public sealed record ChecklistTemplateDto(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<Guid> SectorIds,
    IReadOnlyList<ChecklistColumnDto> Columns,
    int Version);

public sealed record BedMarkerDefinitionDto(
    Guid Id,
    string Name,
    string Code,
    int SortOrder,
    bool IsActive,
    int Version);

public sealed record NotificationConfigurationDto(
    bool SoundEnabled,
    bool VibrationEnabled,
    string Priority,
    bool EnabledOnAndroid,
    bool EnabledOnWindows,
    bool AllowFullScreenIntent,
    string TitleTemplate,
    string BodyTemplate,
    int Version);

public sealed record InstitutionSettingsDto(
    string TimeZoneId,
    TimeOnly ShiftStart,
    TimeOnly ShiftEnd,
    int RetentionAfterCloseHours,
    int OfflineLoginValidityDays,
    int OfflineLoginMaxAttempts,
    bool AutoOpenSession);

public sealed record PermissionDto(string Key, string Description);

public sealed record AccessGroupDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    bool GrantsAllSectors,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid> SectorIds,
    int Version);

public sealed record AppUserDto(
    Guid Id,
    string UserName,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<Guid> GroupIds,
    int Version);

/// <summary>
/// Fotografia completa da configuração. É o que um dispositivo novo baixa no primeiro login e o
/// que ele regrava quando o cursor de sincronização fica velho demais.
/// </summary>
public sealed record BootstrapResponse(
    InstitutionSettingsDto Settings,
    NotificationConfigurationDto Notifications,
    IReadOnlyList<PermissionDto> Permissions,
    IReadOnlyList<SectorDto> Sectors,
    IReadOnlyList<BedDto> Beds,
    IReadOnlyList<ChecklistTemplateDto> Templates,
    IReadOnlyList<BedMarkerDefinitionDto> Markers,
    long SyncCursor,
    DateTime ServerTimeUtc);
