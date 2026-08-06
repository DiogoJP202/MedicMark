namespace ChecklistPlantao.Domain.Access;

/// <summary>Associação entre grupo e chave de permissão.</summary>
public sealed class GroupPermission
{
    private GroupPermission()
    {
        PermissionKey = string.Empty;
    }

    public GroupPermission(Guid groupId, string permissionKey)
    {
        GroupId = groupId;
        PermissionKey = permissionKey;
    }

    public Guid GroupId { get; private set; }

    public string PermissionKey { get; private set; }
}

/// <summary>Associação entre grupo e setor autorizado.</summary>
public sealed class GroupSectorAccess
{
    private GroupSectorAccess()
    {
    }

    public GroupSectorAccess(Guid groupId, Guid sectorId)
    {
        GroupId = groupId;
        SectorId = sectorId;
    }

    public Guid GroupId { get; private set; }

    public Guid SectorId { get; private set; }
}

/// <summary>Associação entre usuário e grupo.</summary>
public sealed class UserGroup
{
    private UserGroup()
    {
    }

    public UserGroup(Guid userId, Guid groupId)
    {
        UserId = userId;
        GroupId = groupId;
    }

    public Guid UserId { get; private set; }

    public Guid GroupId { get; private set; }
}

/// <summary>
/// Definição persistida de uma permissão. Existe para que o painel administrativo liste as
/// permissões vindas do servidor em vez de depender de uma constante compilada no cliente.
/// </summary>
public sealed class PermissionDefinition
{
    private PermissionDefinition()
    {
        Key = string.Empty;
        Description = string.Empty;
    }

    public PermissionDefinition(string key, string description)
    {
        Key = key;
        Description = description;
    }

    public string Key { get; private set; }

    public string Description { get; private set; }
}
