using Bunit;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Client.Abstractions;
using ChecklistPlantao.UI.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// Estado da conexão na tela de entrada.
///
/// Antes do primeiro login nada dispara sincronização, e o estado inicial
/// (<see cref="SyncStatus.Unknown"/>) carrega <see cref="ConnectivityState.Offline"/>. A tela
/// exibia isso como fato — dizia "Offline" e "Sem servidor no momento" com o servidor no ar,
/// logo após um "Testar conexão" bem-sucedido. Agora a tela mede antes de afirmar.
/// </summary>
public sealed class LoginConnectionStateTests : BunitContext
{
    private FakeSyncStatus Sync { get; } = new();

    private void RegistrarServicos()
    {
        Services.AddSingleton<IAppSession>(new FakeSession());
        Services.AddSingleton<IServerConfigurationService>(new FakeServerConfiguration());
        Services.AddSingleton<ISyncStatusService>(Sync);
    }

    [Fact]
    public void Enquanto_mede_nao_afirma_que_esta_offline()
    {
        RegistrarServicos();

        var cut = Render<LoginPage>();

        Assert.Contains("Verificando o servidor…", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Sem servidor no momento", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Servidor_respondendo_mostra_conectado()
    {
        RegistrarServicos();

        var cut = Render<LoginPage>();
        Sync.Concluir(ConnectivityState.Online);

        cut.WaitForAssertion(() =>
            Assert.Contains("Tudo sincronizado", cut.Markup, StringComparison.Ordinal));

        Assert.DoesNotContain("Sem servidor no momento", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Servidor_sem_resposta_mostra_indisponivel_e_o_aviso()
    {
        RegistrarServicos();

        var cut = Render<LoginPage>();
        Sync.Concluir(ConnectivityState.ServerUnreachable);

        cut.WaitForAssertion(() =>
            Assert.Contains("Servidor indisponível", cut.Markup, StringComparison.Ordinal));

        Assert.Contains("Sem servidor no momento", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tela_de_entrada_pede_a_medicao()
    {
        RegistrarServicos();

        Render<LoginPage>();

        Assert.Equal(1, Sync.ChamadasDeVerificacao);
    }

    /// <summary>Só conclui a medição quando o teste mandar — é o que permite ver o estado do meio.</summary>
    private sealed class FakeSyncStatus : ISyncStatusService
    {
        private readonly TaskCompletionSource _porta = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SyncStatus Current { get; private set; } = SyncStatus.Unknown;

        public int ChamadasDeVerificacao { get; private set; }

        public event Action<SyncStatus>? Changed;

        public Task SyncNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RefreshConnectivityAsync(CancellationToken cancellationToken = default)
        {
            ChamadasDeVerificacao++;
            return _porta.Task;
        }

        public void Concluir(ConnectivityState estado)
        {
            Current = Current with { Connectivity = estado };
            Changed?.Invoke(Current);
            _porta.SetResult();
        }
    }

    private sealed class FakeServerConfiguration : IServerConfigurationService
    {
        public string? ServerUrl => "http://servidor:5136";

        public string DeviceName => "Aparelho de teste";

        public Guid DeviceId { get; } = Guid.CreateVersion7();

        public bool IsConfigured => true;

        public Task<ServerProbeResponse?> TestConnectionAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult<ServerProbeResponse?>(null);

        public Task SaveAsync(string url, string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
}
