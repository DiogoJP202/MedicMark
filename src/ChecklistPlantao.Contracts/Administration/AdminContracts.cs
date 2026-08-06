namespace ChecklistPlantao.Contracts.Administration;

public sealed record CreateUserRequest(string UserName, string DisplayName, string Password, IReadOnlyList<Guid> GroupIds);

public sealed record UpdateUserRequest(string DisplayName, bool IsActive, IReadOnlyList<Guid> GroupIds, int BaseVersion);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record SaveGroupRequest(
    string Name,
    string? Description,
    bool IsActive,
    bool GrantsAllSectors,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid> SectorIds,
    int BaseVersion);

public sealed record SaveSectorRequest(
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    TimeOnly? ShiftStart,
    TimeOnly? ShiftEnd,
    int BaseVersion);

public sealed record SaveBedRequest(
    Guid SectorId,
    string Code,
    string? Description,
    int SortOrder,
    bool IsActive,
    int BaseVersion);

public sealed record SaveTemplateRequest(
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<Guid> SectorIds,
    int BaseVersion);

public sealed record SaveColumnRequest(
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
    int BaseVersion);

public sealed record SaveMarkerRequest(string Name, int SortOrder, bool IsActive, int BaseVersion);

public sealed record SaveNotificationConfigurationRequest(
    bool SoundEnabled,
    bool VibrationEnabled,
    string Priority,
    bool EnabledOnAndroid,
    bool EnabledOnWindows,
    bool AllowFullScreenIntent,
    string TitleTemplate,
    string BodyTemplate,
    int BaseVersion);

public sealed record SaveInstitutionSettingsRequest(
    string TimeZoneId,
    TimeOnly ShiftStart,
    TimeOnly ShiftEnd,
    int RetentionAfterCloseHours,
    int OfflineLoginValidityDays,
    int OfflineLoginMaxAttempts,
    bool AutoOpenSession);

/// <summary>Reordenação em lote, para arrastar itens na tela sem uma chamada por item.</summary>
public sealed record ReorderRequest(IReadOnlyList<ReorderItem> Items);

public sealed record ReorderItem(Guid Id, int SortOrder);
