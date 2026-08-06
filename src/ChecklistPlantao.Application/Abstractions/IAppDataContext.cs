using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Domain.Structure;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Application.Abstractions;

/// <summary>
/// Superfície do banco central que os casos de uso enxergam.
///
/// Não é um repositório genérico — é o próprio <c>DbContext</c> com a superfície reduzida ao que
/// os serviços precisam, mais o registro do log de alterações. Isso mantém as consultas legíveis
/// (LINQ direto, sem camada de tradução) e ainda permite trocar a implementação nos testes.
/// </summary>
public interface IAppDataContext
{
    DbSet<AppUser> AppUsers { get; }

    DbSet<AccessGroup> AccessGroups { get; }

    DbSet<UserGroup> UserGroups { get; }

    DbSet<PermissionDefinition> PermissionDefinitions { get; }

    DbSet<Sector> Sectors { get; }

    DbSet<Bed> Beds { get; }

    DbSet<ChecklistTemplate> ChecklistTemplates { get; }

    DbSet<ChecklistColumn> ChecklistColumns { get; }

    DbSet<BedMarkerDefinition> BedMarkerDefinitions { get; }

    DbSet<OperationalSession> OperationalSessions { get; }

    DbSet<SessionBed> SessionBeds { get; }

    DbSet<ChecklistEntry> ChecklistEntries { get; }

    DbSet<SessionBedMarker> SessionBedMarkers { get; }

    DbSet<NotificationConfiguration> NotificationConfigurations { get; }

    DbSet<DeviceRegistration> DeviceRegistrations { get; }

    DbSet<AppSetting> AppSettings { get; }

    /// <summary>Registra uma alteração para a sincronização incremental.</summary>
    void AppendChange(string entityType, Guid entityId, SyncChangeType changeType, int version, Guid? sectorId, DateTime nowUtc);

    /// <summary>Última sequência registrada. É o cursor atual do servidor.</summary>
    Task<long> LatestChangeSequenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sequência mais antiga ainda no log. Se o cursor do cliente for anterior a ela, o log já foi
    /// podado e não há como sincronizar de forma incremental — o cliente precisa refazer o bootstrap.
    /// </summary>
    Task<long?> OldestChangeSequenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Alterações após o cursor. <paramref name="sectorFilter"/> nulo significa sem restrição de
    /// setor (usuário com acesso total); caso contrário retorna apenas alterações globais e dos
    /// setores informados.
    /// </summary>
    Task<IReadOnlyList<ChangeLogRow>> ChangesAfterAsync(
        long cursor,
        IReadOnlyCollection<Guid>? sectorFilter,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Versões resultantes das operações já processadas — base da idempotência do push.</summary>
    Task<Dictionary<Guid, int?>> ProcessedOperationVersionsAsync(
        IReadOnlyCollection<Guid> operationIds,
        CancellationToken cancellationToken = default);

    void AppendProcessedOperation(Guid operationId, string entityType, Guid entityId, string status, int? resultingVersion, DateTime processedAtUtc);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Linha do log de alterações, sem acoplar a Application à entidade de persistência.</summary>
public sealed record ChangeLogRow(
    long Sequence,
    string EntityType,
    Guid EntityId,
    SyncChangeType ChangeType,
    int Version,
    Guid? SectorId,
    DateTime ChangedAtUtc);

