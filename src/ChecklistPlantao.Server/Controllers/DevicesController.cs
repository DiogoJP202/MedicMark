using System.Reflection;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Devices;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChecklistPlantao.Server.Controllers;

[Route("api")]
public sealed class DevicesController(
    DeviceService devices,
    IInstitutionSettingsProvider settings,
    IClock clock,
    IHostEnvironment environment) : ApiControllerBase
{
    [HttpPost("devices/register")]
    [Authorize]
    [ProducesResponseType<DeviceDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterDeviceRequest request, CancellationToken cancellationToken) =>
        FromResult(await devices.RegisterAsync(request, User.GetUserId(), cancellationToken));

    [HttpPost("devices/heartbeat")]
    [Authorize]
    [ProducesResponseType<DeviceDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> HeartbeatAsync([FromBody] DeviceHeartbeatRequest request, CancellationToken cancellationToken) =>
        FromResult(await devices.HeartbeatAsync(request, User.GetUserId(), cancellationToken));

    /// <summary>
    /// Sonda anônima usada pela tela de configuração inicial para testar o endereço do servidor.
    /// Devolve apenas o necessário para confirmar que se falou com a aplicação certa — sem expor
    /// versão de framework, caminho de banco ou qualquer detalhe de infraestrutura.
    /// </summary>
    [HttpGet("server-info")]
    [AllowAnonymous]
    [ProducesResponseType<ServerProbeResponse>(StatusCodes.Status200OK)]
    public IActionResult ServerInfo() => Ok(new ServerProbeResponse(
        "ChecklistPlantao",
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
        environment.EnvironmentName,
        clock.UtcNow,
        settings.Current.TimeZoneId));
}
