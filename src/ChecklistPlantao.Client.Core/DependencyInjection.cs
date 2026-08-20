using System.Globalization;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Auth;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Domain.Sync;
using ChecklistPlantao.Client.Abstractions;
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
        services.AddScoped<LocalNotificationPlanService>();
        // Uma implementação, três contratos. Cada tela de administração injeta só o que usa —
        // a de leitos deixou de depender de redefinição de senha. Ver docs/DECISIONS.md (D-022).
        services.AddScoped<AdministrationService>();
        services.AddScoped<IStructureAdminService>(sp => sp.GetRequiredService<AdministrationService>());
        services.AddScoped<IAccessAdminService>(sp => sp.GetRequiredService<AdministrationService>());
        services.AddScoped<ISystemAdminService>(sp => sp.GetRequiredService<AdministrationService>());

        services.AddScoped<ServerConfigurationService>();
        services.AddScoped<IServerConfigurationService>(sp => sp.GetRequiredService<ServerConfigurationService>());
        services.AddScoped<IServerAddressProvider>(sp => sp.GetRequiredService<ServerConfigurationService>());

        services.AddScoped<ITokenStore, SecureTokenStore>();
        services.AddScoped<IServerApi, HttpServerApi>();

        // O estado de sincronização é singleton: a barra do topo precisa sobreviver às trocas
        // de página, e o próprio serviço abre escopos quando precisa do banco.
        services.AddSingleton<SyncStatusService>();
        services.AddSingleton<ISyncStatusService>(sp => sp.GetRequiredService<SyncStatusService>());

        // Uma conexão de tempo real por aplicativo, não por tela: singleton. Ela acompanha o
        // estado da sessão e abre escopos quando precisa do token ou do endereço.
        services.AddSingleton<RealtimeSyncClient>();

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
        EnsureLocalSchema(db, services.GetService<ILogger<LocalDbContext>>());

        if (db.DeviceState.FirstOrDefault() is null)
        {
            db.DeviceState.Add(new DeviceState());
        }

        if (db.SyncState.FirstOrDefault() is null)
        {
            db.SyncState.Add(new SyncState());
        }

        db.SaveChanges();

        // Liga o acompanhamento do hub. Só assina o evento de sessão aqui — a conexão em si
        // acontece quando houver usuário autenticado e endereço de servidor.
        services.GetRequiredService<RealtimeSyncClient>().Start();
    }

    /// <summary>
    /// Garante que o esquema em disco corresponde ao modelo desta versão do aplicativo.
    ///
    /// Três caminhos:
    /// <list type="bullet">
    ///   <item>arquivo novo — o esquema é criado e a versão gravada;</item>
    ///   <item>versão igual — nada a fazer, que é o caso de toda abertura normal;</item>
    ///   <item>versão diferente — o banco é DESCARTADO e recriado.</item>
    /// </list>
    ///
    /// Descartar custa os itens da fila que ainda não subiram, e isso é registrado no log. É o
    /// preço aceito em D-017: o banco local é reconstituível a partir do servidor, e um aplicativo
    /// que não abre é pior que uma fila perdida. A alternativa silenciosa — seguir com o esquema
    /// velho — é a pior das três, porque falha depois, em uso, com erro que não se explica.
    /// </summary>
    internal static void EnsureLocalSchema(LocalDbContext db, ILogger? logger)
    {
        var noDisco = ReadSchemaVersion(db);

        if (db.Database.EnsureCreated())
        {
            WriteSchemaVersion(db, LocalDbContext.LocalSchemaVersion);
            return;
        }

        if (noDisco == LocalDbContext.LocalSchemaVersion)
        {
            return;
        }

        var pendentes = db.Outbox.Count(o => o.Status != OutboxItemStatus.Done);

        logger?.LogWarning(
            "Esquema local na versão {Antiga}, esperado {Nova}. O banco do aparelho será recriado e "
            + "a configuração virá do servidor. {Pendentes} alteração(ões) ainda não enviada(s) serão perdidas.",
            noDisco,
            LocalDbContext.LocalSchemaVersion,
            pendentes);

        // Fechar antes de apagar: o SQLite mantém o arquivo travado enquanto houver conexão aberta,
        // e o EnsureDeleted apaga o arquivo, não as tabelas.
        db.Database.CloseConnection();

        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();

        WriteSchemaVersion(db, LocalDbContext.LocalSchemaVersion);
    }

    /// <summary>
    /// <c>PRAGMA user_version</c> mora no cabeçalho do arquivo SQLite, e não numa tabela — o que
    /// evita o problema circular de guardar a versão dentro do esquema que se quer versionar.
    /// Num arquivo recém-criado ele vale zero.
    /// </summary>
    private static int ReadSchemaVersion(LocalDbContext db)
    {
        // Devolve a conexão ao estado em que estava: deixá-la aberta travaria o arquivo, e o
        // caminho de recriação precisa apagá-lo logo em seguida.
        var jaEstavaAberta = db.Database.GetDbConnection().State == System.Data.ConnectionState.Open;

        if (!jaEstavaAberta)
        {
            db.Database.OpenConnection();
        }

        try
        {
            using var comando = db.Database.GetDbConnection().CreateCommand();
            comando.CommandText = "PRAGMA user_version";

            return Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        finally
        {
            if (!jaEstavaAberta)
            {
                db.Database.CloseConnection();
            }
        }
    }

    /// <summary>
    /// Gravada pela conexão direta, e não por <c>ExecuteSqlRaw</c>: <c>PRAGMA</c> não aceita
    /// parâmetro, e montar a instrução por interpolação dispararia o alerta de injeção de SQL —
    /// corretamente, ainda que aqui o valor seja uma constante do próprio código.
    /// </summary>
    private static void WriteSchemaVersion(LocalDbContext db, int versao)
    {
        var jaEstavaAberta = db.Database.GetDbConnection().State == System.Data.ConnectionState.Open;

        if (!jaEstavaAberta)
        {
            db.Database.OpenConnection();
        }

        try
        {
            using var comando = db.Database.GetDbConnection().CreateCommand();
            comando.CommandText = "PRAGMA user_version = " + versao.ToString(CultureInfo.InvariantCulture);

            comando.ExecuteNonQuery();
        }
        finally
        {
            if (!jaEstavaAberta)
            {
                db.Database.CloseConnection();
            }
        }
    }
}
