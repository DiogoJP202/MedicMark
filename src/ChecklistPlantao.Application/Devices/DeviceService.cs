using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Common;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Domain.Common;
using ChecklistPlantao.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Application.Devices;

/// <summary>
/// Registro e saúde dos dispositivos.
///
/// Nada aqui é operacional: nenhum dado de checklist, nenhum vínculo entre usuário e marcação.
/// O objetivo é o administrador conseguir responder "por que este aparelho não está alertando?".
/// </summary>
public sealed class DeviceService(IAppDataContext db, IClock clock)
{
    public async Task<Result<DeviceDto>> RegisterAsync(
        RegisterDeviceRequest request,
        Guid? currentUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.TryParse<DevicePlatform>(request.Platform, ignoreCase: true, out var platform))
        {
            platform = DevicePlatform.Unknown;
        }

        var now = clock.UtcNow;

        var device = await db.DeviceRegistrations
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken)
            .ConfigureAwait(false);

        if (device is null)
        {
            try
            {
                device = new DeviceRegistration(request.DeviceId, request.DeviceName, platform, request.AppVersion, now);
            }
            catch (DomainRuleException ex)
            {
                return OperationError.Validation(ex.Message);
            }

            db.DeviceRegistrations.Add(device);
        }
        else
        {
            device.Rename(request.DeviceName, now);
            device.SetActive(true, now);
        }

        device.Heartbeat(
            request.AppVersion,
            currentUserId,
            device.NotificationsPermissionGranted,
            device.ExactAlarmPermissionGranted,
            device.BatteryOptimizationIgnored,
            device.NotificationHealth,
            device.LastNotificationTestAtUtc,
            now);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Map(device);
    }

    public async Task<Result<DeviceDto>> HeartbeatAsync(
        DeviceHeartbeatRequest request,
        Guid? currentUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var device = await db.DeviceRegistrations
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken)
            .ConfigureAwait(false);

        if (device is null)
        {
            return OperationError.NotFound("Dispositivo não registrado. Registre antes de enviar o estado.");
        }

        if (!Enum.TryParse<NotificationHealth>(request.NotificationHealth, ignoreCase: true, out var health))
        {
            health = NotificationHealth.Unknown;
        }

        var now = clock.UtcNow;

        device.Heartbeat(
            request.AppVersion,
            currentUserId,
            request.NotificationsPermissionGranted,
            request.ExactAlarmPermissionGranted,
            request.BatteryOptimizationIgnored,
            health,
            request.LastNotificationTestAtUtc,
            now);

        device.MarkSynchronized(now);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Map(device);
    }

    public async Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var devices = await db.DeviceRegistrations
            .AsNoTracking()
            .OrderByDescending(d => d.LastSeenAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. devices.Select(Map)];
    }

    /// <summary>
    /// Marca como inativos os aparelhos sem contato há muito tempo. Não apaga o registro: um
    /// aparelho pode voltar do modo offline depois de semanas e o histórico técnico ajuda a
    /// entender o que aconteceu.
    /// </summary>
    public async Task<int> DeactivateStaleAsync(TimeSpan threshold, CancellationToken cancellationToken = default)
    {
        var limit = clock.UtcNow - threshold;

        var stale = await db.DeviceRegistrations
            .Where(d => d.IsActive && d.LastSeenAtUtc < limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var device in stale)
        {
            device.SetActive(false, clock.UtcNow);
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return stale.Count;
    }

    private static DeviceDto Map(DeviceRegistration device) => new(
        device.Id,
        device.DeviceName,
        device.Platform.ToString(),
        device.AppVersion,
        device.LastSeenAtUtc,
        device.LastSyncAtUtc,
        device.NotificationsPermissionGranted,
        device.ExactAlarmPermissionGranted,
        device.BatteryOptimizationIgnored,
        device.NotificationHealth.ToString(),
        device.LastNotificationTestAtUtc,
        device.CurrentUserId,
        device.IsActive);
}
