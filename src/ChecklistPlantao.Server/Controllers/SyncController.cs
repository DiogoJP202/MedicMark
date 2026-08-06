using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Application.Sync;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Server.Auth;
using ChecklistPlantao.Server.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChecklistPlantao.Server.Controllers;

[Authorize]
[Route("api")]
public sealed class SyncController(
    SyncService sync,
    ConfigurationQueryService configuration,
    ICurrentUser currentUser,
    SyncNotifier notifier) : ApiControllerBase
{
    /// <summary>
    /// Fotografia completa da configuração visível ao usuário. É o ponto de partida de um
    /// dispositivo novo e o caminho de recuperação quando o cursor ficou velho demais.
    /// </summary>
    [HttpGet("bootstrap")]
    [RequirePermission(Permissions.ChecklistView)]
    [ProducesResponseType<BootstrapResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BootstrapAsync(CancellationToken cancellationToken)
    {
        var cursor = await sync.CurrentCursorAsync(cancellationToken);
        return Ok(await configuration.GetBootstrapAsync(currentUser, cursor, cancellationToken));
    }

    /// <summary>
    /// Envia o lote de alterações locais e já devolve o que mudou no servidor —
    /// uma ida e volta por ciclo de sincronização.
    /// </summary>
    [HttpPost("sync/push")]
    [RequirePermission(Permissions.ChecklistUpdate)]
    [ProducesResponseType<SyncPushResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PushAsync([FromBody] SyncPushRequest request, CancellationToken cancellationToken)
    {
        var response = await sync.PushAsync(request, currentUser, cancellationToken);

        if (response.Results.Any(r => r.Status == nameof(Domain.Sync.SyncOperationStatus.Applied)))
        {
            await notifier.NotifyChangesAsync(null, response.Cursor, cancellationToken);
        }

        return Ok(response);
    }

    [HttpGet("sync/pull")]
    [RequirePermission(Permissions.ChecklistView)]
    [ProducesResponseType<SyncPullResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PullAsync([FromQuery] long since, CancellationToken cancellationToken) =>
        Ok(await sync.PullAsync(since, currentUser, cancellationToken));

    /// <summary>
    /// Push e pull em uma chamada só. Existe para o cliente móvel gastar uma conexão por ciclo
    /// em rede ruim; o comportamento é idêntico ao de <c>push</c>.
    /// </summary>
    [HttpPost("sync/batch")]
    [RequirePermission(Permissions.ChecklistUpdate)]
    [ProducesResponseType<SyncPushResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> BatchAsync([FromBody] SyncPushRequest request, CancellationToken cancellationToken) =>
        PushAsync(request, cancellationToken);
}
