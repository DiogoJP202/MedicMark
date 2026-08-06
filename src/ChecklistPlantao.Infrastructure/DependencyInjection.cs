using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Infrastructure.Persistence;
using ChecklistPlantao.Infrastructure.Seeding;
using ChecklistPlantao.Infrastructure.Settings;
using ChecklistPlantao.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChecklistPlantao.Infrastructure;

/// <summary>Opções do banco central, vindas da seção <c>Database</c>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Caminho do arquivo SQLite. Precisa apontar para um volume persistente — nunca para a
    /// pasta temporária do sistema. Ver docs/DEPLOYMENT.md.
    /// </summary>
    public string Path { get; set; } = "data/checklistplantao.db";

    /// <summary>Write-Ahead Logging: melhora leitura concorrente. Desligar só para diagnosticar.</summary>
    public bool EnableWriteAheadLogging { get; set; } = true;

    /// <summary>Aplica migrations pendentes na inicialização.</summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>Aplica os dados iniciais na inicialização.</summary>
    public bool SeedOnStartup { get; set; } = true;

    /// <summary>Segundos de espera quando o banco está travado por outra escrita.</summary>
    public int BusyTimeoutSeconds { get; set; } = 15;
}

public static class DependencyInjection
{
    public static IServiceCollection AddChecklistInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        // As opções são resolvidas pelo contêiner, e não lidas aqui da configuração.
        // Ler antes de Build() ignoraria fontes acrescentadas depois — variáveis de ambiente
        // funcionariam, mas User Secrets tardios e a configuração dos testes de integração não,
        // e o servidor abriria silenciosamente o banco errado.
        services.AddDbContext<AppDbContext>((serviceProvider, builder) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseSqlite(SqliteConnectionStringFor(options), sqlite =>
            {
                sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                sqlite.CommandTimeout(options.BusyTimeoutSeconds);
            });
        });

        services.AddScoped<IAppDataContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IInstitutionSettingsProvider, DatabaseInstitutionSettingsProvider>();
        services.AddSingleton<IInstitutionTimeZone, InstitutionTimeZone>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }

    /// <summary>
    /// Monta a cadeia de conexão garantindo que a pasta do arquivo exista. O SQLite não cria
    /// diretórios — sem isso a primeira execução em um volume novo falharia.
    /// </summary>
    public static string SqliteConnectionStringFor(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fullPath = System.IO.Path.GetFullPath(options.Path);
        var directory = System.IO.Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return $"Data Source={fullPath};Cache=Shared;Foreign Keys=True";
    }
}
