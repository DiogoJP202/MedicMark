using ChecklistPlantao.Client.Abstractions;

namespace ChecklistPlantao.UI.Components.Checklist;

/// <summary>
/// Filtro compartilhado entre a grade do desktop e a lista do celular.
///
/// Fica fora dos componentes para que as duas visualizações se comportem exatamente igual e
/// para que a regra possa ser testada sem renderizar nada.
/// </summary>
public static class ChecklistFilter
{
    public static IReadOnlyList<ChecklistRow> Apply(
        IReadOnlyList<ChecklistRow> rows,
        string? search,
        bool onlyPending,
        Guid? markerId = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        IEnumerable<ChecklistRow> resultado = rows;

        if (onlyPending)
        {
            resultado = resultado.Where(r => !r.IsComplete);
        }

        if (markerId is { } marcador)
        {
            resultado = resultado.Where(r => r.MarkerIds.Contains(marcador));
        }

        var termo = search?.Trim();

        if (!string.IsNullOrEmpty(termo))
        {
            resultado = resultado.Where(r => r.BedCode.Contains(termo, StringComparison.OrdinalIgnoreCase));
        }

        return [.. resultado];
    }

    /// <summary>Versão para a lista do celular, que trabalha com uma coluna por vez.</summary>
    public static IReadOnlyList<ChecklistRow> ApplyForColumn(
        IReadOnlyList<ChecklistRow> rows,
        Guid columnId,
        string? search,
        bool onlyPending,
        Guid? markerId = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        IEnumerable<ChecklistRow> resultado = rows;

        if (onlyPending)
        {
            resultado = resultado.Where(r => r.Cells.Any(c => c.ColumnId == columnId && !c.IsCompleted));
        }

        if (markerId is { } marcador)
        {
            resultado = resultado.Where(r => r.MarkerIds.Contains(marcador));
        }

        var termo = search?.Trim();

        if (!string.IsNullOrEmpty(termo))
        {
            resultado = resultado.Where(r => r.BedCode.Contains(termo, StringComparison.OrdinalIgnoreCase));
        }

        return [.. resultado];
    }
}
