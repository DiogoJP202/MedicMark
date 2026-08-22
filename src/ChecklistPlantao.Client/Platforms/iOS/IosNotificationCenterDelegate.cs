using ChecklistPlantao.Client.Core.Notifications;
using Foundation;
using UserNotifications;

namespace ChecklistPlantao.Client.Platforms.iOS;

/// <summary>Mostra alertas em primeiro plano e encaminha o toque para o roteador do aplicativo.</summary>
public sealed class IosNotificationCenterDelegate : UNUserNotificationCenterDelegate
{
    public override void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler) =>
        completionHandler(
            UNNotificationPresentationOptions.Banner
            | UNNotificationPresentationOptions.List
            | UNNotificationPresentationOptions.Sound
            | UNNotificationPresentationOptions.Badge);

    public override void DidReceiveNotificationResponse(
        UNUserNotificationCenter center,
        UNNotificationResponse response,
        Action completionHandler)
    {
        var route = response.Notification.Request.Content.UserInfo[
            new NSString(IosNotificationScheduler.RouteKey)] as NSString;

        NotificationNavigation.Request(route?.ToString());
        completionHandler();
    }
}
