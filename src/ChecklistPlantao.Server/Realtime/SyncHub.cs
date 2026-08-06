using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChecklistPlantao.Server.Realtime;

/// <summary>
/// Hub de avisos. Nunca transporta estado: informa que existe algo novo e o cliente busca pelo
/// endpoint de pull. Assim uma mensagem perdida não gera divergência — na pior hipótese o
/// dispositivo descobre a novidade no próximo ciclo de sincronização.
/// </summary>
[Authorize]
public sealed class SyncHub(ICurrentUser currentUser, ILogger<SyncHub> logger) : Hub
{
    public static string SectorGroup(Guid sectorId) => $"setor:{sectorId}";

    public static string UserGroup(Guid userId) => $"usuario:{userId}";

    public override async Task OnConnectedAsync()
    {
        // O usuário entra apenas nos grupos dos setores que realmente pode ver.
        // Sem isso, um cliente poderia se inscrever em um setor alheio e inferir atividade dele.
        foreach (var sectorId in currentUser.Access.ExplicitSectorIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SectorGroup(sectorId)).ConfigureAwait(false);
        }

        if (currentUser.IsAuthenticated)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(currentUser.UserId)).ConfigureAwait(false);
        }

        logger.LogDebug("Dispositivo conectado ao hub: conexão {ConnectionId}.", Context.ConnectionId);

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Inscrição explícita em um setor, para quem tem acesso a todos e por isso não recebeu
    /// claims individuais. A autorização é reavaliada aqui — o cliente não escolhe sozinho.
    /// </summary>
    public async Task SubscribeSector(Guid sectorId)
    {
        if (!currentUser.Access.CanAccessSector(sectorId))
        {
            throw new HubException("Você não tem acesso a este setor.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, SectorGroup(sectorId)).ConfigureAwait(false);
    }

    public Task UnsubscribeSector(Guid sectorId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, SectorGroup(sectorId));
}

/// <summary>Publica os avisos do hub. Encapsulado para os controllers não conhecerem SignalR.</summary>
public sealed class SyncNotifier(IHubContext<SyncHub> hub, IClock clock, ILogger<SyncNotifier> logger)
{
    public Task NotifyChangesAsync(Guid? sectorId, long cursor, CancellationToken cancellationToken = default) =>
        SendAsync(SyncHubEvents.ChangesAvailable, sectorId, cursor, cancellationToken);

    public Task NotifySessionUpdatedAsync(Guid? sectorId, CancellationToken cancellationToken = default) =>
        SendAsync(SyncHubEvents.SessionUpdated, sectorId, 0, cancellationToken);

    public Task NotifySessionClosedAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync(
            SyncHubEvents.SessionClosed,
            new SyncNotification(Domain.Sync.SyncEntityTypes.OperationalSession, null, 0, clock.UtcNow),
            cancellationToken);

    public Task NotifyConfigurationChangedAsync(CancellationToken cancellationToken = default) =>
        SendAsync(SyncHubEvents.ConfigurationChanged, null, 0, cancellationToken);

    public Task NotifyPermissionsChangedAsync(Guid userId, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(SyncHub.UserGroup(userId)).SendAsync(
            SyncHubEvents.PermissionsChanged,
            new SyncNotification(Domain.Sync.SyncEntityTypes.AppUser, null, 0, clock.UtcNow),
            cancellationToken);

    private async Task SendAsync(string eventName, Guid? sectorId, long cursor, CancellationToken cancellationToken)
    {
        var notification = new SyncNotification(
            eventName,
            sectorId,
            cursor,
            clock.UtcNow);

        var target = sectorId is null ? hub.Clients.All : hub.Clients.Group(SyncHub.SectorGroup(sectorId.Value));

        try
        {
            await target.SendAsync(eventName, notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Aviso em tempo real é complemento, não fonte de verdade: falhar aqui não pode
            // derrubar a requisição que já persistiu a alteração.
            logger.LogWarning(ex, "Falha ao publicar o aviso {Evento} no hub.", eventName);
        }
    }
}
