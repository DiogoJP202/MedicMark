using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Services;
using ChecklistPlantao.UI.Services;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Client;

public static class MauiProgram
{
    /// <summary>Nome do arquivo do banco local, dentro da pasta de dados do aplicativo.</summary>
    public const string DatabaseFileName = "checklistplantao.db";

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // O banco fica na pasta de dados do aplicativo — persistente, e não temporária.
        var caminhoBanco = Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);
        builder.Services.AddChecklistClientCore(caminhoBanco);

        // Serviços de plataforma: as únicas peças realmente diferentes entre Android e Windows.
        builder.Services.AddSingleton<ISecureStore, MauiSecureStore>();
        builder.Services.AddSingleton<IConnectivityProbe, MauiConnectivityProbe>();
        builder.Services.AddSingleton<IPlatformInfo, MauiPlatformInfo>();
        builder.Services.AddSingleton<IInstitutionTimeZone, ClientInstitutionTimeZone>();
        builder.Services.AddScoped<INotificationHealthService, NotificationHealthService>();
        // Escolha de aparência: vive no WebView, e não no banco local. Ver docs/DECISIONS.md (D-024).
        builder.Services.AddScoped<IThemeService, WebViewThemeService>();
        builder.Services.AddSingleton<IInstitutionSettingsProvider, LocalInstitutionSettingsProvider>();
        builder.Services.AddSingleton<IDeviceStartupRescheduler, NotificationCoordinator>();

#if ANDROID
        builder.Services.AddSingleton<ILocalNotificationScheduler, Platforms.Android.AndroidNotificationScheduler>();
        builder.Services.AddSingleton<INotificationPermissionService, Platforms.Android.AndroidNotificationPermissionService>();
#elif WINDOWS
        builder.Services.AddSingleton<ILocalNotificationScheduler, Platforms.Windows.WindowsNotificationScheduler>();
        builder.Services.AddSingleton<INotificationPermissionService, Platforms.Windows.WindowsNotificationPermissionService>();
#endif

        var app = builder.Build();

        // O banco local precisa existir antes da primeira tela. É criação de esquema local,
        // rápida e feita uma vez — por isso a API síncrona do EF, e não um bloqueio em async.
        app.Services.InitializeChecklistClient();

        return app;
    }
}
