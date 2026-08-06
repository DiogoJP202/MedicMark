namespace ChecklistPlantao.Contracts.Sync;

/// <summary>
/// Uma alteração local pronta para envio. <see cref="OperationId"/> é o que garante idempotência:
/// reenviar a mesma operação nunca a aplica duas vezes.
/// </summary>
public sealed record SyncOperationDto(
    Guid OperationId,
    string EntityType,
    Guid EntityId,
    string OperationType,
    string Payload,
    int BaseVersion,
    DateTime CreatedAtUtc);

public sealed record SyncPushRequest(
    string DeviceId,
    long KnownCursor,
    IReadOnlyList<SyncOperationDto> Operations);

/// <summary>
/// Resultado de uma operação. Em conflito, <see cref="CurrentState"/> traz o estado autoritativo
/// para o cliente adotar sem precisar de uma segunda chamada.
/// </summary>
public sealed record SyncOperationResultDto(
    Guid OperationId,
    string Status,
    string? Reason,
    string? CurrentState,
    int? CurrentVersion);

public sealed record ServerChangeDto(
    long Sequence,
    string EntityType,
    Guid EntityId,
    string ChangeType,
    int Version,
    DateTime ChangedAtUtc,
    string? Payload);

public sealed record SyncPushResponse(
    IReadOnlyList<SyncOperationResultDto> Results,
    IReadOnlyList<ServerChangeDto> Changes,
    long Cursor,
    DateTime ServerTimeUtc);

public sealed record SyncPullResponse(
    IReadOnlyList<ServerChangeDto> Changes,
    long Cursor,
    bool HasMore,
    DateTime ServerTimeUtc,
    bool RequiresBootstrap);

/// <summary>Nomes dos eventos do hub. O evento apenas avisa; o estado vem sempre do pull.</summary>
public static class SyncHubEvents
{
    public const string HubPath = "/hubs/sync";

    public const string ChangesAvailable = "AlteracoesDisponiveis";
    public const string SessionUpdated = "SessaoAtualizada";
    public const string ConfigurationChanged = "ConfiguracaoAlterada";
    public const string PermissionsChanged = "PermissoesAlteradas";
    public const string SessionClosed = "SessaoEncerrada";
}

/// <summary>Aviso enviado pelo hub. Propositalmente pequeno: nunca transporta o estado.</summary>
public sealed record SyncNotification(string EntityType, Guid? SectorId, long Cursor, DateTime SentAtUtc);
