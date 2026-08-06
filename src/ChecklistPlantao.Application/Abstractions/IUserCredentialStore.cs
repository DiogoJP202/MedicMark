namespace ChecklistPlantao.Application.Abstractions;

public enum CredentialCheckResult
{
    Success = 0,
    InvalidCredentials = 1,
    LockedOut = 2,
    NotFound = 3,
}

/// <summary>
/// Credenciais e bloqueio por tentativas. Implementado com ASP.NET Identity na infraestrutura —
/// esta interface existe para que os casos de uso não conheçam <c>UserManager</c> nem o hash.
/// O hash NUNCA é exposto: não há método para lê-lo.
/// </summary>
public interface IUserCredentialStore
{
    Task<bool> ExistsAsync(string userName, CancellationToken cancellationToken = default);

    Task CreateAsync(Guid userId, string userName, string password, CancellationToken cancellationToken = default);

    Task SetPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    /// <summary>Valida a senha aplicando contagem de tentativas e bloqueio temporário.</summary>
    Task<CredentialCheckResult> CheckPasswordAsync(string userName, string password, CancellationToken cancellationToken = default);

    Task<Guid?> FindIdByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    Task SetEnabledAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
