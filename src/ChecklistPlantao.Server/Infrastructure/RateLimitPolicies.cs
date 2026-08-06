using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ChecklistPlantao.Server;

/// <summary>Nomes das políticas de limite de requisições.</summary>
public static class RateLimitPolicies
{
    /// <summary>Login e refresh: protege contra tentativa de senha em massa.</summary>
    public const string Login = "login";

    /// <summary>Sincronização: evita que um cliente em laço de retry sobrecarregue o servidor.</summary>
    public const string Sync = "sync";
}

/// <summary>
/// Limites configuráveis. São generosos por padrão: o plantão inteiro pode compartilhar um mesmo
/// IP de saída, e travar o pessoal é pior do que aceitar algumas requisições a mais.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    [Range(1, 10_000)]
    public int LoginPerMinute { get; set; } = 20;

    [Range(1, 100_000)]
    public int SyncBurst { get; set; } = 60;

    [Range(1, 100_000)]
    public int SyncPerMinute { get; set; } = 30;
}

public static class RateLimitingSetup
{
    public static IServiceCollection AddChecklistRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Os limites são lidos por requisição, do contêiner: ler a configuração aqui, antes
            // de Build(), ignoraria fontes acrescentadas depois (ambiente, User Secrets, testes).
            // Particionado por IP: um aparelho com problema não deve bloquear o plantão inteiro.
            options.AddPolicy(RateLimitPolicies.Login, context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ClientKey(context),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Limits(context).LoginPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy(RateLimitPolicies.Sync, context => RateLimitPartition.GetTokenBucketLimiter(
                partitionKey: ClientKey(context),
                factory: _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = Limits(context).SyncBurst,
                    TokensPerPeriod = Limits(context).SyncPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });

        return services;
    }

    private static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
}
