using ChecklistPlantao.Domain.Structure;

namespace ChecklistPlantao.Domain.Scheduling;

public enum NotificationOccurrenceKind
{
    /// <summary>Aviso de antecedência, antes da hora da coluna.</summary>
    Lead = 0,

    /// <summary>Alerta na hora da coluna.</summary>
    Due = 1,

    /// <summary>Repetição após a tolerância, enquanto houver pendências.</summary>
    Reminder = 2,
}

/// <summary>
/// Um disparo previsto, em horário LOCAL. Quem agenda converte para o instante absoluto.
/// </summary>
public readonly record struct NotificationOccurrence(
    Guid ChecklistColumnId,
    Guid ChecklistTemplateId,
    DateOnly ServiceDate,
    DateTime FireAtLocal,
    NotificationOccurrenceKind Kind,
    int RepeatIndex);

/// <summary>
/// Calcula, sem qualquer dependência de plataforma, quando uma coluna deve alertar.
/// Toda a aritmética de horário do sistema passa por aqui — é o ponto que os testes cobrem.
/// </summary>
public static class NotificationPlanner
{
    /// <summary>
    /// Plano completo de uma coluna dentro de um plantão: antecedência (se houver),
    /// alerta na hora e as repetições após a tolerância.
    /// Retorna vazio para colunas que não podem ser agendadas.
    /// </summary>
    public static IReadOnlyList<NotificationOccurrence> Plan(ShiftWindow window, DateOnly serviceDate, ChecklistColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (!column.IsSchedulable)
        {
            return [];
        }

        var due = window.OccurrenceOf(serviceDate, column.TriggerTime!.Value);
        var occurrences = new List<NotificationOccurrence>(column.MaximumRepeats + 2);

        if (column.LeadTimeMinutes > 0)
        {
            occurrences.Add(Create(column, serviceDate, due.AddMinutes(-column.LeadTimeMinutes), NotificationOccurrenceKind.Lead, 0));
        }

        occurrences.Add(Create(column, serviceDate, due, NotificationOccurrenceKind.Due, 0));

        for (var i = 0; i < column.MaximumRepeats; i++)
        {
            var fireAt = due
                .AddMinutes(column.GracePeriodMinutes)
                .AddMinutes((double)column.RepeatIntervalMinutes * i);

            occurrences.Add(Create(column, serviceDate, fireAt, NotificationOccurrenceKind.Reminder, i + 1));
        }

        return occurrences;
    }

    /// <summary>
    /// Disparos ainda futuros a partir de <paramref name="fromLocal"/>, considerando o plantão atual
    /// e o seguinte. Dois plantões bastam: o horizonte de agendamento nunca precisa ir além do
    /// próximo turno, e recalcular a cada sincronização mantém tudo alinhado.
    /// </summary>
    public static IReadOnlyList<NotificationOccurrence> UpcomingFor(
        ShiftWindow window,
        DateTime fromLocal,
        IEnumerable<ChecklistColumn> columns,
        int shiftsAhead = 1)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentOutOfRangeException.ThrowIfNegative(shiftsAhead);

        var currentServiceDate = window.ServiceDateFor(fromLocal);
        var materialized = columns as IReadOnlyCollection<ChecklistColumn> ?? [.. columns];
        var result = new List<NotificationOccurrence>();

        for (var offset = 0; offset <= shiftsAhead; offset++)
        {
            var serviceDate = currentServiceDate.AddDays(offset);

            foreach (var column in materialized)
            {
                result.AddRange(Plan(window, serviceDate, column).Where(o => o.FireAtLocal > fromLocal));
            }
        }

        return [.. result.OrderBy(o => o.FireAtLocal)];
    }

    /// <summary>
    /// Momento a partir do qual a coluna é considerada atrasada: a hora dela mais a tolerância.
    /// Usado pelas faixas de "tarefas atrasadas" dentro do aplicativo.
    /// </summary>
    public static DateTime? OverdueThreshold(ShiftWindow window, DateOnly serviceDate, ChecklistColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (!column.IsActive || !column.TriggerTime.HasValue)
        {
            return null;
        }

        return window
            .OccurrenceOf(serviceDate, column.TriggerTime.Value)
            .AddMinutes(column.GracePeriodMinutes);
    }

    private static NotificationOccurrence Create(
        ChecklistColumn column,
        DateOnly serviceDate,
        DateTime fireAtLocal,
        NotificationOccurrenceKind kind,
        int repeatIndex) =>
        new(column.Id, column.ChecklistTemplateId, serviceDate, fireAtLocal, kind, repeatIndex);
}
