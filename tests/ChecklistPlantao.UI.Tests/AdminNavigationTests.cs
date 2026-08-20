using Bunit;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Client.Abstractions;
using ChecklistPlantao.UI.Layout;
using ChecklistPlantao.UI.Pages.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// Hierarquia da administração e barra de navegação.
///
/// A administração passou a ter três níveis: Administração → Configurações do sistema →
/// Configurações avançadas. O que estes testes protegem não é o desenho, e sim a regra que o
/// desenho não pode quebrar — cada item continua condicionado à permissão que o servidor exige.
/// </summary>
public sealed class AdminNavigationTests : BunitContext
{
    private static EffectiveAccess ComPermissoes(params string[] permissoes)
    {
        var grupo = new AccessGroup(Guid.CreateVersion7(), "Teste", null, grantsAllSectors: true, DateTime.UnixEpoch);
        grupo.ReplacePermissions(permissoes, DateTime.UnixEpoch);
        return EffectiveAccess.FromGroups([grupo]);
    }

    private void RegistrarSessao(EffectiveAccess acesso) =>
        Services.AddSingleton<IAppSession>(new StubSession(acesso));

    // ------------------------------------------------------------------ barra de navegação

    /// <summary>
    /// "Dispositivo" saiu da barra a pedido. A tela em si NÃO pode sumir: é o critério 22, e o
    /// caminho para ela passou a ser o cartão do painel.
    /// </summary>
    [Fact]
    public void A_barra_de_navegacao_nao_tem_mais_o_item_dispositivo()
    {
        Services.AddSingleton<IAppSession>(new StubSession(ComPermissoes(Permissions.ChecklistView)));
        Services.AddSingleton<ISyncStatusService>(new StubSyncStatus());
        Services.AddSingleton<INotificationStatusService>(new StubNotificationStatus());
        Services.AddSingleton<ChecklistPlantao.UI.Services.IThemeService>(new TemaFalso());

        var cut = Render<MainLayout>();

        // A ilha usa botões, não âncoras: a asserção é sobre o destino, não sobre o href.
        cut.Find("[data-testid=island-toggle]").Click();

        Assert.Empty(cut.FindAll("[data-testid=island-link-dispositivo]"));
        Assert.DoesNotContain("Estado do dispositivo", cut.Find("[data-testid=island-list]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_barra_mantem_os_demais_destinos()
    {
        Services.AddSingleton<IAppSession>(new StubSession(ComPermissoes(Permissions.ChecklistView)));
        Services.AddSingleton<ISyncStatusService>(new StubSyncStatus());
        Services.AddSingleton<INotificationStatusService>(new StubNotificationStatus());
        Services.AddSingleton<ChecklistPlantao.UI.Services.IThemeService>(new TemaFalso());

        var cut = Render<MainLayout>();

        cut.Find("[data-testid=island-toggle]").Click();

        var lista = cut.Find("[data-testid=island-list]").TextContent;

        Assert.Contains("Checklist", lista, StringComparison.Ordinal);
        Assert.Contains("Pendências", lista, StringComparison.Ordinal);
        Assert.Contains("Plantão", lista, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ níveis do menu

    [Fact]
    public void Administracao_leva_as_configuracoes_do_sistema()
    {
        RegistrarSessao(ComPermissoes(Permissions.AdminSettings));

        var cut = Render<AdminPage>();

        Assert.NotNull(cut.Find("[data-testid=admin-configuracoes-sistema]"));
    }

    /// <summary>Setores e tipos são o uso do dia a dia e não devem ficar a três cliques.</summary>
    [Fact]
    public void Administracao_mantem_atalhos_para_o_uso_diario()
    {
        RegistrarSessao(ComPermissoes(Permissions.AdminSettings, Permissions.AdminSectors, Permissions.AdminTemplates));

        var cut = Render<AdminPage>();

        Assert.Contains("Setores e leitos", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Tipos de checklist", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracoes_do_sistema_reune_os_quatro_itens()
    {
        RegistrarSessao(ComPermissoes(Permissions.AdminSectors, Permissions.AdminTemplates, Permissions.AdminSettings));

        var cut = Render<AdminSystemSettingsPage>();

        Assert.Contains("Setores e Leitos", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Tipos de Checklist", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Marcadores", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Configurações Avançadas", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracoes_avancadas_reune_os_quatro_itens()
    {
        RegistrarSessao(ComPermissoes(
            Permissions.AdminUsers,
            Permissions.AdminNotifications,
            Permissions.AdminSettings,
            Permissions.AdminDevices));

        var cut = Render<AdminAdvancedPage>();

        Assert.Contains("Usuários e Grupos", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Notificações", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Configurações Gerais", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Dispositivos", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>Reorganizar o menu não pode virar uma porta lateral para quem não tem acesso.</summary>
    [Fact]
    public void Quem_so_administra_dispositivos_ve_somente_dispositivos()
    {
        RegistrarSessao(ComPermissoes(Permissions.AdminDevices));

        var cut = Render<AdminAdvancedPage>();

        Assert.Contains("Dispositivos", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Usuários e Grupos", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Configurações Gerais", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_permissao_nenhuma_o_menu_fica_vazio()
    {
        RegistrarSessao(EffectiveAccess.None);

        var cut = Render<AdminSystemSettingsPage>();

        Assert.Empty(cut.FindAll("button.cartao--acionavel"));
    }
}
