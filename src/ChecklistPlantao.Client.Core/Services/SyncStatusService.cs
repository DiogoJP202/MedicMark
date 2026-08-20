using System.Net.Http.Json;
using System.Text.Json;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Client.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UiResult = ChecklistPlantao.Client.Abstractions.Result;

namespace ChecklistPlantao.Client.Core.Services;

/// <summary>Estado de sincronização observável pela interface.</summary>
public sealed class SyncStatusService(
    IServiceProvider services,
    IConnectivityProbe connectivity,
    ILogger<SyncStatusService> logger) : ISyncStatusService
{
    private readonly SemaphoreSlim _porta = new(1, 1);

    public SyncStatus Current { get; private set; } = SyncStatus.Unknown;

    public event Action<SyncStatus>? Changed;

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        // Uma sincronização por vez: duas em paralelo enviariam o mesmo item duas vezes
        // (inofensivo pela idempotência, mas desperdício de rede e bateria).
        if (!await _porta.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            Publish(Current with { IsSyncing = true });

            using var escopo = services.CreateScope();
            var motor = escopo.ServiceProvider.GetRequiredService<SyncEngine>();
            var fila = escopo.ServiceProvider.GetRequiredService<OutboxWriter>();
            var api = escopo.ServiceProvider.GetRequiredService<IServerApi>();

            var resultado = await motor.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
            var pendentes = await fila.PendingCountAsync(cancellationToken).ConfigureAwait(false);

            await using var db = await escopo.ServiceProvider
                .GetRequiredService<IDbContextFactory<LocalDbContext>>()
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var estado = await db.SyncState.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            Publish(new SyncStatus(Resolve(api), pendentes, estado?.LastSyncAtUtc, false, resultado.Error));

            var reagendador = escopo.ServiceProvider.GetService<IDeviceStartupRescheduler>();

            if (reagendador is not null)
            {
                // Configuração pode ter mudado no servidor: os horários precisam ser refeitos.
                await reagendador.RescheduleAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ciclo de sincronização falhou.");
            Publish(Current with { IsSyncing = false, LastError = ex.Message });
        }
        finally
        {
            _porta.Release();
        }
    }

    /// <summary>
    /// Mede o alcance do servidor e publica o resultado, sem sincronizar.
    ///
    /// A tela de entrada precisa disto: antes do primeiro login nenhuma sincronização acontece,
    /// e o estado inicial (<see cref="SyncStatus.Unknown"/>) era exibido como "Offline" sem que
    /// nada tivesse sido verificado. Aqui o rótulo passa a ser resultado de uma medida.
    ///
    /// Usa <c>ProbeAsync</c> em vez de <c>IsReachable</c> de propósito: uma instância recém-criada
    /// de <c>HttpServerApi</c> é otimista por padrão e responderia "alcançável" sem ter falado
    /// com ninguém.
    /// </summary>
    public async Task RefreshConnectivityAsync(CancellationToken cancellationToken = default)
    {
        if (!connectivity.HasNetwork)
        {
            Publish(Current with { Connectivity = ConnectivityState.Offline });
            return;
        }

        using var escopo = services.CreateScope();
        var endereco = escopo.ServiceProvider.GetRequiredService<IServerAddressProvider>();

        if (!endereco.IsConfigured)
        {
            Publish(Current with { Connectivity = ConnectivityState.ServerUnreachable });
            return;
        }

        try
        {
            var api = escopo.ServiceProvider.GetRequiredService<IServerApi>();
            var resposta = await api.ProbeAsync(endereco.ServerUrl!, cancellationToken).ConfigureAwait(false);

            Publish(Current with
            {
                Connectivity = resposta is null
                    ? ConnectivityState.ServerUnreachable
                    : connectivity.HasInternet ? ConnectivityState.Online : ConnectivityState.LocalNetwork,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Verificação de alcance do servidor falhou.");
            Publish(Current with { Connectivity = ConnectivityState.ServerUnreachable });
        }
    }

    /// <summary>Atualiza apenas os contadores, sem chamar o servidor.</summary>
    public async Task RefreshCountersAsync(CancellationToken cancellationToken = default)
    {
        using var escopo = services.CreateScope();
        var fila = escopo.ServiceProvider.GetRequiredService<OutboxWriter>();
        var api = escopo.ServiceProvider.GetRequiredService<IServerApi>();

        var pendentes = await fila.PendingCountAsync(cancellationToken).ConfigureAwait(false);

        await using var db = await escopo.ServiceProvider
            .GetRequiredService<IDbContextFactory<LocalDbContext>>()
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var estado = await db.SyncState.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        Publish(new SyncStatus(Resolve(api), pendentes, estado?.LastSyncAtUtc, false, Current.LastError));
    }

    /// <summary>
    /// Traduz rede + servidor nos três estados que a interface distingue.
    ///
    /// Recebe o <see cref="IServerApi"/> do escopo que acabou de sincronizar, e não um guardado no
    /// construtor. Além de este serviço ser singleton e não poder segurar um serviço com escopo,
    /// a memória de alcance (<c>IsReachable</c>) vive na instância: só a instância que participou
    /// da sincronização sabe se o servidor respondeu.
    /// </summary>
    private ConnectivityState Resolve(IServerApi api)
    {
        if (!connectivity.HasNetwork)
        {
            return ConnectivityState.Offline;
        }

        if (!api.IsReachable)
        {
            return ConnectivityState.ServerUnreachable;
        }

        return connectivity.HasInternet ? ConnectivityState.Online : ConnectivityState.LocalNetwork;
    }

    private void Publish(SyncStatus status)
    {
        Current = status;
        Changed?.Invoke(status);
    }
}
