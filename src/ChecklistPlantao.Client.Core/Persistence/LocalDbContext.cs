using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Domain.Structure;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Client.Core.Persistence;

/// <summary>
/// Banco SQLite local do aparelho.
///
/// Não é cache descartável: é a fonte de verdade enquanto o dispositivo está sem servidor.
/// Guarda a configuração sincronizada, a sessão operacional atual, as marcações, as
/// classificações, a fila de envio, o cursor e o estado técnico do aparelho.
///
/// As entidades de configuração e operação são as MESMAS do domínio usadas pelo servidor —
/// o que evita duas modelagens divergindo com o tempo.
/// </summary>
public sealed class LocalDbContext(DbContextOptions<LocalDbContext> options) : DbContext(options)
{
    public DbSet<Sector> Sectors => Set<Sector>();

    public DbSet<Bed> Beds => Set<Bed>();

    public DbSet<ChecklistTemplate> ChecklistTemplates => Set<ChecklistTemplate>();

    public DbSet<ChecklistColumn> ChecklistColumns => Set<ChecklistColumn>();

    public DbSet<BedMarkerDefinition> BedMarkerDefinitions => Set<BedMarkerDefinition>();

    public DbSet<OperationalSession> OperationalSessions => Set<OperationalSession>();

    public DbSet<SessionBed> SessionBeds => Set<SessionBed>();

    public DbSet<ChecklistEntry> ChecklistEntries => Set<ChecklistEntry>();

    public DbSet<SessionBedMarker> SessionBedMarkers => Set<SessionBedMarker>();

    public DbSet<NotificationConfiguration> NotificationConfigurations => Set<NotificationConfiguration>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<SyncOutboxItem> Outbox => Set<SyncOutboxItem>();

    public DbSet<SyncState> SyncState => Set<SyncState>();

    public DbSet<LocalCredential> Credentials => Set<LocalCredential>();

    public DbSet<DeviceState> DeviceState => Set<DeviceState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Mesmas propriedades calculadas ignoradas no servidor: o EF as trataria como
        // navegação ou coluna. Ver AppDbContext.IgnoreComputedProperties.
        builder.Entity<ChecklistTemplate>().Ignore(t => t.ActiveColumnsInOrder).Ignore(t => t.AppliesToAllSectors);
        builder.Entity<ChecklistColumn>().Ignore(c => c.IsSchedulable);
        builder.Entity<OperationalSession>().Ignore(s => s.IsOpen);

        builder.Entity<Sector>(e =>
        {
            e.ToTable("Setores");
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(128).IsRequired();
        });

        builder.Entity<Bed>(e =>
        {
            e.ToTable("Leitos");
            e.HasKey(b => b.Id);
            e.Property(b => b.Code).HasMaxLength(32).IsRequired();
            e.HasIndex(b => new { b.SectorId, b.SortOrder });
        });

        builder.Entity<ChecklistTemplate>(e =>
        {
            e.ToTable("TiposChecklist");
            e.HasKey(t => t.Id);
            e.HasMany(t => t.Columns).WithOne().HasForeignKey(c => c.ChecklistTemplateId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.Sectors).WithOne().HasForeignKey(s => s.ChecklistTemplateId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(t => t.Columns).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(t => t.Sectors).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<ChecklistTemplateSector>(e =>
        {
            e.ToTable("TipoChecklistSetores");
            e.HasKey(s => new { s.ChecklistTemplateId, s.SectorId });
        });

        builder.Entity<ChecklistColumn>(e =>
        {
            e.ToTable("ColunasChecklist");
            e.HasKey(c => c.Id);
            e.Property(c => c.DisplayName).HasMaxLength(64).IsRequired();
        });

        builder.Entity<BedMarkerDefinition>(e =>
        {
            e.ToTable("Marcadores");
            e.HasKey(m => m.Id);
        });

        builder.Entity<OperationalSession>(e =>
        {
            e.ToTable("Sessoes");
            e.HasKey(s => s.Id);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            e.HasMany(s => s.Beds).WithOne().HasForeignKey(b => b.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(s => s.Beds).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasIndex(s => new { s.SectorId, s.Status });
        });

        builder.Entity<SessionBed>(e =>
        {
            e.ToTable("SessaoLeitos");
            e.HasKey(b => new { b.SessionId, b.BedId });
        });

        builder.Entity<ChecklistEntry>(e =>
        {
            e.ToTable("Marcacoes");
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.SessionId, c.BedId, c.ChecklistTemplateId, c.ChecklistColumnId })
                .IsUnique()
                .HasDatabaseName("IX_Marcacoes_Celula");
        });

        builder.Entity<SessionBedMarker>(e =>
        {
            e.ToTable("SessaoLeitoMarcadores");
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.SessionId, m.BedId, m.MarkerDefinitionId })
                .IsUnique()
                .HasDatabaseName("IX_SessaoLeitoMarcadores_Unico");
        });

        builder.Entity<NotificationConfiguration>(e =>
        {
            e.ToTable("ConfiguracaoNotificacoes");
            e.HasKey(c => c.Id);
            e.Property(c => c.Priority).HasConversion<string>().HasMaxLength(16);
        });

        builder.Entity<AppSetting>(e =>
        {
            e.ToTable("Configuracoes");
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.Key).IsUnique();
        });

        builder.Entity<SyncOutboxItem>(e =>
        {
            e.ToTable("FilaEnvio");
            e.HasKey(o => o.Id);
            e.Property(o => o.EntityType).HasMaxLength(64).IsRequired();
            e.Property(o => o.OperationType).HasConversion<string>().HasMaxLength(16);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(o => o.LastError).HasMaxLength(300);
            e.HasIndex(o => o.OperationId).IsUnique();
            e.HasIndex(o => new { o.Status, o.CreatedAtUtc });
        });

        builder.Entity<SyncState>(e =>
        {
            e.ToTable("EstadoSincronizacao");
            e.HasKey(s => s.Id);
        });

        builder.Entity<LocalCredential>(e =>
        {
            e.ToTable("CredenciaisLocais");
            e.HasKey(c => c.UserId);
            e.Property(c => c.UserName).HasMaxLength(64).IsRequired();
            e.Property(c => c.DisplayName).HasMaxLength(128).IsRequired();
            e.HasIndex(c => c.UserName).IsUnique();
        });

        builder.Entity<DeviceState>(e =>
        {
            e.ToTable("EstadoDispositivo");
            e.HasKey(d => d.Id);
            e.Property(d => d.DeviceName).HasMaxLength(128).IsRequired();
            e.Property(d => d.ServerUrl).HasMaxLength(256);
            e.Property(d => d.NotificationHealth).HasConversion<string>().HasMaxLength(16);
        });
    }
}
