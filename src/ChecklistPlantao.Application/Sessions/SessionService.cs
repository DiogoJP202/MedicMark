using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Common;
using ChecklistPlantao.Contracts.Common;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Application.Sessions;

/// <summary>
/// Abre, consulta, fecha e reinicia sessões de plantão.
///
/// Toda operação verifica o acesso ao setor no servidor. Esconder o botão na interface é
/// conveniência; a recusa acontece aqui.
/// </summary>
public sealed class SessionService(
    IAppDataContext db,
    IClock clock,
    IInstitutionTimeZone timeZone,
    IInstitutionSettingsProvider settingsProvider,
    ILogger<SessionService> logger)
{
    /// <summary>
    /// Sessão aberta do setor. Se não houver e a abertura automática estiver ligada, cria uma —
    /// o plantão não deve começar com o usuário tendo que apertar um botão de "abrir".
    /// </summary>
    public async Task<Result<SessionStateDto>> GetCurrentAsync(
        Guid sectorId,
        ICurrentUser user,
        bool createIfMissing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!user.Access.CanAccessSector(sectorId))
        {
            return OperationError.SectorAccessDenied();
        }

        var settings = await settingsProvider.GetAsync(cancellationToken).ConfigureAwait(false);
        var sector = await db.Sectors.FirstOrDefaultAsync(s => s.Id == sectorId, cancellationToken).ConfigureAwait(false);

        if (sector is null)
        {
            return OperationError.NotFound("Setor não encontrado.");
        }

        var window = ShiftResolver.For(settings, sector);
        var serviceDate = window.ServiceDateFor(timeZone.ToLocal(clock.UtcNow));

        var session = await db.OperationalSessions
            .Include(s => s.Beds)
            .FirstOrDefaultAsync(s => s.SectorId == sectorId && s.Status == SessionStatus.Open, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            if (!createIfMissing || !settings.AutoOpenSession)
            {
                return OperationError.NotFound("Não há sessão de plantão aberta para este setor.");
            }

            var opened = await OpenAsync(sectorId, serviceDate, user, cancellationToken).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                return opened.Error!;
            }

            session = await db.OperationalSessions
                .Include(s => s.Beds)
                .FirstAsync(s => s.Id == opened.Required.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        return await BuildStateAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<OperationalSessionDto>> OpenAsync(
        Guid sectorId,
        DateOnly? serviceDate,
        ICurrentUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!user.Access.CanAccessSector(sectorId))
        {
            return OperationError.SectorAccessDenied();
        }

        var alreadyOpen = await db.OperationalSessions
            .AnyAsync(s => s.SectorId == sectorId && s.Status == SessionStatus.Open, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyOpen)
        {
            return new OperationError(ApiErrorCodes.SessionAlreadyOpen, "Este setor já tem uma sessão de plantão aberta.");
        }

        var settings = await settingsProvider.GetAsync(cancellationToken).ConfigureAwait(false);
        var sector = await db.Sectors.FirstOrDefaultAsync(s => s.Id == sectorId, cancellationToken).ConfigureAwait(false);

        if (sector is null)
        {
            return OperationError.NotFound("Setor não encontrado.");
        }

        if (!sector.IsActive)
        {
            return OperationError.Validation("Este setor está desativado.");
        }

        var now = clock.UtcNow;
        var window = ShiftResolver.For(settings, sector);
        var date = serviceDate ?? window.ServiceDateFor(timeZone.ToLocal(now));

        var session = new OperationalSession(Guid.CreateVersion7(), sectorId, date, now);

        var bedIds = await db.Beds
            .Where(b => b.SectorId == sectorId && b.IsActive)
            .OrderBy(b => b.SortOrder)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var bedId in bedIds)
        {
            session.AddBed(bedId);
        }

        db.OperationalSessions.Add(session);
        db.AppendChange(SyncEntityTypes.OperationalSession, session.Id, SyncChangeType.Created, session.Version, sectorId, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // O Any acima entrega a mensagem amigável no caminho comum; o índice filtrado fecha
            // a janela de concorrência entre duas requisições que fizeram o Any ao mesmo tempo.
            var competingSessionExists = await db.OperationalSessions
                .AsNoTracking()
                .AnyAsync(s => s.Id != session.Id && s.SectorId == sectorId && s.Status == SessionStatus.Open, cancellationToken)
                .ConfigureAwait(false);

            if (competingSessionExists)
            {
                return new OperationError(ApiErrorCodes.SessionAlreadyOpen, "Este setor já tem uma sessão de plantão aberta.");
            }

            throw;
        }

        logger.LogInformation(
            "Sessão {SessionId} aberta no setor {SectorId} para a data de serviço {ServiceDate} com {Leitos} leito(s).",
            session.Id,
            sectorId,
            date,
            bedIds.Count);

        return Map(session);
    }

    /// <summary>Resumo mostrado na confirmação de fechamento.</summary>
    public async Task<Result<SessionSummaryDto>> GetSummaryAsync(
        Guid sessionId,
        ICurrentUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var session = await db.OperationalSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return OperationError.NotFound("Sessão não encontrada.");
        }

        if (!user.Access.CanAccessSector(session.SectorId))
        {
            return OperationError.SectorAccessDenied();
        }

        return await BuildSummaryAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SessionSummaryDto>> CloseAsync(
        Guid sessionId,
        bool confirmWithPending,
        ICurrentUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!user.Access.Has(Permissions.ChecklistClose))
        {
            return OperationError.PermissionDenied("Você não tem permissão para encerrar o plantão.");
        }

        var session = await db.OperationalSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return OperationError.NotFound("Sessão não encontrada.");
        }

        if (!user.Access.CanAccessSector(session.SectorId))
        {
            return OperationError.SectorAccessDenied();
        }

        if (!session.IsOpen)
        {
            return new OperationError(ApiErrorCodes.SessionClosed, "Esta sessão já está encerrada.");
        }

        var summary = await BuildSummaryAsync(session, cancellationToken).ConfigureAwait(false);

        if (summary.HasPending && !confirmWithPending)
        {
            return OperationError.Validation(
                $"Ainda há {summary.Overall.Pending} tarefa(s) pendente(s). Confirme para encerrar mesmo assim.");
        }

        var now = clock.UtcNow;
        session.Close(now);
        db.AppendChange(SyncEntityTypes.OperationalSession, session.Id, SyncChangeType.Updated, session.Version, session.SectorId, now);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Sessão {SessionId} encerrada com {Pendentes} pendência(s) de {Total}.",
            session.Id,
            summary.Overall.Pending,
            summary.Overall.Total);

        return summary;
    }

    /// <summary>
    /// Reinicia o checklist: todas as marcações voltam a pendente. As classificações de leito
    /// (C.I., Sondas, Drenos) permanecem — reiniciar o checklist não é recomeçar o plantão.
    /// </summary>
    public async Task<Result> ResetAsync(Guid sessionId, ICurrentUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!user.Access.Has(Permissions.ChecklistClose))
        {
            return OperationError.PermissionDenied("Você não tem permissão para reiniciar o checklist.");
        }

        var session = await db.OperationalSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return OperationError.NotFound("Sessão não encontrada.");
        }

        if (!user.Access.CanAccessSector(session.SectorId))
        {
            return OperationError.SectorAccessDenied();
        }

        if (!session.IsOpen)
        {
            return new OperationError(ApiErrorCodes.SessionClosed, "Não é possível reiniciar uma sessão encerrada.");
        }

        var now = clock.UtcNow;

        var entries = await db.ChecklistEntries
            .Where(e => e.SessionId == sessionId && e.IsCompleted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var entry in entries)
        {
            entry.SetCompletion(false, now);
            db.AppendChange(SyncEntityTypes.ChecklistEntry, entry.Id, SyncChangeType.Updated, entry.Version, session.SectorId, now);
        }

        session.MarkReset(now);
        db.AppendChange(SyncEntityTypes.OperationalSession, session.Id, SyncChangeType.Updated, session.Version, session.SectorId, now);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Sessão {SessionId} reiniciada: {Quantidade} marcação(ões) desfeita(s).", sessionId, entries.Count);

        return Result.Success();
    }

    /// <summary>
    /// Apaga os dados operacionais das sessões já fora da janela de recuperação.
    /// Cadastros e configurações não são tocados.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetAsync(cancellationToken).ConfigureAwait(false);
        var policy = RetentionPolicy.From(settings);
        var now = clock.UtcNow;

        var closed = await db.OperationalSessions
            .Where(s => s.Status == SessionStatus.Closed && s.ClosedAtUtc != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var expired = closed.Where(s => policy.IsPurgeable(s, now)).ToList();

        if (expired.Count == 0)
        {
            return 0;
        }

        var ids = expired.Select(s => s.Id).ToList();

        await db.ChecklistEntries.Where(e => ids.Contains(e.SessionId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.SessionBedMarkers.Where(m => ids.Contains(m.SessionId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.SessionBeds.Where(b => ids.Contains(b.SessionId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.OperationalSessions.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        foreach (var session in expired)
        {
            db.AppendChange(SyncEntityTypes.OperationalSession, session.Id, SyncChangeType.Deleted, session.Version, session.SectorId, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Retenção aplicada: {Quantidade} sessão(ões) encerrada(s) removida(s).", expired.Count);

        return expired.Count;
    }

    private async Task<SessionStateDto> BuildStateAsync(OperationalSession session, CancellationToken cancellationToken)
    {
        var entries = await db.ChecklistEntries
            .AsNoTracking()
            .Where(e => e.SessionId == session.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var markers = await db.SessionBedMarkers
            .AsNoTracking()
            .Where(m => m.SessionId == session.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new SessionStateDto(
            Map(session),
            [.. session.Beds.Where(b => b.IsActiveInSession).Select(b => b.BedId)],
            [.. entries.Select(MapEntry)],
            [.. markers.Select(MapMarker)]);
    }

    private async Task<SessionSummaryDto> BuildSummaryAsync(OperationalSession session, CancellationToken cancellationToken)
    {
        var entries = await db.ChecklistEntries
            .AsNoTracking()
            .Where(e => e.SessionId == session.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var templates = await db.ChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Columns)
            .Include(t => t.Sectors)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var markers = await db.SessionBedMarkers
            .AsNoTracking()
            .Where(m => m.SessionId == session.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var definitions = await db.BedMarkerDefinitions
            .AsNoTracking()
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

        var summary = SessionSummaryCalculator.Build(
            entries,
            templates.Where(t => t.AppliesTo(session.SectorId)),
            markers,
            definitions,
            bedCodes);

        return new SessionSummaryDto(
            new ProgressDto(summary.Overall.Total, summary.Overall.Completed),
            [.. summary.Templates.Select(t => new TemplateSummaryDto(
                t.TemplateId,
                t.TemplateName,
                new ProgressDto(t.Progress.Total, t.Progress.Completed),
                [.. t.Columns.Select(c => new ColumnSummaryDto(
                    c.ColumnId,
                    c.ColumnName,
                    c.TriggerTime,
                    new ProgressDto(c.Progress.Total, c.Progress.Completed)))]))],
            [.. summary.Markers.Select(m => new MarkerSummaryDto(m.MarkerDefinitionId, m.MarkerName, m.BedCodes))]);
    }

    internal static OperationalSessionDto Map(OperationalSession session) => new(
        session.Id,
        session.SectorId,
        session.ServiceDate,
        session.StartedAtUtc,
        session.ClosedAtUtc,
        session.Status.ToString(),
        session.Version);

    internal static ChecklistEntryDto MapEntry(ChecklistEntry entry) => new(
        entry.Id,
        entry.SessionId,
        entry.BedId,
        entry.ChecklistTemplateId,
        entry.ChecklistColumnId,
        entry.IsCompleted,
        entry.Version,
        entry.UpdatedAtUtc);

    internal static SessionBedMarkerDto MapMarker(SessionBedMarker marker) => new(
        marker.Id,
        marker.SessionId,
        marker.BedId,
        marker.MarkerDefinitionId,
        marker.IsSelected,
        marker.Version,
        marker.UpdatedAtUtc);
}
