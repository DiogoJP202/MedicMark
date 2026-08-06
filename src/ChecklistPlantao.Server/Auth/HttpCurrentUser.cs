using System.Security.Claims;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Domain.Access;

namespace ChecklistPlantao.Server.Auth;

/// <summary>
/// <see cref="ICurrentUser"/> montado a partir das claims do JWT.
///
/// Permissões e setores vêm do token, e não de uma consulta por requisição — o token é curto,
/// então o atraso máximo entre revogar um acesso e ele deixar de valer é a validade do access token.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private EffectiveAccess? _access;

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId => Principal?.GetUserId() ?? Guid.Empty;

    public string UserName => Principal?.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    public string DisplayName => Principal?.FindFirstValue(AppClaimTypes.DisplayName) ?? UserName;

    public EffectiveAccess Access => _access ??= BuildAccess();

    /// <summary>
    /// Reconstrói o acesso efetivo a partir das claims. Usa um grupo sintético porque
    /// <see cref="EffectiveAccess"/> só sabe somar grupos — o token já traz a soma pronta.
    /// </summary>
    private EffectiveAccess BuildAccess()
    {
        var principal = Principal;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return EffectiveAccess.None;
        }

        var permissions = principal.FindAll(AppClaimTypes.Permission).Select(c => c.Value);
        var sectors = principal.FindAll(AppClaimTypes.Sector)
            .Select(c => Guid.TryParse(c.Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty);
        var allSectors = principal.HasClaim(AppClaimTypes.AllSectors, "true");

        var snapshot = new AccessGroup(Guid.Empty, "token", null, allSectors, DateTime.UnixEpoch);
        snapshot.ReplacePermissions(permissions.Where(Permissions.IsKnown), DateTime.UnixEpoch);
        snapshot.ReplaceSectors(sectors, DateTime.UnixEpoch);

        return EffectiveAccess.FromGroups([snapshot]);
    }
}
