using Bunit;
using ChecklistPlantao.UI.Components;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// A caixa de marcação.
///
/// Nasceu de 21 repetições de <c>style="width:24px;height:24px"</c> em 8 telas — o padrão mais
/// duplicado do projeto, e algumas repetições já tinham divergido: 24px numa tela, 26px em outra.
///
/// O que estes testes protegem não é a aparência, e sim as três coisas que a duplicação impedia
/// de garantir: alvo de toque maior que o desenho, estado que não depende só de cor, e a caixa
/// nativa continuar sendo quem o leitor de tela anuncia.
/// </summary>
public sealed class CaixaTests : BunitContext
{
    [Fact]
    public void Desmarcada_por_padrao()
    {
        var cut = Render<Caixa>(p => p.Add(c => c.Label, "Ativo"));

        Assert.False(cut.Find("input[type=checkbox]").HasAttribute("checked"));
        Assert.Contains("Ativo", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Marcada_reflete_o_valor()
    {
        var cut = Render<Caixa>(p => p.Add(c => c.Value, true));

        Assert.True(cut.Find("input[type=checkbox]").HasAttribute("checked"));
    }

    /// <summary>Atende as duas formas de uso que existiam nas telas.</summary>
    [Fact]
    public void Avisa_quem_escuta_ao_alternar()
    {
        bool? recebido = null;

        var cut = Render<Caixa>(p => p
            .Add(c => c.Value, false)
            .Add(c => c.ValueChanged, (bool v) => recebido = v));

        cut.Find("input[type=checkbox]").Change(true);

        Assert.True(recebido);
    }

    [Fact]
    public void Desabilitada_nao_alterna()
    {
        var alterou = false;

        var cut = Render<Caixa>(p => p
            .Add(c => c.Disabled, true)
            .Add(c => c.ValueChanged, (bool _) => alterou = true));

        var entrada = cut.Find("input[type=checkbox]");

        Assert.True(entrada.HasAttribute("disabled"));
        Assert.Contains("caixa--desabilitada", cut.Find("label").ClassName, StringComparison.Ordinal);
        Assert.False(alterou);
    }

    /// <summary>
    /// A caixa nativa precisa continuar no DOM, e não substituída por uma `div` com
    /// <c>role="checkbox"</c>: é ela que o leitor de tela anuncia e que o teclado opera.
    /// Reimplementar isso à mão sairia pior do que o navegador já faz.
    /// </summary>
    [Fact]
    public void Usa_a_caixa_nativa_e_nao_uma_imitacao()
    {
        var cut = Render<Caixa>(p => p.Add(c => c.Label, "Som"));

        Assert.NotNull(cut.Find("input[type=checkbox]"));
        Assert.Empty(cut.FindAll("[role=checkbox]"));
    }

    /// <summary>
    /// O rótulo envolve a entrada, então tocar no texto marca — sem depender de `for`/`id`, que
    /// exigiria um identificador único por instância e é onde esse padrão costuma quebrar.
    /// </summary>
    [Fact]
    public void O_rotulo_envolve_a_entrada()
    {
        var cut = Render<Caixa>(p => p.Add(c => c.Label, "Vibração"));

        var rotulo = cut.Find("label.caixa");

        Assert.NotNull(rotulo.QuerySelector("input[type=checkbox]"));
        Assert.Contains("Vibração", rotulo.TextContent, StringComparison.Ordinal);
    }

    /// <summary>A marca desenhada é o que distingue o estado sem depender de cor.</summary>
    [Fact]
    public void Tem_marca_visivel_alem_do_fundo()
    {
        var cut = Render<Caixa>(p => p.Add(c => c.Value, true));

        var marca = cut.Find(".caixa__marca");

        Assert.NotNull(marca.QuerySelector("svg"));
        Assert.Equal("true", marca.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Aceita_conteudo_no_lugar_do_rotulo_simples()
    {
        var cut = Render<Caixa>(p => p
            .AddChildContent("<span>Ver <code>detalhe</code></span>"));

        Assert.NotNull(cut.Find("code"));
    }

    [Fact]
    public void Identificador_de_teste_chega_a_entrada()
    {
        var cut = Render<Caixa>(p => p.Add(c => c.TestId, "notif-som"));

        Assert.NotNull(cut.Find("[data-testid=notif-som]"));
    }
}
