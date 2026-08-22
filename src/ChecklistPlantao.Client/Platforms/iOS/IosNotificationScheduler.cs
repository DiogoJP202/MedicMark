using ChecklistPlantao.Client.Core.Notifications;
using Foundation;
using Microsoft.Extensions.Logging;
using UserNotifications;

namespace ChecklistPlantao.Client.Platforms.iOS;

/// <summary>
/// Agenda notificações locais no centro de notificações do iOS. Os pedidos pertencem ao sistema
/// operacional e continuam válidos com o aplicativo fechado; não dependem de push nem de rede.
/// </summary>
public sealed class IosNotificationScheduler(ILogger<IosNotificationScheduler> logger) : ILocalNotificationScheduler
{
    internal const string RouteKey = "checklistplantao_route";
    private const int MaxPendingNotifications = 64;

    private static UNUserNotificationCenter Center => UNUserNotificationCenter.Current;

    public bool RequiresAppRunning => false;

    public bool SupportsExactTiming => true;

    public async Task ScheduleAsync(
        IReadOnlyList<ScheduledNotification> notifications,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        var now = DateTimeOffset.Now;
        var pending = notifications
            .Where(notification => notification.FireAt > now)
            .OrderBy(notification => notification.FireAt)
            .Take(MaxPendingNotifications)
            .ToArray();

        var futureCount = notifications.Count(notification => notification.FireAt > now);

        if (futureCount > MaxPendingNotifications)
        {
            logger.LogWarning(
                "O iOS aceita no máximo {Limite} notificações locais pendentes. " +
                "Foram mantidas as ocorrências mais próximas de um total de {Total}.",
                MaxPendingNotifications,
                futureCount);
        }

        foreach (var notification in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var delay = notification.FireAt - now;

            using var content = CreateContent(notification.Title, notification.Body, notification.DeepLink);
            using var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(
                Math.Max(1, delay.TotalSeconds),
                repeats: false);
            using var request = UNNotificationRequest.FromIdentifier(notification.Id, content, trigger);
            await Center.AddNotificationRequestAsync(request);
        }
    }

    public Task CancelAsync(IEnumerable<string> notificationIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notificationIds);
        cancellationToken.ThrowIfCancellationRequested();
        Center.RemovePendingNotificationRequests([.. notificationIds]);
        return Task.CompletedTask;
    }

    public Task CancelAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Center.RemoveAllPendingNotificationRequests();
        return Task.CompletedTask;
    }

    public async Task ShowNowAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var content = CreateContent(title, body, route: null);
        using var request = UNNotificationRequest.FromIdentifier(
            $"teste-{Guid.NewGuid():N}",
            content,
            trigger: null);
        await Center.AddNotificationRequestAsync(request);
    }

    public async Task<IReadOnlyList<string>> GetScheduledIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requests = await Center.GetPendingNotificationRequestsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return requests.Select(request => request.Identifier).ToArray();
    }

    private static UNMutableNotificationContent CreateContent(string title, string body, string? route)
    {
        var content = new UNMutableNotificationContent
        {
            Title = title,
            Body = body,
            Sound = UNNotificationSound.Default,
        };

        if (!string.IsNullOrWhiteSpace(route))
        {
            content.UserInfo = NSDictionary.FromObjectAndKey(
                new NSString(route),
                new NSString(RouteKey));
        }

        return content;
    }
}
