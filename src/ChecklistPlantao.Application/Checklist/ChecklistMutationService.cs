using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Common;
using ChecklistPlantao.Application.Sessions;
using ChecklistPlantao.Contracts.Common;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Application.Checklist;

/// <summary>Resultado de aplicar uma marcação, com o estado final para o cliente adotar.</summary>
public sealed record ChecklistMutationOutcome(SyncOperationStatus Status, ChecklistEntryDto Entry);

public sealed record MarkerMutationOutcome(SyncOperationStatus Status, SessionBedMarkerDto Marker);

/// <summary>
/// Aplica marcações e classificações resolvendo conflito.
///
/// É o único caminho de escrita dessas duas entidades: tanto o endpoint REST direto quanto o
/// push em lote passam por aqui, de modo que a regra de convergência não existe em duas versões.
/// Não persiste — quem chama decide o momento do <c>SaveChanges</c>, o que permite processar um
/// lote inteiro em uma transação só.
/// </summary>
public sealed class ChecklistMutationService(IAppDataContext db, IClock clock)
{
    public async Task<Result<ChecklistMutationOutcome>> ApplyEntryAsync(
        Guid sessionId,
        Guid bedId,
        Guid templateId,
        Guid columnId,
        bool isCompleted,
        int baseVersion,
        ICurrentUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var guard = await GuardAsync(sessionId, user, Permissions.ChecklistUpdate, cancellationToken).ConfigureAwait(false);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var session = guard.Required;
        var now = clock.UtcNow;

        // Procura PRIMEIRO entre as entidades já rastreadas nesta unidade de trabalho.
        //
        // Um lote de sincronização pode trazer várias operações para a mesma célula — é o caso
        // normal de quem marcou, desmarcou e marcou de novo offline. A consulta vai ao banco, e a
        // entrada criada pela operação anterior do MESMO lote ainda não foi gravada: sem esta
        // checagem, uma segunda entrada era criada para a mesma célula e o SaveChanges do lote
        // inteiro estourava a restrição única, derrubando toda a sincronização com 500.
        var entry = db.ChecklistEntries.Local.FirstOrDefault(
            e => e.SessionId == sessionId
                && e.BedId == bedId
                && e.ChecklistTemplateId == templateId
                && e.ChecklistColumnId == columnId)
            ?? await db.ChecklistEntries.FirstOrDefaultAsync(
                e => e.SessionId == sessionId
                    && e.BedId == bedId
                    && e.ChecklistTemplateId == templateId
                    && e.ChecklistColumnId == columnId,
                cancellationToken).ConfigureAwait(false);

        if (entry is null)
        {
            var validation = await ValidateCellAsync(session, bedId, templateId, columnId, cancellationToken).ConfigureAwait(false);
            if (validation.IsFailure)
            {
                return validation.Error!;
            }

            // A célula ainda não existia no servidor: é o caso normal de uma marcação feita
            // offline, antes de qualquer sincronização desta sessão.
            entry = new ChecklistEntry(Guid.CreateVersion7(), sessionId, bedId, templateId, columnId, now);
            entry.SetCompletion(isCompleted, now);
            db.ChecklistEntries.Add(entry);
            db.AppendChange(SyncEntityTypes.ChecklistEntry, entry.Id, SyncChangeType.Created, entry.Version, session.SectorId, now);

            return new ChecklistMutationOutcome(SyncOperationStatus.Applied, SessionService.MapEntry(entry));
        }

        var decision = MergePolicies.ResolveChecklistEntry(entry.IsCompleted, entry.Version, isCompleted, baseVersion);

        switch (decision)
        {
            case MergeDecision.Apply:
                entry.SetCompletion(isCompleted, now);
                db.AppendChange(SyncEntityTypes.ChecklistEntry, entry.Id, SyncChangeType.Updated, entry.Version, session.SectorId, now);
                return new ChecklistMutationOutcome(SyncOperationStatus.Applied, SessionService.MapEntry(entry));

            case MergeDecision.AlreadyInDesiredState:
                return new ChecklistMutationOutcome(SyncOperationStatus.NoChange, SessionService.MapEntry(entry));

            default:
                return new ChecklistMutationOutcome(SyncOperationStatus.Conflict, SessionService.MapEntry(entry));
        }
    }

    public async Task<Result<MarkerMutationOutcome>> ApplyMarkerAsync(
        Guid sessionId,
        Guid bedId,
        Guid markerDefinitionId,
        bool isSelected,
        int baseVersion,
        ICurrentUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var guard = await GuardAsync(sessionId, user, Permissions.ChecklistUpdate, cancellationToken).ConfigureAwait(false);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var session = guard.Required;
        var now = clock.UtcNow;

        // Mesma razão da marcação: o lote pode alterar a mesma classificação mais de uma vez.
        var marker = db.SessionBedMarkers.Local.FirstOrDefault(
            m => m.SessionId == sessionId && m.BedId == bedId && m.MarkerDefinitionId == markerDefinitionId)
            ?? await db.SessionBedMarkers.FirstOrDefaultAsync(
                m => m.SessionId == sessionId && m.BedId == bedId && m.MarkerDefinitionId == markerDefinitionId,
                cancellationToken).ConfigureAwait(false);

        if (marker is null)
        {
            var bedBelongs = await db.Beds.AnyAsync(b => b.Id == bedId && b.SectorId == session.SectorId, cancellationToken).ConfigureAwait(false);
            if (!bedBelongs)
            {
                return OperationError.Validation("Este leito não pertence ao setor da sessão.");
            }

            var definitionExists = await db.BedMarkerDefinitions
                .AnyAsync(d => d.Id == markerDefinitionId && d.IsActive, cancellationToken)
                .ConfigureAwait(false);

            if (!definitionExists)
            {
                return OperationError.NotFound("Marcador não encontrado ou desativado.");
            }

            marker = new SessionBedMarker(Guid.CreateVersion7(), sessionId, bedId, markerDefinitionId, isSelected, now);
            db.SessionBedMarkers.Add(marker);
            db.AppendChange(SyncEntityTypes.SessionBedMarker, marker.Id, SyncChangeType.Created, marker.Version, session.SectorId, now);

            return new MarkerMutationOutcome(SyncOperationStatus.Applied, SessionService.MapMarker(marker));
        }

        // Classificações não usam "conclusão vence": quem estava desatualizado perde e recebe o estado atual.
        var decision = MergePolicies.ResolveVersioned(marker.Version, baseVersion, wouldChangeState: marker.IsSelected != isSelected);

        switch (decision)
        {
            case MergeDecision.Apply:
                marker.SetSelected(isSelected, now);
                db.AppendChange(SyncEntityTypes.SessionBedMarker, marker.Id, SyncChangeType.Updated, marker.Version, session.SectorId, now);
                return new MarkerMutationOutcome(SyncOperationStatus.Applied, SessionService.MapMarker(marker));

            case MergeDecision.AlreadyInDesiredState:
                return new MarkerMutationOutcome(SyncOperationStatus.NoChange, SessionService.MapMarker(marker));

            default:
                return new MarkerMutationOutcome(SyncOperationStatus.Conflict, SessionService.MapMarker(marker));
        }
    }

    /// <summary>Sessão existe, está aberta, o usuário tem permissão e enxerga o setor.</summary>
    private async Task<Result<OperationalSession>> GuardAsync(
        Guid sessionId,
        ICurrentUser user,
        string permission,
        CancellationToken cancellationToken)
    {
        if (!user.Access.Has(permission))
        {
            return OperationError.PermissionDenied();
        }

        var session = await db.OperationalSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);

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
            return new OperationError(ApiErrorCodes.SessionClosed, "Esta sessão de plantão já foi encerrada.");
        }

        return session;
    }

    private async Task<Result> ValidateCellAsync(
        OperationalSession session,
        Guid bedId,
        Guid templateId,
        Guid columnId,
        CancellationToken cancellationToken)
    {
        var bedBelongs = await db.Beds
            .AnyAsync(b => b.Id == bedId && b.SectorId == session.SectorId, cancellationToken)
            .ConfigureAwait(false);

        if (!bedBelongs)
        {
            return OperationError.Validation("Este leito não pertence ao setor da sessão.");
        }

        var columnBelongs = await db.ChecklistColumns
            .AnyAsync(c => c.Id == columnId && c.ChecklistTemplateId == templateId, cancellationToken)
            .ConfigureAwait(false);

        if (!columnBelongs)
        {
            return OperationError.Validation("Coluna inválida para este tipo de checklist.");
        }

        return Result.Success();
    }
}
