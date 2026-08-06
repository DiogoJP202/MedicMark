using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChecklistPlantao.Server.Controllers;

[Route("api/auth")]
public sealed class AuthController(AuthenticationEndpointService auth) : ApiControllerBase
{
    /// <summary>
    /// Primeiro acesso de um dispositivo. Precisa de rede: só depois de um login online
    /// bem-sucedido o aparelho passa a aceitar entrada offline.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken) =>
        FromResult(await auth.LoginAsync(request, cancellationToken));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshAsync([FromBody] RefreshRequest request, CancellationToken cancellationToken) =>
        FromResult(await auth.RefreshAsync(request, cancellationToken));

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAsync([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Permissões e setores atuais do usuário autenticado.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<AuthenticatedUserDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MeAsync(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        return userId is null
            ? Unauthorized()
            : FromResult(await auth.DescribeAsync(userId.Value, cancellationToken));
    }
}
