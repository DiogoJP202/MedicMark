using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.UI.Abstractions;
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
    ISyncStatusService sincronizacao,
    ILogger<RealtimeSyncClient> logger) : IAsyncDisposable
{
    /// <summary>Uma operação de conexão por vez: entrar e sair podem chegar quase juntos.</summary>
    private readonly SemaphoreSlim _porta = new(1, 1);

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
        AoMudarSessao();
    }

    private void AoMudarSessao() => _ = AjustarAsync();

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
        }
        finally
        {
            _porta.Release();
        }
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
            .WithAutomaticReconnect()
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

        _conexao = conexao;

        await conexao.StartAsync().ConfigureAwait(false);
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

        await DescartarConexaoAsync().ConfigureAwait(false);
        _porta.Dispose();
    }
}
