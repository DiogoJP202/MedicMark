using Bunit;
using ChecklistPlantao.UI.Components;

namespace ChecklistPlantao.UI.Tests;

public sealed class AdminSharedComponentsTests : BunitContext
{
    [Fact]
    public void Cabecalho_expoe_retorno_descricao_e_acao()
    {
        var cut = Render<AdminPageHeader>(parameters => parameters
            .Add(p => p.Title, "Setores e leitos")
            .Add(p => p.Description, "Estrutura do plantão")
            .Add(p => p.BackHref, "/admin/sistema")
            .Add(p => p.Actions, builder => builder.AddMarkupContent(0, "<button>Novo setor</button>")));

        Assert.Equal("/admin/sistema", cut.Find("a").GetAttribute("href"));
        Assert.Contains("Estrutura do plantão", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Novo setor", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Ajuda_abre_por_botao_e_fecha_com_escape()
    {
        var cut = Render<HelpPopover>(parameters => parameters
            .Add(p => p.Label, "Ajuda sobre fuso")
            .AddChildContent("Use um identificador IANA."));
        var button = cut.Find("button");

        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        button.Click();
        Assert.Equal("true", cut.Find("button").GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("[role=tooltip]"));

        cut.Find(".ajuda-popover").KeyDown(key: "Escape");

        Assert.Equal("false", cut.Find("button").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("[role=tooltip]"));
    }

    [Fact]
    public void Badge_expoe_texto_e_tom_sem_depender_apenas_de_cor()
    {
        var cut = Render<StatusBadge>(parameters => parameters
            .Add(p => p.Text, "Ativo")
            .Add(p => p.Tone, "sucesso"));

        Assert.Equal("Ativo", cut.Find(".badge").TextContent);
        Assert.Equal("sucesso", cut.Find(".badge").GetAttribute("data-tone"));
    }
}
