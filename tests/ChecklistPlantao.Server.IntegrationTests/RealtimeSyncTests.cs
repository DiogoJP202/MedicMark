using System.Net.Http.Json;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChecklistPlantao.Server.IntegrationTests;

/// <summary>
/// Aviso em tempo real, do servidor até o cliente.
///
/// O hub estava pronto no servidor e nenhum arquivo do cliente abria uma conexão: marcar em um
/// aparelho não avisava o outro. Estes testes ligam o <see cref="RealtimeSyncClient"/> real ao
/// hub real e verificam que o aviso chega e provoca a busca.
/// </summary>
public sealed class RealtimeSyncTests(ChecklistServerFactory factory) : IClassFixture<ChecklistServerFactory>
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Sem_sessao_autenticada_o_cliente_nao_conecta()
    {
        await using var cliente = await MontarAsync(autenticar: false);

        await Task.Delay(300);

        Assert.False(cliente.Cliente.IsConnected);
    }

    [Fact]
    public async Task Com_sessao_autenticada_o_cliente_conecta()
    {
        await using var cliente = await MontarAsync();

        await EsperarAsync(() => cliente.Cliente.IsConnected);

        Assert.True(cliente.Cliente.IsConnected);
    }

    /// <summary>
    /// Alteração de configuração vai para todos os conectados. É o caminho completo: o servidor
    /// publica, o cliente ouve e manda sincronizar.
    /// </summary>
    [Fact]
    public async Task Alteracao_de_configuracao_no_servidor_provoca_sincronizacao_no_cliente()
    {
        await using var cliente = await MontarAsync();
        await EsperarAsync(() => cliente.Cliente.IsConnected);

        var admin = await factory.CreateAdminClientAsync();
        await admin.PostAsJsonAsync("/api/admin/markers", new SaveMarkerRequest($"M{DateTime.UtcNow.Ticks % 100000}", 80, true, 0));

        await EsperarAsync(() => cliente.Sincronizacao.Chamadas > 0);

        Assert.True(cliente.Sincronizacao.Chamadas > 0);
    }

    /// <summary>
    /// A armadilha do hub: <c>OnConnectedAsync</c> inscreve apenas nos setores EXPLÍCITOS, e o
    /// grupo Administradores é semeado com <c>GrantsAllSectors</c> e nenhum setor nominal. Sem a
    /// chamada a <c>SubscribeSector</c>, justamente o administrador ficaria sem os avisos de setor.
    /// </summary>
    [Fact]
    public async Task Aviso_de_setor_chega_a_quem_tem_acesso_a_todos_os_setores()
    {
        var admin = await factory.CreateAdminClientAsync();
        var bootstrap = (await admin.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var setor = bootstrap.Sectors[0].Id;

        await using var cliente = await MontarAsync(setorId: setor);
        await EsperarAsync(() => cliente.Cliente.IsConnected);

        // Dá tempo de a inscrição no setor chegar ao servidor antes de provocar o aviso.
        await EsperarAsync(() => cliente.Cliente.IsConnected, TimeSpan.FromSeconds(2));
        cliente.Sincronizacao.Zerar();

        await admin.PostAsJsonAsync($"/api/sectors/{setor}/sessions", new { });

        await EsperarAsync(() => cliente.Sincronizacao.Chamadas > 0);

        Assert.True(
            cliente.Sincronizacao.Chamadas > 0,
            "O aviso do setor não chegou: provavelmente o SubscribeSector não foi chamado.");
    }

    /// <summary>
    /// Servidor fora do ar quando o aplicativo tenta conectar, e de volta em seguida.
    ///
    /// Foi o defeito encontrado em campo: o WithAutomaticReconnect padrão desiste em cerca de
    /// 40 segundos, e ele nem se aplica quando a PRIMEIRA conexão falha. O aparelho ficava mudo
    /// até alguém sair e entrar da conta — parecendo funcionar, sem funcionar.
    /// </summary>
    [Fact]
    public async Task Servidor_fora_do_ar_na_conexao_e_o_cliente_tenta_de_novo()
    {
        using var manipulador = new ServidorQueVolta(factory.Server.CreateHandler(), recusasIniciais: 2);

        await using var cliente = await MontarAsync(manipulador: manipulador);

        Assert.False(cliente.Cliente.IsConnected);

        await EsperarAsync(() => cliente.Cliente.IsConnected, TimeSpan.FromSeconds(30));

        Assert.True(
            cliente.Cliente.IsConnected,
            "O cliente desistiu depois da primeira recusa: sem retentativa, o aparelho fica mudo.");
    }

    // ------------------------------------------------------------------ apoio

    private static async Task EsperarAsync(Func<bool> condicao, TimeSpan? limite = null)
    {
        var fim = DateTime.UtcNow + (limite ?? Espera);

        while (DateTime.UtcNow < fim && !condicao())
        {
            await Task.Delay(50);
        }
    }

    private async Task<Montagem> MontarAsync(bool autenticar = true, Guid? setorId = null, HttpMessageHandler? manipulador = null)
    {
        var login = await ChecklistServerFactory.LoginAsync(
            factory.CreateClient(),
            ChecklistServerFactory.AdminUserName,
            ChecklistServerFactory.AdminPassword);

        var services = new ServiceCollection();
        services.AddScoped<IServerAddressProvider>(_ => new EnderecoFalso("http://localhost"));
        services.AddScoped<ITokenStore>(_ => new TokenStoreFalso(login.Tokens.AccessToken));

        var provider = services.BuildServiceProvider();
        var estado = new AuthenticatedSessionState();
        var espia = new SincronizacaoEspia();

        var cliente = new RealtimeSyncClient(
            provider,
            estado,
            new ConectividadeFalsa(),
            espia,
            NullLogger<RealtimeSyncClient>.Instance)
        {
            // O servidor de teste não tem socket: o SignalR precisa do manipulador em memória.
            ConfigureConnection = opcoes => opcoes.HttpMessageHandlerFactory =
                _ => manipulador ?? factory.Server.CreateHandler(),
        };

        cliente.Start();

        if (autenticar)
        {
            estado.SignIn(Guid.CreateVersion7(), "admin", "Administrador", EffectiveAccess.None, DateTime.UtcNow);

            if (setorId is { } setor)
            {
                estado.SelectSector(setor, "Oeste");
            }
        }

        return new Montagem(cliente, espia, provider);
    }

    private sealed record Montagem(RealtimeSyncClient Cliente, SincronizacaoEspia Sincronizacao, ServiceProvider Provider)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Cliente.DisposeAsync();
            await Provider.DisposeAsync();
        }
    }

    /// <summary>Conta as buscas provocadas pelos avisos — é o que prova que o aviso chegou.</summary>
    private sealed class SincronizacaoEspia : ISyncStatusService
    {
        private int _chamadas;

        public int Chamadas => Volatile.Read(ref _chamadas);

        public SyncStatus Current => SyncStatus.Unknown;

        public event Action<SyncStatus>? Changed
        {
            add { }
            remove { }
        }

        public void Zerar() => Volatile.Write(ref _chamadas, 0);

        public Task SyncNowAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _chamadas);
            return Task.CompletedTask;
        }

        public Task RefreshConnectivityAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TokenStoreFalso(string token) : ITokenStore
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(token);

        public Task SaveAsync(string accessToken, DateTime accessExpiresAtUtc, string refreshToken, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ConectividadeFalsa : IConnectivityProbe
    {
        public bool HasNetwork => true;

        public bool HasInternet => true;

        public event Action? ConnectivityChanged
        {
            add { }
            remove { }
        }
    }

    /// <summary>
    /// Recusa as primeiras tentativas e depois deixa passar — é o servidor que estava fora do ar
    /// e voltou.
    /// </summary>
    private sealed class ServidorQueVolta(HttpMessageHandler real, int recusasIniciais) : HttpMessageHandler
    {
        private int _recusas;

        public bool JaLiberou => Volatile.Read(ref _recusas) >= recusasIniciais;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _recusas) <= recusasIniciais)
            {
                throw new HttpRequestException("Servidor fora do ar (simulado).");
            }

            return new HttpMessageInvoker(real).SendAsync(request, cancellationToken);
        }
    }

    private sealed class EnderecoFalso(string url) : IServerAddressProvider
    {
        public string? ServerUrl => url;

        public bool IsConfigured => true;
    }
}
