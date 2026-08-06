using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Structure;

/// <summary>
/// Definição de um marcador opcional de leito — "C.I.", "Sondas", "Drenos" e o que o
/// administrador cadastrar depois. Nenhuma regra do sistema compara o nome: tudo é feito por Id.
/// </summary>
public sealed class BedMarkerDefinition : ISyncVersioned
{
    private BedMarkerDefinition()
    {
        Name = string.Empty;
        Code = string.Empty;
    }

    public BedMarkerDefinition(Guid id, string name, string code, int sortOrder, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(name, "nome do marcador");
        DomainRuleException.ThrowIfNullOrWhiteSpace(code, "código do marcador");

        Id = id;
        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        SortOrder = sortOrder;
        IsActive = true;
        Version = 1;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Code { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Update(string name, int sortOrder, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(name, "nome do marcador");

        Name = name.Trim();
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

    private void Touch(DateTime nowUtc)
    {
        Version++;
        UpdatedAtUtc = nowUtc;
    }
}
