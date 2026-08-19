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

/// <summary>Saúde das notificações, agregando permissões e capacidade da plataforma.</summary>
public sealed class NotificationStatusService(
    INotificationPermissionService permissions,
    ILocalNotificationScheduler scheduler,
    INotificationHealthService health,
    IDbContextFactory<LocalDbContext> contextos,
    IClock clock) : INotificationStatusService
{
    public NotificationStatus Current { get; private set; } = NotificationStatus.Unknown;

    public event Action<NotificationStatus>? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var estado = await permissions.GetAsync(cancellationToken).ConfigureAwait(false);
        var problemas = await health.DiagnoseAsync(cancellationToken).ConfigureAwait(false);

        await using var db = await contextos.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var config = await db.NotificationConfigurations.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        // Rastreado de propósito: logo abaixo o estado das permissões é gravado nele.
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

            await using (var db = await contextos.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
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
