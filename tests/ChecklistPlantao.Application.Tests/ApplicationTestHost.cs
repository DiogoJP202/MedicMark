using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Administration;
using ChecklistPlantao.Application.Checklist;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Application.Sessions;
using ChecklistPlantao.Application.Sync;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Infrastructure.Persistence;
using ChecklistPlantao.Infrastructure.Seeding;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChecklistPlantao.Application.Tests;

/// <summary>Relógio controlável: o tempo é entrada dos testes, não acaso.</summary>
internal sealed class TestClock(DateTime start) : IClock
{
    public DateTime UtcNow { get; private set; } = start;

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);

    public void Set(DateTime utc) => UtcNow = utc;
}

internal sealed class TestSettingsProvider(InstitutionSettings settings) : IInstitutionSettingsProvider
{
    public InstitutionSettings Current { get; private set; } = settings;

    public ValueTask<InstitutionSettings> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Current);

    public ValueTask ReloadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public void Replace(InstitutionSettings settings) => Current = settings;
}

/// <summary>Fuso fixo em UTC: as regras de turno já são cobertas em Domain.Tests.</summary>
internal sealed class UtcTimeZone : IInstitutionTimeZone
{
    public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

    public DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

    public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Utc);
}

internal sealed class TestUser(EffectiveAccess access) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId { get; } = Guid.CreateVersion7();

    public string UserName => "teste";

    public string DisplayName => "Usuário de Teste";

    public EffectiveAccess Access { get; } = access;

    public static TestUser WithEverything()
    {
        var group = new AccessGroup(Guid.CreateVersion7(), "Tudo", null, grantsAllSectors: true, DateTime.UnixEpoch);
        group.ReplacePermissions(Permissions.All, DateTime.UnixEpoch);
        return new TestUser(EffectiveAccess.FromGroups([group]));
    }

    public static TestUser With(IEnumerable<string> permissions, IEnumerable<Guid> sectors)
    {
        var group = new AccessGroup(Guid.CreateVersion7(), "Parcial", null, grantsAllSectors: false, DateTime.UnixEpoch);
        group.ReplacePermissions(permissions, DateTime.UnixEpoch);
        group.ReplaceSectors(sectors, DateTime.UnixEpoch);
        return new TestUser(EffectiveAccess.FromGroups([group]));
    }
}

/// <summary>
/// Banco SQLite em memória com o esquema real e os dados de seed, mais os serviços de aplicação
/// prontos para uso. A conexão fica aberta durante toda a vida do host: fechá-la apaga o banco.
/// </summary>
internal sealed class ApplicationTestHost : IDisposable
{
    private readonly SqliteConnection _connection;

    private ApplicationTestHost(SqliteConnection connection, AppDbContext db, TestClock clock, TestSettingsProvider settings)
    {
        _connection = connection;
        Db = db;
        Clock = clock;
        Settings = settings;

        Sessions = new SessionService(db, clock, new UtcTimeZone(), settings, NullLogger<SessionService>.Instance);
        Mutations = new ChecklistMutationService(db, clock);
        Configuration = new ConfigurationQueryService(db, clock, settings);
        Sync = new SyncService(db, Mutations, Configuration, clock, NullLogger<SyncService>.Instance);
        Structure = new StructureAdminService(db, clock);
        System = new SystemAdminService(db, clock, settings);
    }

    public AppDbContext Db { get; }

    public TestClock Clock { get; }

    public TestSettingsProvider Settings { get; }

    public SessionService Sessions { get; }

    public ChecklistMutationService Mutations { get; }

    public ConfigurationQueryService Configuration { get; }

    public SyncService Sync { get; }

    public StructureAdminService Structure { get; }

    public SystemAdminService System { get; }

    public static async Task<ApplicationTestHost> CreateAsync(DateTime? startUtc = null, InstitutionSettings? settings = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var clock = new TestClock(startUtc ?? new DateTime(2026, 8, 6, 21, 0, 0, DateTimeKind.Utc));
        var provider = new TestSettingsProvider(settings ?? InstitutionSettings.Default);

        var seeder = new DatabaseSeeder(db, NullLogger<DatabaseSeeder>.Instance);
        await seeder.SeedAsync(clock.UtcNow);

        return new ApplicationTestHost(connection, db, clock, provider);
    }

    public async Task<Guid> OesteSectorIdAsync() =>
        await Db.Sectors.Where(s => s.Name == "Oeste").Select(s => s.Id).FirstAsync();

    public async Task<IReadOnlyList<Guid>> OesteBedIdsAsync()
    {
        var sectorId = await OesteSectorIdAsync();
        return await Db.Beds.Where(b => b.SectorId == sectorId).OrderBy(b => b.SortOrder).Select(b => b.Id).ToListAsync();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
