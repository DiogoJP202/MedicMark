using Bunit;
using Bunit.TestDoubles;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Client.Abstractions;
using ChecklistPlantao.UI.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// Desvio da primeira execução.
///
/// O aplicativo abre em "/", que é o painel. Sem autenticação o painel só sabe mostrar
/// "Verificando o acesso…" — e como o menu do layout só aparece autenticado, nenhuma outra tela
/// alcançável leva à configuração. O aparelho ficava preso nessa espera para sempre.
/// </summary>
public sealed class DashboardRoutingTests : BunitContext
{
    private FakeSession Session { get; } = new();

    private FakeServerConfiguration ServerConfiguration { get; } = new();

    private void RegistrarServicos()
    {
        Services.AddSingleton<IAppSession>(Session);
        Services.AddSingleton<IServerConfigurationService>(ServerConfiguration);
        Services.AddSingleton<IChecklistStore>(new FakeStore());
        Services.AddSingleton<ISyncStatusService>(new FakeSyncStatus());
        Services.AddSingleton<INotificationStatusService>(new FakeNotificationStatus());
    }

    [Fact]
    public void Sem_servidor_configurado_vai_para_a_configuracao()
    {
        ServerConfiguration.IsConfigured = false;
        RegistrarServicos();

        Render<DashboardPage>();

        Assert.EndsWith("/configuracao", Services.GetRequiredService<BunitNavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Com_servidor_configurado_mas_sem_sessao_vai_para_o_login()
    {
        ServerConfiguration.IsConfigured = true;
        RegistrarServicos();

        Render<DashboardPage>();

        Assert.EndsWith("/entrar", Services.GetRequiredService<BunitNavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Autenticado_permanece_no_painel()
    {
        ServerConfiguration.IsConfigured = true;
        Session.IsAuthenticated = true;
        RegistrarServicos();

        var navegacao = Services.GetRequiredService<BunitNavigationManager>();
        var inicial = navegacao.Uri;

        Render<DashboardPage>();

        Assert.Equal(inicial, navegacao.Uri);
    }

    /// <summary>
    /// "Dispositivo" saiu da barra de navegação porque continua alcançável por aqui. Se este
    /// cartão sumir, a tela de saúde das notificações fica sem porta — e ela é o critério 22.
    /// </summary>
    [Fact]
    public void O_painel_continua_levando_ao_estado_do_dispositivo()
    {
        ServerConfiguration.IsConfigured = true;
        Session.IsAuthenticated = true;
        Session.CurrentSectorId = Guid.CreateVersion7();
        Session.CurrentSectorName = "Oeste";
        RegistrarServicos();

        var cut = Render<DashboardPage>();

        Assert.Contains("Estado do dispositivo", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>O desvio não pode empilhar histórico: "voltar" devolveria o usuário à espera.</summary>
    [Fact]
    public void O_desvio_substitui_a_entrada_no_historico()
    {
        ServerConfiguration.IsConfigured = false;
        RegistrarServicos();

        Render<DashboardPage>();

        var historico = Services.GetRequiredService<BunitNavigationManager>().History;
        Assert.NotEmpty(historico);
        Assert.True(historico.Last().Options.ReplaceHistoryEntry);
    }

    private sealed class FakeSession : IAppSession
    {
        public bool IsAuthenticated { get; set; }

        public string DisplayName => "Teste";

        public EffectiveAccess Access => EffectiveAccess.None;

        public bool PermissionsAreStale => false;

        public DateTime? LastServerValidationUtc => null;

        public Guid? CurrentSectorId { get; set; }

        public string? CurrentSectorName { get; set; }

        public string? LastSignInError => null;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<SectorSummary>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SectorSummary>>([]);

        public Task SelectSectorAsync(Guid sectorId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> SignInAsync(string userName, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeServerConfiguration : IServerConfigurationService
    {
        public string? ServerUrl => IsConfigured ? "http://servidor:5136" : null;

        public string DeviceName => "Aparelho de teste";

        public Guid DeviceId { get; } = Guid.CreateVersion7();

        public bool IsConfigured { get; set; }

        public Task<ServerProbeResponse?> TestConnectionAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult<ServerProbeResponse?>(null);

        public Task SaveAsync(string url, string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSyncStatus : ISyncStatusService
    {
        public SyncStatus Current => SyncStatus.Unknown;

        public event Action<SyncStatus>? Changed
        {
            add { }
            remove { }
        }

        public Task SyncNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RefreshConnectivityAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeNotificationStatus : INotificationStatusService
    {
        public NotificationStatus Current => NotificationStatus.Unknown;

        public event Action<NotificationStatus>? Changed
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> SendTestNotificationAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task OpenSystemSettingsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeStore : IChecklistStore
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<ChecklistTemplateDto>> GetTemplatesAsync(Guid sectorId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChecklistTemplateDto>>([]);

        public Task<ChecklistBoard> GetBoardAsync(Guid sectorId, Guid templateId, CancellationToken cancellationToken = default) =>
            Task.FromResult(UiTestData.Board());

        public Task<ToggleOutcome> ToggleCellAsync(ChecklistCell cell, bool isCompleted, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToggleOutcome(true, null, null));

        public Task<BedMarkersView> GetBedMarkersAsync(Guid sessionId, Guid bedId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BedMarkersView>> GetAllBedMarkersAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BedMarkersView>>([]);

        public Task<ToggleOutcome> ToggleMarkerAsync(Guid sessionId, Guid bedId, BedMarkerState marker, bool isSelected, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToggleOutcome(true, null, null));

        public Task<IReadOnlyList<PendingGroup>> GetPendingAsync(Guid sectorId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PendingGroup>>([]);

        public Task<SessionSummaryDto> GetSessionSummaryAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CloseSessionAsync(Guid sessionId, bool confirmWithPending, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ResetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
