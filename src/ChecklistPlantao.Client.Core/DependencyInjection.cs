using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Auth;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.UI.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

        // FÁBRICA, e não um contexto com escopo. No MAUI Blazor Hybrid o escopo do BlazorWebView
        // dura a vida inteira do aplicativo: um contexto "com escopo" seria, na prática, um
        // singleton. O rastreador acumularia entidades do plantão inteiro, uma gravação que
        // falhasse contaminaria todas as seguintes, e duas operações simultâneas usariam a mesma
        // instância — que não é segura para isso. Ver docs/DECISIONS.md (D-021).
        //
        // Com a fábrica, cada unidade de trabalho abre e descarta o seu próprio contexto.
        services.AddDbContextFactory<LocalDbContext>(builder => builder.UseSqlite($"Data Source={databasePath}"));

        services.AddSingleton<IClock, SystemClock>();
        services.AddOptions<OfflineAuthOptions>();

        // Estado da sessão como SINGLETON: a sessão em si precisa ser por escopo (depende do
        // banco local), mas quem está usando o aplicativo é um só. Ver AuthenticatedSessionState.
        services.AddSingleton<AuthenticatedSessionState>();

        // Construtor escolhido À MÃO. Estas classes têm dois: um que recebe a fábrica (produção) e
        // outro que recebe um contexto emprestado, para quem já abriu uma unidade de trabalho. O
        // contêiner não sabe decidir entre construtores de mesma aridade — e é bom que a escolha
        // fique visível aqui, e não escondida numa regra de resolução.
        services.AddScoped(sp => new OutboxWriter(sp.GetRequiredService<IDbContextFactory<LocalDbContext>>()));

        services.AddScoped(sp => new SyncEngine(
            sp.GetRequiredService<IDbContextFactory<LocalDbContext>>(),
            sp.GetRequiredService<IServerApi>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<SyncEngine>>()));

        services.AddScoped(sp => new ClientSession(
            sp.GetRequiredService<IDbContextFactory<LocalDbContext>>(),
            sp.GetRequiredService<IServerApi>(),
            sp.GetRequiredService<ITokenStore>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IInstitutionSettingsProvider>(),
            sp.GetRequiredService<AuthenticatedSessionState>(),
            sp.GetRequiredService<IOptions<OfflineAuthOptions>>(),
            sp.GetRequiredService<ILogger<ClientSession>>()));

        services.AddScoped<IAppSession>(sp => sp.GetRequiredService<ClientSession>());

        services.AddScoped<IChecklistStore>(sp => new LocalChecklistStore(
            sp.GetRequiredService<IDbContextFactory<LocalDbContext>>(),
            sp.GetRequiredService<OutboxWriter>(),
            sp.GetRequiredService<IServerApi>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IInstitutionTimeZone>(),
            sp.GetRequiredService<IInstitutionSettingsProvider>(),
            sp.GetRequiredService<ILogger<LocalChecklistStore>>()));
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

    /// <summary>
    /// Cria o banco local e as linhas de estado. Chamado uma vez, na subida do aplicativo.
    ///
    /// Deliberadamente SÍNCRONO: a subida do MAUI é síncrona e bloquear em um método assíncrono
    /// (<c>.Result</c>, <c>GetAwaiter().GetResult()</c>) arrisca deadlock. Usar a API síncrona do
    /// EF Core é correto aqui — é criação de esquema local, rápida e feita uma única vez.
    /// </summary>
    public static void InitializeChecklistClient(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var db = services.GetRequiredService<IDbContextFactory<LocalDbContext>>().CreateDbContext();

        // EnsureCreated e não Migrate: o esquema local é recriado a partir do bootstrap quando
        // a versão muda, e um banco que é reconstituível do servidor não justifica carregar
        // histórico de migrations no aparelho. Ver docs/DECISIONS.md (D-017).
        db.Database.EnsureCreated();

        if (db.DeviceState.FirstOrDefault() is null)
        {
            db.DeviceState.Add(new DeviceState());
        }

        if (db.SyncState.FirstOrDefault() is null)
        {
            db.SyncState.Add(new SyncState());
        }

        db.SaveChanges();
    }
}
