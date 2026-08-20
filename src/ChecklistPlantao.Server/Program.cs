using System.Text;
using System.Text.Json.Serialization;
using ChecklistPlantao.Application;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Infrastructure;
using ChecklistPlantao.Infrastructure.Identity;
using ChecklistPlantao.Infrastructure.Persistence;
using ChecklistPlantao.Server;
using ChecklistPlantao.Server.Auth;
using ChecklistPlantao.Server.Infrastructure;
using ChecklistPlantao.Server.Realtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuração e opções
// ---------------------------------------------------------------------------
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<LoginLockoutOptions>()
    .Bind(builder.Configuration.GetSection(LoginLockoutOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Preencher só metade do par cria o administrador em silêncio: nenhum usuário é criado, o servidor
// sobe normalmente, e a pessoa descobre no primeiro login que não consegue entrar. Recusar na
// subida transforma isso numa mensagem, e não numa investigação.
builder.Services.AddOptions<BootstrapOptions>()
    .Bind(builder.Configuration.GetSection(BootstrapOptions.SectionName))
    .Validate(
        opcoes => string.IsNullOrWhiteSpace(opcoes.AdminUserName) == string.IsNullOrWhiteSpace(opcoes.AdminPassword),
        "Bootstrap:AdminUserName e Bootstrap:AdminPassword precisam ser informados juntos, ou nenhum dos dois.")
    .ValidateOnStart();

builder.Services.AddOptions<MaintenanceOptions>()
    .Bind(builder.Configuration.GetSection(MaintenanceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ---------------------------------------------------------------------------
// Persistência, casos de uso e identidade
// ---------------------------------------------------------------------------
builder.Services.AddChecklistInfrastructure(builder.Configuration);
builder.Services.AddChecklistApplication();

builder.Services
    .AddIdentityCore<AppIdentityUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedAccount = false;

        // Regras de senha alinhadas a AccessAdminService.ValidatePassword.
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>();

// Os limites de bloqueio vêm das opções resolvidas pelo contêiner, e não de uma leitura
// antecipada da configuração — ver o comentário em ConfigureJwtBearerOptions.
builder.Services
    .AddOptions<IdentityOptions>()
    .Configure<IOptions<LoginLockoutOptions>>((identity, lockout) =>
    {
        identity.Lockout.MaxFailedAccessAttempts = lockout.Value.MaxFailedAttempts;
        identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(lockout.Value.LockoutMinutes);
    });

builder.Services.AddScoped<IUserCredentialStore, IdentityUserCredentialStore>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthenticationEndpointService>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<SyncNotifier>();
builder.Services.AddHttpContextAccessor();

// ---------------------------------------------------------------------------
// Autenticação e autorização
// ---------------------------------------------------------------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddProblemDetails();

// Antes do tratador padrão: violação de restrição do banco é conflito de dados (409), não falha
// do servidor (500). Ver DatabaseConflictExceptionHandler.
builder.Services.AddExceptionHandler<DatabaseConflictExceptionHandler>();
builder.Services.AddChecklistRateLimiting(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddHostedService<MaintenanceHostedService>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("banco", tags: ["ready"]);

// OpenAPI só em desenvolvimento: em produção a API não publica o próprio contrato.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

// CORS existe apenas para a hipótese de uma interface web na rede local. Nunca "*".
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}

var app = builder.Build();

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // HSTS e redirecionamento só fora de desenvolvimento: em rede local o HTTP é documentado
    // e forçar HTTPS na máquina de desenvolvimento só atrapalha. Ver docs/DEPLOYMENT.md.
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SyncHub>(SyncHubEvents.HubPath);

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await DatabaseInitializer.InitializeAsync(app);

await app.RunAsync();

/// <summary>
/// Exposto para que <c>WebApplicationFactory&lt;Program&gt;</c> encontre o host nos testes de integração.
/// </summary>
public partial class Program;
