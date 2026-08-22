using Foundation;
using UserNotifications;

namespace ChecklistPlantao.Client;

[Register("AppDelegate")]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
    private readonly Platforms.iOS.IosNotificationCenterDelegate _notificationDelegate = new();

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIKit.UIApplication application, NSDictionary? launchOptions)
    {
        // Delegate é uma referência fraca no iOS; o campo acima precisa mantê-lo vivo.
        UNUserNotificationCenter.Current.Delegate = _notificationDelegate;
        return base.FinishedLaunching(application, launchOptions);
    }
}
