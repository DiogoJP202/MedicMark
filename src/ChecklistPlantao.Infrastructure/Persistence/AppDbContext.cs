using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Domain.Structure;
using ChecklistPlantao.Domain.Sync;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Infrastructure.Persistence;

/// <summary>
/// Banco central. Herda de <see cref="IdentityUserContext{TUser, TKey}"/> — e não de
/// <c>IdentityDbContext</c> — porque o sistema não usa papéis do Identity: a autorização é feita
/// por grupos e chaves de permissão próprios.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<AppIdentityUser, Guid>(options), Application.Abstractions.IAppDataContext
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();

    public DbSet<AccessGroup> AccessGroups => Set<AccessGroup>();

    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    public DbSet<PermissionDefinition> PermissionDefinitions => Set<PermissionDefinition>();

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

    public DbSet<DeviceRegistration> DeviceRegistrations => Set<DeviceRegistration>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();

    public DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();

    /// <summary>
    /// Registra uma alteração no log de sincronização. É chamado explicitamente pelos serviços de
    /// aplicação em vez de por mágica no SaveChanges: o serviço é quem sabe a que setor a
    /// alteração pertence, e um log implícito seria mais difícil de auditar do que a chamada direta.
    /// </summary>
    public void AppendChange(string entityType, Guid entityId, SyncChangeType changeType, int version, Guid? sectorId, DateTime nowUtc) =>
        ChangeLog.Add(new ChangeLogEntry(entityType, entityId, changeType, version, sectorId, nowUtc));

    public Task<long> LatestChangeSequenceAsync(CancellationToken cancellationToken = default) =>
        ChangeLog.AsNoTracking().OrderByDescending(c => c.Sequence).Select(c => c.Sequence).FirstOrDefaultAsync(cancellationToken);

    public async Task<long?> OldestChangeSequenceAsync(CancellationToken cancellationToken = default)
    {
        var oldest = await ChangeLog
            .AsNoTracking()
            .OrderBy(c => c.Sequence)
            .Select(c => (long?)c.Sequence)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return oldest;
    }

    /// <summary>
    /// A projeção para <c>ChangeLogRow</c> acontece depois de filtrar e ordenar: um construtor
    /// posicional impede o EF de enxergar as propriedades dentro da consulta.
    /// </summary>
    public async Task<IReadOnlyList<Application.Abstractions.ChangeLogRow>> ChangesAfterAsync(
        long cursor,
        IReadOnlyCollection<Guid>? sectorFilter,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = ChangeLog.AsNoTracking().Where(c => c.Sequence > cursor);

        if (sectorFilter is not null)
        {
            var ids = sectorFilter.ToList();
            query = query.Where(c => c.SectorId == null || ids.Contains(c.SectorId.Value));
        }

        var rows = await query
            .OrderBy(c => c.Sequence)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(c => new Application.Abstractions.ChangeLogRow(
            c.Sequence,
            c.EntityType,
            c.EntityId,
            c.ChangeType,
            c.Version,
            c.SectorId,
            c.ChangedAtUtc))];
    }

    public async Task<Dictionary<Guid, int?>> ProcessedOperationVersionsAsync(
        IReadOnlyCollection<Guid> operationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationIds);

        if (operationIds.Count == 0)
        {
            return [];
        }

        var ids = operationIds.ToList();

        var rows = await ProcessedOperations
            .AsNoTracking()
            .Where(p => ids.Contains(p.OperationId))
            .Select(p => new { p.OperationId, p.ResultingVersion })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.OperationId, r => r.ResultingVersion);
    }

    public void AppendProcessedOperation(
        Guid operationId,
        string entityType,
        Guid entityId,
        string status,
        int? resultingVersion,
        DateTime processedAtUtc) =>
        ProcessedOperations.Add(new ProcessedOperation(operationId, entityType, entityId, status, resultingVersion, processedAtUtc));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppIdentityUser>().ToTable("Credenciais");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("CredenciaisClaims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("CredenciaisLogins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("CredenciaisTokens");

        IgnoreComputedProperties(builder);
        ConfigureAccess(builder);
        ConfigureStructure(builder);
        ConfigureOperations(builder);
        ConfigureSystem(builder);
    }

    /// <summary>
    /// Propriedades calculadas do domínio que o EF tentaria mapear por convenção.
    ///
    /// <c>ChecklistTemplate.ActiveColumnsInOrder</c> é o caso crítico: por ser
    /// <c>IEnumerable&lt;ChecklistColumn&gt;</c>, o EF a trata como navegação e falha ao tentar
    /// materializar nela. As demais entram por precaução — todas são projeções, nunca estado.
    /// </summary>
    private static void IgnoreComputedProperties(ModelBuilder builder)
    {
        builder.Entity<ChecklistTemplate>().Ignore(t => t.ActiveColumnsInOrder).Ignore(t => t.AppliesToAllSectors);
        builder.Entity<ChecklistColumn>().Ignore(c => c.IsSchedulable);
        builder.Entity<AccessGroup>().Ignore(g => g.PermissionKeys).Ignore(g => g.SectorIds);
        builder.Entity<AppUser>().Ignore(u => u.GroupIds);
        builder.Entity<OperationalSession>().Ignore(s => s.IsOpen);
    }

    private static void ConfigureAccess(ModelBuilder builder)
    {
        builder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Usuarios");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserName).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => e.UserName).IsUnique();
            entity.HasMany(e => e.Groups).WithOne().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(e => e.Groups).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<AccessGroup>(entity =>
        {
            entity.ToTable("Grupos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(512);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasMany(e => e.Permissions).WithOne().HasForeignKey(p => p.GroupId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.SectorAccesses).WithOne().HasForeignKey(s => s.GroupId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(e => e.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(e => e.SectorAccesses).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<UserGroup>(entity =>
        {
            entity.ToTable("UsuarioGrupos");
            entity.HasKey(e => new { e.UserId, e.GroupId });
            entity.HasOne<AccessGroup>().WithMany().HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GroupPermission>(entity =>
        {
            entity.ToTable("GrupoPermissoes");
            entity.HasKey(e => new { e.GroupId, e.PermissionKey });
            entity.Property(e => e.PermissionKey).HasMaxLength(64).IsRequired();
        });

        builder.Entity<GroupSectorAccess>(entity =>
        {
            entity.ToTable("GrupoSetores");
            entity.HasKey(e => new { e.GroupId, e.SectorId });
            entity.HasOne<Sector>().WithMany().HasForeignKey(e => e.SectorId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PermissionDefinition>(entity =>
        {
            entity.ToTable("Permissoes");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(64);
            entity.Property(e => e.Description).HasMaxLength(256).IsRequired();
        });
    }

    private static void ConfigureStructure(ModelBuilder builder)
    {
        builder.Entity<Sector>(entity =>
        {
            entity.ToTable("Setores");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(512);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        builder.Entity<Bed>(entity =>
        {
            entity.ToTable("Leitos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(256);
            entity.HasOne<Sector>().WithMany().HasForeignKey(e => e.SectorId).OnDelete(DeleteBehavior.Restrict);

            // Impede dois leitos ATIVOS com o mesmo código no mesmo setor. Leitos desativados
            // podem repetir o código porque o histórico de um leito antigo não deve travar o cadastro.
            entity.HasIndex(e => new { e.SectorId, e.Code })
                .IsUnique()
                .HasFilter("\"IsActive\" = 1");
        });

        builder.Entity<ChecklistTemplate>(entity =>
        {
            entity.ToTable("TiposChecklist");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Code).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(512);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasMany(e => e.Columns).WithOne().HasForeignKey(c => c.ChecklistTemplateId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Sectors).WithOne().HasForeignKey(s => s.ChecklistTemplateId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(e => e.Columns).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(e => e.Sectors).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<ChecklistTemplateSector>(entity =>
        {
            entity.ToTable("TipoChecklistSetores");
            entity.HasKey(e => new { e.ChecklistTemplateId, e.SectorId });
            entity.HasOne<Sector>().WithMany().HasForeignKey(e => e.SectorId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChecklistColumn>(entity =>
        {
            entity.ToTable("ColunasChecklist");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DisplayName).HasMaxLength(64).IsRequired();

            // Nome único por template entre as colunas ativas.
            entity.HasIndex(e => new { e.ChecklistTemplateId, e.DisplayName })
                .IsUnique()
                .HasFilter("\"IsActive\" = 1");

            entity.HasIndex(e => new { e.ChecklistTemplateId, e.SortOrder });
        });

        builder.Entity<BedMarkerDefinition>(entity =>
        {
            entity.ToTable("Marcadores");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Code).HasMaxLength(32).IsRequired();
            entity.HasIndex(e => e.Code).IsUnique();
        });
    }

    private static void ConfigureOperations(ModelBuilder builder)
    {
        builder.Entity<OperationalSession>(entity =>
        {
            entity.ToTable("Sessoes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<Sector>().WithMany().HasForeignKey(e => e.SectorId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Beds).WithOne().HasForeignKey(b => b.SessionId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(e => e.Beds).UsePropertyAccessMode(PropertyAccessMode.Field);

            // No máximo uma sessão ABERTA por setor e data de serviço.
            entity.HasIndex(e => new { e.SectorId, e.ServiceDate })
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'")
                .HasDatabaseName("IX_Sessoes_Setor_Data_Aberta");
        });

        builder.Entity<SessionBed>(entity =>
        {
            entity.ToTable("SessaoLeitos");
            entity.HasKey(e => new { e.SessionId, e.BedId });
            entity.HasOne<Bed>().WithMany().HasForeignKey(e => e.BedId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChecklistEntry>(entity =>
        {
            entity.ToTable("Marcacoes");
            entity.HasKey(e => e.Id);
            entity.HasOne<OperationalSession>().WithMany().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Bed>().WithMany().HasForeignKey(e => e.BedId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ChecklistColumn>().WithMany().HasForeignKey(e => e.ChecklistColumnId).OnDelete(DeleteBehavior.Cascade);

            // Uma única marcação por sessão + leito + tipo + coluna.
            entity.HasIndex(e => new { e.SessionId, e.BedId, e.ChecklistTemplateId, e.ChecklistColumnId })
                .IsUnique()
                .HasDatabaseName("IX_Marcacoes_Celula");

            entity.HasIndex(e => new { e.SessionId, e.ChecklistColumnId });
        });

        builder.Entity<SessionBedMarker>(entity =>
        {
            entity.ToTable("SessaoLeitoMarcadores");
            entity.HasKey(e => e.Id);
            entity.HasOne<OperationalSession>().WithMany().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Bed>().WithMany().HasForeignKey(e => e.BedId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<BedMarkerDefinition>().WithMany().HasForeignKey(e => e.MarkerDefinitionId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.SessionId, e.BedId, e.MarkerDefinitionId })
                .IsUnique()
                .HasDatabaseName("IX_SessaoLeitoMarcadores_Unico");
        });
    }

    private static void ConfigureSystem(ModelBuilder builder)
    {
        builder.Entity<NotificationConfiguration>(entity =>
        {
            entity.ToTable("ConfiguracaoNotificacoes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Priority).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.TitleTemplate).HasMaxLength(256).IsRequired();
            entity.Property(e => e.BodyTemplate).HasMaxLength(512).IsRequired();
        });

        builder.Entity<DeviceRegistration>(entity =>
        {
            entity.ToTable("Dispositivos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeviceName).HasMaxLength(128).IsRequired();
            entity.Property(e => e.AppVersion).HasMaxLength(32);
            entity.Property(e => e.Platform).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.NotificationHealth).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(e => e.LastSeenAtUtc);
        });

        builder.Entity<AppSetting>(entity =>
        {
            entity.ToTable("Configuracoes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Value).HasMaxLength(512).IsRequired();
            entity.HasIndex(e => e.Key).IsUnique();
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.DeviceId).HasMaxLength(64);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.ExpiresAtUtc);
            entity.HasOne<AppIdentityUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChangeLogEntry>(entity =>
        {
            entity.ToTable("LogAlteracoes");
            entity.HasKey(e => e.Sequence);
            entity.Property(e => e.Sequence).ValueGeneratedOnAdd();
            entity.Property(e => e.EntityType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ChangeType).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(e => e.Sequence);
            entity.HasIndex(e => new { e.SectorId, e.Sequence });
        });

        builder.Entity<ProcessedOperation>(entity =>
        {
            entity.ToTable("OperacoesProcessadas");
            entity.HasKey(e => e.OperationId);
            entity.Property(e => e.EntityType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(16).IsRequired();
            entity.HasIndex(e => e.ProcessedAtUtc);
        });
    }
}
