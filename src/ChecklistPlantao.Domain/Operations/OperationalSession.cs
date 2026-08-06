using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Operations;

public enum SessionStatus
{
    Open = 0,
    Closed = 1,
}

/// <summary>
/// Sessão operacional: um plantão de um setor. É o container de tudo que é temporário —
/// marcações e classificações de leito — e o que a política de retenção apaga depois de fechado.
/// </summary>
public sealed class OperationalSession : ISyncVersioned
{
    private readonly List<SessionBed> _beds = [];

    private OperationalSession()
    {
    }

    public OperationalSession(Guid id, Guid sectorId, DateOnly serviceDate, DateTime nowUtc)
    {
        Id = id;
        SectorId = sectorId;
        ServiceDate = serviceDate;
        StartedAtUtc = nowUtc;
        Status = SessionStatus.Open;
        Version = 1;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public Guid SectorId { get; private set; }

    /// <summary>Dia em que o plantão começou. Ver <see cref="Scheduling.ShiftWindow"/>.</summary>
    public DateOnly ServiceDate { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime? ClosedAtUtc { get; private set; }

    public SessionStatus Status { get; private set; }

    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<SessionBed> Beds => _beds;

    public bool IsOpen => Status == SessionStatus.Open;

    public void AddBed(Guid bedId)
    {
        if (_beds.Any(b => b.BedId == bedId))
        {
            return;
        }

        _beds.Add(new SessionBed(Id, bedId));
    }

    public void SetBedActive(Guid bedId, bool isActive, DateTime nowUtc)
    {
        var bed = _beds.FirstOrDefault(b => b.BedId == bedId)
            ?? throw new DomainRuleException("Este leito não faz parte da sessão atual.");

        bed.SetActive(isActive);
        Touch(nowUtc);
    }

    public void Close(DateTime nowUtc)
    {
        if (Status == SessionStatus.Closed)
        {
            throw new DomainRuleException("Esta sessão já está encerrada.");
        }

        Status = SessionStatus.Closed;
        ClosedAtUtc = nowUtc;
        Touch(nowUtc);
    }

    /// <summary>
    /// Reabre uma sessão fechada por engano, desde que ainda esteja dentro da janela de recuperação.
    /// Depois da retenção os dados já não existem e reabrir não faria sentido.
    /// </summary>
    public void Reopen(DateTime nowUtc)
    {
        if (Status == SessionStatus.Open)
        {
            return;
        }

        Status = SessionStatus.Open;
        ClosedAtUtc = null;
        Touch(nowUtc);
    }

    /// <summary>
    /// Marca a sessão como reiniciada. Apagar as marcações é responsabilidade de quem tem acesso
    /// ao repositório; aqui só se registra a nova versão para que os dispositivos ressincronizem.
    /// </summary>
    public void MarkReset(DateTime nowUtc) => Touch(nowUtc);

    private void Touch(DateTime nowUtc)
    {
        Version++;
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>Leito participando de uma sessão. Permite tirar um leito do plantão sem desativá-lo no cadastro.</summary>
public sealed class SessionBed
{
    private SessionBed()
    {
    }

    public SessionBed(Guid sessionId, Guid bedId)
    {
        SessionId = sessionId;
        BedId = bedId;
        IsActiveInSession = true;
    }

    public Guid SessionId { get; private set; }

    public Guid BedId { get; private set; }

    public bool IsActiveInSession { get; private set; }

    internal void SetActive(bool isActive) => IsActiveInSession = isActive;
}
