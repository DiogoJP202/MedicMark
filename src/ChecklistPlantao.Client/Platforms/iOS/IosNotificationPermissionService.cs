using ChecklistPlantao.Client.Core.Notifications;
using Foundation;
using UserNotifications;

namespace ChecklistPlantao.Client.Platforms.iOS;

/// <summary>Permissão de alertas do iPhone. Alarme exato e otimização de bateria são conceitos do Android.</summary>
public sealed class IosNotificationPermissionService : INotificationPermissionService
{
    public async Task<NotificationPermissions> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var granted = settings.AuthorizationStatus is UNAuthorizationStatus.Authorized
            or UNAuthorizationStatus.Provisional
            or UNAuthorizationStatus.Ephemeral;

        return new NotificationPermissions(granted, ExactAlarmGranted: null, BatteryOptimizationIgnored: true);
    }

    public async Task<NotificationPermissions> RequestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge);
        cancellationToken.ThrowIfCancellationRequested();
        return await GetAsync(cancellationToken);
    }

    public async Task OpenSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var url = new NSUrl(UIKit.UIApplication.OpenSettingsUrlString);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await UIKit.UIApplication.SharedApplication.OpenUrlAsync(
                url,
                new UIKit.UIApplicationOpenUrlOptions());
        });
    }
}
