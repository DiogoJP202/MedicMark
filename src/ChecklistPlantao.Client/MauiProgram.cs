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

#if ANDROID || IOS
    // Endereço da instalação móvel oficial. Mantê-lo aqui deixa a configuração de implantação
    // visível e permite que a tela de configuração continue aceitando um servidor alternativo.
    private const string? DefaultServerUrl = "https://163.176.119.139";
    private static readonly string[] ReplacedServerUrls = ["http://137.131.172.104"];
#else
    // No Windows, desenvolvimento e servidor normalmente rodam juntos em endereço local.
    private const string? DefaultServerUrl = null;
    private static readonly string[] ReplacedServerUrls = [];
#endif

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
        builder.Services.AddChecklistClientCore(caminhoBanco, DefaultServerUrl, ReplacedServerUrls);

        // Serviços de plataforma: as únicas peças realmente diferentes entre Android, iOS e Windows.
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
#elif IOS
        builder.Services.AddSingleton<ILocalNotificationScheduler, Platforms.iOS.IosNotificationScheduler>();
        builder.Services.AddSingleton<INotificationPermissionService, Platforms.iOS.IosNotificationPermissionService>();
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
