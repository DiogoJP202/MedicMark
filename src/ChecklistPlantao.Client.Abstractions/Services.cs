using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Domain.Access;

namespace ChecklistPlantao.Client.Abstractions;

/// <summary>
/// Contratos que a interface precisa. São declarados aqui, na camada de apresentação, e
/// implementados por <c>ChecklistPlantao.Client.Core</c>. Assim a RCL não conhece EF Core,
/// HttpClient nem MAUI, e pode ser testada com bUnit contra duplos de teste simples.
/// </summary>
public interface IAppSession
{
    bool IsAuthenticated { get; }

    string DisplayName { get; }

    EffectiveAccess Access { get; }

    /// <summary>Verdadeiro quando as permissões vêm de um snapshot antigo, sem sincronização recente.</summary>
    bool PermissionsAreStale { get; }

    DateTime? LastServerValidationUtc { get; }

    Guid? CurrentSectorId { get; }

    string? CurrentSectorName { get; }

    event Action? Changed;

    Task<IReadOnlyList<SectorSummary>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default);

    Task SelectSectorAsync(Guid sectorId, CancellationToken cancellationToken = default);

    Task<bool> SignInAsync(string userName, string password, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>Motivo da última falha de entrada, já em linguagem para o usuário final.</summary>
    string? LastSignInError { get; }

    /// <summary>
    /// Detalhe técnico da última falha, quando houver. Exibido recolhido: quem está no plantão não
    /// precisa dele, e quem vai resolver o problema não consegue trabalhar sem ele.
    /// </summary>
    string? LastSignInErrorDetail => null;
}

/// <summary>Leitura e escrita do checklist. Escrever é sempre local primeiro.</summary>
public interface IChecklistStore
{
    event Action? Changed;

    Task<IReadOnlyList<ChecklistTemplateDto>> GetTemplatesAsync(Guid sectorId, CancellationToken cancellationToken = default);

    Task<CurrentSessionView?> GetCurrentSessionAsync(Guid sectorId, CancellationToken cancellationToken = default);

    Task<ChecklistBoard> GetBoardAsync(Guid sectorId, Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Alterna a célula. Grava localmente e enfileira para envio; não espera o servidor.
    /// A interface já atualizou antes de chamar — aqui só se confirma ou se reverte.
    /// </summary>
    Task<ToggleOutcome> ToggleCellAsync(ChecklistCell cell, bool isCompleted, CancellationToken cancellationToken = default);

    Task<BedMarkersView> GetBedMarkersAsync(Guid sessionId, Guid bedId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BedMarkersView>> GetAllBedMarkersAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<ToggleOutcome> ToggleMarkerAsync(Guid sessionId, Guid bedId, BedMarkerState marker, bool isSelected, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingGroup>> GetPendingAsync(Guid sectorId, CancellationToken cancellationToken = default);

    Task<SessionSummaryDto> GetSessionSummaryAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<bool> CloseSessionAsync(Guid sessionId, bool confirmWithPending, CancellationToken cancellationToken = default);

    Task<bool> ResetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

public interface ISyncStatusService
{
    SyncStatus Current { get; }

    event Action<SyncStatus>? Changed;

    /// <summary>Sincronização manual, disparada pelo usuário.</summary>
    Task SyncNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica de fato se o servidor responde e publica o resultado, sem sincronizar nada.
    ///
    /// Existe para a tela de entrada: antes do primeiro login nada dispara sincronização, e o
    /// estado inicial acabava sendo exibido como "Offline" sem que nada tivesse sido medido.
    /// </summary>
    Task RefreshConnectivityAsync(CancellationToken cancellationToken = default);
}

public interface INotificationStatusService
{
    NotificationStatus Current { get; }

    event Action<NotificationStatus>? Changed;

    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Dispara uma notificação de teste imediata.</summary>
    Task<bool> SendTestNotificationAsync(CancellationToken cancellationToken = default);

    /// <summary>Abre a tela do sistema operacional onde a permissão que falta pode ser concedida.</summary>
    Task OpenSystemSettingsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Endereço do servidor e identidade do dispositivo, editáveis na configuração inicial.</summary>
public interface IServerConfigurationService
{
    string? ServerUrl { get; }

    /// <summary>
    /// A distribuição oficial não revela o endereço salvo na interface. Isto é privacidade de
    /// apresentação, não uma tentativa de transformar o endereço público em segredo.
    /// </summary>
    bool IsServerAddressHidden => false;

    /// <summary>Impede que uma configuração de produção aceite transporte sem TLS.</summary>
    bool RequiresHttps => false;

    string DeviceName { get; }

    Guid DeviceId { get; }

    bool IsConfigured { get; }

    Task<ServerProbeResponse?> TestConnectionAsync(string url, CancellationToken cancellationToken = default);

    Task SaveAsync(string url, string deviceName, CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Diagnóstico exibido na tela "Estado do dispositivo".</summary>
public sealed record DeviceDiagnostics(
    string Platform,
    string AppVersion,
    string? ServerUrl,
    bool ServerReachable,
    DateTime? LastSyncAtUtc,
    int PendingOperations,
    string TimeZoneId,
    DateTime DeviceLocalTime,
    NotificationStatus Notifications);

public interface IDeviceDiagnosticsService
{
    Task<DeviceDiagnostics> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Operações administrativas. Só funcionam com o servidor acessível.</summary>
/// <summary>
/// Cadastro de estrutura: setores, leitos, tipos de checklist, colunas e marcadores.
///
/// Separada de acessos e de sistema porque a tela que cadastra um leito não tem nada que ver com
/// redefinição de senha — e, num contrato só, dependia dela. Ver docs/DECISIONS.md (D-022).
/// </summary>
public interface IStructureAdminService
{
    Task<IReadOnlyList<SectorDto>> GetSectorsAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveSectorAsync(Guid? id, SaveSectorRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BedDto>> GetBedsAsync(Guid? sectorId, CancellationToken cancellationToken = default);

    Task<Result> SaveBedAsync(Guid? id, SaveBedRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChecklistTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveTemplateAsync(Guid? id, SaveTemplateRequest request, CancellationToken cancellationToken = default);

    Task<Result> SaveColumnAsync(Guid templateId, Guid? columnId, SaveColumnRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BedMarkerDefinitionDto>> GetMarkersAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveMarkerAsync(Guid? id, SaveMarkerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Grupos, usuários e permissões. A tela de acessos é a única que precisa disto.</summary>
public interface IAccessAdminService
{
    Task<IReadOnlyList<AccessGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveGroupAsync(Guid? id, SaveGroupRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppUserDto>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<Result> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuração do sistema: notificações, ajustes institucionais e a lista de dispositivos.
///
/// O nome é "sistema", e não "configurações", porque a lista de dispositivos é diagnóstico e não
/// ajuste — chamar de configurações seria mentir sobre o que há aqui dentro.
/// </summary>
public interface ISystemAdminService
{
    Task<NotificationConfigurationDto> GetNotificationConfigurationAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveNotificationConfigurationAsync(SaveNotificationConfigurationRequest request, CancellationToken cancellationToken = default);

    Task<InstitutionSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveSettingsAsync(SaveInstitutionSettingsRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resultado simples para a interface. Mensagem já em português; o código serve para a tela
/// decidir se pede atualização (conflito) ou apenas mostra o erro.
/// </summary>
public sealed record Result(bool Succeeded, string? Message = null, string? Code = null)
{
    public static Result Ok() => new(true);

    public static Result Fail(string message, string? code = null) => new(false, message, code);

    public bool IsVersionConflict => Code == Contracts.Common.ApiErrorCodes.VersionConflict;
}
