using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Access;

/// <summary>
/// Grupo de acesso. Um usuário pode pertencer a vários; suas permissões e setores são a união
/// de todos os grupos ativos aos quais ele pertence.
/// </summary>
public sealed class AccessGroup : ISyncVersioned
{
    private readonly List<GroupPermission> _permissions = [];
    private readonly List<GroupSectorAccess> _sectorAccesses = [];

    private AccessGroup()
    {
        Name = string.Empty;
    }

    public AccessGroup(Guid id, string name, string? description, bool grantsAllSectors, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(name, "nome do grupo");

        Id = id;
        Name = name.Trim();
        Description = description?.Trim();
        GrantsAllSectors = grantsAllSectors;
        IsActive = true;
        Version = 1;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Acesso a todos os setores, inclusive os criados depois. Evita que um setor novo fique
    /// invisível para os administradores até alguém lembrar de reeditar o grupo.
    /// </summary>
    public bool GrantsAllSectors { get; private set; }

    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<GroupPermission> Permissions => _permissions;

    public IReadOnlyCollection<GroupSectorAccess> SectorAccesses => _sectorAccesses;

    public IEnumerable<string> PermissionKeys => _permissions.Select(p => p.PermissionKey);

    public IEnumerable<Guid> SectorIds => _sectorAccesses.Select(s => s.SectorId);

    public void Rename(string name, string? description, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(name, "nome do grupo");

        Name = name.Trim();
        Description = description?.Trim();
        Touch(nowUtc);
    }

    public void SetActive(bool isActive, DateTime nowUtc)
    {
        if (IsActive == isActive)
        {
            return;
        }

        IsActive = isActive;
        Touch(nowUtc);
    }

    public void SetGrantsAllSectors(bool value, DateTime nowUtc)
    {
        if (GrantsAllSectors == value)
        {
            return;
        }

        GrantsAllSectors = value;
        Touch(nowUtc);
    }

    /// <summary>Substitui o conjunto de permissões. Chaves desconhecidas são recusadas.</summary>
    public void ReplacePermissions(IEnumerable<string> permissionKeys, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(permissionKeys);

        var desired = permissionKeys
            .Select(k => k?.Trim() ?? string.Empty)
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Qualificado porque a propriedade Permissions desta classe sombreia a classe estática.
        var unknown = desired.Where(k => !Access.Permissions.IsKnown(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new DomainRuleException($"Permissões desconhecidas: {string.Join(", ", unknown)}.");
        }

        _permissions.RemoveAll(p => !desired.Contains(p.PermissionKey, StringComparer.Ordinal));

        foreach (var key in desired.Where(k => !PermissionKeys.Contains(k, StringComparer.Ordinal)))
        {
            _permissions.Add(new GroupPermission(Id, key));
        }

        Touch(nowUtc);
    }

    public void ReplaceSectors(IEnumerable<Guid> sectorIds, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(sectorIds);

        var desired = sectorIds.Distinct().ToList();

        _sectorAccesses.RemoveAll(s => !desired.Contains(s.SectorId));

        foreach (var sectorId in desired.Where(id => !SectorIds.Contains(id)))
        {
            _sectorAccesses.Add(new GroupSectorAccess(Id, sectorId));
        }

        Touch(nowUtc);
    }

    private void Touch(DateTime nowUtc)
    {
        Version++;
        UpdatedAtUtc = nowUtc;
    }
}
