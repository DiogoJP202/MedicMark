using Bunit;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Client.Abstractions;
using ChecklistPlantao.UI.Pages.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// Cadastro por modal.
///
/// Antes cada tela mostrava um formulário permanente acima da lista, mesmo sem ninguém querer
/// cadastrar nada. Agora o formulário só existe quando foi pedido — e o teste fixa isso, senão
/// a regressão passaria despercebida: o formulário voltar a aparecer sozinho não quebra nada.
/// </summary>
public sealed class AdminModalTests : BunitContext
{
    private FakeAdministrationService Admin { get; } = new();

    private void Registrar()
    {
        Services.AddSingleton<IStructureAdminService>(Admin);
        Services.AddSingleton<IAccessAdminService>(Admin);
        Services.AddSingleton<ISystemAdminService>(Admin);
    }

    private static SectorDto Setor(string nome = "Oeste") =>
        new(Guid.CreateVersion7(), nome, null, 10, true, null, null, 3);

    private static BedMarkerDefinitionDto Marcador(string nome = "C.I.") =>
        new(Guid.CreateVersion7(), nome, "CI", 10, true, 2);

    // ------------------------------------------------------------------ marcadores

    [Fact]
    public void Marcadores_nao_mostram_formulario_antes_de_pedir()
    {
        Admin.Markers.Add(Marcador());
        Registrar();

        var cut = Render<AdminMarkersPage>();

        Assert.Empty(cut.FindAll("[data-testid=form-dialog]"));
    }

    [Fact]
    public void Novo_marcador_abre_o_modal_vazio()
    {
        Admin.Markers.Add(Marcador());
        Registrar();

        var cut = Render<AdminMarkersPage>();
        cut.Find("[data-testid=marker-new]").Click();

        Assert.NotNull(cut.Find("[data-testid=form-dialog]"));
        Assert.Equal(string.Empty, cut.Find("[data-testid=marker-name]").GetAttribute("value"));
    }

    [Fact]
    public void Editar_marcador_abre_o_modal_preenchido()
    {
        Admin.Markers.Add(Marcador("Sondas"));
        Registrar();

        var cut = Render<AdminMarkersPage>();
        cut.Find("[data-testid^=marker-row-] button").Click();

        Assert.Equal("Sondas", cut.Find("[data-testid=marker-name]").GetAttribute("value"));
    }

    [Fact]
    public void Salvar_marcador_com_sucesso_fecha_o_modal()
    {
        Admin.Markers.Add(Marcador());
        Registrar();

        var cut = Render<AdminMarkersPage>();
        cut.Find("[data-testid=marker-new]").Click();
        cut.Find("[data-testid=form-dialog-save]").Click();

        Assert.Empty(cut.FindAll("[data-testid=form-dialog]"));
        Assert.Equal(1, Admin.SaveCount);
    }

    /// <summary>
    /// O modal permanece aberto quando o servidor recusa. Fechá-lo jogaria fora o que foi
    /// digitado junto com a explicação do erro — que é o caso do nome duplicado de coluna.
    /// </summary>
    [Fact]
    public void Salvar_com_erro_mantem_o_modal_aberto_e_mostra_a_mensagem()
    {
        Admin.Markers.Add(Marcador());
        Admin.SaveResult = Result.Fail("Já existe um marcador com esse código.");
        Registrar();

        var cut = Render<AdminMarkersPage>();
        cut.Find("[data-testid=marker-new]").Click();
        cut.Find("[data-testid=form-dialog-save]").Click();

        Assert.NotNull(cut.Find("[data-testid=form-dialog]"));
        Assert.Contains("Já existe um marcador com esse código.", cut.Markup, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ setores e leitos

    [Fact]
    public void Setores_e_leitos_nao_mostram_formulario_antes_de_pedir()
    {
        Admin.Sectors.Add(Setor());
        Registrar();

        var cut = Render<AdminSectorsPage>();

        Assert.Empty(cut.FindAll("[data-testid=form-dialog]"));
    }

    [Fact]
    public void Existem_os_botoes_de_novo_setor_e_novo_leito()
    {
        Admin.Sectors.Add(Setor());
        Registrar();

        var cut = Render<AdminSectorsPage>();

        Assert.NotNull(cut.Find("[data-testid=sector-new]"));
        Assert.NotNull(cut.Find("[data-testid=bed-new]"));
    }

    [Fact]
    public void Editar_setor_abre_o_modal_preenchido()
    {
        Admin.Sectors.Add(Setor("Leste"));
        Registrar();

        var cut = Render<AdminSectorsPage>();
        cut.Find("[data-testid^=sector-row-] button").Click();

        Assert.Equal("Leste", cut.Find("[data-testid=sector-name]").GetAttribute("value"));
    }

    [Fact]
    public void Novo_leito_abre_o_modal_com_o_setor_disponivel()
    {
        var setor = Setor();
        Admin.Sectors.Add(setor);
        Registrar();

        var cut = Render<AdminSectorsPage>();
        cut.Find("[data-testid=bed-new]").Click();

        Assert.NotNull(cut.Find("[data-testid=bed-sector]"));
        Assert.Equal(string.Empty, cut.Find("[data-testid=bed-code]").GetAttribute("value"));
    }
}
