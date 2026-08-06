using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Common;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Common;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Common;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Application.Administration;

/// <summary>
/// Administração de usuários e grupos.
///
/// O usuário existe em duas tabelas: <c>Usuarios</c> (autorização, domínio) e <c>Credenciais</c>
/// (Identity). As duas são escritas na mesma unidade de trabalho — ver D-013 em docs/DECISIONS.md.
/// </summary>
public sealed class AccessAdminService(
    IAppDataContext db,
    IUserCredentialStore credentials,
    IClock clock,
    ILogger<AccessAdminService> logger)
{
    public async Task<IReadOnlyList<AccessGroupDto>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        var groups = await db.AccessGroups
            .AsNoTracking()
            .Include(g => g.Permissions)
            .Include(g => g.SectorAccesses)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. groups.Select(ConfigurationQueryService.Map)];
    }

    public async Task<Result<AccessGroupDto>> SaveGroupAsync(Guid? id, SaveGroupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        var name = request.Name.Trim();

        if (await db.AccessGroups.AnyAsync(g => g.Name == name && (id == null || g.Id != id), cancellationToken).ConfigureAwait(false))
        {
            return OperationError.Duplicate($"Já existe um grupo chamado \"{name}\".");
        }

        AccessGroup group;

        if (id is null)
        {
            group = new AccessGroup(Guid.CreateVersion7(), name, request.Description, request.GrantsAllSectors, now);
            db.AccessGroups.Add(group);
        }
        else
        {
            var found = await db.AccessGroups
                .Include(g => g.Permissions)
                .Include(g => g.SectorAccesses)
                .FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (found is null)
            {
                return OperationError.NotFound("Grupo não encontrado.");
            }

            if (found.Version != request.BaseVersion)
            {
                return OperationError.Conflict("Outra pessoa alterou este grupo enquanto você editava. Atualize a tela e refaça a alteração.");
            }

            group = found;
            group.Rename(name, request.Description, now);
            group.SetActive(request.IsActive, now);
            group.SetGrantsAllSectors(request.GrantsAllSectors, now);
        }

        try
        {
            group.ReplacePermissions(request.Permissions, now);
        }
        catch (DomainRuleException ex)
        {
            return OperationError.Validation(ex.Message);
        }

        group.ReplaceSectors(request.SectorIds, now);

        db.AppendChange(SyncEntityTypes.AccessGroup, group.Id, id is null ? SyncChangeType.Created : SyncChangeType.Updated, group.Version, null, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Grupo {GroupId} salvo com {Permissoes} permissão(ões).", group.Id, request.Permissions.Count);

        return ConfigurationQueryService.Map(group);
    }

    public async Task<IReadOnlyList<AppUserDto>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await db.AppUsers
            .AsNoTracking()
            .Include(u => u.Groups)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. users.Select(ConfigurationQueryService.Map)];
    }

    public async Task<Result<AppUserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = ValidatePassword(request.Password);
        if (validation is not null)
        {
            return validation;
        }

        var normalized = AppUser.NormalizeUserName(request.UserName);

        if (await db.AppUsers.AnyAsync(u => u.UserName == normalized, cancellationToken).ConfigureAwait(false)
            || await credentials.ExistsAsync(normalized, cancellationToken).ConfigureAwait(false))
        {
            return OperationError.Duplicate($"Já existe um usuário \"{normalized}\".");
        }

        var now = clock.UtcNow;
        var userId = Guid.CreateVersion7();

        AppUser user;

        try
        {
            user = new AppUser(userId, request.UserName, request.DisplayName, now);
        }
        catch (DomainRuleException ex)
        {
            return OperationError.Validation(ex.Message);
        }

        user.ReplaceGroups(request.GroupIds, now);
        db.AppUsers.Add(user);

        await credentials.CreateAsync(userId, normalized, request.Password, cancellationToken).ConfigureAwait(false);

        db.AppendChange(SyncEntityTypes.AppUser, user.Id, SyncChangeType.Created, user.Version, null, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Usuário {UserId} criado.", user.Id);

        return ConfigurationQueryService.Map(user);
    }

    public async Task<Result<AppUserDto>> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await db.AppUsers
            .Include(u => u.Groups)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return OperationError.NotFound("Usuário não encontrado.");
        }

        if (user.Version != request.BaseVersion)
        {
            return OperationError.Conflict("Outra pessoa alterou este usuário enquanto você editava. Atualize a tela e refaça a alteração.");
        }

        var now = clock.UtcNow;

        try
        {
            user.Rename(request.DisplayName, now);
        }
        catch (DomainRuleException ex)
        {
            return OperationError.Validation(ex.Message);
        }

        user.SetActive(request.IsActive, now);
        user.ReplaceGroups(request.GroupIds, now);

        await credentials.SetEnabledAsync(userId, request.IsActive, cancellationToken).ConfigureAwait(false);

        db.AppendChange(SyncEntityTypes.AppUser, user.Id, SyncChangeType.Updated, user.Version, null, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ConfigurationQueryService.Map(user);
    }

    public async Task<Result> ResetPasswordAsync(Guid userId, ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = ValidatePassword(request.NewPassword);
        if (validation is not null)
        {
            return validation;
        }

        if (!await db.AppUsers.AnyAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false))
        {
            return OperationError.NotFound("Usuário não encontrado.");
        }

        await credentials.SetPasswordAsync(userId, request.NewPassword, cancellationToken).ConfigureAwait(false);

        // Nada da senha entra no log — só o fato de ter sido redefinida.
        logger.LogInformation("Senha do usuário {UserId} redefinida por um administrador.", userId);

        return Result.Success();
    }

    /// <summary>Permissões e setores efetivos de um usuário, pela união dos grupos ativos.</summary>
    public async Task<EffectiveAccess> GetEffectiveAccessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var groupIds = await db.UserGroups
            .AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (groupIds.Count == 0)
        {
            return EffectiveAccess.None;
        }

        var groups = await db.AccessGroups
            .AsNoTracking()
            .Include(g => g.Permissions)
            .Include(g => g.SectorAccesses)
            .Where(g => groupIds.Contains(g.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return EffectiveAccess.FromGroups(groups);
    }

    /// <summary>
    /// Regras mínimas de senha, alinhadas às opções do Identity configuradas no servidor.
    /// Mantidas aqui para que a mensagem chegue clara ao administrador antes de tentar gravar.
    /// </summary>
    public static OperationError? ValidatePassword(string password)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            errors.Add(new ValidationError("Password", "A senha precisa ter pelo menos 8 caracteres."));
        }

        if (!string.IsNullOrEmpty(password) && !password.Any(char.IsDigit))
        {
            errors.Add(new ValidationError("Password", "A senha precisa conter ao menos um número."));
        }

        if (!string.IsNullOrEmpty(password) && !password.Any(char.IsLetter))
        {
            errors.Add(new ValidationError("Password", "A senha precisa conter ao menos uma letra."));
        }

        return errors.Count == 0 ? null : OperationError.Validation("Senha fora das regras mínimas.", errors);
    }
}
