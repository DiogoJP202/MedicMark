using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Seeding;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Domain.Structure;
using ChecklistPlantao.Infrastructure.Persistence;
using ChecklistPlantao.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Infrastructure.Seeding;

/// <summary>
/// Aplica os dados iniciais. É idempotente: rodar de novo não duplica nada e não desfaz o que
/// o administrador editou — só cria o que ainda não existe.
///
/// A criação do usuário administrador NÃO está aqui: depende do Identity e acontece no servidor,
/// a partir de configuração, para que nenhuma senha viva no repositório (D-012).
/// </summary>
public sealed class DatabaseSeeder(AppDbContext db, ILogger<DatabaseSeeder> logger)
{
    public async Task<int> SeedAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var created = 0;

        created += await SeedPermissionsAsync(cancellationToken).ConfigureAwait(false);
        created += await SeedSettingsAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        created += await SeedNotificationConfigurationAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        created += await SeedSectorsAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        created += await SeedBedsAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        created += await SeedTemplatesAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        created += await SeedMarkersAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        created += await SeedGroupsAsync(nowUtc, cancellationToken).ConfigureAwait(false);

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seed aplicado: {Quantidade} registro(s) criado(s).", created);
        }

        return created;
    }

    private async Task<int> SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.PermissionDefinitions
            .Select(p => p.Key)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var created = 0;

        foreach (var (key, description) in Permissions.Catalog.Where(p => !existing.Contains(p.Key)))
        {
            db.PermissionDefinitions.Add(new PermissionDefinition(key, description));
            created++;
        }

        return created;
    }

    private async Task<int> SeedSettingsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existing = await db.AppSettings
            .Select(s => s.Key)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var created = 0;

        foreach (var (key, value) in InstitutionSettingsSerializer.Flatten(InstitutionSettings.Default))
        {
            if (existing.Contains(key))
            {
                continue;
            }

            db.AppSettings.Add(new AppSetting(key, value, nowUtc));
            created++;
        }

        return created;
    }

    private async Task<int> SeedNotificationConfigurationAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var exists = await db.NotificationConfigurations
            .AnyAsync(c => c.Id == NotificationConfiguration.SingletonId, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return 0;
        }

        db.NotificationConfigurations.Add(new NotificationConfiguration(nowUtc));
        return 1;
    }

    private async Task<int> SeedSectorsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existing = await db.Sectors.Select(s => s.Id).ToHashSetAsync(cancellationToken).ConfigureAwait(false);
        var created = 0;

        foreach (var seed in SeedCatalog.Sectors.Where(s => !existing.Contains(s.Id)))
        {
            db.Sectors.Add(new Sector(seed.Id, seed.Name, description: null, seed.SortOrder, nowUtc));
            created++;
        }

        return created;
    }

    private async Task<int> SeedBedsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existing = await db.Beds.Select(b => b.Id).ToHashSetAsync(cancellationToken).ConfigureAwait(false);
        var sectorIds = SeedCatalog.Sectors.ToDictionary(s => s.Name, s => s.Id, StringComparer.Ordinal);
        var created = 0;

        foreach (var seed in SeedCatalog.Beds.Where(b => !existing.Contains(b.Id)))
        {
            if (!sectorIds.TryGetValue(seed.SectorName, out var sectorId))
            {
                continue;
            }

            db.Beds.Add(new Bed(seed.Id, sectorId, seed.Code, description: null, seed.SortOrder, nowUtc));
            created++;
        }

        return created;
    }

    private async Task<int> SeedTemplatesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existingTemplates = await db.ChecklistTemplates.Select(t => t.Id).ToHashSetAsync(cancellationToken).ConfigureAwait(false);
        var existingColumns = await db.ChecklistColumns.Select(c => c.Id).ToHashSetAsync(cancellationToken).ConfigureAwait(false);
        var created = 0;

        foreach (var seed in SeedCatalog.Templates)
        {
            ChecklistTemplate template;

            if (existingTemplates.Contains(seed.Id))
            {
                template = await db.ChecklistTemplates
                    .Include(t => t.Columns)
                    .FirstAsync(t => t.Id == seed.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                template = new ChecklistTemplate(seed.Id, seed.Name, seed.Code, description: null, seed.SortOrder, nowUtc);
                db.ChecklistTemplates.Add(template);
                created++;
            }

            foreach (var column in SeedCatalog.Columns.Where(c => c.TemplateCode == seed.Code && !existingColumns.Contains(c.Id)))
            {
                template.AddColumn(column.Id, column.DisplayName, column.TriggerTime, column.SortOrder, nowUtc);
                created++;
            }
        }

        return created;
    }

    private async Task<int> SeedMarkersAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existing = await db.BedMarkerDefinitions.Select(m => m.Id).ToHashSetAsync(cancellationToken).ConfigureAwait(false);
        var created = 0;

        foreach (var seed in SeedCatalog.Markers.Where(m => !existing.Contains(m.Id)))
        {
            db.BedMarkerDefinitions.Add(new BedMarkerDefinition(seed.Id, seed.Name, seed.Code, seed.SortOrder, nowUtc));
            created++;
        }

        return created;
    }

    private async Task<int> SeedGroupsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existing = await db.AccessGroups.Select(g => g.Id).ToHashSetAsync(cancellationToken).ConfigureAwait(false);
        var sectorIds = SeedCatalog.Sectors.ToDictionary(s => s.Name, s => s.Id, StringComparer.Ordinal);
        var created = 0;

        foreach (var seed in SeedCatalog.Groups.Where(g => !existing.Contains(g.Id)))
        {
            var group = new AccessGroup(seed.Id, seed.Name, seed.Description, seed.GrantsAllSectors, nowUtc);
            group.ReplacePermissions(seed.Permissions, nowUtc);
            group.ReplaceSectors(
                seed.SectorNames.Select(name => sectorIds.TryGetValue(name, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty),
                nowUtc);

            db.AccessGroups.Add(group);
            created++;
        }

        return created;
    }
}
