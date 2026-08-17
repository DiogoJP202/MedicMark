using Bunit;
using ChecklistPlantao.UI.Abstractions;
using ChecklistPlantao.UI.Components.Status;

namespace ChecklistPlantao.UI.Tests;

public sealed class StatusComponentTests : BunitContext
{
    [Fact]
    public void Faixa_de_conexao_some_quando_esta_tudo_sincronizado()
    {
        var cut = Render<ConnectionStatusBanner>(p => p
            .Add(b => b.Status, new SyncStatus(ConnectivityState.Online, 0, DateTime.UtcNow, false, null)));

        Assert.Empty(cut.FindAll("[data-testid=connection-banner]"));
    }

    [Fact]
    public void Faixa_de_conexao_avisa_quantas_alteracoes_aguardam_quando_offline()
    {
        var cut = Render<ConnectionStatusBanner>(p => p
            .Add(b => b.Status, new SyncStatus(ConnectivityState.Offline, 4, null, false, null)));

        var faixa = cut.Find("[data-testid=connection-banner]");
        Assert.Contains("Offline", faixa.TextContent, StringComparison.Ordinal);
        Assert.Contains("4 alteração(ões) aguardando sincronização", faixa.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Servidor_indisponivel_e_distinto_de_offline_e_tranquiliza_sobre_os_dados()
    {
        var cut = Render<ConnectionStatusBanner>(p => p
            .Add(b => b.Status, new SyncStatus(ConnectivityState.ServerUnreachable, 2, null, false, null)));

        var texto = cut.Find("[data-testid=connection-banner]").TextContent;
        Assert.Contains("Servidor indisponível", texto, StringComparison.Ordinal);
        Assert.Contains("salvas neste dispositivo", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Rede_local_sem_internet_ainda_conta_como_conectado()
    {
        var status = new SyncStatus(ConnectivityState.LocalNetwork, 0, DateTime.UtcNow, false, null);

        Assert.Equal("Tudo sincronizado", status.Headline);
        Assert.Contains("rede local", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Indicador_de_sincronizacao_mostra_icone_e_texto_nunca_so_cor()
    {
        var cut = Render<SyncStatusIndicator>(p => p
            .Add(s => s.Status, new SyncStatus(ConnectivityState.Online, 0, DateTime.UtcNow, false, null)));

        var indicador = cut.Find("[data-testid=sync-status]");
        Assert.Contains("Tudo sincronizado", indicador.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(indicador.QuerySelectorAll("span"));
    }

    [Fact]
    public async Task Indicador_dispara_sincronizacao_manual()
    {
        var chamou = false;

        var cut = Render<SyncStatusIndicator>(p => p
            .Add(s => s.Status, new SyncStatus(ConnectivityState.Online, 3, null, false, null))
            .Add(s => s.OnSyncNow, () => chamou = true));

        await cut.Find("[data-testid=sync-status]").ClickAsync(new());

        Assert.True(chamou);
    }

    [Fact]
    public void Faixa_de_notificacao_nao_aparece_quando_esta_tudo_certo()
    {
        var cut = Render<NotificationHealthBanner>(p => p
            .Add(b => b.Status, new NotificationStatus(true, true, true, true, true, false, DateTime.UtcNow, DateTime.Now, [])));

        Assert.Empty(cut.FindAll("[data-testid=notification-health-banner]"));
    }

    [Fact]
    public void Faixa_de_notificacao_lista_os_problemas()
    {
        var cut = Render<NotificationHealthBanner>(p => p
            .Add(b => b.Status, new NotificationStatus(false, false, true, true, false, false, null, null,
                ["Permissão de notificações negada.", "Alarmes exatos não permitidos."])));

        var faixa = cut.Find("[data-testid=notification-health-banner]");

        Assert.Contains("ATENÇÃO", faixa.TextContent, StringComparison.Ordinal);
        Assert.Contains("Permissão de notificações negada.", faixa.TextContent, StringComparison.Ordinal);
        Assert.Contains("Alarmes exatos não permitidos.", faixa.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Notificacao_degradada_e_diferente_de_permissao_negada()
    {
        var degradada = new NotificationStatus(true, false, true, true, true, false, null, null, ["O horário pode não ser exato."]);
        var negada = new NotificationStatus(false, null, false, false, false, false, null, null, ["Permissão negada."]);

        Assert.True(degradada.IsDegraded);
        Assert.False(degradada.IsHealthy);
        Assert.False(negada.IsDegraded);
        Assert.False(negada.IsHealthy);
    }

    [Fact]
    public void Faixa_de_pendencias_soma_apenas_o_que_esta_atrasado()
    {
        var cut = Render<PendingTasksBanner>(p => p.Add(b => b.Groups,
        [
            UiTestData.Pending("Gelo", "22H", overdue: true, "1148", "1150"),
            UiTestData.Pending("SSVV", "AM", overdue: false, "1152", "1153", "1154"),
        ]));

        var faixa = cut.Find("[data-testid=pending-banner]");
        Assert.Contains("2 TAREFAS ATRASADAS", faixa.TextContent, StringComparison.Ordinal);
        Assert.Contains("Gelo 22H", faixa.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("SSVV", faixa.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Faixa_de_pendencias_some_quando_nada_esta_atrasado()
    {
        var cut = Render<PendingTasksBanner>(p => p.Add(b => b.Groups,
        [
            UiTestData.Pending("Gelo", "22H", overdue: false, "1148"),
        ]));

        Assert.Empty(cut.FindAll("[data-testid=pending-banner]"));
    }
}
