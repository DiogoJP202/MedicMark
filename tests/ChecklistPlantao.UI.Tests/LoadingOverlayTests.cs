using Bunit;
using ChecklistPlantao.UI.Components;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// A sobreposição de carregamento.
///
/// Existe porque várias telas faziam requisição sem sinal nenhum: a pessoa via a tela antiga, ou
/// vazia, sem saber se o aplicativo estava trabalhando ou travado — e dava para tocar nos botões
/// durante a busca, disparando a mesma operação duas vezes.
/// </summary>
public sealed class LoadingOverlayTests : BunitContext
{
    /// <summary>Invisível não pode deixar resíduo no DOM: o fundo escuro cobriria a tela inteira.</summary>
    [Fact]
    public void Invisivel_nao_renderiza_nada()
    {
        var cut = Render<LoadingOverlay>(p => p.Add(o => o.Visible, false));

        Assert.Empty(cut.FindAll("[data-testid=loading-overlay]"));
    }

    [Fact]
    public void Visivel_mostra_a_mensagem()
    {
        var cut = Render<LoadingOverlay>(p => p
            .Add(o => o.Visible, true)
            .Add(o => o.Message, "Carregando o plantão…"));

        var fundo = cut.Find("[data-testid=loading-overlay]");

        Assert.Contains("Carregando o plantão…", fundo.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A animação é decorativa e fica escondida do leitor de tela; a mensagem é o que informa.
    /// Sem <c>aria-busy</c>, quem não vê a roda não sabe que há algo em andamento.
    /// </summary>
    [Fact]
    public void Anuncia_que_esta_ocupado_para_leitor_de_tela()
    {
        var cut = Render<LoadingOverlay>(p => p.Add(o => o.Visible, true));

        var fundo = cut.Find("[data-testid=loading-overlay]");

        Assert.Equal("true", fundo.GetAttribute("aria-busy"));
        Assert.Equal("assertive", fundo.GetAttribute("aria-live"));
        Assert.Equal("true", cut.Find(".carregando-roda").GetAttribute("aria-hidden"));
    }

    /// <summary>
    /// O bloqueio é o ponto: a caixa fica sobre um fundo que cobre a tela inteira. Sem isso, a
    /// sobreposição seria só enfeite e o toque duplo continuaria possível.
    /// </summary>
    [Fact]
    public void Cobre_a_tela_inteira()
    {
        var cut = Render<LoadingOverlay>(p => p.Add(o => o.Visible, true));

        Assert.NotNull(cut.Find(".carregando-fundo"));
        Assert.NotNull(cut.Find(".carregando-fundo .carregando-caixa"));
    }

    [Fact]
    public void Mensagem_padrao_quando_nenhuma_e_informada()
    {
        var cut = Render<LoadingOverlay>(p => p.Add(o => o.Visible, true));

        Assert.Contains("Carregando…", cut.Markup, StringComparison.Ordinal);
    }
}
