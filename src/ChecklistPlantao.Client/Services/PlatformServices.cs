using ChecklistPlantao.Client.Core.Services;

namespace ChecklistPlantao.Client.Services;

/// <summary>
/// Armazenamento seguro sobre o <c>SecureStorage</c> do MAUI: Keystore no Android e DPAPI no
/// Windows. É onde ficam os tokens — nunca no banco local em texto puro.
/// </summary>
public sealed class MauiSecureStore : ISecureStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Alguns aparelhos com Keystore corrompido lançam aqui. Tratar como "não existe"
            // é melhor do que impedir o login: o usuário só precisará entrar de novo.
            return null;
        }
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
        SecureStorage.Default.SetAsync(key, value);

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Conectividade em três níveis.
///
/// O MAUI informa se há rede e se o acesso é à internet. "O servidor responde" é uma terceira
/// pergunta, respondida pelo próprio cliente HTTP — numa instituição com servidor local, não ter
/// internet não impede nada.
/// </summary>
public sealed class MauiConnectivityProbe : IConnectivityProbe, IDisposable
{
    public MauiConnectivityProbe() =>
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

    public event Action? ConnectivityChanged;

    public bool HasNetwork => Connectivity.Current.NetworkAccess is not (NetworkAccess.None or NetworkAccess.Unknown);

    public bool HasInternet => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) => ConnectivityChanged?.Invoke();

    public void Dispose() => Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
}

public sealed class MauiPlatformInfo : IPlatformInfo
{
    public string PlatformName => $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}";

    public string AppVersion => AppInfo.Current.VersionString;
}
