using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Checklist;
using ChecklistPlantao.Application.Common;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Application.Sync;

/// <summary>
/// Envio e recebimento de alterações.
///
/// Duas garantias sustentam o modo offline:
///   1. Idempotência — cada operação carrega um <c>OperationId</c>; reenviar não aplica de novo.
///   2. Cursor monotônico — o cliente guarda a última sequência vista e pede só o que veio depois.
///
/// O que o cliente pode enviar é restrito a marcações e classificações
/// (<see cref="SyncEntityTypes.ClientWritable"/>). Configuração administrativa só muda pela API
/// de administração, com autorização própria.
/// </summary>
public sealed class SyncService(
    IAppDataContext db,
    ChecklistMutationService mutations,
    ConfigurationQueryService configuration,
    IClock clock,
    ILogger<SyncService> logger)
{
    /// <summary>Teto de alterações por resposta de pull. Evita respostas gigantes em rede fraca.</summary>
    public const int MaxChangesPerPull = 500;

    public async Task<SyncPushResponse> PushAsync(
        SyncPushRequest request,
        ICurrentUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        var results = new List<SyncOperationResultDto>(request.Operations.Count);
        var operationIds = request.Operations.Select(o => o.OperationId).ToList();
        var alreadyProcessed = await db.ProcessedOperationVersionsAsync(operationIds, cancellationToken).ConfigureAwait(false);

        foreach (var operation in request.Operations)
        {
            if (alreadyProcessed.TryGetValue(operation.OperationId, out var previous))
            {
                results.Add(new SyncOperationResultDto(
                    operation.OperationId,
                    SyncOperationStatus.Duplicate.ToString(),
                    "Operação já processada anteriormente.",
                    null,
                    previous));
                continue;
            }

            results.Add(await ApplyAsync(operation, user, cancellationToken).ConfigureAwait(false));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var pull = await PullAsync(request.KnownCursor, user, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Push do dispositivo {DeviceId}: {Total} operação(ões), {Aplicadas} aplicada(s), {Conflitos} conflito(s).",
            request.DeviceId,
            request.Operations.Count,
            results.Count(r => r.Status == nameof(SyncOperationStatus.Applied)),
            results.Count(r => r.Status == nameof(SyncOperationStatus.Conflict)));

        return new SyncPushResponse(results, pull.Changes, pull.Cursor, clock.UtcNow);
    }

    public async Task<SyncPullResponse> PullAsync(
        long knownCursor,
        ICurrentUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Alterações globais (SectorId nulo) valem para todo mundo; as de setor só para quem tem acesso.
        // Filtro nulo significa acesso irrestrito.
        IReadOnlyCollection<Guid>? sectorFilter = user.Access.GrantsAllSectors ? null : [.. user.Access.ExplicitSectorIds];

        var fetched = await db
            .ChangesAfterAsync(knownCursor, sectorFilter, MaxChangesPerPull + 1, cancellationToken)
            .ConfigureAwait(false);

        var changes = fetched.ToList();
        var hasMore = changes.Count > MaxChangesPerPull;

        if (hasMore)
        {
            changes.RemoveAt(changes.Count - 1);
        }

        var cursor = changes.Count > 0 ? changes[^1].Sequence : knownCursor;

        // O log é podado por retenção. Se o cliente ficou para trás demais, não há como
        // reconstruir de forma incremental: ele precisa refazer o bootstrap.
        var oldestSequence = await db.OldestChangeSequenceAsync(cancellationToken).ConfigureAwait(false);
        var requiresBootstrap = knownCursor > 0 && oldestSequence.HasValue && knownCursor < oldestSequence.Value - 1;

        var dtos = await BuildChangeDtosAsync(changes, cancellationToken).ConfigureAwait(false);

        return new SyncPullResponse(dtos, cursor, hasMore, clock.UtcNow, requiresBootstrap);
    }

    public Task<long> CurrentCursorAsync(CancellationToken cancellationToken = default) =>
        db.LatestChangeSequenceAsync(cancellationToken);

    private async Task<SyncOperationResultDto> ApplyAsync(
        SyncOperationDto operation,
        ICurrentUser user,
        CancellationToken cancellationToken)
    {
        if (!SyncEntityTypes.ClientWritable.Contains(operation.EntityType))
        {
            return Rejected(operation, "Este tipo de entidade não pode ser alterado pela sincronização.");
        }

        try
        {
            return operation.EntityType switch
            {
                SyncEntityTypes.ChecklistEntry => await ApplyEntryAsync(operation, user, cancellationToken).ConfigureAwait(false),
                SyncEntityTypes.SessionBedMarker => await ApplyMarkerAsync(operation, user, cancellationToken).ConfigureAwait(false),
                _ => Rejected(operation, "Tipo de entidade desconhecido."),
            };
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(ex, "Operação {OperationId} com payload inválido.", operation.OperationId);
            return Rejected(operation, "Conteúdo da operação inválido.");
        }
    }

    private async Task<SyncOperationResultDto> ApplyEntryAsync(
        SyncOperationDto operation,
        ICurrentUser user,
        CancellationToken cancellationToken)
    {
        var payload = SyncJson.Deserialize<ChecklistEntryPayload>(operation.Payload);

        if (payload is null)
        {
            return Rejected(operation, "Conteúdo da operação vazio.");
        }

        var outcome = await mutations.ApplyEntryAsync(
            payload.SessionId,
            payload.BedId,
            payload.ChecklistTemplateId,
            payload.ChecklistColumnId,
            payload.IsCompleted,
            operation.BaseVersion,
            user,
            cancellationToken).ConfigureAwait(false);

        if (outcome.IsFailure)
        {
            return Rejected(operation, outcome.Error!.Message);
        }

        var result = outcome.Required;
        RecordProcessed(operation, result.Status, result.Entry.Version);

        return new SyncOperationResultDto(
            operation.OperationId,
            result.Status.ToString(),
            result.Status == SyncOperationStatus.Conflict ? "A conclusão registrada no servidor prevaleceu." : null,
            SyncJson.Serialize(result.Entry),
            result.Entry.Version);
    }

    private async Task<SyncOperationResultDto> ApplyMarkerAsync(
        SyncOperationDto operation,
        ICurrentUser user,
        CancellationToken cancellationToken)
    {
        var payload = SyncJson.Deserialize<SessionBedMarkerPayload>(operation.Payload);

        if (payload is null)
        {
            return Rejected(operation, "Conteúdo da operação vazio.");
        }

        var outcome = await mutations.ApplyMarkerAsync(
            payload.SessionId,
            payload.BedId,
            payload.MarkerDefinitionId,
            payload.IsSelected,
            operation.BaseVersion,
            user,
            cancellationToken).ConfigureAwait(false);

        if (outcome.IsFailure)
        {
            return Rejected(operation, outcome.Error!.Message);
        }

        var result = outcome.Required;
        RecordProcessed(operation, result.Status, result.Marker.Version);

        return new SyncOperationResultDto(
            operation.OperationId,
            result.Status.ToString(),
            result.Status == SyncOperationStatus.Conflict ? "Outro dispositivo alterou esta classificação antes." : null,
            SyncJson.Serialize(result.Marker),
            result.Marker.Version);
    }

    private SyncOperationResultDto Rejected(SyncOperationDto operation, string reason)
    {
        RecordProcessed(operation, SyncOperationStatus.Rejected, null);
        return new SyncOperationResultDto(operation.OperationId, SyncOperationStatus.Rejected.ToString(), reason, null, null);
    }

    private void RecordProcessed(SyncOperationDto operation, SyncOperationStatus status, int? version) =>
        db.AppendProcessedOperation(operation.OperationId, operation.EntityType, operation.EntityId, status.ToString(), version, clock.UtcNow);

    /// <summary>
    /// Anexa o estado atual de cada entidade alterada, para o cliente aplicar sem uma segunda ida
    /// ao servidor. As consultas são agrupadas por tipo — nunca uma por alteração.
    /// </summary>
    private async Task<IReadOnlyList<ServerChangeDto>> BuildChangeDtosAsync(
        IReadOnlyList<ChangeLogRow> changes,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return [];
        }

        var payloads = await configuration.LoadPayloadsAsync(changes, cancellationToken).ConfigureAwait(false);

        return [.. changes.Select(c => new ServerChangeDto(
            c.Sequence,
            c.EntityType,
            c.EntityId,
            c.ChangeType.ToString(),
            c.Version,
            c.ChangedAtUtc,
            payloads.TryGetValue((c.EntityType, c.EntityId), out var payload) ? payload : null))];
    }
}
