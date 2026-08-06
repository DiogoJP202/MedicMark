using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Infrastructure.Identity;

/// <summary>
/// Credenciais sobre ASP.NET Identity: hash PBKDF2 do próprio framework, contagem de tentativas
/// e bloqueio temporário.
///
/// O hash não é exposto por nenhum método — nem para o cliente, nem para a camada de aplicação.
/// O acesso offline usa um verificador separado, derivado no dispositivo (ver docs/SECURITY.md).
/// </summary>
public sealed class IdentityUserCredentialStore(
    UserManager<AppIdentityUser> userManager,
    ILogger<IdentityUserCredentialStore> logger) : IUserCredentialStore
{
    public async Task<bool> ExistsAsync(string userName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await userManager.FindByNameAsync(userName).ConfigureAwait(false) is not null;
    }

    public async Task CreateAsync(Guid userId, string userName, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new AppIdentityUser
        {
            Id = userId,
            UserName = userName,
            LockoutEnabled = true,
        };

        var result = await userManager.CreateAsync(user, password).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(Describe(result));
        }
    }

    public async Task SetPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Credencial não encontrada para este usuário.");

        var token = await userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        var result = await userManager.ResetPasswordAsync(user, token, password).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(Describe(result));
        }

        // Invalida sessões existentes deste usuário: trocar a senha precisa expulsar quem estava dentro.
        await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false);
    }

    public async Task<CredentialCheckResult> CheckPasswordAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByNameAsync(userName).ConfigureAwait(false);

        if (user is null)
        {
            // Não distinguimos "usuário inexistente" de "senha errada" na resposta ao cliente;
            // o valor de retorno serve apenas para o log técnico do servidor.
            return CredentialCheckResult.NotFound;
        }

        if (await userManager.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            return CredentialCheckResult.LockedOut;
        }

        if (await userManager.CheckPasswordAsync(user, password).ConfigureAwait(false))
        {
            await userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
            return CredentialCheckResult.Success;
        }

        await userManager.AccessFailedAsync(user).ConfigureAwait(false);

        if (await userManager.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            logger.LogWarning("Conta bloqueada por excesso de tentativas: {UserId}.", user.Id);
            return CredentialCheckResult.LockedOut;
        }

        return CredentialCheckResult.InvalidCredentials;
    }

    public async Task<Guid?> FindIdByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByNameAsync(userName).ConfigureAwait(false);
        return user?.Id;
    }

    /// <summary>
    /// Desativar um usuário aplica um bloqueio de longuíssima duração e troca o security stamp,
    /// derrubando os tokens já emitidos. Ainda assim, um aparelho totalmente offline só descobre
    /// a desativação quando voltar a sincronizar — limitação registrada em KNOWN_LIMITATIONS.
    /// </summary>
    public async Task SetEnabledAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

        if (user is null)
        {
            return;
        }

        await userManager.SetLockoutEnabledAsync(user, true).ConfigureAwait(false);
        await userManager.SetLockoutEndDateAsync(user, enabled ? null : DateTimeOffset.MaxValue).ConfigureAwait(false);
        await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

        if (user is not null)
        {
            await userManager.DeleteAsync(user).ConfigureAwait(false);
        }
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));
}
