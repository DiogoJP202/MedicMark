using Bunit;
using ChecklistPlantao.Client.Abstractions;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.UI.Pages.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

public sealed class AdminAdvancedPagesTests : BunitContext
{
    private FakeAdministrationService Admin { get; } = new();

    private void Registrar()
    {
        Services.AddSingleton<IStructureAdminService>(Admin);
        Services.AddSingleton<IAccessAdminService>(Admin);
        Services.AddSingleton<ISystemAdminService>(Admin);
    }

    private static AccessGroupDto Grupo(Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), "Enfermagem", null, true, true, ["checklist.view"], [], 7);

    private static AppUserDto Usuario(Guid grupoId, Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), "maria", "Maria Silva", true, [grupoId], 5);

    [Fact]
    public void Acessos_nao_exibem_formularios_permanentes_e_adicionar_oferece_as_duas_opcoes()
    {
        Registrar();

        var cut = Render<AdminAccessPage>();

        Assert.Empty(cut.FindAll("[data-testid=form-dialog]"));
        cut.Find("[data-testid=access-add]").Click();

        Assert.NotNull(cut.Find("[data-testid=access-add-user]"));
        Assert.NotNull(cut.Find("[data-testid=access-add-group]"));
    }

    [Fact]
    public void Novo_usuario_valida_a_primeira_etapa_e_permanece_no_passo_em_erro_do_servidor()
    {
        Admin.SaveResult = Result.Fail("Usuário já existe.", "usuario.duplicado");
        Registrar();

        var cut = Render<AdminAccessPage>();
        cut.Find("[data-testid=access-add]").Click();
        cut.Find("[data-testid=access-add-user]").Click();

        cut.Find("[data-testid=user-wizard-next]").Click();
        Assert.Contains("Informe o usuário", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Etapa 1 de 3", cut.Find("[data-testid=user-wizard-step]").TextContent, StringComparison.Ordinal);

        cut.Find("[data-testid=user-login]").Change("enfermeira");
        cut.Find("[data-testid=user-name]").Change("Enfermeira do plantão");
        cut.Find("[data-testid=user-password]").Change("segura123");
        cut.Find("[data-testid=user-wizard-next]").Click();
        Assert.Contains("Etapa 2 de 3", cut.Find("[data-testid=user-wizard-step]").TextContent, StringComparison.Ordinal);

        cut.Find("[data-testid=user-wizard-back]").Click();
        Assert.Equal("enfermeira", cut.Find("[data-testid=user-login]").GetAttribute("value"));
        cut.Find("[data-testid=user-wizard-next]").Click();
        cut.Find("[data-testid=user-wizard-next]").Click();
        cut.Find("[data-testid=user-wizard-save]").Click();

        Assert.Contains("Etapa 3 de 3", cut.Find("[data-testid=user-wizard-step]").TextContent, StringComparison.Ordinal);
        Assert.Contains("Usuário já existe", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("enfermeira", Admin.LastCreatedUser?.UserName);
        Assert.Equal("segura123", Admin.LastCreatedUser?.Password);
    }

    [Fact]
    public void Cancelar_novo_usuario_descarta_o_fluxo_sem_salvar()
    {
        Registrar();
        var cut = Render<AdminAccessPage>();

        cut.Find("[data-testid=access-add]").Click();
        cut.Find("[data-testid=access-add-user]").Click();
        cut.Find("[data-testid=user-login]").Change("temporario");
        cut.Find("[data-testid=user-wizard-cancel]").Click();

        Assert.Empty(cut.FindAll("[data-testid=form-dialog]"));
        Assert.Equal(0, Admin.SaveCount);
    }

    [Fact]
    public void Editar_grupo_percorre_as_tres_etapas_e_preserva_a_versao()
    {
        var grupo = Grupo();
        Admin.Groups.Add(grupo);
        Admin.Permissions.Add(new PermissionDto("checklist.view", "Ver checklist"));
        Registrar();

        var cut = Render<AdminAccessPage>();
        cut.Find($"[data-testid='group-row-{grupo.Id}'] button").Click();

        Assert.Equal("Enfermagem", cut.Find("[data-testid=group-name]").GetAttribute("value"));
        cut.Find("[data-testid=group-wizard-next]").Click();
        Assert.True(cut.Find("[data-testid=group-all-sectors]").HasAttribute("checked"));
        cut.Find("[data-testid=group-wizard-next]").Click();
        cut.Find("[data-testid=group-wizard-save]").Click();

        Assert.Equal(grupo.Id, Admin.LastEditedId);
        Assert.Equal(7, Admin.LastGroup?.BaseVersion);
        Assert.Contains("checklist.view", Admin.LastGroup?.Permissions ?? []);
    }

    [Fact]
    public void Edicao_de_usuario_mantem_login_somente_leitura_e_senha_em_confirmacao_separada()
    {
        var grupo = Grupo();
        var usuario = Usuario(grupo.Id);
        Admin.Groups.Add(grupo);
        Admin.Users.Add(usuario);
        Registrar();

        var cut = Render<AdminAccessPage>();
        cut.Find($"[data-testid='user-row-{usuario.Id}'] button").Click();

        Assert.True(cut.Find("[data-testid=edit-user-login]").HasAttribute("readonly"));
        cut.Find("[data-testid=edit-user-name]").Change("Maria Souza");
        cut.Find("[data-testid=form-dialog-save]").Click();

        Assert.Equal("Maria Souza", Admin.LastUpdatedUser?.DisplayName);
        Assert.Equal(5, Admin.LastUpdatedUser?.BaseVersion);

        cut.Find($"[data-testid='user-row-{usuario.Id}'] button").Click();
        cut.Find("[data-testid=user-reset-password]").Click();
        Assert.NotNull(cut.Find("[data-testid=confirm-dialog]"));
        cut.Find("[data-testid=new-password]").Change("novaSenha9");
        cut.Find("[data-testid=confirm-accept]").Click();
        Assert.Equal("novaSenha9", Admin.LastPasswordReset?.NewPassword);
    }

    [Fact]
    public void Notificacoes_separam_canais_conteudo_e_android_e_preservam_dados_em_erro()
    {
        Admin.SaveResult = Result.Fail("Conflito de versão.");
        Registrar();

        var cut = Render<AdminNotificationsPage>();
        Assert.NotNull(cut.Find("[data-testid=notification-channels]"));
        Assert.NotNull(cut.Find("[data-testid=notification-content]"));
        Assert.NotNull(cut.Find("[data-testid=notification-android-options]"));

        cut.Find("[data-testid=notif-title]").Change("Alerta {setor}");
        cut.Find("[data-testid=notif-save]").Click();

        Assert.Equal("Alerta {setor}", cut.Find("[data-testid=notif-title]").GetAttribute("value"));
        Assert.Equal("Alerta {setor}", Admin.LastNotifications?.TitleTemplate);
        Assert.Equal("/admin/avancado", cut.Find(".admin-cabecalho__voltar").GetAttribute("href"));
    }

    [Fact]
    public void Plantao_e_acesso_mantem_a_rota_e_separa_os_quatro_assuntos()
    {
        Registrar();

        var cut = Render<AdminSettingsPage>();

        Assert.Contains("Plantão e acesso", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=settings-shift]"));
        Assert.NotNull(cut.Find("[data-testid=settings-retention-group]"));
        Assert.NotNull(cut.Find("[data-testid=settings-offline]"));
        Assert.NotNull(cut.Find("[data-testid=settings-auto-open]"));
        Assert.Equal("/admin/avancado", cut.Find(".admin-cabecalho__voltar").GetAttribute("href"));
    }

    [Fact]
    public void Dispositivos_oferecem_tabela_e_cards_sem_acoes_de_edicao()
    {
        var id = Guid.CreateVersion7();
        Admin.Devices.Add(new DeviceDto(
            id, "Posto 1", "Android", "1.2.3", DateTime.UtcNow, DateTime.UtcNow,
            true, true, false, "Saudável", DateTime.UtcNow, null, true));
        Registrar();

        var cut = Render<AdminDevicesPage>();

        Assert.NotNull(cut.Find($"[data-testid='device-row-{id}']"));
        Assert.NotNull(cut.Find($"[data-testid='device-card-{id}']"));
        Assert.DoesNotContain("Editar", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Salvar", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("/admin/avancado", cut.Find(".admin-cabecalho__voltar").GetAttribute("href"));
    }
}
