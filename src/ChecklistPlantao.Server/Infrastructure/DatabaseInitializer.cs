using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Seeding;
using ChecklistPlantao.Infrastructure;
using ChecklistPlantao.Infrastructure.Persistence;
using ChecklistPlantao.Infrastructure.Seeding;
using ChecklistPlantao.Server.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChecklistPlantao.Server.Infrastructure;

/// <summary>
/// Prepara o banco na subida: migrations, WAL, seed e — se e somente se houver configuração —
/// o usuário administrador inicial.
///
/// Nenhuma senha padrão existe no código. Sem <c>Bootstrap:AdminUserName</c> e
/// <c>Bootstrap:AdminPassword</c>, o servidor sobe e registra um aviso explicando o que fazer.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ChecklistPlantao.Database");
        var db = services.GetRequiredService<AppDbContext>();
        var options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        if (options.MigrateOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Migrations aplicadas.");
        }

        if (options.EnableWriteAheadLogging)
        {
            await EnableWriteAheadLoggingAsync(db, logger, cancellationToken).ConfigureAwait(false);
        }

        if (options.SeedOnStartup)
        {
            var clock = services.GetRequiredService<IClock>();
            var seeder = services.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        await services.GetRequiredService<IInstitutionSettingsProvider>().ReloadAsync(cancellationToken).ConfigureAwait(false);
        await EnsureAdministratorAsync(services, logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// WAL melhora a leitura concorrente e sobrevive ao fechamento do processo. É configuração do
    /// arquivo, não da conexão: basta aplicar uma vez, mas reaplicar é inofensivo.
    /// </summary>
    private static async Task EnableWriteAheadLoggingAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível habilitar WAL. O servidor continua com o modo padrão de journal.");
        }
    }

    private static async Task EnsureAdministratorAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        var bootstrap = services.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        var db = services.GetRequiredService<AppDbContext>();

        var hasAnyUser = await db.AppUsers.AnyAsync(cancellationToken).ConfigureAwait(false);

        if (hasAnyUser)
        {
            return;
        }

        if (!bootstrap.IsConfigured)
        {
            logger.LogWarning(
                "Nenhum usuário cadastrado e nenhum administrador inicial configurado. " +
                "Defina Bootstrap__AdminUserName e Bootstrap__AdminPassword (variáveis de ambiente ou User Secrets) e reinicie. " +
                "Ver docs/DEPLOYMENT.md.");
            return;
        }

        var accessAdmin = services.GetRequiredService<Application.Administration.AccessAdminService>();
        var adminGroupId = SeedCatalog.Groups.Single(g => g.Name == SeedCatalog.AdministratorsGroupName).Id;

        var result = await accessAdmin.CreateUserAsync(
            new Contracts.Administration.CreateUserRequest(
                bootstrap.AdminUserName!,
                string.IsNullOrWhiteSpace(bootstrap.AdminDisplayName) ? "Administrador" : bootstrap.AdminDisplayName!,
                bootstrap.AdminPassword!,
                [adminGroupId]),
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            // A senha jamais aparece no log — só o motivo da recusa.
            logger.LogError("Não foi possível criar o administrador inicial: {Motivo}", result.Error!.Message);
            return;
        }

        logger.LogInformation(
            "Administrador inicial criado: {UserName}. Troque a senha no primeiro acesso.",
            AppUser.NormalizeUserName(bootstrap.AdminUserName!));
    }
}
