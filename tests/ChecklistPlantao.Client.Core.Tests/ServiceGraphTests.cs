using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Client.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Client.Core.Tests;

/// <summary>
/// Constrói o contêiner REAL do aplicativo — o mesmo <c>AddChecklistClientCore</c> que o head MAUI
/// usa — e valida o grafo inteiro.
///
/// Existe porque os outros testes registram os serviços à mão e substituem
/// <see cref="IServerApi"/> por um duplo, o que quebra os ciclos por acidente. O resultado é que
/// a composição real nunca era exercitada: o aplicativo abria em tela branca no aparelho com
/// "A circular dependency was detected for the service of type 'IServerApi'", e nenhum teste via.
///
/// <c>ValidateOnBuild</c> percorre todos os registros e falha na construção; <c>ValidateScopes</c>
/// pega dependência cativa (singleton segurando serviço com escopo).
/// </summary>
public sealed class ServiceGraphTests
{
    /// <summary>Duplos de plataforma, com os MESMOS tempos de vida que <c>MauiProgram</c> registra.</summary>
    private static ServiceProvider BuildRealContainer(string databasePath)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddChecklistClientCore(databasePath);

        services.AddSingleton<ISecureStore, FakeSecureStore>();
        services.AddSingleton<IConnectivityProbe, FakeConnectivityProbe>();
        services.AddSingleton<IPlatformInfo, FakePlatformInfo>();
        services.AddSingleton<IInstitutionTimeZone, FakeInstitutionTimeZone>();
        services.AddSingleton<IInstitutionSettingsProvider, FakeSettingsProvider>();
        services.AddSingleton<ILocalNotificationScheduler, FakeScheduler>();
        services.AddSingleton<INotificationPermissionService, FakePermissions>();
        services.AddSingleton<IDeviceStartupRescheduler, FakeRescheduler>();
        services.AddScoped<INotificationHealthService, FakeHealth>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Fact]
    public void Conteiner_real_do_aplicativo_e_valido()
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"checklist-di-{Guid.CreateVersion7():N}.db");

        try
        {
            using var provider = BuildRealContainer(arquivo);
            Assert.NotNull(provider);
        }
        finally
        {
            Delete(arquivo);
        }
    }

    /// <summary>
    /// Cada serviço que a interface injeta precisa ser resolvível de verdade. <c>ValidateOnBuild</c>
    /// não constrói instâncias — só a resolução prova que o grafo fecha.
    /// </summary>
    [Theory]
    [InlineData(typeof(IAppSession))]
    [InlineData(typeof(IChecklistStore))]
    [InlineData(typeof(ISyncStatusService))]
    [InlineData(typeof(INotificationStatusService))]
    [InlineData(typeof(IServerConfigurationService))]
    [InlineData(typeof(IDeviceDiagnosticsService))]
    [InlineData(typeof(IStructureAdminService))]
    [InlineData(typeof(IAccessAdminService))]
    [InlineData(typeof(ISystemAdminService))]
    [InlineData(typeof(IServerApi))]
    [InlineData(typeof(ITokenStore))]
    public void Servicos_da_interface_sao_resolviveis(Type servico)
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"checklist-di-{Guid.CreateVersion7():N}.db");

        try
        {
            using var provider = BuildRealContainer(arquivo);
            using var escopo = provider.CreateScope();

            Assert.NotNull(escopo.ServiceProvider.GetRequiredService(servico));
        }
        finally
        {
            Delete(arquivo);
        }
    }

    /// <summary>
    /// O ciclo original era <c>SyncStatusService → IServerApi → ITokenStore → IServerApi</c>.
    /// Resolver os dois lados no mesmo escopo é o que reproduz exatamente aquele caminho.
    /// </summary>
    [Fact]
    public void Api_e_deposito_de_tokens_convivem_no_mesmo_escopo()
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"checklist-di-{Guid.CreateVersion7():N}.db");

        try
        {
            using var provider = BuildRealContainer(arquivo);
            using var escopo = provider.CreateScope();

            var api = escopo.ServiceProvider.GetRequiredService<IServerApi>();
            var tokens = escopo.ServiceProvider.GetRequiredService<ITokenStore>();
            var estado = escopo.ServiceProvider.GetRequiredService<ISyncStatusService>();

            Assert.IsType<HttpServerApi>(api);
            Assert.IsType<SecureTokenStore>(tokens);
            Assert.NotNull(estado);
        }
        finally
        {
            Delete(arquivo);
        }
    }

    /// <summary>
    /// O defeito no aparelho: com o usuário logado, tocar em "Painel" às vezes voltava para a tela de
    /// entrada e a navegação sumia. A sessão é por escopo (depende do banco local) e guardava o estado
    /// de autenticação nela mesma, então cada escopo tinha a sua verdade — qualquer resolução fora do
    /// escopo corrente devolvia uma sessão nova, não autenticada.
    ///
    /// Este teste fixa a regra: escopos diferentes enxergam a MESMA sessão.
    /// </summary>
    [Fact]
    public void Sessao_e_a_mesma_em_escopos_diferentes()
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"checklist-di-{Guid.CreateVersion7():N}.db");

        try
        {
            using var provider = BuildRealContainer(arquivo);

            // Quem entra é um escopo — na prática, o do WebView que atendeu a tela de entrada.
            using (var escopoDaEntrada = provider.CreateScope())
            {
                Assert.False(escopoDaEntrada.ServiceProvider.GetRequiredService<IAppSession>().IsAuthenticated);

                escopoDaEntrada.ServiceProvider.GetRequiredService<AuthenticatedSessionState>()
                    .SignIn(Guid.CreateVersion7(), "maria", "Maria", EffectiveAccess.None, DateTime.UtcNow);
            }

            // Quem pergunta é outro — o painel, o motor de sincronização, o reagendador.
            using var outroEscopo = provider.CreateScope();
            var sessao = outroEscopo.ServiceProvider.GetRequiredService<IAppSession>();

            Assert.True(sessao.IsAuthenticated);
            Assert.Equal("Maria", sessao.DisplayName);
        }
        finally
        {
            Delete(arquivo);
        }
    }

    /// <summary>
    /// O aviso de mudança precisa atravessar escopos também: quem assina é o layout, e quem dispara
    /// pode ser um serviço singleton. Assinar na instância errada seria assinar o silêncio.
    /// </summary>
    [Fact]
    public void Aviso_de_mudanca_de_sessao_atravessa_escopos()
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"checklist-di-{Guid.CreateVersion7():N}.db");

        try
        {
            using var provider = BuildRealContainer(arquivo);
            using var escopoQueOuve = provider.CreateScope();

            var avisos = 0;
            var ouvinte = escopoQueOuve.ServiceProvider.GetRequiredService<IAppSession>();
            ouvinte.Changed += () => avisos++;

            using (var escopoQueAge = provider.CreateScope())
            {
                escopoQueAge.ServiceProvider.GetRequiredService<AuthenticatedSessionState>()
                    .SelectSector(Guid.CreateVersion7(), "Oeste");
            }

            Assert.Equal(1, avisos);
            Assert.Equal("Oeste", ouvinte.CurrentSectorName);
        }
        finally
        {
            Delete(arquivo);
        }
    }

    private static void Delete(string arquivo)
    {
        try
        {
            if (File.Exists(arquivo))
            {
                File.Delete(arquivo);
            }
        }
        catch (IOException)
        {
            // Arquivo temporário: o sistema limpa depois.
        }
    }

    private sealed class FakeSecureStore : ISecureStore
    {
        private readonly Dictionary<string, string> _valores = [];

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_valores.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _valores[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _valores.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConnectivityProbe : IConnectivityProbe
    {
        public bool HasNetwork => true;

        public bool HasInternet => true;

        public event Action? ConnectivityChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class FakePlatformInfo : IPlatformInfo
    {
        public string PlatformName => "Testing";

        public string AppVersion => "0.0.0";
    }

    private sealed class FakeInstitutionTimeZone : IInstitutionTimeZone
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateTime ToLocal(DateTime utc) => utc;

        public DateTime ToUtc(DateTime local) => local;
    }

    private sealed class FakeSettingsProvider : IInstitutionSettingsProvider
    {
        public InstitutionSettings Current => InstitutionSettings.Default;

        public ValueTask<InstitutionSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InstitutionSettings.Default);

        public ValueTask ReloadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FakeScheduler : ILocalNotificationScheduler
    {
        public bool RequiresAppRunning => false;

        public bool SupportsExactTiming => true;

        public Task ScheduleAsync(IReadOnlyList<ScheduledNotification> notifications, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CancelAsync(IEnumerable<string> notificationIds, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CancelAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ShowNowAsync(string title, string body, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetScheduledIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FakePermissions : INotificationPermissionService
    {
        private static readonly NotificationPermissions Concedidas = new(true, true, true);

        public Task<NotificationPermissions> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Concedidas);

        public Task<NotificationPermissions> RequestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Concedidas);

        public Task OpenSettingsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeHealth : INotificationHealthService
    {
        public Task<IReadOnlyList<string>> DiagnoseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FakeRescheduler : IDeviceStartupRescheduler
    {
        public Task RescheduleAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
