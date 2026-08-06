using System.Collections.Frozen;

namespace ChecklistPlantao.Domain.Access;

/// <summary>
/// Resultado da união das permissões e dos setores de todos os grupos ativos de um usuário.
/// Um usuário pode pertencer a vários grupos e recebe a soma — nunca a interseção.
/// </summary>
public sealed class EffectiveAccess
{
    private readonly FrozenSet<string> _permissions;
    private readonly FrozenSet<Guid> _sectorIds;

    private EffectiveAccess(FrozenSet<string> permissions, FrozenSet<Guid> sectorIds, bool grantsAllSectors)
    {
        _permissions = permissions;
        _sectorIds = sectorIds;
        GrantsAllSectors = grantsAllSectors;
    }

    public static EffectiveAccess None { get; } = new(
        FrozenSet<string>.Empty,
        FrozenSet<Guid>.Empty,
        grantsAllSectors: false);

    /// <summary>
    /// Verdadeiro quando algum grupo concede acesso a todos os setores. Nesse caso setores criados
    /// depois ficam automaticamente visíveis, sem precisar reeditar o grupo.
    /// </summary>
    public bool GrantsAllSectors { get; }

    public IReadOnlySet<string> Permissions => _permissions;

    /// <summary>
    /// Setores explicitamente concedidos. Quando <see cref="GrantsAllSectors"/> é verdadeiro esta
    /// coleção pode estar vazia e ainda assim o usuário enxerga tudo — sempre consulte
    /// <see cref="CanAccessSector"/> em vez de ler esta lista diretamente.
    /// </summary>
    public IReadOnlySet<Guid> ExplicitSectorIds => _sectorIds;

    public static EffectiveAccess FromGroups(IEnumerable<AccessGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        var sectors = new HashSet<Guid>();
        var allSectors = false;

        foreach (var group in groups)
        {
            if (!group.IsActive)
            {
                continue;
            }

            foreach (var permission in group.PermissionKeys)
            {
                permissions.Add(permission);
            }

            foreach (var sectorId in group.SectorIds)
            {
                sectors.Add(sectorId);
            }

            allSectors |= group.GrantsAllSectors;
        }

        return new EffectiveAccess(
            permissions.ToFrozenSet(StringComparer.Ordinal),
            sectors.ToFrozenSet(),
            allSectors);
    }

    public bool Has(string permissionKey) => _permissions.Contains(permissionKey);

    public bool HasAny(params string[] permissionKeys) => permissionKeys.Any(Has);

    public bool CanAccessSector(Guid sectorId) => GrantsAllSectors || _sectorIds.Contains(sectorId);

    /// <summary>
    /// Filtra uma lista de setores pelo que o usuário realmente enxerga. Usado tanto pela interface
    /// quanto pelo servidor — a interface esconde, o servidor recusa.
    /// </summary>
    public IEnumerable<T> FilterSectors<T>(IEnumerable<T> sectors, Func<T, Guid> idSelector)
    {
        ArgumentNullException.ThrowIfNull(sectors);
        ArgumentNullException.ThrowIfNull(idSelector);

        return GrantsAllSectors ? sectors : sectors.Where(s => _sectorIds.Contains(idSelector(s)));
    }
}
