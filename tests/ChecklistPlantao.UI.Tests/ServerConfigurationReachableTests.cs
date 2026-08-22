using Bunit;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Client.Abstractions;
using ChecklistPlantao.UI.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// A configuração do servidor precisa ser alcançável a partir da tela de entrada — SEMPRE, e não
/// apenas enquanto falta configurar.
///
/// O beco sem saída era real e apareceu no aparelho: com um endereço salvo (certo ou errado), a
/// entrada não oferecia nenhum caminho de volta. E a configuração só era alcançável de dentro do
/// aplicativo, que exige entrar, que exige o servidor certo. Quem errasse o endereço — ou tivesse
/// a conta bloqueada — ficava preso até reinstalar.
/// </summary>
public sealed class ServerConfigurationReachableTests : BunitContext
{
    private void RegistrarServicos(bool configurado, bool oculto = false, bool exigeHttps = false)
    {
        Services.AddSingleton<IAppSession>(new FakeSession());
        Services.AddSingleton<IServerConfigurationService>(new FakeServerConfiguration(configurado, oculto, exigeHttps));
        Services.AddSingleton<ISyncStatusService>(new FakeSyncStatusService());
    }

    [Fact]
    public void Entrada_sem_servidor_configurado_leva_a_configuracao()
    {
        RegistrarServicos(configurado: false);

        var cut = Render<LoginPage>();

        Assert.NotEmpty(cut.FindAll("[data-testid=login-configure-server]"));
    }

    [Fact]
    public void Entrada_com_servidor_configurado_TAMBEM_leva_a_configuracao()
    {
        RegistrarServicos(configurado: true);

        var cut = Render<LoginPage>();

        Assert.NotEmpty(cut.FindAll("[data-testid=login-configure-server]"));
    }

    [Fact]
    public void Entrada_informa_configuracao_sem_expor_endereco_do_servidor()
    {
        RegistrarServicos(configurado: true);

        var cut = Render<LoginPage>();
        var linha = cut.Find("[data-testid=login-server-line]").TextContent;

        Assert.Contains("Servidor configurado", linha, StringComparison.Ordinal);
        Assert.DoesNotContain("http://servidor:5136", linha, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracao_tem_saida_sem_salvar_quando_ja_ha_servidor()
    {
        RegistrarServicos(configurado: true);

        var cut = Render<SetupPage>();

        Assert.NotEmpty(cut.FindAll("[data-testid=setup-back]"));
        Assert.Contains("http://servidor:5136", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracao_no_primeiro_uso_nao_oferece_voltar()
    {
        RegistrarServicos(configurado: false);

        var cut = Render<SetupPage>();

        // Sem servidor não há para onde voltar: a entrada seria outro beco sem saída.
        Assert.Empty(cut.FindAll("[data-testid=setup-back]"));
    }

    [Fact]
    public void Configuracao_oficial_nao_renderiza_nem_pre_preenche_o_endereco()
    {
        RegistrarServicos(configurado: true, oculto: true, exigeHttps: true);

        var cut = Render<SetupPage>();

        Assert.Contains("Servidor de produção configurado", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("http://servidor:5136", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid=setup-url]"));

        cut.Find("[data-testid=setup-use-custom]").Click();

        var entrada = cut.Find("[data-testid=setup-url]");
        Assert.Equal(string.Empty, entrada.GetAttribute("value"));
        Assert.DoesNotContain("http://servidor:5136", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracao_oficial_recusa_http_antes_de_tentar_conectar()
    {
        RegistrarServicos(configurado: true, oculto: true, exigeHttps: true);

        var cut = Render<SetupPage>();
        cut.Find("[data-testid=setup-use-custom]").Click();
        cut.Find("[data-testid=setup-url]").Input("http://servidor-inseguro.example");
        cut.Find("[data-testid=setup-test]").Click();

        Assert.Contains("somente servidores HTTPS", cut.Markup, StringComparison.Ordinal);
    }

    private sealed class FakeServerConfiguration(bool configurado, bool oculto, bool exigeHttps) : IServerConfigurationService
    {
        public string? ServerUrl => configurado ? "http://servidor:5136" : null;

        public bool IsServerAddressHidden => oculto;

        public bool RequiresHttps => exigeHttps;

        public string DeviceName => "Aparelho de teste";

        public Guid DeviceId { get; } = Guid.CreateVersion7();

        public bool IsConfigured => configurado;

        public Task<ServerProbeResponse?> TestConnectionAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult<ServerProbeResponse?>(null);

        public Task SaveAsync(string url, string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSyncStatusService : ISyncStatusService
    {
        public SyncStatus Current { get; } = SyncStatus.Unknown;

        public event Action<SyncStatus>? Changed;

        public Task SyncNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RefreshConnectivityAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(Current);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSession : IAppSession
    {
        public bool IsAuthenticated => false;

        public string DisplayName => "Teste";

        public EffectiveAccess Access => EffectiveAccess.None;

        public bool PermissionsAreStale => false;

        public DateTime? LastServerValidationUtc => null;

        public Guid? CurrentSectorId => null;

        public string? CurrentSectorName => null;

        public string? LastSignInError => null;

        public event Action? Changed;

        public Task<IReadOnlyList<SectorSummary>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SectorSummary>>([]);

        public Task SelectSectorAsync(Guid sectorId, CancellationToken cancellationToken = default)
        {
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public Task<bool> SignInAsync(string userName, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
