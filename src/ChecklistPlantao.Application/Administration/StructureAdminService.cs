using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Common;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Domain.Common;
using ChecklistPlantao.Domain.Structure;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Application.Administration;

/// <summary>
/// Cadastro de setores, leitos, tipos de checklist, colunas e marcadores.
///
/// Toda alteração é versionada: a requisição informa a versão que o administrador estava vendo e,
/// se outra pessoa já alterou, a operação é recusada com o estado atual. Nunca há sobrescrita
/// silenciosa em configuração — diferente das marcações, aqui não existe "conclusão vence".
/// </summary>
public sealed class StructureAdminService(IAppDataContext db, IClock clock)
{
    public async Task<Result<SectorDto>> SaveSectorAsync(Guid? id, SaveSectorRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;

        var duplicate = await db.Sectors
            .AnyAsync(s => s.Name == request.Name.Trim() && (id == null || s.Id != id), cancellationToken)
            .ConfigureAwait(false);

        if (duplicate)
        {
            return OperationError.Duplicate($"Já existe um setor chamado \"{request.Name.Trim()}\".");
        }

        Sector sector;

        if (id is null)
        {
            sector = new Sector(Guid.CreateVersion7(), request.Name, request.Description, request.SortOrder, now);
            db.Sectors.Add(sector);
        }
        else
        {
            var found = await db.Sectors.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return OperationError.NotFound("Setor não encontrado.");
            }

            if (found.Version != request.BaseVersion)
            {
                return VersionConflict("setor");
            }

            sector = found;
            sector.Update(request.Name, request.Description, request.SortOrder, now);
            sector.SetActive(request.IsActive, now);
        }

        try
        {
            sector.SetShiftOverride(request.ShiftStart, request.ShiftEnd, now);
        }
        catch (DomainRuleException ex)
        {
            return OperationError.Validation(ex.Message);
        }

        db.AppendChange(SyncEntityTypes.Sector, sector.Id, id is null ? SyncChangeType.Created : SyncChangeType.Updated, sector.Version, sector.Id, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ConfigurationQueryService.Map(sector);
    }

    public async Task<Result<BedDto>> SaveBedAsync(Guid? id, SaveBedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        var code = request.Code.Trim();

        var sectorExists = await db.Sectors.AnyAsync(s => s.Id == request.SectorId, cancellationToken).ConfigureAwait(false);
        if (!sectorExists)
        {
            return OperationError.NotFound("Setor não encontrado.");
        }

        var duplicate = await db.Beds
            .AnyAsync(b => b.SectorId == request.SectorId && b.Code == code && b.IsActive && (id == null || b.Id != id), cancellationToken)
            .ConfigureAwait(false);

        if (duplicate && request.IsActive)
        {
            return OperationError.Duplicate($"Já existe um leito ativo com o código \"{code}\" neste setor.");
        }

        Bed bed;

        if (id is null)
        {
            bed = new Bed(Guid.CreateVersion7(), request.SectorId, code, request.Description, request.SortOrder, now);
            db.Beds.Add(bed);
        }
        else
        {
            var found = await db.Beds.FirstOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return OperationError.NotFound("Leito não encontrado.");
            }

            if (found.Version != request.BaseVersion)
            {
                return VersionConflict("leito");
            }

            bed = found;
            bed.Update(code, request.Description, request.SortOrder, now);
            bed.MoveTo(request.SectorId, now);
            bed.SetActive(request.IsActive, now);
        }

        db.AppendChange(SyncEntityTypes.Bed, bed.Id, id is null ? SyncChangeType.Created : SyncChangeType.Updated, bed.Version, bed.SectorId, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ConfigurationQueryService.Map(bed);
    }

    public async Task<Result<ChecklistTemplateDto>> SaveTemplateAsync(Guid? id, SaveTemplateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        ChecklistTemplate template;

        if (id is null)
        {
            var code = Slug(request.Name);
            if (await db.ChecklistTemplates.AnyAsync(t => t.Code == code, cancellationToken).ConfigureAwait(false))
            {
                return OperationError.Duplicate($"Já existe um tipo de checklist com o código \"{code}\".");
            }

            template = new ChecklistTemplate(Guid.CreateVersion7(), request.Name, code, request.Description, request.SortOrder, now);
            db.ChecklistTemplates.Add(template);
        }
        else
        {
            var found = await db.ChecklistTemplates
                .Include(t => t.Columns)
                .Include(t => t.Sectors)
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (found is null)
            {
                return OperationError.NotFound("Tipo de checklist não encontrado.");
            }

            if (found.Version != request.BaseVersion)
            {
                return VersionConflict("tipo de checklist");
            }

            template = found;
            template.Update(request.Name, request.Description, request.SortOrder, now);
            template.SetActive(request.IsActive, now);
        }

        template.ReplaceSectors(request.SectorIds, now);

        db.AppendChange(SyncEntityTypes.ChecklistTemplate, template.Id, id is null ? SyncChangeType.Created : SyncChangeType.Updated, template.Version, null, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ConfigurationQueryService.Map(template);
    }

    public async Task<Result<ChecklistColumnDto>> SaveColumnAsync(
        Guid templateId,
        Guid? columnId,
        SaveColumnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;

        var template = await db.ChecklistTemplates
            .Include(t => t.Columns)
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            .ConfigureAwait(false);

        if (template is null)
        {
            return OperationError.NotFound("Tipo de checklist não encontrado.");
        }

        ChecklistColumn column;

        try
        {
            if (columnId is null)
            {
                column = template.AddColumn(Guid.CreateVersion7(), request.DisplayName, request.TriggerTime, request.SortOrder, now);
            }
            else
            {
                var found = template.Columns.FirstOrDefault(c => c.Id == columnId);
                if (found is null)
                {
                    return OperationError.NotFound("Coluna não encontrada neste tipo de checklist.");
                }

                if (found.Version != request.BaseVersion)
                {
                    return VersionConflict("coluna");
                }

                column = found;
                column.Update(request.DisplayName, request.TriggerTime, request.SortOrder, now);
                column.SetActive(request.IsActive, now);
            }

            column.ConfigureNotification(
                request.NotificationEnabled,
                request.LeadTimeMinutes,
                request.GracePeriodMinutes,
                request.RepeatIntervalMinutes,
                request.MaximumRepeats,
                request.AllowSnooze,
                request.SnoozeMinutes,
                now);
        }
        catch (DomainRuleException ex)
        {
            return OperationError.Validation(ex.Message);
        }

        db.AppendChange(SyncEntityTypes.ChecklistColumn, column.Id, columnId is null ? SyncChangeType.Created : SyncChangeType.Updated, column.Version, null, now);
        db.AppendChange(SyncEntityTypes.ChecklistTemplate, template.Id, SyncChangeType.Updated, template.Version, null, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ConfigurationQueryService.Map(column);
    }

    public async Task<Result<BedMarkerDefinitionDto>> SaveMarkerAsync(Guid? id, SaveMarkerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        BedMarkerDefinition marker;

        if (id is null)
        {
            var code = Slug(request.Name);
            if (await db.BedMarkerDefinitions.AnyAsync(m => m.Code == code, cancellationToken).ConfigureAwait(false))
            {
                return OperationError.Duplicate($"Já existe um marcador com o código \"{code}\".");
            }

            marker = new BedMarkerDefinition(Guid.CreateVersion7(), request.Name, code, request.SortOrder, now);
            db.BedMarkerDefinitions.Add(marker);
        }
        else
        {
            var found = await db.BedMarkerDefinitions.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return OperationError.NotFound("Marcador não encontrado.");
            }

            if (found.Version != request.BaseVersion)
            {
                return VersionConflict("marcador");
            }

            marker = found;
            marker.Update(request.Name, request.SortOrder, now);
            marker.SetActive(request.IsActive, now);
        }

        db.AppendChange(SyncEntityTypes.BedMarkerDefinition, marker.Id, id is null ? SyncChangeType.Created : SyncChangeType.Updated, marker.Version, null, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ConfigurationQueryService.Map(marker);
    }

    /// <summary>Reordenação em lote de setores, leitos, colunas ou marcadores.</summary>
    public async Task<Result> ReorderAsync(string entityType, ReorderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        var order = request.Items.ToDictionary(i => i.Id, i => i.SortOrder);

        switch (entityType)
        {
            case SyncEntityTypes.Sector:
                foreach (var sector in await db.Sectors.Where(s => order.Keys.Contains(s.Id)).ToListAsync(cancellationToken).ConfigureAwait(false))
                {
                    sector.Update(sector.Name, sector.Description, order[sector.Id], now);
                    db.AppendChange(SyncEntityTypes.Sector, sector.Id, SyncChangeType.Updated, sector.Version, sector.Id, now);
                }

                break;

            case SyncEntityTypes.Bed:
                foreach (var bed in await db.Beds.Where(b => order.Keys.Contains(b.Id)).ToListAsync(cancellationToken).ConfigureAwait(false))
                {
                    bed.Update(bed.Code, bed.Description, order[bed.Id], now);
                    db.AppendChange(SyncEntityTypes.Bed, bed.Id, SyncChangeType.Updated, bed.Version, bed.SectorId, now);
                }

                break;

            case SyncEntityTypes.ChecklistColumn:
                foreach (var column in await db.ChecklistColumns.Where(c => order.Keys.Contains(c.Id)).ToListAsync(cancellationToken).ConfigureAwait(false))
                {
                    column.Update(column.DisplayName, column.TriggerTime, order[column.Id], now);
                    db.AppendChange(SyncEntityTypes.ChecklistColumn, column.Id, SyncChangeType.Updated, column.Version, null, now);
                }

                break;

            case SyncEntityTypes.BedMarkerDefinition:
                foreach (var marker in await db.BedMarkerDefinitions.Where(m => order.Keys.Contains(m.Id)).ToListAsync(cancellationToken).ConfigureAwait(false))
                {
                    marker.Update(marker.Name, order[marker.Id], now);
                    db.AppendChange(SyncEntityTypes.BedMarkerDefinition, marker.Id, SyncChangeType.Updated, marker.Version, null, now);
                }

                break;

            default:
                return OperationError.Validation("Tipo de entidade não suporta reordenação.");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private static OperationError VersionConflict(string what) =>
        OperationError.Conflict($"Outra pessoa alterou este {what} enquanto você editava. Atualize a tela e refaça a alteração.");

    /// <summary>
    /// Código estável a partir do nome. Só é gerado na criação — renomear não muda o código,
    /// para não quebrar referências já sincronizadas nos dispositivos.
    /// </summary>
    private static string Slug(string name)
    {
        var normalized = name.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);

            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
        }

        return builder.Length > 0 ? builder.ToString() : Guid.CreateVersion7().ToString("N")[..8].ToUpperInvariant();
    }
}
