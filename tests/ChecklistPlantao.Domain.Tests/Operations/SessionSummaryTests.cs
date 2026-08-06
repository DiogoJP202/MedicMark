using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Structure;

namespace ChecklistPlantao.Domain.Tests.Operations;

public sealed class SessionSummaryTests
{
    private readonly Guid _sessionId = Guid.CreateVersion7();
    private readonly ChecklistTemplate _gelo = TestData.Template("Gelo", "GELO");
    private readonly ChecklistColumn _vinteHoras;
    private readonly ChecklistColumn _vinteEDuas;
    private readonly Guid _leito1148 = Guid.CreateVersion7();
    private readonly Guid _leito1150 = Guid.CreateVersion7();
    private readonly BedMarkerDefinition _sondas = new(Guid.CreateVersion7(), "Sondas", "SONDAS", 20, TestData.NowUtc);
    private readonly BedMarkerDefinition _drenos = new(Guid.CreateVersion7(), "Drenos", "DRENOS", 30, TestData.NowUtc);

    public SessionSummaryTests()
    {
        _vinteHoras = _gelo.Column("20H", new TimeOnly(20, 0), 10);
        _vinteEDuas = _gelo.Column("22H", new TimeOnly(22, 0), 20);
    }

    private ChecklistEntry Entry(Guid bedId, ChecklistColumn column, bool completed)
    {
        var entry = new ChecklistEntry(Guid.CreateVersion7(), _sessionId, bedId, _gelo.Id, column.Id, TestData.NowUtc);
        if (completed)
        {
            entry.SetCompletion(true, TestData.NowUtc);
        }

        return entry;
    }

    private Dictionary<Guid, string> BedCodes() => new()
    {
        [_leito1148] = "1148",
        [_leito1150] = "1150",
    };

    [Fact]
    public void Resumo_conta_concluidas_e_pendentes_por_coluna()
    {
        ChecklistEntry[] entradas =
        [
            Entry(_leito1148, _vinteHoras, completed: true),
            Entry(_leito1150, _vinteHoras, completed: false),
            Entry(_leito1148, _vinteEDuas, completed: false),
            Entry(_leito1150, _vinteEDuas, completed: false),
        ];

        var resumo = SessionSummaryCalculator.Build(entradas, [_gelo], [], [], BedCodes());

        Assert.Equal(4, resumo.Overall.Total);
        Assert.Equal(1, resumo.Overall.Completed);
        Assert.Equal(3, resumo.Overall.Pending);
        Assert.True(resumo.HasPending);

        var gelo = Assert.Single(resumo.Templates);
        Assert.Equal("Gelo", gelo.TemplateName);
        Assert.Equal(2, gelo.Columns.Count);
        Assert.Equal(1, gelo.Columns[0].Progress.Completed);
        Assert.Equal(0, gelo.Columns[1].Progress.Completed);
    }

    [Fact]
    public void Resumo_sem_pendencia_avisa_que_esta_completo()
    {
        ChecklistEntry[] entradas =
        [
            Entry(_leito1148, _vinteHoras, completed: true),
            Entry(_leito1150, _vinteHoras, completed: true),
        ];

        var resumo = SessionSummaryCalculator.Build(entradas, [_gelo], [], [], BedCodes());

        Assert.False(resumo.HasPending);
        Assert.True(resumo.Overall.IsComplete);
    }

    [Fact]
    public void Resumo_lista_os_leitos_de_cada_marcador()
    {
        SessionBedMarker[] marcadores =
        [
            new(Guid.CreateVersion7(), _sessionId, _leito1148, _sondas.Id, isSelected: true, TestData.NowUtc),
            new(Guid.CreateVersion7(), _sessionId, _leito1150, _sondas.Id, isSelected: true, TestData.NowUtc),
            new(Guid.CreateVersion7(), _sessionId, _leito1148, _drenos.Id, isSelected: false, TestData.NowUtc),
        ];

        var resumo = SessionSummaryCalculator.Build([], [_gelo], marcadores, [_sondas, _drenos], BedCodes());

        var sondas = resumo.Markers.Single(m => m.MarkerName == "Sondas");
        Assert.Equal(["1148", "1150"], sondas.BedCodes);
        Assert.Equal(2, sondas.Count);

        var drenos = resumo.Markers.Single(m => m.MarkerName == "Drenos");
        Assert.Empty(drenos.BedCodes);
    }

    [Fact]
    public void Um_leito_pode_ter_mais_de_um_marcador()
    {
        SessionBedMarker[] marcadores =
        [
            new(Guid.CreateVersion7(), _sessionId, _leito1148, _sondas.Id, isSelected: true, TestData.NowUtc),
            new(Guid.CreateVersion7(), _sessionId, _leito1148, _drenos.Id, isSelected: true, TestData.NowUtc),
        ];

        var resumo = SessionSummaryCalculator.Build([], [_gelo], marcadores, [_sondas, _drenos], BedCodes());

        Assert.All(resumo.Markers, m => Assert.Equal(["1148"], m.BedCodes));
    }

    [Fact]
    public void Marcador_inativo_nao_aparece_no_resumo()
    {
        _drenos.SetActive(false, TestData.NowUtc);

        var resumo = SessionSummaryCalculator.Build([], [_gelo], [], [_sondas, _drenos], BedCodes());

        Assert.Equal("Sondas", Assert.Single(resumo.Markers).MarkerName);
    }

    [Fact]
    public void Tipo_sem_entradas_nao_polui_o_resumo()
    {
        var glicemia = TestData.Template("Glicemia", "GLICEMIA");
        glicemia.Column("Jantar", new TimeOnly(19, 30));

        var resumo = SessionSummaryCalculator.Build(
            [Entry(_leito1148, _vinteHoras, completed: true)],
            [_gelo, glicemia],
            [],
            [],
            BedCodes());

        Assert.Equal("Gelo", Assert.Single(resumo.Templates).TemplateName);
    }
}
