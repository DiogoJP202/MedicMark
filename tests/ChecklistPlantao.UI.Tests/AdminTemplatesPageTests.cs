using Bunit;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.UI.Abstractions;
using ChecklistPlantao.UI.Pages.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// Tipos de checklist e colunas.
///
/// O teste central aqui não é visual: é o que a tela ENVIA. Ela mandava a lista de setores
/// vazia em toda gravação, e lista vazia significa "vale para todos os setores" — então editar
/// o nome de um tipo apagava em silêncio a restrição configurada pelo administrador.
/// </summary>
public sealed class AdminTemplatesPageTests : BunitContext
{
    private FakeAdministrationService Admin { get; } = new();

    private static readonly Guid SetorOeste = Guid.CreateVersion7();
    private static readonly Guid SetorLeste = Guid.CreateVersion7();

    private void Registrar()
    {
        Admin.Sectors.Add(new SectorDto(SetorOeste, "Oeste", null, 10, true, null, null, 1));
        Admin.Sectors.Add(new SectorDto(SetorLeste, "Leste", null, 20, true, null, null, 1));
        Services.AddSingleton<IAdministrationService>(Admin);
    }

    private static ChecklistTemplateDto Template(params Guid[] setores) =>
        new(
            Guid.CreateVersion7(),
            "Gelo",
            "GELO",
            null,
            10,
            true,
            setores,
            [Coluna("20H", new TimeOnly(20, 0)), Coluna("22H", new TimeOnly(22, 0))],
            4);

    private static ChecklistColumnDto Coluna(string nome, TimeOnly hora) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), nome, hora, 10, true, true, 0, 15, 10, 3, true, 5, 2);

    [Fact]
    public void Nao_mostra_formulario_antes_de_pedir()
    {
        Admin.Templates.Add(Template());
        Registrar();

        var cut = Render<AdminTemplatesPage>();

        Assert.Empty(cut.FindAll("[data-testid=form-dialog]"));
    }

    /// <summary>O defeito: os setores do tipo precisam ir junto na gravação, não uma lista vazia.</summary>
    [Fact]
    public void Editar_o_tipo_reenvia_os_setores_que_ele_ja_tinha()
    {
        Admin.Templates.Add(Template(SetorOeste));
        Registrar();

        var cut = Render<AdminTemplatesPage>();
        cut.Find("[data-testid=template-new]");
        cut.FindAll("button").First(b => b.TextContent.Contains("Editar tipo", StringComparison.Ordinal)).Click();
        cut.Find("[data-testid=form-dialog-save]").Click();

        Assert.NotNull(Admin.LastTemplate);
        Assert.Contains(SetorOeste, Admin.LastTemplate!.SectorIds);
    }

    [Fact]
    public void Da_para_acrescentar_um_setor_ao_tipo()
    {
        Admin.Templates.Add(Template(SetorOeste));
        Registrar();

        var cut = Render<AdminTemplatesPage>();
        cut.FindAll("button").First(b => b.TextContent.Contains("Editar tipo", StringComparison.Ordinal)).Click();
        cut.Find($"[data-testid=template-sector-{SetorLeste}]").Change(true);
        cut.Find("[data-testid=form-dialog-save]").Click();

        Assert.Contains(SetorOeste, Admin.LastTemplate!.SectorIds);
        Assert.Contains(SetorLeste, Admin.LastTemplate.SectorIds);
    }

    /// <summary>Desmarcar todos é uma escolha legítima: significa "vale para todos os setores".</summary>
    [Fact]
    public void Da_para_liberar_o_tipo_para_todos_os_setores()
    {
        Admin.Templates.Add(Template(SetorOeste));
        Registrar();

        var cut = Render<AdminTemplatesPage>();
        cut.FindAll("button").First(b => b.TextContent.Contains("Editar tipo", StringComparison.Ordinal)).Click();
        cut.Find($"[data-testid=template-sector-{SetorOeste}]").Change(false);
        cut.Find("[data-testid=form-dialog-save]").Click();

        Assert.Empty(Admin.LastTemplate!.SectorIds);
    }

    [Fact]
    public void Novo_tipo_comeca_sem_setor_marcado()
    {
        Admin.Templates.Add(Template(SetorOeste));
        Registrar();

        var cut = Render<AdminTemplatesPage>();
        cut.Find("[data-testid=template-new]").Click();
        cut.Find("[data-testid=form-dialog-save]").Click();

        Assert.Empty(Admin.LastTemplate!.SectorIds);
        Assert.Null(Admin.LastEditedId);
    }

    [Fact]
    public void Editar_coluna_abre_o_modal_preenchido()
    {
        Admin.Templates.Add(Template());
        Registrar();

        var cut = Render<AdminTemplatesPage>();
        cut.Find("[data-testid^=column-row-] button").Click();

        Assert.Equal("20H", cut.Find("[data-testid=column-name]").GetAttribute("value"));
    }

    [Fact]
    public void Nova_coluna_abre_o_modal_vazio()
    {
        Admin.Templates.Add(Template());
        Registrar();

        var cut = Render<AdminTemplatesPage>();
        cut.FindAll("button").First(b => b.TextContent.Contains("Nova coluna", StringComparison.Ordinal)).Click();

        Assert.Equal(string.Empty, cut.Find("[data-testid=column-name]").GetAttribute("value"));
    }

    /// <summary>É o caso do 500 relatado: o servidor recusa e a pessoa precisa ver o motivo.</summary>
    [Fact]
    public void Coluna_recusada_pelo_servidor_mantem_o_modal_com_a_mensagem()
    {
        Admin.Templates.Add(Template());
        Admin.SaveResult = Result.Fail("Já existe uma coluna ativa chamada \"20H\" neste tipo de checklist.");
        Registrar();

        var cut = Render<AdminTemplatesPage>();
        cut.Find("[data-testid^=column-row-] button").Click();
        cut.Find("[data-testid=form-dialog-save]").Click();

        Assert.NotNull(cut.Find("[data-testid=form-dialog]"));
        Assert.Contains("Já existe uma coluna ativa", cut.Markup, StringComparison.Ordinal);
    }
}
