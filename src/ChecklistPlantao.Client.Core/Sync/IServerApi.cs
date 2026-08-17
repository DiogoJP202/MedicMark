using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Contracts.Sync;

namespace ChecklistPlantao.Client.Core.Sync;

/// <summary>
/// Resultado de uma tentativa de entrada no servidor.
///
/// Distinguir "o servidor não respondeu" de "o servidor recusou" é essencial: no primeiro caso
/// cabe tentar o acesso offline; no segundo, NÃO — o servidor já deu a resposta, e mascará-la com
/// uma mensagem genérica de offline confunde o usuário. Foi exatamente o que acontecia com uma
/// conta bloqueada: o servidor dizia "bloqueada" e o aplicativo dizia "você nunca entrou aqui".
/// </summary>
public sealed record ServerLoginResult(
    bool ServerReached,
    LoginResponse? Response,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static ServerLoginResult Unreachable() => new(false, null);

    public static ServerLoginResult Success(LoginResponse response) => new(true, response);

    public static ServerLoginResult Refused(string? code, string? message) => new(true, null, code, message);

    /// <summary>O servidor respondeu e recusou. Não há por que tentar o caminho offline.</summary>
    public bool WasRefused => ServerReached && Response is null;
}

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

    Task<ServerLoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

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
