using Bunit;
using Bunit.TestDoubles;
using ChecklistPlantao.UI.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// A navegação em ilha.
///
/// A ilha É o componente de navegação, e não uma cápsula com um menu dentro: o mesmo elemento
/// muda de forma entre compacto e expandido. Fechada mostra onde você está; aberta, para onde
/// pode ir.
///
/// O que estes testes protegem é o comportamento, não a aparência — a animação é do CSS. O
/// essencial: a lista não é alcançável fechada, o item atual é reconhecível sem depender de cor,
/// e navegar sempre recolhe.
/// </summary>
public sealed class IslandNavTests : BunitContext
{
    private static readonly IReadOnlyList<IslandNav.ItemDaIlha> Destinos =
    [
        new("/", "Painel", "painel"),
        new("/checklist", "Checklist", "checklist"),
        new("/classificacoes", "Classificações", "classificacoes"),
        new("/pendencias", "Pendências", "pendencias"),
        new("/sessao", "Plantão", "plantao"),
        new("/admin", "Administração", "administracao"),
    ];

    private BunitNavigationManager Navegacao => Services.GetRequiredService<BunitNavigationManager>();

    private IRenderedComponent<IslandNav> Renderizar(string rota = "/")
    {
        var cut = Render<IslandNav>(p => p.Add(i => i.Items, Destinos));

        if (rota != "/")
        {
            Navegacao.NavigateTo(rota);
        }

        return cut;
    }

    private static void Abrir(IRenderedComponent<IslandNav> cut) =>
        cut.Find("[data-testid=island-toggle]").Click();

    [Fact]
    public void Fechada_mostra_a_pagina_atual()
    {
        var cut = Renderizar();

        Assert.Equal("Painel", cut.Find("[data-testid=island-current]").TextContent.Trim());
        Assert.Equal("false", cut.Find("[data-testid=island-toggle]").GetAttribute("aria-expanded"));
    }

    /// <summary>
    /// A rota mais específica ganha. "/" é prefixo de tudo — sem a ordenação por tamanho, o
    /// checklist mostraria "Painel".
    /// </summary>
    [Theory]
    [InlineData("/checklist", "Checklist")]
    [InlineData("/classificacoes", "Classificações")]
    [InlineData("/pendencias", "Pendências")]
    [InlineData("/sessao", "Plantão")]
    [InlineData("/admin", "Administração")]
    public void Titulo_acompanha_a_rota(string rota, string esperado)
    {
        var cut = Renderizar(rota);

        Assert.Equal(esperado, cut.Find("[data-testid=island-current]").TextContent.Trim());
    }

    /// <summary>Rota com parâmetro continua sendo a mesma seção.</summary>
    [Fact]
    public void Rota_com_parametro_ainda_e_a_secao()
    {
        var cut = Renderizar("/checklist/8a1f0f8e-0000-0000-0000-000000000000");

        Assert.Equal("Checklist", cut.Find("[data-testid=island-current]").TextContent.Trim());
    }

    /// <summary>
    /// Tela fora do menu — "Estado do dispositivo" é alcançada pelo painel. A ilha não pode ficar
    /// vazia nem sumir: cai no primeiro destino, que é a saída.
    /// </summary>
    [Fact]
    public void Rota_fora_do_menu_nao_deixa_a_ilha_vazia()
    {
        var cut = Renderizar("/dispositivo");

        Assert.Equal("Painel", cut.Find("[data-testid=island-current]").TextContent.Trim());
    }

    [Fact]
    public void Abrir_revela_todos_os_destinos()
    {
        var cut = Renderizar();
        Abrir(cut);

        Assert.Equal("true", cut.Find("[data-testid=island-toggle]").GetAttribute("aria-expanded"));

        foreach (var destino in Destinos)
        {
            Assert.Contains(destino.Titulo, cut.Find("[data-testid=island-list]").TextContent, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// O item atual precisa ser reconhecível SEM depender de cor: além do fundo, ele carrega
    /// aria-current e uma marca visível.
    /// </summary>
    [Fact]
    public void Item_atual_tem_destaque_que_nao_e_so_cor()
    {
        var cut = Renderizar("/pendencias");
        Abrir(cut);

        var atual = cut.Find("[data-testid=island-link-pendencias]");

        Assert.Equal("page", atual.GetAttribute("aria-current"));
        Assert.Contains("ilha__link--atual", atual.ClassName, StringComparison.Ordinal);
        Assert.NotNull(atual.QuerySelector(".ilha__marca"));

        // E os demais não têm nenhuma das três marcas.
        var outro = cut.Find("[data-testid=island-link-sessao]");

        Assert.Null(outro.GetAttribute("aria-current"));
        Assert.DoesNotContain("ilha__link--atual", outro.ClassName, StringComparison.Ordinal);
        Assert.Null(outro.QuerySelector(".ilha__marca"));
    }

    [Fact]
    public void Selecionar_navega_e_recolhe()
    {
        var cut = Renderizar();
        Abrir(cut);

        cut.Find("[data-testid=island-link-classificacoes]").Click();

        Assert.EndsWith("/classificacoes", Navegacao.Uri, StringComparison.Ordinal);
        Assert.Equal("false", cut.Find("[data-testid=island-toggle]").GetAttribute("aria-expanded"));
        Assert.Equal("Classificações", cut.Find("[data-testid=island-current]").TextContent.Trim());
    }

    [Fact]
    public void Tocar_na_ilha_de_novo_recolhe()
    {
        var cut = Renderizar();

        Abrir(cut);
        Abrir(cut);

        Assert.Equal("false", cut.Find("[data-testid=island-toggle]").GetAttribute("aria-expanded"));
    }

    /// <summary>Sem isto, sair do menu exigiria acertar a própria ilha.</summary>
    [Fact]
    public void Tocar_fora_recolhe()
    {
        var cut = Renderizar();
        Abrir(cut);

        cut.Find("[data-testid=island-backdrop]").Click();

        Assert.Equal("false", cut.Find("[data-testid=island-toggle]").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("[data-testid=island-backdrop]"));
    }

    /// <summary>Fechada não existe fundo capturando toque sobre a página inteira.</summary>
    [Fact]
    public void Fechada_nao_bloqueia_a_pagina()
    {
        var cut = Renderizar();

        Assert.Empty(cut.FindAll("[data-testid=island-backdrop]"));
    }

    /// <summary>
    /// Navegar por fora — botão voltar do Android, deep link de notificação — precisa recolher a
    /// ilha. Aberta sobre uma tela nova seria um estado órfão.
    /// </summary>
    [Fact]
    public void Navegacao_externa_recolhe_a_ilha()
    {
        var cut = Renderizar();
        Abrir(cut);

        Navegacao.NavigateTo("/sessao");

        cut.WaitForAssertion(() =>
            Assert.Equal("false", cut.Find("[data-testid=island-toggle]").GetAttribute("aria-expanded")));
    }

    /// <summary>A lista vem do layout, que decide se a administração entra. A ilha só desenha.</summary>
    [Fact]
    public void Sem_permissao_a_administracao_nao_aparece()
    {
        var semAdmin = Destinos.Where(d => d.Rota != "/admin").ToList();

        var cut = Render<IslandNav>(p => p.Add(i => i.Items, semAdmin));
        cut.Find("[data-testid=island-toggle]").Click();

        Assert.Empty(cut.FindAll("[data-testid=island-link-admin]"));
        Assert.DoesNotContain("Administração", cut.Find("[data-testid=island-list]").TextContent, StringComparison.Ordinal);
    }

    /// <summary>Cada destino leva um ícone: é o que a ilha mostra quando está fechada.</summary>
    [Fact]
    public void Cada_destino_tem_icone()
    {
        var cut = Renderizar();
        Abrir(cut);

        foreach (var destino in Destinos)
        {
            var link = cut.Find($"[data-testid=island-link-{destino.Rota.Trim('/')}]");

            Assert.NotNull(link.QuerySelector(".ilha__icone svg"));
        }
    }
}
