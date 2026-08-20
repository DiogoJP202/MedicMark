using Bunit;
using ChecklistPlantao.UI.Components;
using ChecklistPlantao.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

public sealed class SeletorDeTemaTests : BunitContext
{
    private readonly TemaFalso _tema = new();

    public SeletorDeTemaTests() => Services.AddSingleton<IThemeService>(_tema);

    [Fact]
    public void Oferece_as_tres_escolhas()
    {
        var cut = Render<SeletorDeTema>();

        Assert.NotNull(cut.Find("[data-testid=tema-automatico]"));
        Assert.NotNull(cut.Find("[data-testid=tema-claro]"));
        Assert.NotNull(cut.Find("[data-testid=tema-escuro]"));
    }

    /// <summary>"Seguir o aparelho" é o padrão: cobre a maioria sem ninguém configurar nada.</summary>
    [Fact]
    public void Comeca_seguindo_o_aparelho()
    {
        var cut = Render<SeletorDeTema>();

        Assert.True(cut.Find("[data-testid=tema-automatico]").HasAttribute("checked"));
    }

    [Fact]
    public void Escolher_grava_a_preferencia()
    {
        var cut = Render<SeletorDeTema>();

        cut.Find("[data-testid=tema-escuro]").Change(true);

        Assert.Equal(ThemeChoice.Dark, _tema.Gravado);
    }

    [Fact]
    public void Mostra_a_escolha_que_ja_estava_valendo()
    {
        _tema.Atual = ThemeChoice.Light;

        var cut = Render<SeletorDeTema>();

        Assert.True(cut.Find("[data-testid=tema-claro]").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid=tema-automatico]").HasAttribute("checked"));
    }

    /// <summary>
    /// A escolhida se distingue por fundo, peso e a marca de conferido — e não só pela cor.
    /// É a regra transversal do projeto, e aqui ela vale para um controle novo.
    /// </summary>
    [Fact]
    public void A_escolhida_nao_se_distingue_so_pela_cor()
    {
        var cut = Render<SeletorDeTema>();

        cut.Find("[data-testid=tema-escuro]").Change(true);

        var ativa = cut.Find(".opcao--ativa");
        Assert.Contains("Escuro", ativa.TextContent, StringComparison.Ordinal);
        Assert.NotNull(ativa.QuerySelector(".opcao__marca"));
    }

    /// <summary>
    /// O rádio nativo continua no DOM: é ele que o leitor de tela anuncia e o teclado opera.
    /// Escondê-lo com display:none o tiraria da ordem de tabulação.
    /// </summary>
    [Fact]
    public void Mantem_o_radio_nativo_no_documento()
    {
        var cut = Render<SeletorDeTema>();

        var radios = cut.FindAll("input[type=radio]");

        Assert.Equal(3, radios.Count);
        Assert.All(radios, r => Assert.Equal("tema", r.GetAttribute("name")));
    }

    private sealed class TemaFalso : IThemeService
    {
        public ThemeChoice Atual { get; set; } = ThemeChoice.Automatic;

        public ThemeChoice? Gravado { get; private set; }

        public Task<ThemeChoice> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Atual);

        public Task SetAsync(ThemeChoice choice, CancellationToken cancellationToken = default)
        {
            Gravado = choice;
            Atual = choice;
            return Task.CompletedTask;
        }
    }
}
