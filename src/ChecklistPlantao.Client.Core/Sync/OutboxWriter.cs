using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Client.Core.Sync;

/// <summary>
/// Grava uma alteração local e a enfileira para envio — sempre na MESMA transação.
///
/// Essa atomicidade é o que garante o requisito central do modo offline: não existe estado em
/// que a tela mostre a marcação mas a fila não tenha o item, nem o contrário.
/// </summary>
public sealed class OutboxWriter(LocalDbContext db)
{
    /// <summary>Marca ou desmarca uma célula localmente e enfileira a operação.</summary>
    public async Task<ChecklistEntry> ToggleEntryAsync(
        Guid sessionId,
        Guid bedId,
        Guid templateId,
        Guid columnId,
        bool isCompleted,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var entry = await db.ChecklistEntries.FirstOrDefaultAsync(
            e => e.SessionId == sessionId
                && e.BedId == bedId
                && e.ChecklistTemplateId == templateId
                && e.ChecklistColumnId == columnId,
            cancellationToken).ConfigureAwait(false);

        if (entry is null)
        {
            entry = new ChecklistEntry(Guid.CreateVersion7(), sessionId, bedId, templateId, columnId, nowUtc);
            db.ChecklistEntries.Add(entry);
        }

        // A versão-base é a que o dispositivo conhece agora. É ela que o servidor usa para
        // decidir se um "desmarcar" antigo pode apagar uma conclusão mais nova.
        var baseVersion = entry.Version;
        entry.SetCompletion(isCompleted, nowUtc);

        var payload = new ChecklistEntryPayload(sessionId, bedId, templateId, columnId, isCompleted);

        db.Outbox.Add(new SyncOutboxItem(
            Guid.CreateVersion7(),
            SyncEntityTypes.ChecklistEntry,
            entry.Id,
            SyncOperationType.Upsert,
            SyncJson.Serialize(payload),
            baseVersion,
            nowUtc));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return entry;
    }

    public async Task<SessionBedMarker> ToggleMarkerAsync(
        Guid sessionId,
        Guid bedId,
        Guid markerDefinitionId,
        bool isSelected,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var marker = await db.SessionBedMarkers.FirstOrDefaultAsync(
            m => m.SessionId == sessionId && m.BedId == bedId && m.MarkerDefinitionId == markerDefinitionId,
            cancellationToken).ConfigureAwait(false);

        if (marker is null)
        {
            marker = new SessionBedMarker(Guid.CreateVersion7(), sessionId, bedId, markerDefinitionId, isSelected, nowUtc);
            db.SessionBedMarkers.Add(marker);
        }

        var baseVersion = marker.Version;
        marker.SetSelected(isSelected, nowUtc);

        var payload = new SessionBedMarkerPayload(sessionId, bedId, markerDefinitionId, isSelected);

        db.Outbox.Add(new SyncOutboxItem(
            Guid.CreateVersion7(),
            SyncEntityTypes.SessionBedMarker,
            marker.Id,
            SyncOperationType.Upsert,
            SyncJson.Serialize(payload),
            baseVersion,
            nowUtc));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return marker;
    }

    public Task<int> PendingCountAsync(CancellationToken cancellationToken = default) =>
        db.Outbox.CountAsync(o => o.Status != OutboxItemStatus.Done, cancellationToken);

    /// <summary>Itens prontos para envio agora, na ordem em que foram criados.</summary>
    public async Task<IReadOnlyList<SyncOutboxItem>> TakeReadyAsync(
        DateTime nowUtc,
        int max,
        CancellationToken cancellationToken = default)
    {
        var candidatos = await db.Outbox
            .Where(o => o.Status != OutboxItemStatus.Done)
            .OrderBy(o => o.CreatedAtUtc)
            .Take(max * 2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. candidatos.Where(o => o.IsReady(nowUtc)).Take(max)];
    }
}
