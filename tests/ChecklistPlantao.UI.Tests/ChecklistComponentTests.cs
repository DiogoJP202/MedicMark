using Bunit;
using ChecklistPlantao.UI.Abstractions;
using ChecklistPlantao.UI.Components;
using ChecklistPlantao.UI.Components.Checklist;

namespace ChecklistPlantao.UI.Tests;

public sealed class ChecklistComponentTests : BunitContext
{
    [Fact]
    public void Grade_desktop_desenha_uma_linha_por_leito_e_uma_coluna_por_horario()
    {
        var board = UiTestData.Board();

        var cut = Render<ChecklistDesktopGrid>(p => p.Add(g => g.Board, board));

        Assert.Equal(3, cut.FindAll("tbody tr").Count);
        // Cabeçalho: leito + duas colunas.
        Assert.Equal(3, cut.FindAll("thead th").Count);
    }

    [Fact]
    public void Grade_desktop_mostra_o_horario_real_sob_o_nome_da_coluna()
    {
        var cut = Render<ChecklistDesktopGrid>(p => p.Add(g => g.Board, UiTestData.Board()));

        var cabecalho = cut.Find("thead").TextContent;
        Assert.Contains("20H", cabecalho, StringComparison.Ordinal);
        Assert.Contains("20:00", cabecalho, StringComparison.Ordinal);
        Assert.Contains("22:00", cabecalho, StringComparison.Ordinal);
    }

    [Fact]
    public void Celula_marcada_mostra_X_e_nao_apenas_cor()
    {
        var board = UiTestData.Board();
        var cut = Render<ChecklistDesktopGrid>(p => p.Add(g => g.Board, board));

        var marcada = cut.Find($"[data-testid='cell-{board.Rows[0].BedId}-{UiTestData.Coluna20H}']");

        Assert.Contains("✕", marcada.TextContent, StringComparison.Ordinal);
        Assert.Equal("true", marcada.GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Celula_pendente_nao_mostra_X()
    {
        var board = UiTestData.Board();
        var cut = Render<ChecklistDesktopGrid>(p => p.Add(g => g.Board, board));

        var pendente = cut.Find($"[data-testid='cell-{board.Rows[2].BedId}-{UiTestData.Coluna20H}']");

        Assert.DoesNotContain("✕", pendente.TextContent, StringComparison.Ordinal);
        Assert.Equal("false", pendente.GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Clicar_na_celula_dispara_a_alternancia()
    {
        var board = UiTestData.Board();
        ChecklistCell? recebida = null;

        var cut = Render<ChecklistDesktopGrid>(p => p
            .Add(g => g.Board, board)
            .Add(g => g.OnToggle, cell => recebida = cell));

        await cut.Find($"[data-testid='cell-{board.Rows[2].BedId}-{UiTestData.Coluna20H}']").ClickAsync(new());

        Assert.NotNull(recebida);
        Assert.Equal(board.Rows[2].BedId, recebida.BedId);
        Assert.False(recebida.IsCompleted);
    }

    [Fact]
    public void Sessao_encerrada_deixa_a_grade_somente_leitura()
    {
        var cut = Render<ChecklistDesktopGrid>(p => p.Add(g => g.Board, UiTestData.Board(sessionOpen: false)));

        Assert.All(cut.FindAll(".celula"), botao => Assert.True(botao.HasAttribute("disabled")));
    }

    [Fact]
    public void Filtro_de_pendentes_esconde_os_leitos_completos()
    {
        var cut = Render<ChecklistDesktopGrid>(p => p
            .Add(g => g.Board, UiTestData.Board())
            .Add(g => g.OnlyPending, true));

        var linhas = cut.FindAll("tbody tr");
        Assert.Equal(2, linhas.Count);
        Assert.DoesNotContain("1148", cut.Find("tbody").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Busca_filtra_pelo_numero_do_leito()
    {
        var cut = Render<ChecklistDesktopGrid>(p => p
            .Add(g => g.Board, UiTestData.Board())
            .Add(g => g.Search, "1152"));

        Assert.Single(cut.FindAll("tbody tr"));
        Assert.Contains("1152", cut.Find("tbody").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Grade_sem_resultado_mostra_estado_vazio()
    {
        var cut = Render<ChecklistDesktopGrid>(p => p
            .Add(g => g.Board, UiTestData.Board())
            .Add(g => g.Search, "9999"));

        Assert.NotEmpty(cut.FindAll("[data-testid=empty-state]"));
    }

    [Fact]
    public void Lista_mobile_mostra_apenas_a_coluna_selecionada()
    {
        var board = UiTestData.Board();

        var cut = Render<ChecklistMobileList>(p => p
            .Add(l => l.Rows, board.Rows)
            .Add(l => l.ColumnId, UiTestData.Coluna22H));

        var itens = cut.FindAll("[data-testid^=bed-]");
        Assert.Equal(3, itens.Count);

        // Na coluna 22H apenas o leito 1148 está marcado.
        Assert.Equal("true", itens[0].GetAttribute("aria-pressed"));
        Assert.Equal("false", itens[1].GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Lista_mobile_com_somente_pendentes_esconde_os_marcados_daquela_coluna()
    {
        var board = UiTestData.Board();

        var cut = Render<ChecklistMobileList>(p => p
            .Add(l => l.Rows, board.Rows)
            .Add(l => l.ColumnId, UiTestData.Coluna22H)
            .Add(l => l.OnlyPending, true));

        Assert.Equal(2, cut.FindAll("[data-testid^=bed-]").Count);
    }

    [Fact]
    public void Lista_mobile_exibe_os_marcadores_do_leito()
    {
        var board = UiTestData.Board();

        var cut = Render<ChecklistMobileList>(p => p
            .Add(l => l.Rows, board.Rows)
            .Add(l => l.ColumnId, UiTestData.Coluna20H));

        var texto = cut.Markup;
        Assert.Contains("Sondas", texto, StringComparison.Ordinal);
        Assert.Contains("C.I.", texto, StringComparison.Ordinal);
        Assert.Contains("Drenos", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Seletor_de_coluna_marca_a_ativa_e_mostra_pendencias()
    {
        var board = UiTestData.Board();

        var cut = Render<ChecklistColumnSelector>(p => p
            .Add(s => s.Columns, board.Columns)
            .Add(s => s.SelectedColumnId, UiTestData.Coluna22H));

        var ativa = cut.Find($"[data-testid='column-tab-{UiTestData.Coluna22H}']");
        Assert.Equal("true", ativa.GetAttribute("aria-selected"));
        Assert.Contains("2 pendentes", ativa.TextContent, StringComparison.Ordinal);
        Assert.Contains("atrasado", ativa.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Progresso_mostra_numeros_alem_da_barra()
    {
        var cut = Render<ChecklistProgress>(p => p
            .Add(c => c.Label, "Gelo")
            .Add(c => c.Total, 16)
            .Add(c => c.Completed, 10));

        var texto = cut.Find("[data-testid=checklist-progress]").TextContent;
        Assert.Contains("10 de 16", texto, StringComparison.Ordinal);
        Assert.Contains("6 pendentes", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Progresso_completo_avisa_que_esta_tudo_feito()
    {
        var cut = Render<ChecklistProgress>(p => p.Add(c => c.Total, 5).Add(c => c.Completed, 5));

        Assert.Contains("tudo feito", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Seletor_de_setor_mostra_pendencias_e_avisa_quando_nao_ha_plantao()
    {
        var cut = Render<SectorSelector>(p => p.Add(s => s.Sectors,
        [
            new SectorSummary(UiTestData.SectorId, "Oeste", 4, true),
            new SectorSummary(Guid.CreateVersion7(), "Leste", 0, false),
        ]));

        var markup = cut.Markup;
        Assert.Contains("4 pendentes", markup, StringComparison.Ordinal);
        Assert.Contains("Sem plantão aberto", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Seletor_de_setor_sem_setores_explica_o_motivo()
    {
        var cut = Render<SectorSelector>(p => p.Add(s => s.Sectors, []));

        Assert.Contains("Nenhum setor disponível", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Undo_toast_dispara_o_desfazer_e_se_esconde()
    {
        var desfez = false;

        var cut = Render<UndoToast>(p => p
            .Add(t => t.Visible, true)
            .Add(t => t.Message, "Marcado.")
            .Add(t => t.OnUndo, () => desfez = true));

        await cut.Find("[data-testid=undo-action]").ClickAsync(new());

        Assert.True(desfez);
    }

    [Fact]
    public void Undo_toast_invisivel_nao_renderiza_nada()
    {
        var cut = Render<UndoToast>(p => p.Add(t => t.Visible, false));

        Assert.Empty(cut.FindAll("[data-testid=undo-toast]"));
    }

    [Fact]
    public async Task Confirmacao_so_executa_a_acao_ao_confirmar()
    {
        var confirmou = false;

        var cut = Render<ConfirmDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Encerrar o plantão?")
            .Add(d => d.OnConfirm, () => confirmou = true));

        Assert.NotEmpty(cut.FindAll("[data-testid=confirm-dialog]"));

        await cut.Find("[data-testid=confirm-accept]").ClickAsync(new());

        Assert.True(confirmou);
    }
}
