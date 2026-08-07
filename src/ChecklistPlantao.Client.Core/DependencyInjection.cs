using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Auth;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.UI.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.Client.Core;

public static class DependencyInjection
{
    /// <summary>
    /// Serviços do cliente que não dependem de plataforma.
    ///
    /// O head MAUI acrescenta as implementações específicas — <see cref="ISecureStore"/>,
    /// <see cref="IConnectivityProbe"/>, <see cref="IPlatformInfo"/>,
    /// <see cref="ILocalNotificationScheduler"/>, <see cref="INotificationPermissionService"/> —
    /// que são as únicas peças realmente diferentes entre Android e Windows.
    /// </summary>
    public static IServiceCollection AddChecklistClientCore(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var diretorio = Path.GetDirectoryName(databasePath);

        if (!string.IsNullOrEmpty(diretorio))
        {
            Directory.CreateDirectory(diretorio);
        }

        services.AddDbContext<LocalDbContext>(builder => builder.UseSqlite($"Data Source={databasePath}"));

        services.AddSingleton<IClock, SystemClock>();
        services.AddOptions<OfflineAuthOptions>();

        services.AddScoped<OutboxWriter>();
        services.AddScoped<SyncEngine>();
        services.AddScoped<ClientSession>();
        services.AddScoped<IAppSession>(sp => sp.GetRequiredService<ClientSession>());
        services.AddScoped<IChecklistStore, LocalChecklistStore>();
        services.AddScoped<IDeviceDiagnosticsService, DeviceDiagnosticsService>();
        services.AddScoped<INotificationStatusService, NotificationStatusService>();
        services.AddScoped<IAdministrationService, AdministrationService>();

        services.AddScoped<ServerConfigurationService>();
        services.AddScoped<IServerConfigurationService>(sp => sp.GetRequiredService<ServerConfigurationService>());
        services.AddScoped<IServerAddressProvider>(sp => sp.GetRequiredService<ServerConfigurationService>());

        services.AddScoped<ITokenStore, SecureTokenStore>();
        services.AddScoped<IServerApi, HttpServerApi>();

        // O estado de sincronização é singleton: a barra do topo precisa sobreviver às trocas
        // de página, e o próprio serviço abre escopos quando precisa do banco.
        services.AddSingleton<SyncStatusService>();
        services.AddSingleton<ISyncStatusService>(sp => sp.GetRequiredService<SyncStatusService>());

        services.AddHttpClient(HttpServerApi.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20));

        return services;
    }

    /// <summary>Cria o banco local e carrega as configurações. Chamado uma vez, na subida do aplicativo.</summary>
    public static async Task InitializeChecklistClientAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var escopo = services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<LocalDbContext>();

        // EnsureCreated e não Migrate: o esquema local é recriado a partir do bootstrap quando
        // muda de versão, e um banco de cache não justifica manter histórico de migrations no
        // aparelho. Ver docs/DECISIONS.md (D-017).
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        if (await db.DeviceState.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            db.DeviceState.Add(new DeviceState());
        }

        if (await db.SyncState.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            db.SyncState.Add(new SyncState());
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await escopo.ServiceProvider.GetRequiredService<IInstitutionSettingsProvider>()
            .ReloadAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
