using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Seeding;
using ChecklistPlantao.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChecklistPlantao.Client.Core.Tests;

public sealed class SessionSnapshotTests
{
    [Fact]
    public async Task Quadro_resumo_e_classificacoes_usam_os_leitos_da_sessao()
    {
        using var host = await LocalTestHost.CreateAsync(new DateTime(2026, 8, 6, 19, 5, 0, DateTimeKind.Utc));
        var (sessionId, sectorId, bedId, templateId, expectedTasks) = await PrepareAsync(host);
        host.Api.IsReachable = false;
        var store = CreateStore(host);

        var current = await store.GetCurrentSessionAsync(sectorId);
        var board = await store.GetBoardAsync(sectorId, templateId);
        var markers = await store.GetAllBedMarkersAsync(sessionId);
        var summary = await store.GetSessionSummaryAsync(sessionId);

        Assert.Equal(sessionId, current?.Id);
        Assert.Equal([bedId], board.Rows.Select(row => row.BedId));
        Assert.Equal([bedId], markers.Select(row => row.BedId));
        Assert.Equal(expectedTasks, summary.Overall.Total);
        Assert.Equal(0, summary.Overall.Completed);
    }

    [Fact]
    public async Task Alertas_ignoram_conclusao_de_leito_fora_do_snapshot()
    {
        using var host = await LocalTestHost.CreateAsync(new DateTime(2026, 8, 6, 19, 5, 0, DateTimeKind.Utc));
        var (sessionId, _, _, templateId, _) = await PrepareAsync(host);
        var bootstrap = LocalTestHost.BuildBootstrap();
        var externalBed = bootstrap.Beds[1].Id;
        var column = bootstrap.Templates.Single(t => t.Id == templateId).Columns.First(c => c.DisplayName == "20H");

        await using (var db = host.CreateContext())
        {
            var entry = new ChecklistEntry(Guid.CreateVersion7(), sessionId, externalBed, templateId, column.Id, host.Clock.UtcNow);
            entry.SetCompletion(true, host.Clock.UtcNow);
            db.ChecklistEntries.Add(entry);
            await db.SaveChangesAsync();
        }

        var planner = new LocalNotificationPlanService(host.CreateFactory(), new FakeSettings(), new FakeTimeZone(), host.Clock);
        var scheduled = await planner.BuildAsync();
        var fromColumn = scheduled.Where(item => item.ColumnId == column.Id).ToList();

        Assert.NotEmpty(fromColumn);
        Assert.All(fromColumn, item => Assert.Contains("1 leito(s) pendente(s)", item.Body, StringComparison.Ordinal));
    }

    private static LocalChecklistStore CreateStore(LocalTestHost host) =>
        new(
            host.CreateFactory(),
            new OutboxWriter(host.CreateFactory()),
            host.Api,
            host.Clock,
            new FakeTimeZone(),
            new FakeSettings(),
            NullLogger<LocalChecklistStore>.Instance);

    private static async Task<(Guid SessionId, Guid SectorId, Guid BedId, Guid TemplateId, int ExpectedTasks)> PrepareAsync(LocalTestHost host)
    {
        var bootstrap = LocalTestHost.BuildBootstrap();
        host.Api.Bootstrap = bootstrap;

        await using var db = host.CreateContext();
        Assert.True(await host.CreateEngine(db).BootstrapAsync());

        var sectorId = bootstrap.Sectors.Single().Id;
        var bedId = bootstrap.Beds[0].Id;
        var template = bootstrap.Templates.Single(t => t.Code == "GELO");
        var session = new OperationalSession(Guid.CreateVersion7(), sectorId, new DateOnly(2026, 8, 6), host.Clock.UtcNow);
        session.AddBed(bedId);

        var bed = await db.Beds.SingleAsync(item => item.Id == bedId);
        bed.SetActive(false, host.Clock.UtcNow);
        bed.MoveTo(Guid.CreateVersion7(), host.Clock.UtcNow);

        var device = await db.DeviceState.FirstOrDefaultAsync() ?? new Persistence.DeviceState();
        if (db.Entry(device).State == EntityState.Detached)
        {
            db.DeviceState.Add(device);
        }

        device.SelectSector(sectorId);
        db.OperationalSessions.Add(session);
        await db.SaveChangesAsync();

        var expectedTasks = bootstrap.Templates.Sum(item => item.Columns.Count(column => column.IsActive));
        return (session.Id, sectorId, bedId, template.Id, expectedTasks);
    }

    private sealed class FakeTimeZone : IInstitutionTimeZone
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateTime ToLocal(DateTime utc) => utc;

        public DateTime ToUtc(DateTime local) => local;
    }

    private sealed class FakeSettings : IInstitutionSettingsProvider
    {
        public InstitutionSettings Current => InstitutionSettings.Default;

        public ValueTask<InstitutionSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);

        public ValueTask ReloadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
