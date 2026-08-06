using System.Text;
using ChecklistPlantao.Contracts.Sync;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ChecklistPlantao.Server.Auth;

/// <summary>
/// Configura a validação do JWT a partir de <see cref="JwtOptions"/> resolvido pelo contêiner.
///
/// Deliberadamente NÃO se lê <c>builder.Configuration</c> direto no Program: fontes de
/// configuração acrescentadas depois (ambiente, User Secrets, testes de integração) só entram no
/// grafo depois de <c>Build()</c>. Lendo cedo, o servidor assinaria com uma chave e validaria com
/// outra — falha silenciosa que se manifesta como 401 em todo endpoint protegido.
/// </summary>
public sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions) : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (name is not null && name != JwtBearerDefaults.AuthenticationScheme && name != Options.DefaultName)
        {
            return;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _jwt.Issuer,
            ValidAudience = _jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // O SignalR não envia cabeçalho Authorization em WebSockets: o token vem na query string.
        // Só aceitamos essa forma no caminho do hub, nunca nos endpoints REST.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments(SyncHubEvents.HubPath, StringComparison.Ordinal))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    }
}
