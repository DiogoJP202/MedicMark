using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Sessions;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Domain.Structure;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Application.Configuration;

/// <summary>
/// Leitura da configuração: a fotografia completa do bootstrap e o estado atual das entidades
/// citadas por um pull incremental.
/// </summary>
public sealed class ConfigurationQueryService(IAppDataContext db, IClock clock, IInstitutionSettingsProvider settingsProvider)
{
    public async Task<BootstrapResponse> GetBootstrapAsync(ICurrentUser user, long cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var settings = await settingsProvider.GetAsync(cancellationToken).ConfigureAwait(false);

        var sectors = await db.Sectors.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(cancellationToken).ConfigureAwait(false);
        var visibleSectors = user.Access.FilterSectors(sectors, s => s.Id).ToList();
        var visibleSectorIds = visibleSectors.Select(s => s.Id).ToHashSet();

        var beds = await db.Beds
            .AsNoTracking()
            .Where(b => visibleSectorIds.Contains(b.SectorId))
            .OrderBy(b => b.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var templates = await db.ChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Columns)
            .Include(t => t.Sectors)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var markers = await db.BedMarkerDefinitions
            .AsNoTracking()
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var notifications = await db.NotificationConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var permissions = await db.PermissionDefinitions
            .AsNoTracking()
            .OrderBy(p => p.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new BootstrapResponse(
            Map(settings),
            notifications is null ? Map(new NotificationConfiguration(clock.UtcNow)) : Map(notifications),
            [.. permissions.Select(p => new PermissionDto(p.Key, p.Description))],
            [.. visibleSectors.Select(Map)],
            [.. beds.Select(Map)],
            [.. templates.Select(Map)],
            [.. markers.Select(Map)],
            cursor,
            clock.UtcNow);
    }

    /// <summary>
    /// Carrega o estado atual das entidades citadas no log, agrupando por tipo para não
    /// disparar uma consulta por alteração.
    /// </summary>
    public async Task<Dictionary<(string EntityType, Guid EntityId), string>> LoadPayloadsAsync(
        IReadOnlyList<ChangeLogRow> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var result = new Dictionary<(string, Guid), string>();

        foreach (var group in changes.Where(c => c.ChangeType != SyncChangeType.Deleted).GroupBy(c => c.EntityType))
        {
            var ids = group.Select(c => c.EntityId).Distinct().ToList();

            switch (group.Key)
            {
                case SyncEntityTypes.ChecklistEntry:
                    await AddAsync(db.ChecklistEntries.AsNoTracking().Where(e => ids.Contains(e.Id)),
                        e => e.Id, e => SyncJson.Serialize(SessionService.MapEntry(e))).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.SessionBedMarker:
                    await AddAsync(db.SessionBedMarkers.AsNoTracking().Where(m => ids.Contains(m.Id)),
                        m => m.Id, m => SyncJson.Serialize(SessionService.MapMarker(m))).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.OperationalSession:
                    await AddAsync(db.OperationalSessions.AsNoTracking().Where(s => ids.Contains(s.Id)),
                        s => s.Id, s => SyncJson.Serialize(SessionService.Map(s))).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.Sector:
                    await AddAsync(db.Sectors.AsNoTracking().Where(s => ids.Contains(s.Id)),
                        s => s.Id, s => SyncJson.Serialize(Map(s))).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.Bed:
                    await AddAsync(db.Beds.AsNoTracking().Where(b => ids.Contains(b.Id)),
                        b => b.Id, b => SyncJson.Serialize(Map(b))).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.ChecklistTemplate:
                    await AddAsync(db.ChecklistTemplates.AsNoTracking().Include(t => t.Columns).Include(t => t.Sectors).Where(t => ids.Contains(t.Id)),
                        t => t.Id, t => SyncJson.Serialize(Map(t))).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.BedMarkerDefinition:
                    await AddAsync(db.BedMarkerDefinitions.AsNoTracking().Where(m => ids.Contains(m.Id)),
                        m => m.Id, m => SyncJson.Serialize(Map(m))).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.NotificationConfiguration:
                    await AddAsync(db.NotificationConfigurations.AsNoTracking().Where(c => ids.Contains(c.Id)),
                        c => c.Id, c => SyncJson.Serialize(Map(c))).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.AppSetting:
                    // Configurações mudam em bloco; o cliente refaz o bootstrap das configurações.
                    break;

                default:
                    break;
            }

            async Task AddAsync<T>(IQueryable<T> query, Func<T, Guid> idSelector, Func<T, string> serializer)
            {
                var items = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var item in items)
                {
                    result[(group.Key, idSelector(item))] = serializer(item);
                }
            }
        }

        return result;
    }

    public static InstitutionSettingsDto Map(InstitutionSettings settings) => new(
        settings.TimeZoneId,
        settings.ShiftStart,
        settings.ShiftEnd,
        (int)settings.RetentionAfterClose.TotalHours,
        (int)settings.OfflineLoginValidity.TotalDays,
        settings.OfflineLoginMaxAttempts,
        settings.AutoOpenSession);

    public static SectorDto Map(Sector sector) => new(
        sector.Id, sector.Name, sector.Description, sector.SortOrder, sector.IsActive,
        sector.ShiftStart, sector.ShiftEnd, sector.Version);

    public static BedDto Map(Bed bed) => new(
        bed.Id, bed.SectorId, bed.Code, bed.Description, bed.SortOrder, bed.IsActive, bed.Version);

    public static ChecklistColumnDto Map(ChecklistColumn column) => new(
        column.Id, column.ChecklistTemplateId, column.DisplayName, column.TriggerTime, column.SortOrder,
        column.IsActive, column.NotificationEnabled, column.LeadTimeMinutes, column.GracePeriodMinutes,
        column.RepeatIntervalMinutes, column.MaximumRepeats, column.AllowSnooze, column.SnoozeMinutes, column.Version);

    public static ChecklistTemplateDto Map(ChecklistTemplate template) => new(
        template.Id, template.Name, template.Code, template.Description, template.SortOrder, template.IsActive,
        [.. template.Sectors.Select(s => s.SectorId)],
        [.. template.Columns.OrderBy(c => c.SortOrder).Select(Map)],
        template.Version);

    public static BedMarkerDefinitionDto Map(BedMarkerDefinition marker) => new(
        marker.Id, marker.Name, marker.Code, marker.SortOrder, marker.IsActive, marker.Version);

    public static NotificationConfigurationDto Map(NotificationConfiguration configuration) => new(
        configuration.SoundEnabled, configuration.VibrationEnabled, configuration.Priority.ToString(),
        configuration.EnabledOnAndroid, configuration.EnabledOnWindows, configuration.AllowFullScreenIntent,
        configuration.TitleTemplate, configuration.BodyTemplate, configuration.Version);

    public static AccessGroupDto Map(AccessGroup group) => new(
        group.Id, group.Name, group.Description, group.IsActive, group.GrantsAllSectors,
        [.. group.PermissionKeys], [.. group.SectorIds], group.Version);

    public static AppUserDto Map(AppUser user) => new(
        user.Id, user.UserName, user.DisplayName, user.IsActive, [.. user.GroupIds], user.Version);
}
