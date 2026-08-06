using ChecklistPlantao.Domain.Sync;
using Microsoft.AspNetCore.Identity;

namespace ChecklistPlantao.Infrastructure.Persistence;

/// <summary>
/// Usuário do ASP.NET Identity. Guarda exclusivamente credenciais e estado de bloqueio.
/// Compartilha o <c>Id</c> com <see cref="Domain.Access.AppUser"/>, que guarda os dados de
/// autorização. Ver D-013 em docs/DECISIONS.md.
/// </summary>
public sealed class AppIdentityUser : IdentityUser<Guid>
{
}

/// <summary>
/// Refresh token emitido para um dispositivo. É rotacionado a cada uso: usar um token já
/// substituído revoga toda a cadeia, o que denuncia reutilização indevida.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    public RefreshToken(Guid id, Guid userId, string tokenHash, string? deviceId, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        DeviceId = deviceId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Hash do token. O valor em claro só existe na resposta ao cliente.</summary>
    public string TokenHash { get; private set; }

    public string? DeviceId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    public void Revoke(DateTime nowUtc, Guid? replacedByTokenId = null)
    {
        RevokedAtUtc ??= nowUtc;
        ReplacedByTokenId ??= replacedByTokenId;
    }
}

/// <summary>
/// Uma linha do log de alterações. A sequência é o cursor da sincronização incremental:
/// o cliente guarda a última que viu e pede tudo o que veio depois.
/// </summary>
public sealed class ChangeLogEntry
{
    private ChangeLogEntry()
    {
        EntityType = string.Empty;
    }

    public ChangeLogEntry(string entityType, Guid entityId, SyncChangeType changeType, int version, Guid? sectorId, DateTime changedAtUtc)
    {
        EntityType = entityType;
        EntityId = entityId;
        ChangeType = changeType;
        Version = version;
        SectorId = sectorId;
        ChangedAtUtc = changedAtUtc;
    }

    /// <summary>Sequência crescente atribuída pelo banco. É o cursor.</summary>
    public long Sequence { get; private set; }

    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public SyncChangeType ChangeType { get; private set; }

    public int Version { get; private set; }

    /// <summary>Nulo para alterações globais (configuração, templates, grupos).</summary>
    public Guid? SectorId { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }
}

/// <summary>
/// Registro de uma operação de sincronização já processada. É o que torna o push idempotente:
/// o mesmo <c>OperationId</c> devolve o resultado original sem reaplicar nada.
/// </summary>
public sealed class ProcessedOperation
{
    private ProcessedOperation()
    {
        EntityType = string.Empty;
        Status = string.Empty;
    }

    public ProcessedOperation(Guid operationId, string entityType, Guid entityId, string status, int? resultingVersion, DateTime processedAtUtc)
    {
        OperationId = operationId;
        EntityType = entityType;
        EntityId = entityId;
        Status = status;
        ResultingVersion = resultingVersion;
        ProcessedAtUtc = processedAtUtc;
    }

    public Guid OperationId { get; private set; }

    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public string Status { get; private set; }

    public int? ResultingVersion { get; private set; }

    public DateTime ProcessedAtUtc { get; private set; }
}
