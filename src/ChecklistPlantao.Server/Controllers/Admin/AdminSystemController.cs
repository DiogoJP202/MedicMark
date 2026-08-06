using ChecklistPlantao.Application.Administration;
using ChecklistPlantao.Application.Devices;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Server.Auth;
using ChecklistPlantao.Server.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChecklistPlantao.Server.Controllers.Admin;

/// <summary>Configurações gerais, notificações e dispositivos conhecidos.</summary>
[Authorize]
[Route("api/admin")]
public sealed class AdminSystemController(
    SystemAdminService system,
    DeviceService devices,
    SyncNotifier notifier) : ApiControllerBase
{
    [HttpGet("notifications")]
    [RequirePermission(Permissions.AdminNotifications)]
    [ProducesResponseType<NotificationConfigurationDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotificationsAsync(CancellationToken cancellationToken) =>
        Ok(await system.GetNotificationsAsync(cancellationToken));

    [HttpPut("notifications")]
    [RequirePermission(Permissions.AdminNotifications)]
    public async Task<IActionResult> SaveNotificationsAsync(
        [FromBody] SaveNotificationConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await system.SaveNotificationsAsync(request, cancellationToken);

        if (result.IsSuccess)
        {
            await notifier.NotifyConfigurationChangedAsync(cancellationToken);
        }

        return FromResult(result);
    }

    [HttpGet("settings")]
    [RequirePermission(Permissions.AdminSettings)]
    [ProducesResponseType<InstitutionSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettingsAsync(CancellationToken cancellationToken) =>
        Ok(await system.GetSettingsAsync(cancellationToken));

    /// <summary>
    /// Grava fuso, janela de turno, retenção e validade do acesso offline.
    /// Qualquer uma dessas mudanças reagenda as notificações de todos os dispositivos.
    /// </summary>
    [HttpPut("settings")]
    [RequirePermission(Permissions.AdminSettings)]
    public async Task<IActionResult> SaveSettingsAsync(
        [FromBody] SaveInstitutionSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await system.SaveSettingsAsync(request, cancellationToken);

        if (result.IsSuccess)
        {
            await notifier.NotifyConfigurationChangedAsync(cancellationToken);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Dispositivos conhecidos com o último estado sincronizado.
    /// Atenção: um aparelho totalmente offline não aparece atualizado aqui — o servidor não tem
    /// como conhecer o estado atual dele. Ver docs/KNOWN_LIMITATIONS.md.
    /// </summary>
    [HttpGet("devices")]
    [RequirePermission(Permissions.AdminDevices)]
    [ProducesResponseType<IReadOnlyList<DeviceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDevicesAsync(CancellationToken cancellationToken) =>
        Ok(await devices.ListAsync(cancellationToken));
}
