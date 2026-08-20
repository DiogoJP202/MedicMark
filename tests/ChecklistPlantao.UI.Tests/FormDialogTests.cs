using Bunit;
using ChecklistPlantao.UI.Components;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// Modal de formulário.
///
/// A diferença que importa em relação ao ConfirmDialog: aqui existe conteúdo digitado, então
/// fechar por engano custa caro. O fundo não fecha, e salvar não fecha sozinho — quem fecha é a
/// página, e só quando o servidor aceitou.
/// </summary>
public sealed class FormDialogTests : BunitContext
{
    [Fact]
    public void Fechado_nao_renderiza_nada()
    {
        var cut = Render<FormDialog>(p => p
            .Add(d => d.Visible, false)
            .Add(d => d.Title, "Novo setor"));

        Assert.Empty(cut.FindAll("[data-testid=form-dialog]"));
    }

    [Fact]
    public void Aberto_mostra_o_titulo_e_o_conteudo()
    {
        var cut = Render<FormDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Novo setor")
            .AddChildContent("<input data-testid=\"campo\" />"));

        Assert.Contains("Novo setor", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=campo]"));
    }

    /// <summary>
    /// O ponto do componente: clicar fora não pode jogar fora o que foi digitado. O ConfirmDialog
    /// fecha nesse clique — aqui o fundo é inerte, sem manipulador nenhum, e é essa ausência que
    /// o teste fixa.
    /// </summary>
    [Fact]
    public void Clique_no_fundo_nao_fecha()
    {
        var cut = Render<FormDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Novo setor"));

        Assert.Throws<MissingEventHandlerException>(() => cut.Find(".modal-fundo").Click());
        Assert.NotNull(cut.Find("[data-testid=form-dialog]"));
    }

    [Fact]
    public void Cancelar_fecha()
    {
        var visivel = true;

        var cut = Render<FormDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Novo setor")
            .Add(d => d.VisibleChanged, (bool v) => visivel = v));

        cut.Find("[data-testid=form-dialog-cancel]").Click();

        Assert.False(visivel);
    }

    [Fact]
    public void Botao_de_fechar_no_canto_tambem_cancela()
    {
        var visivel = true;

        var cut = Render<FormDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Novo setor")
            .Add(d => d.VisibleChanged, (bool v) => visivel = v));

        cut.Find("[data-testid=form-dialog-close]").Click();

        Assert.False(visivel);
    }

    [Fact]
    public void Esc_fecha()
    {
        var visivel = true;

        var cut = Render<FormDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Novo setor")
            .Add(d => d.VisibleChanged, (bool v) => visivel = v));

        cut.Find("[data-testid=form-dialog]").KeyDown(key: "Escape");

        Assert.False(visivel);
    }

    /// <summary>
    /// Salvar avisa a página e NÃO fecha: se o servidor recusar, o formulário precisa continuar
    /// aberto, com os dados e com a mensagem do erro.
    /// </summary>
    [Fact]
    public void Salvar_avisa_a_pagina_sem_fechar()
    {
        var salvou = false;
        var visivel = true;

        var cut = Render<FormDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Novo setor")
            .Add(d => d.VisibleChanged, (bool v) => visivel = v)
            .Add(d => d.OnSave, () => salvou = true));

        cut.Find("[data-testid=form-dialog-save]").Click();

        Assert.True(salvou);
        Assert.True(visivel);
    }

    [Fact]
    public void Enquanto_salva_a_acao_primaria_fica_bloqueada()
    {
        var cut = Render<FormDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Novo setor")
            .Add(d => d.Saving, true));

        Assert.True(cut.Find("[data-testid=form-dialog-save]").HasAttribute("disabled"));
    }

    /// <summary>Cancelar no meio da gravação deixaria a página num estado que ela não espera.</summary>
    [Fact]
    public void Enquanto_salva_cancelar_nao_fecha()
    {
        var visivel = true;

        var cut = Render<FormDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Novo setor")
            .Add(d => d.Saving, true)
            .Add(d => d.VisibleChanged, (bool v) => visivel = v));

        cut.Find("[data-testid=form-dialog-cancel]").Click();

        Assert.True(visivel);
    }

    [Fact]
    public void Rodape_customizado_substitui_as_acoes_padrao()
    {
        var cut = Render<FormDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Fluxo em etapas")
            .Add(d => d.FooterContent, builder => builder.AddMarkupContent(0, "<button data-testid='continuar'>Continuar</button>")));

        Assert.NotNull(cut.Find("[data-testid=continuar]"));
        Assert.Empty(cut.FindAll("[data-testid=form-dialog-save]"));
        Assert.Empty(cut.FindAll("[data-testid=form-dialog-cancel]"));
    }
}
