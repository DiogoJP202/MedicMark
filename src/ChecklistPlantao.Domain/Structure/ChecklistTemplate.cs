using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Structure;

/// <summary>
/// Tipo de checklist — "Gelo", "Glicemia", "SSVV". Cada um tem suas próprias colunas
/// e pode ser restrito a determinados setores.
/// </summary>
public sealed class ChecklistTemplate : ISyncVersioned
{
    private readonly List<ChecklistColumn> _columns = [];
    private readonly List<ChecklistTemplateSector> _sectors = [];

    private ChecklistTemplate()
    {
        Name = string.Empty;
        Code = string.Empty;
    }

    public ChecklistTemplate(Guid id, string name, string code, string? description, int sortOrder, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(name, "nome do tipo de checklist");
        DomainRuleException.ThrowIfNullOrWhiteSpace(code, "código do tipo de checklist");

        Id = id;
        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        Description = description?.Trim();
        SortOrder = sortOrder;
        IsActive = true;
        Version = 1;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Código estável usado em deep links e diagnósticos. Não é exibido ao usuário final.</summary>
    public string Code { get; private set; }

    public string? Description { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<ChecklistColumn> Columns => _columns;

    public IReadOnlyCollection<ChecklistTemplateSector> Sectors => _sectors;

    /// <summary>
    /// Quando não há setor associado, o template vale para todos — é o caso dos três templates
    /// iniciais, que a folha de papel usa em qualquer setor.
    /// </summary>
    public bool AppliesToAllSectors => _sectors.Count == 0;

    public IEnumerable<ChecklistColumn> ActiveColumnsInOrder =>
        _columns.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.DisplayName, StringComparer.Ordinal);

    public bool AppliesTo(Guid sectorId) => AppliesToAllSectors || _sectors.Any(s => s.SectorId == sectorId);

    public void Update(string name, string? description, int sortOrder, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(name, "nome do tipo de checklist");

        Name = name.Trim();
        Description = description?.Trim();
        SortOrder = sortOrder;
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

    public void ReplaceSectors(IEnumerable<Guid> sectorIds, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(sectorIds);

        var desired = sectorIds.Distinct().ToList();

        _sectors.RemoveAll(s => !desired.Contains(s.SectorId));

        foreach (var sectorId in desired.Where(id => _sectors.All(s => s.SectorId != id)))
        {
            _sectors.Add(new ChecklistTemplateSector(Id, sectorId));
        }

        Touch(nowUtc);
    }

    public ChecklistColumn AddColumn(Guid columnId, string displayName, TimeOnly? triggerTime, int sortOrder, DateTime nowUtc)
    {
        if (_columns.Any(c => c.IsActive && string.Equals(c.DisplayName, displayName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainRuleException($"Já existe uma coluna ativa chamada \"{displayName.Trim()}\" neste tipo de checklist.");
        }

        var column = new ChecklistColumn(columnId, Id, displayName, triggerTime, sortOrder, nowUtc);
        _columns.Add(column);
        Touch(nowUtc);
        return column;
    }

    private void Touch(DateTime nowUtc)
    {
        Version++;
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>Restringe um tipo de checklist a um setor.</summary>
public sealed class ChecklistTemplateSector
{
    private ChecklistTemplateSector()
    {
    }

    public ChecklistTemplateSector(Guid checklistTemplateId, Guid sectorId)
    {
        ChecklistTemplateId = checklistTemplateId;
        SectorId = sectorId;
    }

    public Guid ChecklistTemplateId { get; private set; }

    public Guid SectorId { get; private set; }
}
