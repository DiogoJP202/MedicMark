using Bunit;
using ChecklistPlantao.UI.Components.Checklist;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// O atraso precisa de FORMA, e não só de cor.
///
/// Célula atrasada e célula pendente tinham exatamente o mesmo desenho — nenhum — e se
/// distinguiam apenas pelo tom do fundo. No celular era pior: as duas caixas eram idênticas,
/// 34 por 34, mesma borda e mesmo fundo, e nem o leitor de tela ouvia falar do atraso.
///
/// A regra transversal do projeto é que nenhuma informação é transmitida apenas por cor. Esta
/// era a exceção que ninguém tinha visto, justamente na tela mais usada.
/// </summary>
public sealed class AtrasoTests : BunitContext
{
    [Fact]
    public void Celula_atrasada_desenha_um_sinal_que_a_pendente_nao_tem()
    {
        var board = UiTestData.Board();
        var cut = Render<ChecklistDesktopGrid>(p => p.Add(g => g.Board, board));

        // Leito 1152: pendente às 20H, atrasado às 22H. Mesma linha, para isolar a diferença.
        var pendente = cut.Find($"[data-testid='cell-{board.Rows[2].BedId}-{UiTestData.Coluna20H}']");
        var atrasada = cut.Find($"[data-testid='cell-{board.Rows[2].BedId}-{UiTestData.Coluna22H}']");

        Assert.NotNull(atrasada.QuerySelector("svg"));
        Assert.Null(pendente.QuerySelector("svg"));
    }

    /// <summary>O sinal de atraso não é o "X": o X marca o que foi feito, e sobrepô-los confundiria.</summary>
    [Fact]
    public void Celula_atrasada_nao_usa_a_marca_de_feito()
    {
        var board = UiTestData.Board();
        var cut = Render<ChecklistDesktopGrid>(p => p.Add(g => g.Board, board));

        var atrasada = cut.Find($"[data-testid='cell-{board.Rows[2].BedId}-{UiTestData.Coluna22H}']");

        Assert.DoesNotContain("✕", atrasada.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Leito_atrasado_no_celular_desenha_um_sinal_que_o_pendente_nao_tem()
    {
        var board = UiTestData.Board();

        // Às 20H o leito 1152 está pendente; às 22H, atrasado.
        var pendente = Render<ChecklistMobileList>(p => p
            .Add(l => l.Rows, board.Rows)
            .Add(l => l.ColumnId, UiTestData.Coluna20H))
            .Find($"[data-testid='bed-{board.Rows[2].BedId}']");

        var atrasado = Render<ChecklistMobileList>(p => p
            .Add(l => l.Rows, board.Rows)
            .Add(l => l.ColumnId, UiTestData.Coluna22H))
            .Find($"[data-testid='bed-{board.Rows[2].BedId}']");

        Assert.NotNull(atrasado.QuerySelector(".leito-botao__caixa svg"));
        Assert.Null(pendente.QuerySelector(".leito-botao__caixa svg"));
    }

    /// <summary>
    /// A grade do computador já anunciava o atraso; o celular, que é onde o plantão trabalha,
    /// dizia só "não realizado".
    /// </summary>
    [Fact]
    public void Leitor_de_tela_ouve_o_atraso_no_celular()
    {
        var board = UiTestData.Board();

        var cut = Render<ChecklistMobileList>(p => p
            .Add(l => l.Rows, board.Rows)
            .Add(l => l.ColumnId, UiTestData.Coluna22H));

        var atrasado = cut.Find($"[data-testid='bed-{board.Rows[2].BedId}']");

        Assert.Contains("atrasado", atrasado.GetAttribute("aria-label")!, StringComparison.Ordinal);
    }

    [Fact]
    public void Leito_marcado_nao_e_anunciado_como_atrasado()
    {
        var board = UiTestData.Board();

        var cut = Render<ChecklistMobileList>(p => p
            .Add(l => l.Rows, board.Rows)
            .Add(l => l.ColumnId, UiTestData.Coluna22H));

        // Leito 1148: marcado às 22H.
        var marcado = cut.Find($"[data-testid='bed-{board.Rows[0].BedId}']");

        Assert.Contains("realizado", marcado.GetAttribute("aria-label")!, StringComparison.Ordinal);
        Assert.DoesNotContain("atrasado", marcado.GetAttribute("aria-label")!, StringComparison.Ordinal);
    }
}
