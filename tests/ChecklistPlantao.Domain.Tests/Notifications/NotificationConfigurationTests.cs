using ChecklistPlantao.Domain.Notifications;

namespace ChecklistPlantao.Domain.Tests.Notifications;

public sealed class NotificationConfigurationTests
{
    [Fact]
    public void Canal_movel_e_compartilhado_por_android_e_ios_sem_mudar_o_contrato_persistido()
    {
        var now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var configuration = new NotificationConfiguration(now);

        Assert.True(configuration.IsEnabledFor(DevicePlatform.Android));
        Assert.True(configuration.IsEnabledFor(DevicePlatform.Ios));

        configuration.Update(
            soundEnabled: true,
            vibrationEnabled: true,
            priority: NotificationPriority.High,
            enabledOnAndroid: false,
            enabledOnWindows: true,
            allowFullScreenIntent: false,
            titleTemplate: NotificationConfiguration.DefaultTitleTemplate,
            bodyTemplate: NotificationConfiguration.DefaultBodyTemplate,
            nowUtc: now.AddMinutes(1));

        Assert.False(configuration.IsEnabledFor(DevicePlatform.Android));
        Assert.False(configuration.IsEnabledFor(DevicePlatform.Ios));
        Assert.True(configuration.IsEnabledFor(DevicePlatform.Windows));
    }
}
