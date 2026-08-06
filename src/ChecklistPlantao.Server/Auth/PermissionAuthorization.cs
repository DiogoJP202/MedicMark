using System.Security.Claims;
using ChecklistPlantao.Domain.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ChecklistPlantao.Server.Auth;

/// <summary>Tipos de claim usados no token.</summary>
public static class AppClaimTypes
{
    public const string Permission = "perm";
    public const string Sector = "sect";
    public const string AllSectors = "allsect";
    public const string DisplayName = "dname";
    public const string DeviceId = "dev";
}

/// <summary>Exige uma chave de permissão específica.</summary>
public sealed class PermissionRequirement(string permissionKey) : IAuthorizationRequirement
{
    public string PermissionKey { get; } = permissionKey;
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var granted = context.User.Claims.Any(c =>
            c.Type == AppClaimTypes.Permission &&
            string.Equals(c.Value, requirement.PermissionKey, StringComparison.Ordinal));

        if (granted)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Cria a política sob demanda a partir do nome <c>perm:chave</c>, evitando registrar
/// manualmente uma política por permissão — e evitando esquecer de registrar ao criar uma nova.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var existing = await base.GetPolicyAsync(policyName).ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var key = policyName[Prefix.Length..];

        if (!Permissions.IsKnown(key))
        {
            return null;
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(key))
            .Build();
    }
}

/// <summary>Atalho para decorar endpoints: <c>[RequirePermission(Permissions.AdminUsers)]</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permissionKey)
        : base(PermissionPolicyProvider.Prefix + permissionKey)
    {
        PermissionKey = permissionKey;
    }

    public string PermissionKey { get; }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
