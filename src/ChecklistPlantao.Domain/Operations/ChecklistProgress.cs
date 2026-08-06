namespace ChecklistPlantao.Domain.Operations;

/// <summary>Contagem de concluídas e pendentes. Usado da célula ao resumo de fechamento.</summary>
public readonly record struct ChecklistProgress(int Total, int Completed)
{
    public static ChecklistProgress Empty => new(0, 0);

    public int Pending => Total - Completed;

    /// <summary>Fração concluída entre 0 e 1. Sem tarefas conta como completo, não como zero.</summary>
    public double Ratio => Total == 0 ? 1d : (double)Completed / Total;

    public int Percent => (int)Math.Round(Ratio * 100, MidpointRounding.AwayFromZero);

    public bool IsComplete => Pending == 0;

    public static ChecklistProgress operator +(ChecklistProgress left, ChecklistProgress right) =>
        new(left.Total + right.Total, left.Completed + right.Completed);

    public static ChecklistProgress Add(ChecklistProgress left, ChecklistProgress right) => left + right;

    public static ChecklistProgress From(IEnumerable<ChecklistEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var total = 0;
        var completed = 0;

        foreach (var entry in entries)
        {
            total++;
            if (entry.IsCompleted)
            {
                completed++;
            }
        }

        return new ChecklistProgress(total, completed);
    }
}
