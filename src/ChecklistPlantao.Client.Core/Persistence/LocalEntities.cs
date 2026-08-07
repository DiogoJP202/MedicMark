using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Sync;

namespace ChecklistPlantao.Client.Core.Persistence;

/// <summary>
/// Alteração local aguardando envio.
///
/// É o coração do modo offline: a marcação é gravada no estado local E aqui, na mesma transação.
/// Enquanto este item existir, a alteração não se perdeu — nem ao fechar o aplicativo, nem ao
/// reiniciar o aparelho, nem depois de horas sem rede.
/// </summary>
public sealed class SyncOutboxItem
{
    private SyncOutboxItem()
    {
        EntityType = string.Empty;
        Payload = string.Empty;
    }

    public SyncOutboxItem(
        Guid operationId,
        string entityType,
        Guid entityId,
        SyncOperationType operationType,
        string payload,
        int baseVersion,
        DateTime createdAtUtc)
    {
        Id = Guid.CreateVersion7();
        OperationId = operationId;
        EntityType = entityType;
        EntityId = entityId;
        OperationType = operationType;
        Payload = payload;
        BaseVersion = baseVersion;
        CreatedAtUtc = createdAtUtc;
        Status = OutboxItemStatus.Pending;
    }

    public Guid Id { get; private set; }

    /// <summary>Identificador estável da operação. É o que torna o reenvio idempotente.</summary>
    public Guid OperationId { get; private set; }

    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public SyncOperationType OperationType { get; private set; }

    public string Payload { get; private set; }

    /// <summary>Versão que o dispositivo via quando o usuário fez a alteração.</summary>
    public int BaseVersion { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public int RetryCount { get; private set; }

    public DateTime? NextAttemptAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public OutboxItemStatus Status { get; private set; }

    public void MarkInFlight() => Status = OutboxItemStatus.InFlight;

    public void MarkDone() => Status = OutboxItemStatus.Done;

    /// <summary>
    /// Registra a falha e agenda a próxima tentativa com espera crescente.
    /// A mensagem é truncada: um erro longo do servidor não deve inchar o banco do aparelho.
    /// </summary>
    public void MarkFailed(string? error, DateTime nextAttemptAtUtc)
    {
        Status = OutboxItemStatus.Failed;
        RetryCount++;
        LastError = error is { Length: > 300 } ? error[..300] : error;
        NextAttemptAtUtc = nextAttemptAtUtc;
    }

    public void Reschedule() => Status = OutboxItemStatus.Pending;

    public bool IsReady(DateTime nowUtc) =>
        Status is OutboxItemStatus.Pending or OutboxItemStatus.InFlight
        || (Status == OutboxItemStatus.Failed && (NextAttemptAtUtc is null || nowUtc >= NextAttemptAtUtc));
}

/// <summary>Cursor e carimbos da sincronização. Uma linha só.</summary>
public sealed class SyncState
{
    public static readonly Guid SingletonId = new("11111111-0000-0000-0000-000000000001");

    public Guid Id { get; private set; } = SingletonId;

    public long Cursor { get; private set; }

    public DateTime? LastSyncAtUtc { get; private set; }

    public DateTime? LastSuccessfulServerContactUtc { get; private set; }

    public bool BootstrapCompleted { get; private set; }

    public void AdvanceCursor(long cursor, DateTime nowUtc)
    {
        if (cursor > Cursor)
        {
            Cursor = cursor;
        }

        LastSyncAtUtc = nowUtc;
        LastSuccessfulServerContactUtc = nowUtc;
    }

    public void MarkBootstrapped(long cursor, DateTime nowUtc)
    {
        Cursor = cursor;
        BootstrapCompleted = true;
        LastSyncAtUtc = nowUtc;
        LastSuccessfulServerContactUtc = nowUtc;
    }

    /// <summary>Volta o cursor ao início quando o servidor avisa que o log já foi podado.</summary>
    public void RequireBootstrap()
    {
        Cursor = 0;
        BootstrapCompleted = false;
    }
}

/// <summary>
/// Verificador local de senha para o acesso offline.
///
/// NÃO é o hash do ASP.NET Identity: aquele nunca sai do servidor. Este é derivado no próprio
/// aparelho, com sal aleatório, na primeira entrada online bem-sucedida. Ver docs/SECURITY.md.
/// </summary>
public sealed class LocalCredential
{
    private LocalCredential()
    {
        UserName = string.Empty;
        Salt = [];
        Verifier = [];
        PermissionsSnapshot = string.Empty;
        DisplayName = string.Empty;
    }

    public LocalCredential(
        Guid userId,
        string userName,
        string displayName,
        byte[] salt,
        byte[] verifier,
        int iterations,
        string permissionsSnapshot,
        DateTime validatedAtUtc)
    {
        UserId = userId;
        UserName = userName;
        DisplayName = displayName;
        Salt = salt;
        Verifier = verifier;
        Iterations = iterations;
        PermissionsSnapshot = permissionsSnapshot;
        LastServerValidationUtc = validatedAtUtc;
    }

    public Guid UserId { get; private set; }

    public string UserName { get; private set; }

    public string DisplayName { get; private set; }

    public byte[] Salt { get; private set; }

    public byte[] Verifier { get; private set; }

    public int Iterations { get; private set; }

    /// <summary>Permissões e setores em JSON, para o aplicativo funcionar offline.</summary>
    public string PermissionsSnapshot { get; private set; }

    /// <summary>Última vez que o servidor confirmou esta credencial. Base da validade offline.</summary>
    public DateTime LastServerValidationUtc { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTime? LockedUntilUtc { get; private set; }

    public void RefreshFromServer(string displayName, string permissionsSnapshot, DateTime nowUtc)
    {
        DisplayName = displayName;
        PermissionsSnapshot = permissionsSnapshot;
        LastServerValidationUtc = nowUtc;
        FailedAttempts = 0;
        LockedUntilUtc = null;
    }

    public void UpdateVerifier(byte[] salt, byte[] verifier, int iterations)
    {
        Salt = salt;
        Verifier = verifier;
        Iterations = iterations;
    }

    public void RegisterSuccess()
    {
        FailedAttempts = 0;
        LockedUntilUtc = null;
    }

    /// <summary>Bloqueia após tentativas seguidas, para o aparelho perdido não virar porta de entrada.</summary>
    public void RegisterFailure(int maxAttempts, DateTime nowUtc, TimeSpan lockDuration)
    {
        FailedAttempts++;

        if (FailedAttempts >= maxAttempts)
        {
            LockedUntilUtc = nowUtc.Add(lockDuration);
        }
    }

    public bool IsLocked(DateTime nowUtc) => LockedUntilUtc is { } until && nowUtc < until;

    public bool IsOfflineAccessValid(DateTime nowUtc, TimeSpan validity) =>
        nowUtc - LastServerValidationUtc <= validity;
}

/// <summary>Configuração e estado técnico do dispositivo, no próprio aparelho.</summary>
public sealed class DeviceState
{
    public static readonly Guid SingletonId = new("11111111-0000-0000-0000-000000000002");

    public Guid Id { get; private set; } = SingletonId;

    public Guid DeviceId { get; private set; } = Guid.CreateVersion7();

    public string DeviceName { get; private set; } = "Dispositivo";

    public string? ServerUrl { get; private set; }

    public Guid? CurrentSectorId { get; private set; }

    public bool NotificationsPermissionGranted { get; private set; }

    public bool? ExactAlarmPermissionGranted { get; private set; }

    public bool BatteryOptimizationIgnored { get; private set; }

    public NotificationHealth NotificationHealth { get; private set; } = NotificationHealth.Unknown;

    public DateTime? LastNotificationTestAtUtc { get; private set; }

    public void Configure(string serverUrl, string deviceName)
    {
        ServerUrl = serverUrl;
        DeviceName = deviceName;
    }

    public void ClearServer() => ServerUrl = null;

    public void SelectSector(Guid? sectorId) => CurrentSectorId = sectorId;

    public void UpdateNotificationState(
        bool permissionGranted,
        bool? exactAlarmGranted,
        bool batteryOptimizationIgnored,
        NotificationHealth health,
        DateTime? lastTestAtUtc)
    {
        NotificationsPermissionGranted = permissionGranted;
        ExactAlarmPermissionGranted = exactAlarmGranted;
        BatteryOptimizationIgnored = batteryOptimizationIgnored;
        NotificationHealth = health;
        LastNotificationTestAtUtc = lastTestAtUtc ?? LastNotificationTestAtUtc;
    }
}
