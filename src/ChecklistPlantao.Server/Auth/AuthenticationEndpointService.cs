using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Administration;
using ChecklistPlantao.Application.Common;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Contracts.Common;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Server.Auth;

/// <summary>
/// Login, refresh e logout.
///
/// A resposta a credenciais inválidas é sempre a mesma, independentemente de o usuário existir:
/// não damos ao atacante um oráculo de nomes de usuário.
/// </summary>
public sealed class AuthenticationEndpointService(
    AppDbContext db,
    IUserCredentialStore credentials,
    AccessAdminService accessAdmin,
    TokenService tokens,
    ILogger<AuthenticationEndpointService> logger)
{
    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return InvalidCredentials();
        }

        var normalized = AppUser.NormalizeUserName(request.UserName);
        var check = await credentials.CheckPasswordAsync(normalized, request.Password, cancellationToken).ConfigureAwait(false);

        switch (check)
        {
            case CredentialCheckResult.LockedOut:
                logger.LogWarning("Tentativa de login em conta bloqueada: {UserName}.", normalized);
                return new OperationError(
                    ApiErrorCodes.AccountLocked,
                    "Esta conta está temporariamente bloqueada por excesso de tentativas. Aguarde alguns minutos.");

            case CredentialCheckResult.Success:
                break;

            default:
                // Nunca registramos a senha nem distinguimos o motivo para o cliente.
                logger.LogInformation("Falha de autenticação para {UserName}.", normalized);
                return InvalidCredentials();
        }

        var user = await db.AppUsers
            .Include(u => u.Groups)
            .FirstOrDefaultAsync(u => u.UserName == normalized, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            logger.LogError("Credencial {UserName} existe sem usuário de domínio correspondente.", normalized);
            return InvalidCredentials();
        }

        if (!user.IsActive)
        {
            return new OperationError(ApiErrorCodes.AccountInactive, "Este usuário está desativado. Procure o administrador.");
        }

        var access = await accessAdmin.GetEffectiveAccessAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var pair = await tokens
            .IssueAsync(user.Id, user.UserName, user.DisplayName, access, request.DeviceId, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Login concluído para {UserId} no dispositivo {DeviceId}.", user.Id, request.DeviceId ?? "(não informado)");

        return new LoginResponse(pair, Describe(user, access));
    }

    public async Task<Result<LoginResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = await tokens.ConsumeRefreshTokenAsync(request.RefreshToken, cancellationToken).ConfigureAwait(false);

        if (userId is null)
        {
            return new OperationError(ApiErrorCodes.TokenInvalid, "Sua sessão expirou. Entre novamente.");
        }

        var user = await db.AppUsers
            .Include(u => u.Groups)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || !user.IsActive)
        {
            await tokens.RevokeAllForUserAsync(userId.Value, cancellationToken).ConfigureAwait(false);
            return new OperationError(ApiErrorCodes.AccountInactive, "Este usuário está desativado. Procure o administrador.");
        }

        var access = await accessAdmin.GetEffectiveAccessAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var pair = await tokens
            .IssueAsync(user.Id, user.UserName, user.DisplayName, access, request.DeviceId, cancellationToken)
            .ConfigureAwait(false);

        return new LoginResponse(pair, Describe(user, access));
    }

    public Task<bool> LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return tokens.RevokeAsync(request.RefreshToken, cancellationToken);
    }

    public async Task<Result<AuthenticatedUserDto>> DescribeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers
            .AsNoTracking()
            .Include(u => u.Groups)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return OperationError.NotFound("Usuário não encontrado.");
        }

        var access = await accessAdmin.GetEffectiveAccessAsync(userId, cancellationToken).ConfigureAwait(false);
        return Describe(user, access);
    }

    private static AuthenticatedUserDto Describe(AppUser user, EffectiveAccess access) => new(
        user.Id,
        user.UserName,
        user.DisplayName,
        [.. access.Permissions],
        access.GrantsAllSectors,
        [.. access.ExplicitSectorIds],
        [.. user.GroupIds]);

    private static OperationError InvalidCredentials() =>
        new(ApiErrorCodes.InvalidCredentials, "Usuário ou senha inválidos.");
}
