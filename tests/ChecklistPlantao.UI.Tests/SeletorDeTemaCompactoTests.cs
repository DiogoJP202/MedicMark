using Bunit;
using ChecklistPlantao.UI.Components;
using ChecklistPlantao.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// O interruptor de tema do menu de navegação.
///
/// Duas posições, e não as três de "Estado do dispositivo": aqui a troca precisa custar um toque,
/// no meio do plantão, quando a tela está clara demais para a hora.
/// </summary>
public sealed class SeletorDeTemaCompactoTests : BunitContext
{
    private readonly TemaFalso _tema = new();

    public SeletorDeTemaCompactoTests() => Services.AddSingleton<IThemeService>(_tema);

    /// <summary>
    /// O ponto mais delicado: com "seguir o aparelho" escolhido e o aparelho no escuro, a
    /// PREFERÊNCIA é "automático" mas o tema EM VIGOR é escuro. O interruptor mostra o segundo —
    /// mostrar a posição errada seria pior que não ter interruptor.
    /// </summary>
    [Fact]
    public void Segue_o_tema_em_vigor_e_nao_a_preferencia_guardada()
    {
        _tema.Atual = ThemeChoice.Automatic;
        _tema.Efetivo = ThemeChoice.Dark;

        var cut = Render<SeletorDeTemaCompacto>();

        Assert.True(cut.Find("[data-testid=island-theme]").HasAttribute("checked"));
    }

    [Fact]
    public void Desligado_quando_o_tema_em_vigor_e_claro()
    {
        _tema.Efetivo = ThemeChoice.Light;

        var cut = Render<SeletorDeTemaCompacto>();

        Assert.False(cut.Find("[data-testid=island-theme]").HasAttribute("checked"));
    }

    [Fact]
    public void Ligar_grava_o_tema_escuro()
    {
        _tema.Efetivo = ThemeChoice.Light;
        var cut = Render<SeletorDeTemaCompacto>();

        cut.Find("[data-testid=island-theme]").Change(true);

        Assert.Equal(ThemeChoice.Dark, _tema.Gravado);
    }

    /// <summary>
    /// Mexer no interruptor vira uma escolha EXPLÍCITA, e não uma volta para "automático" — que é
    /// o que mexer num interruptor significa em qualquer lugar.
    /// </summary>
    [Fact]
    public void Desligar_a_partir_do_automatico_grava_o_tema_claro()
    {
        _tema.Atual = ThemeChoice.Automatic;
        _tema.Efetivo = ThemeChoice.Dark;
        var cut = Render<SeletorDeTemaCompacto>();

        cut.Find("[data-testid=island-theme]").Change(false);

        Assert.Equal(ThemeChoice.Light, _tema.Gravado);
    }

    /// <summary>O ícone acompanha: sol no claro, lua no escuro. O estado não fica só na posição.</summary>
    [Fact]
    public void O_icone_acompanha_o_estado()
    {
        _tema.Efetivo = ThemeChoice.Light;
        var cut = Render<SeletorDeTemaCompacto>();

        var comSol = cut.Find(".ilha__ajuste svg").InnerHtml;

        cut.Find("[data-testid=island-theme]").Change(true);

        Assert.NotEqual(comSol, cut.Find(".ilha__ajuste svg").InnerHtml);
    }

    /// <summary>
    /// role="switch" e não uma caixa de marcação: o leitor de tela anuncia "ativado/desativado",
    /// e não "marcado". Um interruptor age na hora; uma caixa espera um "salvar".
    /// </summary>
    [Fact]
    public void E_anunciado_como_interruptor()
    {
        var cut = Render<SeletorDeTemaCompacto>();

        var entrada = cut.Find("[data-testid=island-theme]");

        Assert.Equal("switch", entrada.GetAttribute("role"));
        Assert.Contains("Tema escuro", cut.Markup, StringComparison.Ordinal);
    }
}
