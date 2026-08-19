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

/// <summary>Reúne o diagnóstico exibido na tela "Estado do dispositivo".</summary>
public sealed class DeviceDiagnosticsService(
    IDbContextFactory<LocalDbContext> contextos,
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
        await using var db = await contextos.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var estado = await db.SyncState.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var pendentes = await db.Outbox.AsNoTracking().CountAsync(o => o.Status != Domain.Sync.OutboxItemStatus.Done, cancellationToken).ConfigureAwait(false);

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
