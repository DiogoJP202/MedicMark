using ChecklistPlantao.Domain.Structure;

namespace ChecklistPlantao.Domain.Operations;

public sealed record ColumnProgress(Guid ColumnId, string ColumnName, TimeOnly? TriggerTime, ChecklistProgress Progress);

public sealed record TemplateProgress(Guid TemplateId, string TemplateName, ChecklistProgress Progress, IReadOnlyList<ColumnProgress> Columns);

/// <summary>Leitos que receberam um determinado marcador na sessão — "os leitos com Sondas".</summary>
public sealed record MarkerBedGroup(Guid MarkerDefinitionId, string MarkerName, IReadOnlyList<string> BedCodes)
{
    public int Count => BedCodes.Count;
}

/// <summary>
/// Resumo exibido ao encerrar a sessão: quanto foi feito, quanto ficou pendente e quais leitos
/// estavam classificados como C.I., Sondas ou Drenos.
/// </summary>
public sealed record SessionSummary(
    ChecklistProgress Overall,
    IReadOnlyList<TemplateProgress> Templates,
    IReadOnlyList<MarkerBedGroup> Markers)
{
    public bool HasPending => Overall.Pending > 0;

    public static SessionSummary Empty { get; } = new(ChecklistProgress.Empty, [], []);
}

/// <summary>Monta o resumo a partir dos dados brutos da sessão. Cálculo puro, sem I/O.</summary>
public static class SessionSummaryCalculator
{
    public static SessionSummary Build(
        IEnumerable<ChecklistEntry> entries,
        IEnumerable<ChecklistTemplate> templates,
        IEnumerable<SessionBedMarker> markers,
        IEnumerable<BedMarkerDefinition> markerDefinitions,
        IReadOnlyDictionary<Guid, string> bedCodesById)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(markerDefinitions);
        ArgumentNullException.ThrowIfNull(bedCodesById);

        var entriesByColumn = entries
            .GroupBy(e => e.ChecklistColumnId)
            .ToDictionary(g => g.Key, g => ChecklistProgress.From(g));

        var templateProgress = new List<TemplateProgress>();
        var overall = ChecklistProgress.Empty;

        foreach (var template in templates.Where(t => t.IsActive).OrderBy(t => t.SortOrder))
        {
            var columns = new List<ColumnProgress>();
            var templateTotal = ChecklistProgress.Empty;

            foreach (var column in template.ActiveColumnsInOrder)
            {
                var progress = entriesByColumn.TryGetValue(column.Id, out var value) ? value : ChecklistProgress.Empty;
                columns.Add(new ColumnProgress(column.Id, column.DisplayName, column.TriggerTime, progress));
                templateTotal += progress;
            }

            if (templateTotal.Total == 0)
            {
                continue;
            }

            templateProgress.Add(new TemplateProgress(template.Id, template.Name, templateTotal, columns));
            overall += templateTotal;
        }

        var selectedMarkers = markers.Where(m => m.IsSelected).ToList();

        var markerGroups = markerDefinitions
            .Where(d => d.IsActive)
            .OrderBy(d => d.SortOrder)
            .Select(definition => new MarkerBedGroup(
                definition.Id,
                definition.Name,
                [.. selectedMarkers
                    .Where(m => m.MarkerDefinitionId == definition.Id)
                    .Select(m => bedCodesById.TryGetValue(m.BedId, out var code) ? code : null)
                    .Where(code => code is not null)
                    .Select(code => code!)
                    .Order(StringComparer.Ordinal)]))
            .ToList();

        return new SessionSummary(overall, templateProgress, markerGroups);
    }
}
