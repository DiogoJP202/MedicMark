using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Contracts.Sync;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Client.Core.Sync;

/// <summary>
/// Cliente HTTP do servidor.
///
/// Toda chamada é tolerante a falha: sem rede, sem servidor ou com erro, devolve nulo em vez de
/// lançar. Quem chama decide o que fazer — e a resposta correta quase sempre é "continua offline".
/// Falhar aqui nunca pode interromper o plantão.
/// </summary>
public sealed class HttpServerApi(
    IHttpClientFactory httpClientFactory,
    ITokenStore tokens,
    IServerAddressProvider address,
    ILogger<HttpServerApi> logger) : IServerApi
{
    public const string HttpClientName = "checklistplantao";

    private DateTime _lastSuccessUtc = DateTime.MinValue;
    private DateTime _lastFailureUtc = DateTime.MinValue;

    /// <summary>
    /// Otimista por padrão: sem tentativa recente falha, vale tentar. Um servidor que voltou
    /// não pode ficar inalcançável só porque falhou uma vez.
    /// </summary>
    public bool IsReachable => address.IsConfigured && _lastFailureUtc <= _lastSuccessUtc;

    public async Task<ServerProbeResponse?> ProbeAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            client.BaseAddress = new Uri(NormalizeUrl(url));
            client.Timeout = TimeSpan.FromSeconds(8);

            var resposta = await client.GetAsync("api/server-info", cancellationToken).ConfigureAwait(false);

            if (!resposta.IsSuccessStatusCode)
            {
                return null;
            }

            return await resposta.Content.ReadFromJsonAsync<ServerProbeResponse>(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Sonda do servidor falhou para {Url}.", url);
            return null;
        }
    }

    public Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login", request, authenticated: false, cancellationToken);

    public Task<BootstrapResponse?> BootstrapAsync(CancellationToken cancellationToken = default) =>
        SendAsync<BootstrapResponse>(HttpMethod.Get, "api/bootstrap", null, authenticated: true, cancellationToken);

    public Task<SyncPushResponse?> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SyncPushResponse>(HttpMethod.Post, "api/sync/push", request, authenticated: true, cancellationToken);

    public Task<SyncPullResponse?> PullAsync(long since, CancellationToken cancellationToken = default) =>
        SendAsync<SyncPullResponse>(HttpMethod.Get, $"api/sync/pull?since={since}", null, authenticated: true, cancellationToken);

    public Task<SessionStateDto?> GetCurrentSessionAsync(Guid sectorId, CancellationToken cancellationToken = default) =>
        SendAsync<SessionStateDto>(HttpMethod.Get, $"api/sectors/{sectorId}/sessions/current", null, authenticated: true, cancellationToken);

    public Task<SessionSummaryDto?> GetSummaryAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<SessionSummaryDto>(HttpMethod.Get, $"api/sessions/{sessionId}/summary", null, authenticated: true, cancellationToken);

    public async Task<bool> CloseSessionAsync(Guid sessionId, bool confirmWithPending, CancellationToken cancellationToken = default) =>
        await SendAsync<SessionSummaryDto>(
            HttpMethod.Post,
            $"api/sessions/{sessionId}/close",
            new CloseSessionRequest(confirmWithPending),
            authenticated: true,
            cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> ResetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var resposta = await SendRawAsync(HttpMethod.Post, $"api/sessions/{sessionId}/reset", null, true, cancellationToken).ConfigureAwait(false);
        return resposta?.IsSuccessStatusCode == true;
    }

    public Task<DeviceDto?> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<DeviceDto>(HttpMethod.Post, "api/devices/register", request, authenticated: true, cancellationToken);

    public Task<DeviceDto?> HeartbeatAsync(DeviceHeartbeatRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<DeviceDto>(HttpMethod.Post, "api/devices/heartbeat", request, authenticated: true, cancellationToken);

    /// <summary>Envia e desserializa. Devolve nulo em qualquer falha, registrando o motivo.</summary>
    public async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        bool authenticated,
        CancellationToken cancellationToken)
        where T : class
    {
        var resposta = await SendRawAsync(method, path, body, authenticated, cancellationToken).ConfigureAwait(false);

        if (resposta is null || !resposta.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            return await resposta.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Resposta inesperada em {Path}.", path);
            return null;
        }
    }

    public async Task<HttpResponseMessage?> SendRawAsync(
        HttpMethod method,
        string path,
        object? body,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        if (!address.IsConfigured)
        {
            return null;
        }

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            client.BaseAddress = new Uri(NormalizeUrl(address.ServerUrl!));

            using var request = new HttpRequestMessage(method, path);

            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: SyncJson.Options);
            }

            if (authenticated)
            {
                var token = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

                if (token is null)
                {
                    return null;
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var resposta = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (resposta.StatusCode == HttpStatusCode.Unauthorized && authenticated)
            {
                // Token vencido: renova uma vez e repete. Se falhar de novo, o usuário
                // continua trabalhando offline até reconectar.
                if (await tokens.TryRefreshAsync(cancellationToken).ConfigureAwait(false))
                {
                    return await SendRawAsync(method, path, body, authenticated: true, cancellationToken).ConfigureAwait(false);
                }
            }

            _lastSuccessUtc = DateTime.UtcNow;
            return resposta;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            _lastFailureUtc = DateTime.UtcNow;
            logger.LogDebug(ex, "Chamada a {Path} falhou. O aplicativo segue offline.", path);
            return null;
        }
    }

    /// <summary>Garante barra final e esquema, para o usuário poder digitar só "192.168.0.10:5000".</summary>
    public static string NormalizeUrl(string url)
    {
        var texto = url.Trim();

        if (!texto.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !texto.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            texto = "http://" + texto;
        }

        return texto.EndsWith('/') ? texto : texto + "/";
    }
}
