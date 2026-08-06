using ChecklistPlantao.Domain.Structure;

namespace ChecklistPlantao.Domain.Tests;

/// <summary>Fábricas curtas para os testes não repetirem montagem de agregados.</summary>
internal static class TestData
{
    public static readonly DateTime NowUtc = new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);

    public static ChecklistTemplate Template(string name = "Gelo", string code = "GELO") =>
        new(Guid.CreateVersion7(), name, code, description: null, sortOrder: 10, NowUtc);

    /// <summary>Coluna já pronta para agendar: ativa, com horário e notificação ligada.</summary>
    public static ChecklistColumn Column(
        this ChecklistTemplate template,
        string displayName,
        TimeOnly? triggerTime,
        int sortOrder = 10) =>
        template.AddColumn(Guid.CreateVersion7(), displayName, triggerTime, sortOrder, NowUtc);

    public static ChecklistColumn WithNotification(
        this ChecklistColumn column,
        int leadTimeMinutes = 0,
        int gracePeriodMinutes = 15,
        int repeatIntervalMinutes = 10,
        int maximumRepeats = 3,
        bool enabled = true)
    {
        column.ConfigureNotification(
            enabled,
            leadTimeMinutes,
            gracePeriodMinutes,
            repeatIntervalMinutes,
            maximumRepeats,
            allowSnooze: true,
            snoozeMinutes: 5,
            NowUtc);

        return column;
    }
}
