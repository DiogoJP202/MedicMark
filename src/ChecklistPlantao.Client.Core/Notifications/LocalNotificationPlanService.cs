using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Client.Core.Notifications;

/// <summary>
/// Lê o snapshot operacional local e produz o plano completo de alertas do dispositivo.
/// Mantém a consulta fora do head MAUI para que a regra seja testável e compartilhada.
/// </summary>
public sealed class LocalNotificationPlanService(
    IDbContextFactory<LocalDbContext> contexts,
    IInstitutionSettingsProvider settings,
    IInstitutionTimeZone timeZone,
    IClock clock)
{
    public async Task<IReadOnlyList<ScheduledNotification>> BuildAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var device = await db.DeviceState.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (device?.CurrentSectorId is not { } sectorId)
        {
            return [];
        }

        var session = await db.OperationalSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SectorId == sectorId && s.Status == SessionStatus.Open, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return [];
        }

        var sector = await db.Sectors.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectorId, cancellationToken).ConfigureAwait(false);
        var configuration = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
        var window = ShiftResolver.For(configuration, sector);
        var notificationConfiguration = await db.NotificationConfigurations.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? new NotificationConfiguration(clock.UtcNow);

        var templates = await db.ChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Columns)
            .Include(t => t.Sectors)
            .Where(t => t.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var applicableTemplates = templates.Where(t => t.AppliesTo(sectorId)).ToList();
        var entries = await db.ChecklistEntries
            .AsNoTracking()
            .Where(e => e.SessionId == session.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var activeBedIds = await db.SessionBeds
            .AsNoTracking()
            .Where(b => b.SessionId == session.Id && b.IsActiveInSession)
            .Select(b => b.BedId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var bedCodes = await db.Beds
            .AsNoTracking()
            .Where(b => activeBedIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Code, cancellationToken)
            .ConfigureAwait(false);

        var summary = SessionSummaryCalculator.Build(entries, applicableTemplates, [], [], bedCodes);
        var progressByTemplate = summary.Templates.ToDictionary(t => t.TemplateId);
        var scheduled = new List<ScheduledNotification>();

        foreach (var template in applicableTemplates)
        {
            if (!progressByTemplate.TryGetValue(template.Id, out var progress))
            {
                continue;
            }

            var pendingByColumn = progress.Columns.ToDictionary(c => c.ColumnId, c => c.Progress.Pending);

            scheduled.AddRange(NotificationScheduleBuilder.Build(
                window,
                session.ServiceDate,
                sectorId,
                sector?.Name ?? string.Empty,
                template,
                pendingByColumn,
                notificationConfiguration,
                timeZone.TimeZone,
                clock.UtcNow,
                session.IsOpen));
        }

        return [.. scheduled.OrderBy(item => item.FireAt)];
    }
}
