using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Seeding;

public sealed record SeedSector(string Name, int SortOrder)
{
    public Guid Id => DeterministicGuid.From($"sector:{Name}");
}

public sealed record SeedBed(string SectorName, string Code, int SortOrder)
{
    public Guid Id => DeterministicGuid.From($"bed:{SectorName}:{Code}");
}

public sealed record SeedColumn(string TemplateCode, string DisplayName, TimeOnly? TriggerTime, int SortOrder)
{
    public Guid Id => DeterministicGuid.From($"column:{TemplateCode}:{DisplayName}");
}

public sealed record SeedTemplate(string Name, string Code, int SortOrder)
{
    public Guid Id => DeterministicGuid.From($"template:{Code}");
}

public sealed record SeedMarker(string Name, string Code, int SortOrder)
{
    public Guid Id => DeterministicGuid.From($"marker:{Code}");
}

public sealed record SeedGroup(string Name, string Description, bool GrantsAllSectors, IReadOnlyList<string> Permissions, IReadOnlyList<string> SectorNames)
{
    public Guid Id => DeterministicGuid.From($"group:{Name}");
}

/// <summary>
/// Dados iniciais, derivados da folha de papel em docs/reference/CHECKLIST-OESTE-PM.pdf.
///
/// Isto é SEMENTE, não configuração: tudo aqui é editável pelo administrador depois da primeira
/// execução, e nenhuma regra do sistema compara estes nomes. É o único lugar onde estes valores
/// aparecem — nunca replique um código de leito ou nome de coluna em outro ponto do código.
///
/// Os horários de Jantar, Café, PM e AM foram confirmados com o cliente; ver D-002 em
/// docs/DECISIONS.md.
/// </summary>
public static class SeedCatalog
{
    public const string OesteSectorName = "Oeste";

    public static IReadOnlyList<SeedSector> Sectors { get; } =
    [
        new(OesteSectorName, 10),
    ];

    /// <summary>Os 16 leitos da folha, na mesma ordem em que aparecem impressos.</summary>
    public static IReadOnlyList<SeedBed> Beds { get; } =
    [
        .. new[] { "1148", "1150", "1152", "1153", "1154", "1156", "1158", "1160", "1161", "1162", "1163", "1164", "1165", "1166", "1167", "1169" }
            .Select((code, index) => new SeedBed(OesteSectorName, code, (index + 1) * 10)),
    ];

    public static IReadOnlyList<SeedTemplate> Templates { get; } =
    [
        new("Gelo", "GELO", 10),
        new("Glicemia", "GLICEMIA", 20),
        new("SSVV", "SSVV", 30),
    ];

    public static IReadOnlyList<SeedColumn> Columns { get; } =
    [
        new("GELO", "20H", new TimeOnly(20, 0), 10),
        new("GELO", "22H", new TimeOnly(22, 0), 20),
        new("GELO", "00H", new TimeOnly(0, 0), 30),
        new("GELO", "02H", new TimeOnly(2, 0), 40),
        new("GELO", "04H", new TimeOnly(4, 0), 50),
        new("GELO", "06H", new TimeOnly(6, 0), 60),

        new("GLICEMIA", "Jantar", new TimeOnly(19, 30), 10),
        new("GLICEMIA", "Café", new TimeOnly(7, 0), 20),

        new("SSVV", "PM", new TimeOnly(20, 0), 10),
        new("SSVV", "AM", new TimeOnly(6, 0), 20),
    ];

    public static IReadOnlyList<SeedMarker> Markers { get; } =
    [
        new("C.I.", "CI", 10),
        new("Sondas", "SONDAS", 20),
        new("Drenos", "DRENOS", 30),
    ];

    public const string AdministratorsGroupName = "Administradores";
    public const string OesteShiftGroupName = "Plantão Oeste";

    public static IReadOnlyList<SeedGroup> Groups { get; } =
    [
        new(
            AdministratorsGroupName,
            "Acesso completo ao sistema e a todos os setores.",
            GrantsAllSectors: true,
            Permissions: [.. Access.Permissions.All],
            SectorNames: []),
        new(
            OesteShiftGroupName,
            "Equipe do plantão do setor Oeste.",
            GrantsAllSectors: false,
            Permissions: [Access.Permissions.ChecklistView, Access.Permissions.ChecklistUpdate, Access.Permissions.SectorSelect],
            SectorNames: [OesteSectorName]),
    ];
}
