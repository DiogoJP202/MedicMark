using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Administration;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Server.Auth;
using ChecklistPlantao.Server.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Server.Controllers.Admin;

/// <summary>Usuários, grupos, permissões e a associação entre eles.</summary>
[Authorize]
[Route("api/admin")]
public sealed class AdminAccessController(
    AccessAdminService access,
    TokenService tokens,
    IAppDataContext db,
    ICurrentUser currentUser,
    SyncNotifier notifier) : ApiControllerBase
{
    [HttpGet("permissions")]
    [RequirePermission(Permissions.AdminGroups)]
    [ProducesResponseType<IReadOnlyList<PermissionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPermissionsAsync(CancellationToken cancellationToken) =>
        Ok((await db.PermissionDefinitions.AsNoTracking().OrderBy(p => p.Key).ToListAsync(cancellationToken))
            .Select(p => new PermissionDto(p.Key, p.Description)));

    [HttpGet("groups")]
    [RequirePermission(Permissions.AdminGroups)]
    public async Task<IActionResult> ListGroupsAsync(CancellationToken cancellationToken) =>
        Ok(await access.ListGroupsAsync(cancellationToken));

    [HttpPost("groups")]
    [RequirePermission(Permissions.AdminGroups)]
    public async Task<IActionResult> CreateGroupAsync([FromBody] SaveGroupRequest request, CancellationToken cancellationToken) =>
        FromResult(await access.SaveGroupAsync(null, request, cancellationToken));

    /// <summary>
    /// Altera um grupo. Como as permissões vivem no token, todos os membros têm as sessões
    /// revogadas: a mudança precisa valer imediatamente, não só quando o token expirar.
    /// </summary>
    [HttpPut("groups/{id:guid}")]
    [RequirePermission(Permissions.AdminGroups)]
    public async Task<IActionResult> UpdateGroupAsync(Guid id, [FromBody] SaveGroupRequest request, CancellationToken cancellationToken)
    {
        var result = await access.SaveGroupAsync(id, request, cancellationToken);

        if (result.IsSuccess)
        {
            await RevokeGroupMembersAsync(id, cancellationToken);
        }

        return FromResult(result);
    }

    [HttpGet("users")]
    [RequirePermission(Permissions.AdminUsers)]
    public async Task<IActionResult> ListUsersAsync(CancellationToken cancellationToken) =>
        Ok(await access.ListUsersAsync(cancellationToken));

    [HttpPost("users")]
    [RequirePermission(Permissions.AdminUsers)]
    public async Task<IActionResult> CreateUserAsync([FromBody] CreateUserRequest request, CancellationToken cancellationToken) =>
        FromResult(await access.CreateUserAsync(request, cancellationToken));

    [HttpPut("users/{id:guid}")]
    [RequirePermission(Permissions.AdminUsers)]
    public async Task<IActionResult> UpdateUserAsync(Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await access.UpdateUserAsync(id, request, cancellationToken);

        if (result.IsSuccess)
        {
            await tokens.RevokeAllForUserAsync(id, cancellationToken);
            await notifier.NotifyPermissionsChangedAsync(id, cancellationToken);
        }

        return FromResult(result);
    }

    [HttpPost("users/{id:guid}/password")]
    [RequirePermission(Permissions.AdminUsers)]
    public async Task<IActionResult> ResetPasswordAsync(Guid id, [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await access.ResetPasswordAsync(id, request, cancellationToken);

        if (result.IsSuccess)
        {
            await tokens.RevokeAllForUserAsync(id, cancellationToken);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Diagnóstico: mostra o acesso efetivo de um usuário. Responde à pergunta mais comum do
    /// administrador — "por que fulano não consegue ver o setor tal?".
    /// </summary>
    [HttpGet("users/{id:guid}/effective-access")]
    [RequirePermission(Permissions.AdminUsers)]
    public async Task<IActionResult> EffectiveAccessAsync(Guid id, CancellationToken cancellationToken)
    {
        var effective = await access.GetEffectiveAccessAsync(id, cancellationToken);

        return Ok(new
        {
            permissoes = effective.Permissions.Order(StringComparer.Ordinal),
            setores = effective.ExplicitSectorIds,
            todosOsSetores = effective.GrantsAllSectors,
            consultadoPor = currentUser.UserName,
        });
    }

    private async Task RevokeGroupMembersAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var memberIds = await db.UserGroups
            .AsNoTracking()
            .Where(ug => ug.GroupId == groupId)
            .Select(ug => ug.UserId)
            .ToListAsync(cancellationToken);

        foreach (var memberId in memberIds)
        {
            await tokens.RevokeAllForUserAsync(memberId, cancellationToken);
            await notifier.NotifyPermissionsChangedAsync(memberId, cancellationToken);
        }
    }
}
