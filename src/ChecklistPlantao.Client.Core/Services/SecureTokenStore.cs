using System.Net.Http.Json;
using System.Text.Json;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Client.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UiResult = ChecklistPlantao.Client.Abstractions.Result;

namespace ChecklistPlantao.Client.Core.Services;

/// <summary>
/// Tokens no armazenamento seguro da plataforma.
/// O banco local nunca guarda token em texto puro — nem mesmo o de atualização.
/// </summary>
public sealed class SecureTokenStore(ISecureStore secure, IServiceProvider services, IClock clock, ILogger<SecureTokenStore> logger) : ITokenStore
{
    private const string AccessKey = "checklistplantao.access";
    private const string ExpiryKey = "checklistplantao.access.expiry";
    private const string RefreshKey = "checklistplantao.refresh";

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = await secure.GetAsync(AccessKey, cancellationToken).ConfigureAwait(false);

        if (token is null)
        {
            return null;
        }

        var expiraTexto = await secure.GetAsync(ExpiryKey, cancellationToken).ConfigureAwait(false);

        // Renova um pouco antes de vencer: evita perder a chamada por causa de segundos.
        if (DateTime.TryParse(expiraTexto, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expira)
            && clock.UtcNow >= expira.AddMinutes(-1))
        {
            return await TryRefreshAsync(cancellationToken).ConfigureAwait(false)
                ? await secure.GetAsync(AccessKey, cancellationToken).ConfigureAwait(false)
                : null;
        }

        return token;
    }

    public async Task SaveAsync(string accessToken, DateTime accessExpiresAtUtc, string refreshToken, CancellationToken cancellationToken = default)
    {
        await secure.SetAsync(AccessKey, accessToken, cancellationToken).ConfigureAwait(false);
        await secure.SetAsync(ExpiryKey, accessExpiresAtUtc.ToString("O"), cancellationToken).ConfigureAwait(false);
        await secure.SetAsync(RefreshKey, refreshToken, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default)
    {
        var refresh = await secure.GetAsync(RefreshKey, cancellationToken).ConfigureAwait(false);

        // IServerApi é resolvido aqui, e não no construtor, porque HttpServerApi depende de
        // ITokenStore para anexar o bearer — pedi-lo no construtor fecharia um ciclo que o
        // contêiner recusa a construir. A renovação em si é uma chamada NÃO autenticada, então
        // esta dependência só existe para reaproveitar o encanamento HTTP.
        if (refresh is null || services.GetRequiredService<IServerApi>() is not HttpServerApi http)
        {
            return false;
        }

        try
        {
            var resposta = await http
                .SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/refresh", new RefreshRequest(refresh, null), authenticated: false, cancellationToken)
                .ConfigureAwait(false);

            if (resposta is null)
            {
                return false;
            }

            await SaveAsync(resposta.Tokens.AccessToken, resposta.Tokens.AccessTokenExpiresAtUtc, resposta.Tokens.RefreshToken, cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Renovação de token falhou. O aplicativo segue offline.");
            return false;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await secure.RemoveAsync(AccessKey, cancellationToken).ConfigureAwait(false);
        await secure.RemoveAsync(ExpiryKey, cancellationToken).ConfigureAwait(false);
        await secure.RemoveAsync(RefreshKey, cancellationToken).ConfigureAwait(false);
    }
}
