using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Operations;

/// <summary>
/// Classificação opcional de um leito DENTRO da sessão atual — C.I., Sondas, Drenos e
/// quaisquer outros marcadores cadastrados. Não é atributo permanente do leito: some junto
/// com a sessão quando a retenção expira.
/// </summary>
public sealed class SessionBedMarker : ISyncVersioned
{
    private SessionBedMarker()
    {
    }

    public SessionBedMarker(
        Guid id,
        Guid sessionId,
        Guid bedId,
        Guid markerDefinitionId,
        bool isSelected,
        DateTime nowUtc)
    {
        Id = id;
        SessionId = sessionId;
        BedId = bedId;
        MarkerDefinitionId = markerDefinitionId;
        IsSelected = isSelected;
        Version = 1;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid BedId { get; private set; }

    public Guid MarkerDefinitionId { get; private set; }

    public bool IsSelected { get; private set; }

    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public bool SetSelected(bool isSelected, DateTime nowUtc)
    {
        if (IsSelected == isSelected)
        {
            return false;
        }

        IsSelected = isSelected;
        Version++;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    public void OverwriteFromServer(bool isSelected, int version, DateTime updatedAtUtc)
    {
        if (version <= 0)
        {
            throw new DomainRuleException("A versão vinda do servidor precisa ser positiva.");
        }

        IsSelected = isSelected;
        Version = version;
        UpdatedAtUtc = updatedAtUtc;
    }
}
