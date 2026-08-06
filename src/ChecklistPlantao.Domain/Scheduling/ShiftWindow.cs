using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Scheduling;

/// <summary>
/// Janela do plantão, por exemplo 19:00 → 07:00. É o que permite decidir a que dia pertence
/// a coluna "02H" de um checklist que atravessa a meia-noite.
///
/// Todas as datas e horas aqui são LOCAIS (fuso da instituição). A conversão para UTC acontece
/// nas bordas — persistência e agendamento —, nunca dentro do domínio.
/// </summary>
public readonly record struct ShiftWindow
{
    public ShiftWindow(TimeOnly start, TimeOnly end)
    {
        if (start == end)
        {
            throw new DomainRuleException("O início e o fim do plantão não podem ser iguais.");
        }

        Start = start;
        End = end;
    }

    /// <summary>Plantão noturno padrão, usado quando nada foi configurado.</summary>
    public static ShiftWindow Default { get; } = new(new TimeOnly(19, 0), new TimeOnly(7, 0));

    public TimeOnly Start { get; }

    public TimeOnly End { get; }

    /// <summary>Verdadeiro quando o plantão termina no dia seguinte ao que começou.</summary>
    public bool CrossesMidnight => End <= Start;

    /// <summary>Duração total do plantão.</summary>
    public TimeSpan Duration => CrossesMidnight
        ? TimeSpan.FromDays(1) - (Start - End)
        : End - Start;

    /// <summary>
    /// Data de serviço (o dia em que o plantão começou) para um instante local qualquer.
    ///
    /// Num plantão que atravessa a meia-noite, tudo que acontece antes do horário de início
    /// pertence ao plantão iniciado no dia anterior — inclusive as 02:00 da madrugada.
    /// </summary>
    public DateOnly ServiceDateFor(DateTime localDateTime)
    {
        var date = DateOnly.FromDateTime(localDateTime);

        if (!CrossesMidnight)
        {
            return date;
        }

        return TimeOnly.FromDateTime(localDateTime) < Start ? date.AddDays(-1) : date;
    }

    /// <summary>Instante local em que o plantão de <paramref name="serviceDate"/> começa.</summary>
    public DateTime StartOf(DateOnly serviceDate) => serviceDate.ToDateTime(Start);

    /// <summary>Instante local em que o plantão de <paramref name="serviceDate"/> termina.</summary>
    public DateTime EndOf(DateOnly serviceDate) =>
        CrossesMidnight ? serviceDate.AddDays(1).ToDateTime(End) : serviceDate.ToDateTime(End);

    /// <summary>
    /// Resolve a ocorrência local de uma coluna dentro do plantão informado.
    /// Com a janela 19:00 → 07:00: "20H" cai no próprio dia, "02H" cai no dia seguinte.
    /// </summary>
    public DateTime OccurrenceOf(DateOnly serviceDate, TimeOnly triggerTime)
    {
        if (!CrossesMidnight)
        {
            return serviceDate.ToDateTime(triggerTime);
        }

        return triggerTime >= Start
            ? serviceDate.ToDateTime(triggerTime)
            : serviceDate.AddDays(1).ToDateTime(triggerTime);
    }

    /// <summary>
    /// Verdadeiro quando a hora informada cai dentro da janela do plantão. Uma coluna fora da
    /// janela continua funcionando, mas o painel administrativo avisa que ela não pertence ao turno.
    /// </summary>
    public bool Contains(TimeOnly time)
    {
        if (!CrossesMidnight)
        {
            return time >= Start && time <= End;
        }

        return time >= Start || time <= End;
    }

    public bool Contains(DateOnly serviceDate, DateTime localDateTime)
    {
        var start = StartOf(serviceDate);
        var end = EndOf(serviceDate);
        return localDateTime >= start && localDateTime <= end;
    }

    public override string ToString() => $"{Start:HH\\:mm} → {End:HH\\:mm}";
}
