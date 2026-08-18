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
        EnsureColumnNameIsFree(displayName, null);

        var column = new ChecklistColumn(columnId, Id, displayName, triggerTime, sortOrder, nowUtc);
        _columns.Add(column);
        Touch(nowUtc);
        return column;
    }

    /// <summary>
    /// Alteração de coluna existente.
    ///
    /// Existe para que a MESMA regra de nome único valha na edição e na criação. Sem isto, renomear
    /// uma coluna para o nome de outra ativa passava pelo domínio e só era barrado pelo índice
    /// único do banco — que não é erro tratado e virava HTTP 500. Ver docs/DECISIONS.md (D-016).
    /// </summary>
    public void UpdateColumn(
        ChecklistColumn column,
        string displayName,
        TimeOnly? triggerTime,
        int sortOrder,
        bool isActive,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (!_columns.Contains(column))
        {
            throw new DomainRuleException("A coluna não pertence a este tipo de checklist.");
        }

        // Só há colisão possível se a coluna terminar ativa: o índice único ignora as inativas.
        if (isActive)
        {
            EnsureColumnNameIsFree(displayName, column.Id);
        }

        column.Update(displayName, triggerTime, sortOrder, nowUtc);
        column.SetActive(isActive, nowUtc);
        Touch(nowUtc);
    }

    /// <summary>
    /// Espelha o índice único filtrado <c>(ChecklistTemplateId, DisplayName) WHERE IsActive = 1</c>.
    /// A comparação é sem diferenciar maiúsculas — mais estrita que a do SQLite de propósito, para
    /// não permitir "20h" ao lado de "20H".
    /// </summary>
    private void EnsureColumnNameIsFree(string displayName, Guid? ignoringColumnId)
    {
        var name = displayName.Trim();

        var taken = _columns.Any(c =>
            c.IsActive
            && c.Id != ignoringColumnId
            && string.Equals(c.DisplayName, name, StringComparison.OrdinalIgnoreCase));

        if (taken)
        {
            throw new DomainRuleException($"Já existe uma coluna ativa chamada \"{name}\" neste tipo de checklist.");
        }
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
