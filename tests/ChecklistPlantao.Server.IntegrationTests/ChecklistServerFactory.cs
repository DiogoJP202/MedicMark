using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChecklistPlantao.Contracts.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Server.IntegrationTests;

/// <summary>
/// Sobe o servidor real contra um arquivo SQLite temporário.
///
/// Usa arquivo e não banco em memória de propósito: o objetivo é exercitar as migrations, os
/// índices únicos e o comportamento do provedor SQLite de verdade, que é o que roda em produção.
/// </summary>
public sealed class ChecklistServerFactory : WebApplicationFactory<Program>
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Admin12345";

    private readonly string _databaseDirectory = Path.Combine(
        Path.GetTempPath(),
        "checklistplantao-tests",
        Guid.CreateVersion7().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_databaseDirectory);

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Path"] = Path.Combine(_databaseDirectory, "testes.db"),
                ["Database:MigrateOnStartup"] = "true",
                ["Database:SeedOnStartup"] = "true",
                ["Database:EnableWriteAheadLogging"] = "false",

                ["Jwt:SigningKey"] = "chave-exclusiva-de-teste-com-mais-de-32-caracteres-000000",
                ["Jwt:AccessTokenMinutes"] = "30",

                ["Bootstrap:AdminUserName"] = AdminUserName,
                ["Bootstrap:AdminPassword"] = AdminPassword,
                ["Bootstrap:AdminDisplayName"] = "Administrador de Teste",

                // Limites altos: a suíte faz muitas chamadas do mesmo IP e não é isso que
                // queremos testar aqui.
                ["RateLimit:LoginPerMinute"] = "10000",
                ["RateLimit:SyncPerMinute"] = "10000",
                ["RateLimit:SyncBurst"] = "10000",

                // Manutenção periódica desligada para não competir com as asserções.
                ["Maintenance:IntervalMinutes"] = "1440",
            });
        });

        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_databaseDirectory))
            {
                Directory.Delete(_databaseDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Arquivo ainda preso pelo provedor SQLite. O diretório é temporário e o sistema limpa depois.
        }
    }

    /// <summary>Cliente já autenticado como administrador.</summary>
    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient();
        var login = await LoginAsync(client, AdminUserName, AdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Tokens.AccessToken);
        return client;
    }

    public static async Task<LoginResponse> LoginAsync(HttpClient client, string userName, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password, DeviceId: Guid.CreateVersion7().ToString(), DeviceName: "Teste"));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    public T GetService<T>()
        where T : notnull => Services.GetRequiredService<T>();
}
