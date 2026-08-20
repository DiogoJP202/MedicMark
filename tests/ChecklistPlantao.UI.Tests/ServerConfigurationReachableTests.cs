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
    private void RegistrarServicos(bool configurado)
    {
        Services.AddSingleton<IAppSession>(new FakeSession());
        Services.AddSingleton<IServerConfigurationService>(new FakeServerConfiguration(configurado));
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
    public void Entrada_mostra_qual_servidor_esta_configurado()
    {
        RegistrarServicos(configurado: true);

        var cut = Render<LoginPage>();

        // Ver o endereço na tela é o que permite perceber o engano sem precisar procurar.
        Assert.Contains("http://servidor:5136", cut.Find("[data-testid=login-server-line]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracao_tem_saida_sem_salvar_quando_ja_ha_servidor()
    {
        RegistrarServicos(configurado: true);

        var cut = Render<SetupPage>();

        Assert.NotEmpty(cut.FindAll("[data-testid=setup-back]"));
    }

    [Fact]
    public void Configuracao_no_primeiro_uso_nao_oferece_voltar()
    {
        RegistrarServicos(configurado: false);

        var cut = Render<SetupPage>();

        // Sem servidor não há para onde voltar: a entrada seria outro beco sem saída.
        Assert.Empty(cut.FindAll("[data-testid=setup-back]"));
    }

    private sealed class FakeServerConfiguration(bool configurado) : IServerConfigurationService
    {
        public string? ServerUrl => configurado ? "http://servidor:5136" : null;

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
