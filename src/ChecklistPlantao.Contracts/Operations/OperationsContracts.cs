using System.Text.Json.Serialization;

namespace ChecklistPlantao.Contracts.Operations;

public sealed record OperationalSessionDto(
    Guid Id,
    Guid SectorId,
    DateOnly ServiceDate,
    DateTime StartedAtUtc,
    DateTime? ClosedAtUtc,
    string Status,
    int Version);

public sealed record OpenSessionRequest(DateOnly? ServiceDate);

public sealed record ChecklistEntryDto(
    Guid Id,
    Guid SessionId,
    Guid BedId,
    Guid ChecklistTemplateId,
    Guid ChecklistColumnId,
    bool IsCompleted,
    int Version,
    DateTime UpdatedAtUtc);

public sealed record SessionBedMarkerDto(
    Guid Id,
    Guid SessionId,
    Guid BedId,
    Guid MarkerDefinitionId,
    bool IsSelected,
    int Version,
    DateTime UpdatedAtUtc);

/// <summary>Estado completo de uma sessão: o que o cliente precisa para desenhar todas as grades.</summary>
public sealed record SessionStateDto(
    OperationalSessionDto Session,
    IReadOnlyList<Guid> ActiveBedIds,
    IReadOnlyList<ChecklistEntryDto> Entries,
    IReadOnlyList<SessionBedMarkerDto> Markers);

public enum ProgressState
{
    NotStarted = 0,
    Partial = 1,
    Completed = 2,
}

public sealed record ProgressDto(int Total, int Completed)
{
    public int Pending => Total - Completed;

    [JsonIgnore]
    public ProgressState State => Completed <= 0 || Total <= 0
        ? ProgressState.NotStarted
        : Completed >= Total
            ? ProgressState.Completed
            : ProgressState.Partial;
}

public sealed record ColumnSummaryDto(Guid ColumnId, string ColumnName, TimeOnly? TriggerTime, ProgressDto Progress);

public sealed record TemplateSummaryDto(Guid TemplateId, string TemplateName, ProgressDto Progress, IReadOnlyList<ColumnSummaryDto> Columns);

public sealed record MarkerSummaryDto(Guid MarkerDefinitionId, string MarkerName, IReadOnlyList<string> BedCodes);

/// <summary>Resumo exibido na confirmação de fechamento da sessão.</summary>
public sealed record SessionSummaryDto(
    ProgressDto Overall,
    IReadOnlyList<TemplateSummaryDto> Templates,
    IReadOnlyList<MarkerSummaryDto> Markers)
{
    public bool HasPending => Overall.Pending > 0;
}

public sealed record CloseSessionRequest(bool ConfirmWithPending);

public sealed record UpdateChecklistEntryRequest(bool IsCompleted, int BaseVersion, Guid OperationId);

public sealed record UpdateBedMarkerRequest(bool IsSelected, int BaseVersion, Guid OperationId);
