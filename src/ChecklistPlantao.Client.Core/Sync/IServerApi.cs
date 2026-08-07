using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Contracts.Sync;

namespace ChecklistPlantao.Client.Core.Sync;

/// <summary>
/// Fachada do servidor vista pelo cliente.
///
/// Existe como interface para que o motor de sincronização — onde estão as regras que realmente
/// importam — possa ser testado sem rede, sem servidor e sem HTTP.
/// </summary>
public interface IServerApi
{
    /// <summary>Verdadeiro apenas quando o servidor respondeu recentemente.</summary>
    bool IsReachable { get; }

    Task<ServerProbeResponse?> ProbeAsync(string url, CancellationToken cancellationToken = default);

    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<BootstrapResponse?> BootstrapAsync(CancellationToken cancellationToken = default);

    Task<SyncPushResponse?> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default);

    Task<SyncPullResponse?> PullAsync(long since, CancellationToken cancellationToken = default);

    Task<SessionStateDto?> GetCurrentSessionAsync(Guid sectorId, CancellationToken cancellationToken = default);

    Task<SessionSummaryDto?> GetSummaryAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<bool> CloseSessionAsync(Guid sessionId, bool confirmWithPending, CancellationToken cancellationToken = default);

    Task<bool> ResetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<DeviceDto?> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default);

    Task<DeviceDto?> HeartbeatAsync(DeviceHeartbeatRequest request, CancellationToken cancellationToken = default);
}
