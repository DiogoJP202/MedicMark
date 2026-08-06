using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Administration;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Sync;
using ChecklistPlantao.Server.Auth;
using ChecklistPlantao.Server.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Server.Controllers.Admin;

/// <summary>
/// Cadastro de setores, leitos, tipos de checklist, colunas e marcadores.
/// Cada grupo de rotas exige a permissão correspondente — não existe um "é admin" genérico.
/// </summary>
[Authorize]
[Route("api/admin")]
public sealed class AdminStructureController(
    StructureAdminService structure,
    IAppDataContext db,
    SyncNotifier notifier) : ApiControllerBase
{
    [HttpGet("sectors")]
    [RequirePermission(Permissions.AdminSectors)]
    public async Task<IActionResult> ListSectorsAsync(CancellationToken cancellationToken) =>
        Ok((await db.Sectors.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(cancellationToken))
            .Select(ConfigurationQueryService.Map));

    [HttpPost("sectors")]
    [RequirePermission(Permissions.AdminSectors)]
    public Task<IActionResult> CreateSectorAsync([FromBody] SaveSectorRequest request, CancellationToken cancellationToken) =>
        SaveAsync(() => structure.SaveSectorAsync(null, request, cancellationToken), cancellationToken);

    [HttpPut("sectors/{id:guid}")]
    [RequirePermission(Permissions.AdminSectors)]
    public Task<IActionResult> UpdateSectorAsync(Guid id, [FromBody] SaveSectorRequest request, CancellationToken cancellationToken) =>
        SaveAsync(() => structure.SaveSectorAsync(id, request, cancellationToken), cancellationToken);

    [HttpGet("beds")]
    [RequirePermission(Permissions.AdminBeds)]
    public async Task<IActionResult> ListBedsAsync([FromQuery] Guid? sectorId, CancellationToken cancellationToken)
    {
        var query = db.Beds.AsNoTracking();

        if (sectorId is not null)
        {
            query = query.Where(b => b.SectorId == sectorId);
        }

        var beds = await query.OrderBy(b => b.SortOrder).ToListAsync(cancellationToken);
        return Ok(beds.Select(ConfigurationQueryService.Map));
    }

    [HttpPost("beds")]
    [RequirePermission(Permissions.AdminBeds)]
    public Task<IActionResult> CreateBedAsync([FromBody] SaveBedRequest request, CancellationToken cancellationToken) =>
        SaveAsync(() => structure.SaveBedAsync(null, request, cancellationToken), cancellationToken);

    [HttpPut("beds/{id:guid}")]
    [RequirePermission(Permissions.AdminBeds)]
    public Task<IActionResult> UpdateBedAsync(Guid id, [FromBody] SaveBedRequest request, CancellationToken cancellationToken) =>
        SaveAsync(() => structure.SaveBedAsync(id, request, cancellationToken), cancellationToken);

    [HttpGet("templates")]
    [RequirePermission(Permissions.AdminTemplates)]
    public async Task<IActionResult> ListTemplatesAsync(CancellationToken cancellationToken) =>
        Ok((await db.ChecklistTemplates
                .AsNoTracking()
                .Include(t => t.Columns)
                .Include(t => t.Sectors)
                .OrderBy(t => t.SortOrder)
                .ToListAsync(cancellationToken))
            .Select(ConfigurationQueryService.Map));

    [HttpPost("templates")]
    [RequirePermission(Permissions.AdminTemplates)]
    public Task<IActionResult> CreateTemplateAsync([FromBody] SaveTemplateRequest request, CancellationToken cancellationToken) =>
        SaveAsync(() => structure.SaveTemplateAsync(null, request, cancellationToken), cancellationToken);

    [HttpPut("templates/{id:guid}")]
    [RequirePermission(Permissions.AdminTemplates)]
    public Task<IActionResult> UpdateTemplateAsync(Guid id, [FromBody] SaveTemplateRequest request, CancellationToken cancellationToken) =>
        SaveAsync(() => structure.SaveTemplateAsync(id, request, cancellationToken), cancellationToken);

    [HttpPost("templates/{templateId:guid}/columns")]
    [RequirePermission(Permissions.AdminTemplates)]
    public Task<IActionResult> CreateColumnAsync(Guid templateId, [FromBody] SaveColumnRequest request, CancellationToken cancellationToken) =>
        SaveAsync(() => structure.SaveColumnAsync(templateId, null, request, cancellationToken), cancellationToken);

    /// <summary>
    /// Altera nome, horário e parâmetros de notificação da coluna. Mudar o horário obriga os
    /// dispositivos a reagendar — por isso o aviso vai para todos, não só para um setor.
    /// </summary>
    [HttpPut("templates/{templateId:guid}/columns/{columnId:guid}")]
    [RequirePermission(Permissions.AdminTemplates)]
    public Task<IActionResult> UpdateColumnAsync(
        Guid templateId,
        Guid columnId,
        [FromBody] SaveColumnRequest request,
        CancellationToken cancellationToken) =>
        SaveAsync(() => structure.SaveColumnAsync(templateId, columnId, request, cancellationToken), cancellationToken);

    [HttpGet("markers")]
    [RequirePermission(Permissions.AdminTemplates)]
    public async Task<IActionResult> ListMarkersAsync(CancellationToken cancellationToken) =>
        Ok((await db.BedMarkerDefinitions.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync(cancellationToken))
            .Select(ConfigurationQueryService.Map));

    [HttpPost("markers")]
    [RequirePermission(Permissions.AdminTemplates)]
    public Task<IActionResult> CreateMarkerAsync([FromBody] SaveMarkerRequest request, CancellationToken cancellationToken) =>
        SaveAsync(() => structure.SaveMarkerAsync(null, request, cancellationToken), cancellationToken);

    [HttpPut("markers/{id:guid}")]
    [RequirePermission(Permissions.AdminTemplates)]
    public Task<IActionResult> UpdateMarkerAsync(Guid id, [FromBody] SaveMarkerRequest request, CancellationToken cancellationToken) =>
        SaveAsync(() => structure.SaveMarkerAsync(id, request, cancellationToken), cancellationToken);

    [HttpPost("reorder/{entityType}")]
    [RequirePermission(Permissions.AdminSectors)]
    public async Task<IActionResult> ReorderAsync(string entityType, [FromBody] ReorderRequest request, CancellationToken cancellationToken)
    {
        if (!SyncEntityTypes.All.Contains(entityType))
        {
            return BadRequest();
        }

        var result = await structure.ReorderAsync(entityType, request, cancellationToken);

        if (result.IsSuccess)
        {
            await notifier.NotifyConfigurationChangedAsync(cancellationToken);
        }

        return FromResult(result);
    }

    private async Task<IActionResult> SaveAsync<T>(Func<Task<Application.Common.Result<T>>> action, CancellationToken cancellationToken)
    {
        var result = await action();

        if (result.IsSuccess)
        {
            await notifier.NotifyConfigurationChangedAsync(cancellationToken);
        }

        return FromResult(result);
    }
}
