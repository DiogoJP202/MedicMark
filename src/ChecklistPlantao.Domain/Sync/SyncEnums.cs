namespace ChecklistPlantao.Domain.Sync;

/// <summary>
/// Tipos de entidade que trafegam na sincronização. Persistido como texto para que o log de
/// alterações continue legível em diagnóstico e para que acrescentar um tipo não renumere os demais.
/// </summary>
public static class SyncEntityTypes
{
    public const string ChecklistEntry = "checklist-entry";
    public const string SessionBedMarker = "session-bed-marker";
    public const string OperationalSession = "operational-session";
    public const string Sector = "sector";
    public const string Bed = "bed";
    public const string ChecklistTemplate = "checklist-template";
    public const string ChecklistColumn = "checklist-column";
    public const string BedMarkerDefinition = "bed-marker-definition";
    public const string AccessGroup = "access-group";
    public const string AppUser = "app-user";
    public const string NotificationConfiguration = "notification-configuration";
    public const string AppSetting = "app-setting";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ChecklistEntry,
        SessionBedMarker,
        OperationalSession,
        Sector,
        Bed,
        ChecklistTemplate,
        ChecklistColumn,
        BedMarkerDefinition,
        AccessGroup,
        AppUser,
        NotificationConfiguration,
        AppSetting,
    };

    /// <summary>
    /// Tipos que o cliente tem permissão de enviar. Configuração administrativa só muda pela API
    /// de administração, com autorização própria — nunca pelo canal de sincronização em lote.
    /// </summary>
    public static readonly IReadOnlySet<string> ClientWritable = new HashSet<string>(StringComparer.Ordinal)
    {
        ChecklistEntry,
        SessionBedMarker,
    };
}

public enum SyncOperationType
{
    Upsert = 0,
    Delete = 1,
}

public enum SyncChangeType
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
}

/// <summary>Resultado de uma operação enviada no push, devolvido item a item.</summary>
public enum SyncOperationStatus
{
    /// <summary>Aplicada agora.</summary>
    Applied = 0,

    /// <summary>Já tinha sido processada antes — mesmo OperationId. Idempotência.</summary>
    Duplicate = 1,

    /// <summary>O servidor já estava no estado pedido.</summary>
    NoChange = 2,

    /// <summary>Conflito resolvido a favor do servidor; o estado atual acompanha a resposta.</summary>
    Conflict = 3,

    /// <summary>Recusada: entidade inexistente, sessão fechada, sem acesso ao setor, payload inválido.</summary>
    Rejected = 4,
}

/// <summary>Situação de um item na fila local de envio.</summary>
public enum OutboxItemStatus
{
    Pending = 0,
    InFlight = 1,
    Failed = 2,
    Done = 3,
}
