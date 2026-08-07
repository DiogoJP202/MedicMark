namespace ChecklistPlantao.Client.Core.Services;

/// <summary>
/// Armazenamento seguro da plataforma (Keystore no Android, DPAPI no Windows).
///
/// É onde ficam os tokens. Nunca no banco local em texto puro.
/// </summary>
public interface ISecureStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Guarda e renova os tokens de acesso.</summary>
public interface ITokenStore
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string accessToken, DateTime accessExpiresAtUtc, string refreshToken, CancellationToken cancellationToken = default);

    Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Endereço do servidor configurado neste aparelho.</summary>
public interface IServerAddressProvider
{
    string? ServerUrl { get; }

    bool IsConfigured { get; }
}

/// <summary>
/// Conectividade em três níveis, como o enunciado exige distinguir.
///
/// "Tem internet" e "o servidor responde" são perguntas diferentes: numa instituição com o
/// servidor na própria rede, o sistema funciona perfeitamente sem internet nenhuma.
/// </summary>
public interface IConnectivityProbe
{
    /// <summary>Alguma interface de rede ativa.</summary>
    bool HasNetwork { get; }

    /// <summary>Internet externa disponível.</summary>
    bool HasInternet { get; }

    event Action? ConnectivityChanged;
}

/// <summary>Informações da plataforma para diagnóstico.</summary>
public interface IPlatformInfo
{
    string PlatformName { get; }

    string AppVersion { get; }
}
