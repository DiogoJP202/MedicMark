using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Structure;

/// <summary>Setor da instituição, por exemplo "Oeste".</summary>
public sealed class Sector : ISyncVersioned
{
    private Sector()
    {
        Name = string.Empty;
    }

    public Sector(Guid id, string name, string? description, int sortOrder, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(name, "nome do setor");

        Id = id;
        Name = name.Trim();
        Description = description?.Trim();
        SortOrder = sortOrder;
        IsActive = true;
        Version = 1;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Início do plantão específico deste setor. Quando nulo vale a janela global configurada
    /// em <c>Turno:Inicio</c> / <c>Turno:Fim</c>.
    /// </summary>
    public TimeOnly? ShiftStart { get; private set; }

    public TimeOnly? ShiftEnd { get; private set; }

    public void Update(string name, string? description, int sortOrder, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(name, "nome do setor");

        Name = name.Trim();
        Description = description?.Trim();
        SortOrder = sortOrder;
        Touch(nowUtc);
    }

    public void SetShiftOverride(TimeOnly? start, TimeOnly? end, DateTime nowUtc)
    {
        if (start.HasValue != end.HasValue)
        {
            throw new DomainRuleException("Informe o início e o fim do plantão do setor, ou nenhum dos dois.");
        }

        if (start.HasValue && start == end)
        {
            throw new DomainRuleException("O início e o fim do plantão não podem ser iguais.");
        }

        ShiftStart = start;
        ShiftEnd = end;
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

    private void Touch(DateTime nowUtc)
    {
        Version++;
        UpdatedAtUtc = nowUtc;
    }
}
