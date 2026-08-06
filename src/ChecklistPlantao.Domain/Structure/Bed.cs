using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Structure;

/// <summary>
/// Leito identificado apenas por código, por exemplo "1148".
/// Não guarda nada sobre o paciente — por decisão explícita do cliente.
/// </summary>
public sealed class Bed : ISyncVersioned
{
    private Bed()
    {
        Code = string.Empty;
    }

    public Bed(Guid id, Guid sectorId, string code, string? description, int sortOrder, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(code, "código do leito");

        Id = id;
        SectorId = sectorId;
        Code = code.Trim();
        Description = description?.Trim();
        SortOrder = sortOrder;
        IsActive = true;
        Version = 1;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public Guid SectorId { get; private set; }

    public string Code { get; private set; }

    public string? Description { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Update(string code, string? description, int sortOrder, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(code, "código do leito");

        Code = code.Trim();
        Description = description?.Trim();
        SortOrder = sortOrder;
        Touch(nowUtc);
    }

    /// <summary>Move o leito para outro setor mantendo o histórico da sessão em aberto intacto.</summary>
    public void MoveTo(Guid sectorId, DateTime nowUtc)
    {
        if (SectorId == sectorId)
        {
            return;
        }

        SectorId = sectorId;
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
