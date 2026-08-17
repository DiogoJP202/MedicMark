using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.Client.Core.Sync;

/// <summary>
/// Grava uma alteração local e a enfileira para envio — sempre na MESMA transação.
///
/// Essa atomicidade é o que garante o requisito central do modo offline: não existe estado em
/// que a tela mostre a marcação mas a fila não tenha o item, nem o contrário.
/// </summary>
public sealed class OutboxWriter
{
    private readonly IDbContextFactory<LocalDbContext>? _contextos;
    private readonly LocalDbContext? _emprestado;

    /// <summary>
    /// Modo normal: cada operação abre e descarta o seu próprio contexto. Marcar um leito é uma
    /// unidade de trabalho completa e independente. Ver docs/DECISIONS.md (D-021).
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public OutboxWriter(IDbContextFactory<LocalDbContext> contextos) => _contextos = contextos;

    /// <summary>
    /// Modo emprestado: trabalha num contexto que já está aberto, para juntar esta gravação a
    /// outras da mesma unidade de trabalho — é o que a sincronização precisa, porque atualiza os
    /// itens da fila depois da resposta do servidor. Quem emprestou continua dono do contexto.
    /// </summary>
    public OutboxWriter(LocalDbContext db) => _emprestado = db;

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
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // O rastreador antes do banco: tocar duas vezes na mesma célula antes de a primeira
            // gravação concluir encontraria o disco vazio e criaria uma segunda entidade com a
            // mesma chave. Mesma regra aplicada no servidor, em ChecklistMutationService.
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
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    public async Task<SessionBedMarker> ToggleMarkerAsync(
        Guid sessionId,
        Guid bedId,
        Guid markerDefinitionId,
        bool isSelected,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var marker = db.SessionBedMarkers.Local.FirstOrDefault(
                m => m.SessionId == sessionId && m.BedId == bedId && m.MarkerDefinitionId == markerDefinitionId)
                ?? await db.SessionBedMarkers.FirstOrDefaultAsync(
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
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    public async Task<int> PendingCountAsync(CancellationToken cancellationToken = default)
    {
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await db.Outbox
                .CountAsync(o => o.Status != OutboxItemStatus.Done, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Itens prontos para envio agora, na ordem em que foram criados.
    ///
    /// Só faz sentido no modo emprestado: quem sincroniza precisa ATUALIZAR estes mesmos itens
    /// depois da resposta do servidor, e entidades rastreadas por um contexto já descartado não
    /// gravariam nada.
    /// </summary>
    public async Task<IReadOnlyList<SyncOutboxItem>> TakeReadyAsync(
        DateTime nowUtc,
        int max,
        CancellationToken cancellationToken = default)
    {
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var candidatos = await db.Outbox
                .Where(o => o.Status != OutboxItemStatus.Done)
                .OrderBy(o => o.CreatedAtUtc)
                .Take(max * 2)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. candidatos.Where(o => o.IsReady(nowUtc)).Take(max)];
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    /// <summary>Devolve o contexto a usar e se ele é nosso (e portanto precisa ser descartado).</summary>
    private async Task<(LocalDbContext Db, bool Meu)> AbrirAsync(CancellationToken cancellationToken)
    {
        if (_emprestado is not null)
        {
            return (_emprestado, false);
        }

        return (await _contextos!.CreateDbContextAsync(cancellationToken).ConfigureAwait(false), true);
    }

    private static ValueTask FecharAsync(LocalDbContext db, bool meu) =>
        meu ? db.DisposeAsync() : ValueTask.CompletedTask;
}
