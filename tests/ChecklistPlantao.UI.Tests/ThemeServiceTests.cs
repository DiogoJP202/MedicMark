using Bunit;
using ChecklistPlantao.UI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// A ponte entre a escolha de tema e o WebView.
///
/// Os nomes atravessam para o JavaScript e para o armazenamento local, então eles são um
/// contrato: mudá-los aqui sem mudar em tema.js faria a preferência de todo mundo virar
/// "automático" na atualização seguinte.
/// </summary>
public sealed class ThemeServiceTests : BunitContext
{
    private WebViewThemeService Servico
        => new(JSInterop.JSRuntime, NullLogger<WebViewThemeService>.Instance);

    [Theory]
    [InlineData(ThemeChoice.Automatic, "automatico")]
    [InlineData(ThemeChoice.Light, "claro")]
    [InlineData(ThemeChoice.Dark, "escuro")]
    public void Os_nomes_combinam_com_os_do_javascript(ThemeChoice escolha, string esperado)
    {
        Assert.Equal(esperado, WebViewThemeService.Escrever(escolha));
        Assert.Equal(escolha, WebViewThemeService.Interpretar(esperado));
    }

    /// <summary>Lixo gravado por outra versão não pode virar exceção — vira "seguir o aparelho".</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sepia")]
    public void Valor_desconhecido_vira_automatico(string? valor)
        => Assert.Equal(ThemeChoice.Automatic, WebViewThemeService.Interpretar(valor));

    [Fact]
    public async Task Grava_a_escolha_no_webview()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        await Servico.SetAsync(ThemeChoice.Dark);

        var chamada = Assert.Single(JSInterop.Invocations["temaDoAplicativo.definir"]);
        Assert.Equal("escuro", chamada.Arguments[0]);
    }

    [Fact]
    public async Task Le_a_escolha_do_webview()
    {
        JSInterop.Setup<string>("temaDoAplicativo.lido").SetResult("claro");

        Assert.Equal(ThemeChoice.Light, await Servico.GetAsync());
    }

    /// <summary>
    /// Quando o JavaScript não responde — script que não carregou, WebView em pré-renderização —
    /// o tema é o do aparelho. Uma preferência de APARÊNCIA não pode derrubar a tela.
    /// </summary>
    [Fact]
    public async Task Falha_do_javascript_vira_o_tema_do_aparelho()
    {
        JSInterop.Setup<string>("temaDoAplicativo.lido")
            .SetException(new JSException("Could not find 'temaDoAplicativo.lido'."));

        Assert.Equal(ThemeChoice.Automatic, await Servico.GetAsync());
    }

    [Fact]
    public async Task Falha_ao_gravar_nao_estoura_na_tela()
    {
        JSInterop.SetupVoid("temaDoAplicativo.definir", "escuro")
            .SetException(new JSException("localStorage indisponível."));

        await Servico.SetAsync(ThemeChoice.Dark);
    }
}
