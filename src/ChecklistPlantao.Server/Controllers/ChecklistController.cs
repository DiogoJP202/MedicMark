using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Checklist;
using ChecklistPlantao.Application.Sessions;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Server.Auth;
using ChecklistPlantao.Server.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Server.Controllers;

/// <summary>
/// Marcação direta, usada quando o dispositivo está online. O caminho offline usa
/// <c>/api/sync/push</c>, mas os dois convergem no mesmo <see cref="ChecklistMutationService"/>.
/// </summary>
[Authorize]
[Route("api")]
public sealed class ChecklistController(
    SessionService sessions,
    ChecklistMutationService mutations,
    IAppDataContext db,
    ICurrentUser currentUser,
    SyncNotifier notifier) : ApiControllerBase
{
    [HttpGet("sessions/{sessionId:guid}/checklists")]
    [RequirePermission(Permissions.ChecklistView)]
    [ProducesResponseType<SessionStateDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StateAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.OperationalSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return NotFound();
        }

        return FromResult(await sessions.GetCurrentAsync(session.SectorId, currentUser, createIfMissing: false, cancellationToken));
    }

    /// <summary>Marca ou desmarca uma célula. A resposta traz sempre o estado autoritativo.</summary>
    [HttpPut("sessions/{sessionId:guid}/beds/{bedId:guid}/templates/{templateId:guid}/columns/{columnId:guid}")]
    [RequirePermission(Permissions.ChecklistUpdate)]
    [ProducesResponseType<ChecklistEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateEntryAsync(
        Guid sessionId,
        Guid bedId,
        Guid templateId,
        Guid columnId,
        [FromBody] UpdateChecklistEntryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await mutations.ApplyEntryAsync(
            sessionId, bedId, templateId, columnId, request.IsCompleted, request.BaseVersion, currentUser, cancellationToken);

        if (result.IsFailure)
        {
            return Problem(result.Error!);
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifySessionUpdatedAsync(null, cancellationToken);

        var outcome = result.Required;

        // Conflito devolve 409 com o estado atual no corpo: o cliente adota e segue,
        // sem precisar de uma segunda chamada.
        return outcome.Status == Domain.Sync.SyncOperationStatus.Conflict
            ? Conflict(outcome.Entry)
            : Ok(outcome.Entry);
    }

    /// <summary>Liga ou desliga uma classificação (C.I., Sondas, Drenos) do leito na sessão.</summary>
    [HttpPut("sessions/{sessionId:guid}/beds/{bedId:guid}/markers/{markerId:guid}")]
    [RequirePermission(Permissions.ChecklistUpdate)]
    [ProducesResponseType<SessionBedMarkerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMarkerAsync(
        Guid sessionId,
        Guid bedId,
        Guid markerId,
        [FromBody] UpdateBedMarkerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await mutations.ApplyMarkerAsync(
            sessionId, bedId, markerId, request.IsSelected, request.BaseVersion, currentUser, cancellationToken);

        if (result.IsFailure)
        {
            return Problem(result.Error!);
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifySessionUpdatedAsync(null, cancellationToken);

        var outcome = result.Required;

        return outcome.Status == Domain.Sync.SyncOperationStatus.Conflict
            ? Conflict(outcome.Marker)
            : Ok(outcome.Marker);
    }
}
