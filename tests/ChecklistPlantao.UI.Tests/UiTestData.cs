using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Client.Abstractions;

namespace ChecklistPlantao.UI.Tests;

/// <summary>Dados de apoio para os testes de componente, espelhando a folha de papel original.</summary>
internal static class UiTestData
{
    public static readonly Guid TemplateId = Guid.CreateVersion7();
    public static readonly Guid Coluna20H = Guid.CreateVersion7();
    public static readonly Guid Coluna22H = Guid.CreateVersion7();
    public static readonly Guid SectorId = Guid.CreateVersion7();
    public static readonly Guid SessionId = Guid.CreateVersion7();

    public static EffectiveAccess AccessWith(params string[] permissions)
    {
        var group = new AccessGroup(Guid.CreateVersion7(), "Teste", null, grantsAllSectors: false, DateTime.UnixEpoch);
        group.ReplacePermissions(permissions, DateTime.UnixEpoch);
        group.ReplaceSectors([SectorId], DateTime.UnixEpoch);
        return EffectiveAccess.FromGroups([group]);
    }

    public static ChecklistCell Cell(Guid bedId, Guid columnId, bool completed, bool overdue = false) =>
        new(SessionId, bedId, TemplateId, columnId, completed, 1, overdue);

    /// <summary>Três leitos: o primeiro completo, o segundo parcial, o terceiro nada feito.</summary>
    public static ChecklistBoard Board(bool sessionOpen = true)
    {
        var leito1 = Guid.CreateVersion7();
        var leito2 = Guid.CreateVersion7();
        var leito3 = Guid.CreateVersion7();

        var linhas = new List<ChecklistRow>
        {
            new(leito1, "1148", [Cell(leito1, Coluna20H, true), Cell(leito1, Coluna22H, true)], ["Sondas"]),
            new(leito2, "1150", [Cell(leito2, Coluna20H, true), Cell(leito2, Coluna22H, false, overdue: true)], []),
            new(leito3, "1152", [Cell(leito3, Coluna20H, false), Cell(leito3, Coluna22H, false, overdue: true)], ["C.I.", "Drenos"]),
        };

        var colunas = new List<ChecklistColumnView>
        {
            new(Coluna20H, "20H", new TimeOnly(20, 0), 3, 2, false),
            new(Coluna22H, "22H", new TimeOnly(22, 0), 3, 1, true),
        };

        var template = new ChecklistTemplateDto(TemplateId, "Gelo", "GELO", null, 10, true, [], [], 1);

        return new ChecklistBoard(SessionId, SectorId, "Oeste", new DateOnly(2026, 8, 6), template, colunas, linhas, sessionOpen);
    }

    public static PendingGroup Pending(string templateName, string columnName, bool overdue, params string[] beds) =>
        new(SectorId, "Oeste", TemplateId, templateName, Coluna22H, columnName, new TimeOnly(22, 0), overdue, beds);
}
