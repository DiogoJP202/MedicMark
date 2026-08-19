using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Client.Abstractions;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Client.Core.Sync;

/// <summary>
/// Ouve os avisos do servidor e dispara a sincronização.
///
/// O hub existia só do lado do servidor: o pacote do cliente estava referenciado e nenhum arquivo
/// abria uma conexão. Sem isto, marcar em um aparelho não avisava o outro — a convergência
/// dependia do próximo ciclo de sincronização, e o critério 16 não se sustentava.
///
/// O aviso NUNCA traz estado: ele diz que existe novidade e esta classe manda buscar. É o que
/// permite perder uma mensagem sem gerar divergência — na pior hipótese o aparelho descobre no
/// ciclo seguinte, exatamente como antes.
/// </summary>
public sealed class RealtimeSyncClient(
    IServiceProvider services,
    AuthenticatedSessionState session,
    IConnectivityProbe conectividade,
    ISyncStatusService sincronizacao,
    ILogger<RealtimeSyncClient> logger) : IAsyncDisposable
{
    /// <summary>Uma operação de conexão por vez: entrar e sair podem chegar quase juntos.</summary>
    private readonly SemaphoreSlim _porta = new(1, 1);

    /// <summary>Cancela a espera da retentativa quando a rede volta ou o aplicativo encerra.</summary>
    private CancellationTokenSource? _retentativa;

    private HubConnection? _conexao;

    /// <summary>Setor em que este dispositivo está inscrito, para desinscrever ao trocar.</summary>
    private Guid? _setorInscrito;

    private bool _descartado;

    /// <summary>Verdadeiro quando o aviso em tempo real está de fato chegando.</summary>
    public bool IsConnected => _conexao?.State == HubConnectionState.Connected;

    /// <summary>
    /// Costura para os testes ligarem a conexão ao servidor em memória, que não tem socket.
    /// Em produção fica nula e o SignalR usa a pilha HTTP normal.
    ///
    /// Existe porque a alternativa era não testar esta classe — e foi exatamente assim que o
    /// cliente do hub ficou ausente sem ninguém perceber.
    /// </summary>
    public Action<HttpConnectionOptions>? ConfigureConnection { get; set; }

    /// <summary>
    /// Liga o acompanhamento. Chamado na subida do aplicativo — a conexão em si só acontece
    /// quando houver usuário autenticado e endereço de servidor.
    /// </summary>
    public void Start()
    {
        session.Changed += AoMudarSessao;

        // A volta da rede é o melhor momento para tentar de novo, e não custa espera nenhuma.
        conectividade.ConnectivityChanged += AoMudarConectividade;

        AoMudarSessao();
    }

    private void AoMudarSessao() => _ = AjustarAsync();

    private void AoMudarConectividade()
    {
        // Encurta a espera da retentativa em vez de esperar o próximo intervalo.
        _retentativa?.Cancel();
        _ = AjustarAsync();
    }

    private async Task AjustarAsync()
    {
        if (_descartado)
        {
            return;
        }

        await _porta.WaitAsync().ConfigureAwait(false);

        try
        {
            var endereco = EnderecoDoServidor();

            if (!session.IsAuthenticated || endereco is null)
            {
                await DesconectarAsync().ConfigureAwait(false);
                return;
            }

            await ConectarAsync(endereco).ConfigureAwait(false);
            await AjustarSetorAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tempo real é complemento. Falhar aqui não pode atrapalhar quem está trabalhando:
            // os demais gatilhos de sincronização continuam valendo.
            logger.LogDebug(ex, "Não foi possível ajustar a conexão de tempo real.");

            // Servidor fora do ar na hora de conectar. O WithAutomaticReconnect NÃO cobre este
            // caso: ele só age depois de uma conexão que chegou a dar certo. Sem esta retentativa,
            // o aparelho ficaria mudo até alguém entrar ou sair da conta.
            AgendarRetentativa();
        }
        finally
        {
            _porta.Release();
        }
    }

    /// <summary>
    /// Espera crescente antes de tentar de novo, começando em 5 s e parando de crescer em 1 min.
    ///
    /// Sem laço fixo de fundo: o temporizador só existe enquanto DEVERIA haver conexão e não há.
    /// Conectado, nada roda — o requisito sobre bateria é explícito.
    /// </summary>
    private void AgendarRetentativa()
    {
        if (_descartado || _retentativa is not null)
        {
            return;
        }

        var fonte = new CancellationTokenSource();
        _retentativa = fonte;

        _ = Task.Run(async () =>
        {
            var espera = TimeSpan.FromSeconds(5);

            try
            {
                while (!fonte.IsCancellationRequested && !_descartado && !IsConnected)
                {
                    await Task.Delay(espera, fonte.Token).ConfigureAwait(false);
                    espera = TimeSpan.FromSeconds(Math.Min(espera.TotalSeconds * 2, 60));

                    await AjustarAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Rede voltou ou o aplicativo encerrou: quem cancelou já cuidou do próximo passo.
            }
            finally
            {
                Interlocked.CompareExchange(ref _retentativa, null, fonte);
                fonte.Dispose();
            }
        });
    }

    private string? EnderecoDoServidor()
    {
        using var escopo = services.CreateScope();
        var endereco = escopo.ServiceProvider.GetRequiredService<IServerAddressProvider>();

        return endereco.IsConfigured ? endereco.ServerUrl : null;
    }

    private async Task ConectarAsync(string enderecoDoServidor)
    {
        if (_conexao is not null)
        {
            if (_conexao.State != HubConnectionState.Disconnected)
            {
                return;
            }

            await DescartarConexaoAsync().ConfigureAwait(false);
        }

        var url = new Uri(new Uri(HttpServerApi.NormalizeUrl(enderecoDoServidor)), SyncHubEvents.HubPath.TrimStart('/'));

        var conexao = new HubConnectionBuilder()
            .WithUrl(url, opcoes =>
            {
                opcoes.AccessTokenProvider = TokenAtualAsync;
                ConfigureConnection?.Invoke(opcoes);
            })
            // Política própria: o WithAutomaticReconnect sem argumentos tenta em 0s, 2s, 10s e 30s
            // e DESISTE. Um servidor fora do ar por mais de um minuto — reinício, atualização,
            // queda de rede no corredor — deixava o aparelho mudo até alguém sair e entrar de novo.
            .WithAutomaticReconnect(new ReconexaoSemDesistir())
            .Build();

        RegistrarEventos(conexao);

        // Reconectou: os grupos do servidor foram perdidos com a conexão anterior, e o que
        // aconteceu durante a queda precisa ser buscado.
        conexao.Reconnected += async _ =>
        {
            _setorInscrito = null;
            await AjustarSetorAsync().ConfigureAwait(false);
            await SincronizarAsync(SyncHubEvents.ChangesAvailable).ConfigureAwait(false);
        };

        // Fechou de vez: sobrou algum caso que a reconexão automática não cobre, como o servidor
        // recusando a credencial. A retentativa própria assume.
        conexao.Closed += _ =>
        {
            AgendarRetentativa();
            return Task.CompletedTask;
        };

        _conexao = conexao;

        await conexao.StartAsync().ConfigureAwait(false);

        // Conectou: encerra a espera, se houver uma rodando.
        _retentativa?.Cancel();

        logger.LogInformation("Conectado ao hub de avisos em {Url}.", url);
    }

    /// <summary>
    /// O token vem do depósito seguro a cada tentativa, e não uma vez só: o SignalR pede de novo
    /// em cada reconexão, e um token vencido guardado aqui deixaria o aparelho mudo.
    /// </summary>
    private async Task<string?> TokenAtualAsync()
    {
        using var escopo = services.CreateScope();
        var tokens = escopo.ServiceProvider.GetRequiredService<ITokenStore>();

        return await tokens.GetAccessTokenAsync().ConfigureAwait(false);
    }

    private void RegistrarEventos(HubConnection conexao)
    {
        string[] eventos =
        [
            SyncHubEvents.ChangesAvailable,
            SyncHubEvents.SessionUpdated,
            SyncHubEvents.SessionClosed,
            SyncHubEvents.ConfigurationChanged,
            SyncHubEvents.PermissionsChanged,
        ];

        foreach (var evento in eventos)
        {
            var nome = evento;
            conexao.On<SyncNotification>(nome, _ => SincronizarAsync(nome));
        }
    }

    private async Task SincronizarAsync(string evento)
    {
        logger.LogDebug("Aviso {Evento} recebido; buscando as novidades.", evento);

        try
        {
            await sincronizacao.SyncNowAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Sincronização disparada pelo aviso {Evento} falhou.", evento);
        }
    }

    /// <summary>
    /// Inscrição explícita no setor atual.
    ///
    /// Necessária, e não redundante: o servidor inscreve a conexão apenas nos setores EXPLÍCITOS
    /// do usuário, e quem tem acesso a todos — o grupo Administradores é semeado assim, com
    /// GrantsAllSectors e nenhum setor nominal — não entraria em grupo nenhum. Sem esta chamada,
    /// justamente o administrador ficaria sem aviso.
    /// </summary>
    private async Task AjustarSetorAsync()
    {
        if (_conexao is null || _conexao.State != HubConnectionState.Connected)
        {
            return;
        }

        var setor = session.CurrentSectorId;

        if (setor == _setorInscrito)
        {
            return;
        }

        if (_setorInscrito is { } anterior)
        {
            await InvocarAsync("UnsubscribeSector", anterior).ConfigureAwait(false);
        }

        if (setor is { } atual && await InvocarAsync("SubscribeSector", atual).ConfigureAwait(false))
        {
            _setorInscrito = atual;
            return;
        }

        _setorInscrito = null;
    }

    private async Task<bool> InvocarAsync(string metodo, Guid setorId)
    {
        try
        {
            await _conexao!.InvokeAsync(metodo, setorId).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Falha ao chamar {Metodo} para o setor {Setor}.", metodo, setorId);
            return false;
        }
    }

    private async Task DesconectarAsync()
    {
        if (_conexao is null)
        {
            return;
        }

        await DescartarConexaoAsync().ConfigureAwait(false);
        logger.LogInformation("Desconectado do hub de avisos.");
    }

    private async Task DescartarConexaoAsync()
    {
        var conexao = _conexao;
        _conexao = null;
        _setorInscrito = null;

        if (conexao is null)
        {
            return;
        }

        try
        {
            await conexao.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Falha ao encerrar a conexão do hub.");
        }
        finally
        {
            await conexao.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_descartado)
        {
            return;
        }

        _descartado = true;
        session.Changed -= AoMudarSessao;
        conectividade.ConnectivityChanged -= AoMudarConectividade;

        _retentativa?.Cancel();

        await DescartarConexaoAsync().ConfigureAwait(false);
        _porta.Dispose();
    }
}

/// <summary>
/// Reconexão que não desiste, com espera crescente até 1 minuto.
///
/// A política padrão do SignalR para de tentar depois de cerca de 40 segundos. Num plantão isso
/// é pouco: o servidor pode reiniciar, a rede do corredor pode cair, e o aparelho não pode ficar
/// mudo esperando alguém sair e entrar da conta para voltar a receber avisos.
/// </summary>
internal sealed class ReconexaoSemDesistir : IRetryPolicy
{
    private static readonly TimeSpan[] Primeiras =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        ArgumentNullException.ThrowIfNull(retryContext);

        var tentativa = retryContext.PreviousRetryCount;

        return tentativa < Primeiras.Length ? Primeiras[tentativa] : TimeSpan.FromMinutes(1);
    }
}
