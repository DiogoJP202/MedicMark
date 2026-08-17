using System.Net.Http.Json;
using System.Text.Json;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.UI.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UiResult = ChecklistPlantao.UI.Abstractions.Result;

namespace ChecklistPlantao.Client.Core.Services;

/// <summary>
/// Tokens no armazenamento seguro da plataforma.
/// O banco local nunca guarda token em texto puro — nem mesmo o de atualização.
/// </summary>
public sealed class SecureTokenStore(ISecureStore secure, IServiceProvider services, IClock clock, ILogger<SecureTokenStore> logger) : ITokenStore
{
    private const string AccessKey = "checklistplantao.access";
    private const string ExpiryKey = "checklistplantao.access.expiry";
    private const string RefreshKey = "checklistplantao.refresh";

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = await secure.GetAsync(AccessKey, cancellationToken).ConfigureAwait(false);

        if (token is null)
        {
            return null;
        }

        var expiraTexto = await secure.GetAsync(ExpiryKey, cancellationToken).ConfigureAwait(false);

        // Renova um pouco antes de vencer: evita perder a chamada por causa de segundos.
        if (DateTime.TryParse(expiraTexto, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expira)
            && clock.UtcNow >= expira.AddMinutes(-1))
        {
            return await TryRefreshAsync(cancellationToken).ConfigureAwait(false)
                ? await secure.GetAsync(AccessKey, cancellationToken).ConfigureAwait(false)
                : null;
        }

        return token;
    }

    public async Task SaveAsync(string accessToken, DateTime accessExpiresAtUtc, string refreshToken, CancellationToken cancellationToken = default)
    {
        await secure.SetAsync(AccessKey, accessToken, cancellationToken).ConfigureAwait(false);
        await secure.SetAsync(ExpiryKey, accessExpiresAtUtc.ToString("O"), cancellationToken).ConfigureAwait(false);
        await secure.SetAsync(RefreshKey, refreshToken, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default)
    {
        var refresh = await secure.GetAsync(RefreshKey, cancellationToken).ConfigureAwait(false);

        // IServerApi é resolvido aqui, e não no construtor, porque HttpServerApi depende de
        // ITokenStore para anexar o bearer — pedi-lo no construtor fecharia um ciclo que o
        // contêiner recusa a construir. A renovação em si é uma chamada NÃO autenticada, então
        // esta dependência só existe para reaproveitar o encanamento HTTP.
        if (refresh is null || services.GetRequiredService<IServerApi>() is not HttpServerApi http)
        {
            return false;
        }

        try
        {
            var resposta = await http
                .SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/refresh", new RefreshRequest(refresh, null), authenticated: false, cancellationToken)
                .ConfigureAwait(false);

            if (resposta is null)
            {
                return false;
            }

            await SaveAsync(resposta.Tokens.AccessToken, resposta.Tokens.AccessTokenExpiresAtUtc, resposta.Tokens.RefreshToken, cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Renovação de token falhou. O aplicativo segue offline.");
            return false;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await secure.RemoveAsync(AccessKey, cancellationToken).ConfigureAwait(false);
        await secure.RemoveAsync(ExpiryKey, cancellationToken).ConfigureAwait(false);
        await secure.RemoveAsync(RefreshKey, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Endereço do servidor e nome do aparelho, guardados no banco local.</summary>
public sealed class ServerConfigurationService(LocalDbContext db, IServiceProvider services) : IServerConfigurationService, IServerAddressProvider
{
    private DeviceState? _cache;

    public string? ServerUrl => Load().ServerUrl;

    public string DeviceName => Load().DeviceName;

    public Guid DeviceId => Load().DeviceId;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerUrl);

    /// <summary>
    /// <see cref="IServerApi"/> resolvido sob demanda: esta classe atende
    /// <see cref="IServerAddressProvider"/>, de quem o <see cref="HttpServerApi"/> depende no
    /// construtor. Pedi-lo aqui fecharia o ciclo — e como o registro passa por uma fábrica,
    /// <c>ValidateOnBuild</c> não o enxergaria: a falha só apareceria na resolução.
    /// </summary>
    public Task<ServerProbeResponse?> TestConnectionAsync(string url, CancellationToken cancellationToken = default) =>
        services.GetRequiredService<IServerApi>().ProbeAsync(url, cancellationToken);

    public async Task SaveAsync(string url, string deviceName, CancellationToken cancellationToken = default)
    {
        var estado = await EnsureAsync(cancellationToken).ConfigureAwait(false);
        estado.Configure(HttpServerApi.NormalizeUrl(url), deviceName);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        var estado = await EnsureAsync(cancellationToken).ConfigureAwait(false);
        estado.ClearServer();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private DeviceState Load() => _cache ??= db.DeviceState.FirstOrDefault() ?? new DeviceState();

    private async Task<DeviceState> EnsureAsync(CancellationToken cancellationToken)
    {
        var estado = await db.DeviceState.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (estado is null)
        {
            estado = new DeviceState();
            db.DeviceState.Add(estado);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _cache = estado;
        return estado;
    }
}

/// <summary>Estado de sincronização observável pela interface.</summary>
public sealed class SyncStatusService(
    IServiceProvider services,
    IConnectivityProbe connectivity,
    ILogger<SyncStatusService> logger) : ISyncStatusService
{
    private readonly SemaphoreSlim _porta = new(1, 1);

    public SyncStatus Current { get; private set; } = SyncStatus.Unknown;

    public event Action<SyncStatus>? Changed;

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        // Uma sincronização por vez: duas em paralelo enviariam o mesmo item duas vezes
        // (inofensivo pela idempotência, mas desperdício de rede e bateria).
        if (!await _porta.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            Publish(Current with { IsSyncing = true });

            using var escopo = services.CreateScope();
            var motor = escopo.ServiceProvider.GetRequiredService<SyncEngine>();
            var fila = escopo.ServiceProvider.GetRequiredService<OutboxWriter>();
            var db = escopo.ServiceProvider.GetRequiredService<LocalDbContext>();
            var api = escopo.ServiceProvider.GetRequiredService<IServerApi>();

            var resultado = await motor.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
            var pendentes = await fila.PendingCountAsync(cancellationToken).ConfigureAwait(false);
            var estado = await db.SyncState.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            Publish(new SyncStatus(Resolve(api), pendentes, estado?.LastSyncAtUtc, false, resultado.Error));

            var reagendador = escopo.ServiceProvider.GetService<IDeviceStartupRescheduler>();

            if (reagendador is not null)
            {
                // Configuração pode ter mudado no servidor: os horários precisam ser refeitos.
                await reagendador.RescheduleAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ciclo de sincronização falhou.");
            Publish(Current with { IsSyncing = false, LastError = ex.Message });
        }
        finally
        {
            _porta.Release();
        }
    }

    /// <summary>
    /// Mede o alcance do servidor e publica o resultado, sem sincronizar.
    ///
    /// A tela de entrada precisa disto: antes do primeiro login nenhuma sincronização acontece,
    /// e o estado inicial (<see cref="SyncStatus.Unknown"/>) era exibido como "Offline" sem que
    /// nada tivesse sido verificado. Aqui o rótulo passa a ser resultado de uma medida.
    ///
    /// Usa <c>ProbeAsync</c> em vez de <c>IsReachable</c> de propósito: uma instância recém-criada
    /// de <c>HttpServerApi</c> é otimista por padrão e responderia "alcançável" sem ter falado
    /// com ninguém.
    /// </summary>
    public async Task RefreshConnectivityAsync(CancellationToken cancellationToken = default)
    {
        if (!connectivity.HasNetwork)
        {
            Publish(Current with { Connectivity = ConnectivityState.Offline });
            return;
        }

        using var escopo = services.CreateScope();
        var endereco = escopo.ServiceProvider.GetRequiredService<IServerAddressProvider>();

        if (!endereco.IsConfigured)
        {
            Publish(Current with { Connectivity = ConnectivityState.ServerUnreachable });
            return;
        }

        try
        {
            var api = escopo.ServiceProvider.GetRequiredService<IServerApi>();
            var resposta = await api.ProbeAsync(endereco.ServerUrl!, cancellationToken).ConfigureAwait(false);

            Publish(Current with
            {
                Connectivity = resposta is null
                    ? ConnectivityState.ServerUnreachable
                    : connectivity.HasInternet ? ConnectivityState.Online : ConnectivityState.LocalNetwork,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Verificação de alcance do servidor falhou.");
            Publish(Current with { Connectivity = ConnectivityState.ServerUnreachable });
        }
    }

    /// <summary>Atualiza apenas os contadores, sem chamar o servidor.</summary>
    public async Task RefreshCountersAsync(CancellationToken cancellationToken = default)
    {
        using var escopo = services.CreateScope();
        var fila = escopo.ServiceProvider.GetRequiredService<OutboxWriter>();
        var db = escopo.ServiceProvider.GetRequiredService<LocalDbContext>();
        var api = escopo.ServiceProvider.GetRequiredService<IServerApi>();

        var pendentes = await fila.PendingCountAsync(cancellationToken).ConfigureAwait(false);
        var estado = await db.SyncState.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        Publish(new SyncStatus(Resolve(api), pendentes, estado?.LastSyncAtUtc, false, Current.LastError));
    }

    /// <summary>
    /// Traduz rede + servidor nos três estados que a interface distingue.
    ///
    /// Recebe o <see cref="IServerApi"/> do escopo que acabou de sincronizar, e não um guardado no
    /// construtor. Além de este serviço ser singleton e não poder segurar um serviço com escopo,
    /// a memória de alcance (<c>IsReachable</c>) vive na instância: só a instância que participou
    /// da sincronização sabe se o servidor respondeu.
    /// </summary>
    private ConnectivityState Resolve(IServerApi api)
    {
        if (!connectivity.HasNetwork)
        {
            return ConnectivityState.Offline;
        }

        if (!api.IsReachable)
        {
            return ConnectivityState.ServerUnreachable;
        }

        return connectivity.HasInternet ? ConnectivityState.Online : ConnectivityState.LocalNetwork;
    }

    private void Publish(SyncStatus status)
    {
        Current = status;
        Changed?.Invoke(status);
    }
}

/// <summary>Saúde das notificações, agregando permissões e capacidade da plataforma.</summary>
public sealed class NotificationStatusService(
    INotificationPermissionService permissions,
    ILocalNotificationScheduler scheduler,
    INotificationHealthService health,
    LocalDbContext db,
    IClock clock) : INotificationStatusService
{
    public NotificationStatus Current { get; private set; } = NotificationStatus.Unknown;

    public event Action<NotificationStatus>? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var estado = await permissions.GetAsync(cancellationToken).ConfigureAwait(false);
        var problemas = await health.DiagnoseAsync(cancellationToken).ConfigureAwait(false);
        var config = await db.NotificationConfigurations.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var dispositivo = await db.DeviceState.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var agendados = await scheduler.GetScheduledIdsAsync(cancellationToken).ConfigureAwait(false);

        Current = new NotificationStatus(
            estado.NotificationsGranted,
            estado.ExactAlarmGranted,
            config?.SoundEnabled ?? true,
            config?.VibrationEnabled ?? true,
            estado.BatteryOptimizationIgnored,
            scheduler.RequiresAppRunning,
            dispositivo?.LastNotificationTestAtUtc,
            NextScheduled(agendados),
            problemas)
        {
            // A partir daqui o estado é medido, não presumido.
            HasBeenChecked = true,
        };

        if (dispositivo is not null)
        {
            dispositivo.UpdateNotificationState(
                estado.NotificationsGranted,
                estado.ExactAlarmGranted,
                estado.BatteryOptimizationIgnored,
                Current.IsHealthy ? NotificationHealth.Healthy : Current.IsDegraded ? NotificationHealth.Degraded : NotificationHealth.Unhealthy,
                dispositivo.LastNotificationTestAtUtc);

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        Changed?.Invoke(Current);
    }

    public async Task<bool> SendTestNotificationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await permissions.RequestAsync(cancellationToken).ConfigureAwait(false);

            await scheduler
                .ShowNowAsync("Teste do Checklist de Plantão", "Se você está vendo isto, as notificações funcionam neste aparelho.", cancellationToken)
                .ConfigureAwait(false);

            var dispositivo = await db.DeviceState.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (dispositivo is not null)
            {
                dispositivo.UpdateNotificationState(
                    dispositivo.NotificationsPermissionGranted,
                    dispositivo.ExactAlarmPermissionGranted,
                    dispositivo.BatteryOptimizationIgnored,
                    dispositivo.NotificationHealth,
                    clock.UtcNow);

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    public Task OpenSystemSettingsAsync(CancellationToken cancellationToken = default) =>
        permissions.OpenSettingsAsync(cancellationToken);

    /// <summary>Extrai a data do próximo alerta a partir do identificador estável.</summary>
    private static DateTime? NextScheduled(IReadOnlyList<string> ids)
    {
        var datas = ids
            .Select(id => id.Split(':'))
            .Where(partes => partes.Length >= 2)
            .Select(partes => DateTime.TryParseExact(partes[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var data) ? data : (DateTime?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        return datas.Count == 0 ? null : datas.Min();
    }
}

/// <summary>Reúne o diagnóstico exibido na tela "Estado do dispositivo".</summary>
public sealed class DeviceDiagnosticsService(
    LocalDbContext db,
    IPlatformInfo platform,
    IServerConfigurationService configuration,
    IServerApi api,
    INotificationStatusService notifications,
    IInstitutionSettingsProvider settings,
    IInstitutionTimeZone timeZone,
    IClock clock) : IDeviceDiagnosticsService
{
    public async Task<DeviceDiagnostics> GetAsync(CancellationToken cancellationToken = default)
    {
        var estado = await db.SyncState.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var pendentes = await db.Outbox.CountAsync(o => o.Status != Domain.Sync.OutboxItemStatus.Done, cancellationToken).ConfigureAwait(false);

        return new DeviceDiagnostics(
            platform.PlatformName,
            platform.AppVersion,
            configuration.ServerUrl,
            api.IsReachable,
            estado?.LastSyncAtUtc,
            pendentes,
            settings.Current.TimeZoneId,
            timeZone.ToLocal(clock.UtcNow),
            notifications.Current);
    }
}

/// <summary>
/// Administração pela API. Exige servidor: cadastro não é operação offline — mudar um horário
/// sem o servidor criaria divergência entre aparelhos.
/// </summary>
public sealed class AdministrationService(IServerApi api) : IAdministrationService
{
    private HttpServerApi Http => api as HttpServerApi
        ?? throw new InvalidOperationException("A administração exige o cliente HTTP do servidor.");

    public async Task<IReadOnlyList<SectorDto>> GetSectorsAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<SectorDto>("api/admin/sectors", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> SaveSectorAsync(Guid? id, SaveSectorRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/admin/sectors" : $"api/admin/sectors/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<BedDto>> GetBedsAsync(Guid? sectorId, CancellationToken cancellationToken = default) =>
        await GetListAsync<BedDto>(sectorId is null ? "api/admin/beds" : $"api/admin/beds?sectorId={sectorId}", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> SaveBedAsync(Guid? id, SaveBedRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/admin/beds" : $"api/admin/beds/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<ChecklistTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<ChecklistTemplateDto>("api/admin/templates", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> SaveTemplateAsync(Guid? id, SaveTemplateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/admin/templates" : $"api/admin/templates/{id}", request, cancellationToken);

    public Task<UiResult> SaveColumnAsync(Guid templateId, Guid? columnId, SaveColumnRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(
            columnId is null ? HttpMethod.Post : HttpMethod.Put,
            columnId is null ? $"api/admin/templates/{templateId}/columns" : $"api/admin/templates/{templateId}/columns/{columnId}",
            request,
            cancellationToken);

    public async Task<IReadOnlyList<BedMarkerDefinitionDto>> GetMarkersAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<BedMarkerDefinitionDto>("api/admin/markers", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> SaveMarkerAsync(Guid? id, SaveMarkerRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/admin/markers" : $"api/admin/markers/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<AccessGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<AccessGroupDto>("api/admin/groups", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> SaveGroupAsync(Guid? id, SaveGroupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/admin/groups" : $"api/admin/groups/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<AppUserDto>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<AppUserDto>("api/admin/users", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/admin/users", request, cancellationToken);

    public Task<UiResult> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"api/admin/users/{id}", request, cancellationToken);

    public Task<UiResult> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"api/admin/users/{id}/password", request, cancellationToken);

    public async Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<PermissionDto>("api/admin/permissions", cancellationToken).ConfigureAwait(false);

    public async Task<NotificationConfigurationDto> GetNotificationConfigurationAsync(CancellationToken cancellationToken = default) =>
        await Http.SendAsync<NotificationConfigurationDto>(HttpMethod.Get, "api/admin/notifications", null, true, cancellationToken).ConfigureAwait(false)
            ?? new NotificationConfigurationDto(true, true, "High", true, true, false, string.Empty, string.Empty, 0);

    public Task<UiResult> SaveNotificationConfigurationAsync(SaveNotificationConfigurationRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, "api/admin/notifications", request, cancellationToken);

    public async Task<InstitutionSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        await Http.SendAsync<InstitutionSettingsDto>(HttpMethod.Get, "api/admin/settings", null, true, cancellationToken).ConfigureAwait(false)
            ?? new InstitutionSettingsDto("America/Sao_Paulo", new TimeOnly(19, 0), new TimeOnly(7, 0), 24, 7, 5, true);

    public Task<UiResult> SaveSettingsAsync(SaveInstitutionSettingsRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, "api/admin/settings", request, cancellationToken);

    public async Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<DeviceDto>("api/admin/devices", cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken) =>
        await Http.SendAsync<List<T>>(HttpMethod.Get, path, null, authenticated: true, cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Traduz a resposta HTTP em um resultado que a tela entende, inclusive o conflito de versão.</summary>
    private async Task<UiResult> SendAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        var resposta = await Http.SendRawAsync(method, path, body, authenticated: true, cancellationToken).ConfigureAwait(false);

        if (resposta is null)
        {
            return UiResult.Fail("O servidor não está acessível. Esta alteração exige conexão.");
        }

        if (resposta.IsSuccessStatusCode)
        {
            return UiResult.Ok();
        }

        try
        {
            var problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);

            var mensagem = problema.TryGetProperty("detail", out var detalhe) ? detalhe.GetString() : null;
            var codigo = problema.TryGetProperty("codigo", out var chave) ? chave.GetString() : null;

            return UiResult.Fail(mensagem ?? "Não foi possível salvar.", codigo);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return UiResult.Fail($"Não foi possível salvar ({(int)resposta.StatusCode}).");
        }
    }
}
