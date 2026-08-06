using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Sessions;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Server.Auth;
using ChecklistPlantao.Server.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChecklistPlantao.Server.Controllers;

[Authorize]
[Route("api")]
public sealed class SessionsController(SessionService sessions, ICurrentUser currentUser, SyncNotifier notifier) : ApiControllerBase
{
    /// <summary>Sessão aberta do setor, criando uma automaticamente se a configuração permitir.</summary>
    [HttpGet("sectors/{sectorId:guid}/sessions/current")]
    [RequirePermission(Permissions.ChecklistView)]
    [ProducesResponseType<SessionStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CurrentAsync(Guid sectorId, [FromQuery] bool create = true, CancellationToken cancellationToken = default) =>
        FromResult(await sessions.GetCurrentAsync(sectorId, currentUser, create, cancellationToken));

    [HttpPost("sectors/{sectorId:guid}/sessions")]
    [RequirePermission(Permissions.ChecklistClose)]
    [ProducesResponseType<OperationalSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OpenAsync(
        Guid sectorId,
        [FromBody] OpenSessionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sessions.OpenAsync(sectorId, request?.ServiceDate, currentUser, cancellationToken);

        if (result.IsSuccess)
        {
            await notifier.NotifySessionUpdatedAsync(sectorId, cancellationToken);
        }

        return FromResult(result);
    }

    [HttpGet("sessions/{sessionId:guid}/summary")]
    [RequirePermission(Permissions.ChecklistView)]
    [ProducesResponseType<SessionSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SummaryAsync(Guid sessionId, CancellationToken cancellationToken) =>
        FromResult(await sessions.GetSummaryAsync(sessionId, currentUser, cancellationToken));

    /// <summary>
    /// Encerra o plantão. Com pendências, exige <c>ConfirmWithPending</c> — a confirmação é
    /// decisão de quem fecha, não do servidor.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/close")]
    [RequirePermission(Permissions.ChecklistClose)]
    [ProducesResponseType<SessionSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CloseAsync(
        Guid sessionId,
        [FromBody] CloseSessionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sessions.CloseAsync(sessionId, request?.ConfirmWithPending ?? false, currentUser, cancellationToken);

        if (result.IsSuccess)
        {
            await notifier.NotifySessionClosedAsync(sessionId, cancellationToken);
        }

        return FromResult(result);
    }

    [HttpPost("sessions/{sessionId:guid}/reset")]
    [RequirePermission(Permissions.ChecklistClose)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await sessions.ResetAsync(sessionId, currentUser, cancellationToken);

        if (result.IsSuccess)
        {
            await notifier.NotifySessionUpdatedAsync(null, cancellationToken);
        }

        return FromResult(result);
    }
}
