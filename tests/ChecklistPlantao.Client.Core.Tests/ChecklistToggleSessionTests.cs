using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Abstractions;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChecklistPlantao.Client.Core.Tests;

public sealed class ChecklistToggleSessionTests
{
    [Fact]
    public async Task Marcacao_usa_a_sessao_da_celula_quando_ha_varios_plantoes_abertos()
    {
        using var host = await LocalTestHost.CreateAsync();
        var dados = await PrepararDoisSetoresAsync(host);
        host.Api.IsReachable = false;
        var store = CriarStore(host);
        var quadro = await store.GetBoardAsync(dados.SetorTeste, dados.Template);
        var celula = quadro.Rows.Single().Cells.First(item => item.ColumnId == dados.Coluna);

        Assert.Equal(dados.SessaoTeste, quadro.SessionId);
        Assert.Equal(dados.SessaoTeste, celula.SessionId);

        var resultado = await store.ToggleCellAsync(celula, true);

        Assert.True(resultado.Succeeded);

        await using var db = host.CreateContext();
        var marcacao = await db.ChecklistEntries.SingleAsync();

        Assert.Equal(dados.SessaoTeste, marcacao.SessionId);
        Assert.Equal(dados.LeitoTeste, marcacao.BedId);
        Assert.DoesNotContain(
            await db.ChecklistEntries.ToListAsync(),
            item => item.SessionId == dados.SessaoAntigaOeste);
    }

    [Fact]
    public async Task Marcacao_recusa_leito_que_nao_pertence_ao_plantao_exibido()
    {
        using var host = await LocalTestHost.CreateAsync();
        var dados = await PrepararDoisSetoresAsync(host);
        var store = CriarStore(host);

        var resultado = await store.ToggleCellAsync(
            new ChecklistCell(
                dados.SessaoTeste,
                dados.LeitoOeste,
                dados.Template,
                dados.Coluna,
                false,
                0,
                false),
            true);

        Assert.False(resultado.Succeeded);
        Assert.Equal("Este leito não faz parte do plantão exibido.", resultado.Message);

        await using var db = host.CreateContext();
        Assert.Empty(await db.ChecklistEntries.ToListAsync());
        Assert.Empty(await db.Outbox.ToListAsync());
    }

    private static LocalChecklistStore CriarStore(LocalTestHost host) =>
        new(
            host.CreateFactory(),
            new OutboxWriter(host.CreateFactory()),
            host.Api,
            host.Clock,
            new FusoUtc(),
            new ConfiguracoesPadrao(),
            NullLogger<LocalChecklistStore>.Instance);

    private static async Task<Dados> PrepararDoisSetoresAsync(LocalTestHost host)
    {
        var original = LocalTestHost.BuildBootstrap();
        var setorOeste = original.Sectors.Single();
        var leitoOeste = original.Beds[0];
        var setorTeste = Guid.CreateVersion7();
        var leitoTeste = Guid.CreateVersion7();

        host.Api.Bootstrap = original with
        {
            Sectors = [.. original.Sectors, new SectorDto(setorTeste, "Teste#01", null, 2, true, null, null, 1)],
            Beds = [.. original.Beds, new BedDto(leitoTeste, setorTeste, "0011", null, 1, true, 1)],
        };

        await using var db = host.CreateContext();
        Assert.True(await host.CreateEngine(db).BootstrapAsync());

        var sessaoAntigaOeste = new OperationalSession(
            Guid.CreateVersion7(),
            setorOeste.Id,
            new DateOnly(2026, 8, 17),
            host.Clock.UtcNow.AddDays(-4));
        sessaoAntigaOeste.AddBed(leitoOeste.Id);

        var sessaoTeste = new OperationalSession(
            Guid.CreateVersion7(),
            setorTeste,
            new DateOnly(2026, 8, 22),
            host.Clock.UtcNow);
        sessaoTeste.AddBed(leitoTeste);

        db.OperationalSessions.AddRange(sessaoAntigaOeste, sessaoTeste);
        await db.SaveChangesAsync();

        var template = original.Templates[0];
        return new Dados(
            sessaoAntigaOeste.Id,
            sessaoTeste.Id,
            setorTeste,
            leitoOeste.Id,
            leitoTeste,
            template.Id,
            template.Columns[0].Id);
    }

    private sealed record Dados(
        Guid SessaoAntigaOeste,
        Guid SessaoTeste,
        Guid SetorTeste,
        Guid LeitoOeste,
        Guid LeitoTeste,
        Guid Template,
        Guid Coluna);

    private sealed class FusoUtc : IInstitutionTimeZone
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateTime ToLocal(DateTime utc) => utc;

        public DateTime ToUtc(DateTime local) => local;
    }

    private sealed class ConfiguracoesPadrao : IInstitutionSettingsProvider
    {
        public InstitutionSettings Current => InstitutionSettings.Default;

        public ValueTask<InstitutionSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);

        public ValueTask ReloadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
