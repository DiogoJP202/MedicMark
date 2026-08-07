using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Client.Core.Tests;

/// <summary>
/// A promessa central do sistema: o que foi marcado offline não se perde.
/// Cada teste abre um contexto novo sobre o mesmo arquivo — é o equivalente a fechar e reabrir
/// o aplicativo, ou reiniciar o aparelho.
/// </summary>
public sealed class OfflinePersistenceTests
{
    private static readonly Guid Sessao = Guid.CreateVersion7();
    private static readonly Guid Leito = Guid.CreateVersion7();
    private static readonly Guid Template = Guid.CreateVersion7();
    private static readonly Guid Coluna = Guid.CreateVersion7();

    [Fact]
    public async Task Marcar_grava_o_estado_e_a_fila_na_mesma_transacao()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using (var db = host.CreateContext())
        {
            await new OutboxWriter(db).ToggleEntryAsync(Sessao, Leito, Template, Coluna, true, host.Clock.UtcNow);
        }

        await using var verificacao = host.CreateContext();

        var entrada = await verificacao.ChecklistEntries.SingleAsync();
        var fila = await verificacao.Outbox.SingleAsync();

        Assert.True(entrada.IsCompleted);
        Assert.Equal(SyncEntityTypes.ChecklistEntry, fila.EntityType);
        Assert.Equal(OutboxItemStatus.Pending, fila.Status);
    }

    [Fact]
    public async Task Marcacao_sobrevive_ao_fechamento_e_reabertura_do_aplicativo()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using (var primeiraExecucao = host.CreateContext())
        {
            await new OutboxWriter(primeiraExecucao).ToggleEntryAsync(Sessao, Leito, Template, Coluna, true, host.Clock.UtcNow);
        }

        // Aplicativo fechado. Horas depois, aberto de novo.
        host.Clock.Advance(TimeSpan.FromHours(9));

        await using var segundaExecucao = host.CreateContext();

        Assert.True((await segundaExecucao.ChecklistEntries.SingleAsync()).IsCompleted);
        Assert.Equal(1, await segundaExecucao.Outbox.CountAsync());
    }

    [Fact]
    public async Task Muitas_marcacoes_offline_ficam_todas_na_fila()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using (var db = host.CreateContext())
        {
            var writer = new OutboxWriter(db);

            for (var i = 0; i < 48; i++)
            {
                await writer.ToggleEntryAsync(Sessao, Guid.CreateVersion7(), Template, Coluna, true, host.Clock.UtcNow.AddSeconds(i));
            }
        }

        await using var verificacao = host.CreateContext();

        Assert.Equal(48, await verificacao.Outbox.CountAsync());
        Assert.Equal(48, await new OutboxWriter(verificacao).PendingCountAsync());
    }

    [Fact]
    public async Task Alternar_duas_vezes_registra_a_versao_base_correta()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using var db = host.CreateContext();
        var writer = new OutboxWriter(db);

        await writer.ToggleEntryAsync(Sessao, Leito, Template, Coluna, true, host.Clock.UtcNow);
        await writer.ToggleEntryAsync(Sessao, Leito, Template, Coluna, false, host.Clock.UtcNow.AddSeconds(5));

        var itens = await db.Outbox.OrderBy(o => o.CreatedAtUtc).ToListAsync();

        // A primeira operação partiu da versão 1 (entrada recém-criada) e a segunda da 2.
        Assert.Equal(2, itens.Count);
        Assert.Equal(1, itens[0].BaseVersion);
        Assert.Equal(2, itens[1].BaseVersion);
        Assert.False((await db.ChecklistEntries.SingleAsync()).IsCompleted);
    }

    [Fact]
    public async Task Classificacao_de_leito_tambem_entra_na_fila()
    {
        using var host = await LocalTestHost.CreateAsync();
        var marcador = Guid.CreateVersion7();

        await using (var db = host.CreateContext())
        {
            await new OutboxWriter(db).ToggleMarkerAsync(Sessao, Leito, marcador, true, host.Clock.UtcNow);
        }

        await using var verificacao = host.CreateContext();

        Assert.True((await verificacao.SessionBedMarkers.SingleAsync()).IsSelected);
        Assert.Equal(SyncEntityTypes.SessionBedMarker, (await verificacao.Outbox.SingleAsync()).EntityType);
    }

    [Fact]
    public async Task Um_leito_pode_receber_varias_classificacoes()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using var db = host.CreateContext();
        var writer = new OutboxWriter(db);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            await writer.ToggleMarkerAsync(Sessao, Leito, Guid.CreateVersion7(), true, host.Clock.UtcNow);
        }

        Assert.Equal(3, await db.SessionBedMarkers.CountAsync(m => m.BedId == Leito && m.IsSelected));
    }

    [Fact]
    public async Task Item_com_falha_so_fica_pronto_depois_da_espera()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using var db = host.CreateContext();
        var writer = new OutboxWriter(db);

        await writer.ToggleEntryAsync(Sessao, Leito, Template, Coluna, true, host.Clock.UtcNow);

        var item = await db.Outbox.SingleAsync();
        item.MarkFailed("servidor fora do ar", host.Clock.UtcNow.AddMinutes(5));
        await db.SaveChangesAsync();

        Assert.Empty(await writer.TakeReadyAsync(host.Clock.UtcNow, 10));

        host.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Single(await writer.TakeReadyAsync(host.Clock.UtcNow, 10));
    }

    [Fact]
    public async Task Fila_devolve_os_itens_na_ordem_de_criacao()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using var db = host.CreateContext();
        var writer = new OutboxWriter(db);

        for (var i = 0; i < 5; i++)
        {
            await writer.ToggleEntryAsync(Sessao, Guid.CreateVersion7(), Template, Coluna, true, host.Clock.UtcNow.AddMinutes(i));
        }

        var prontos = await writer.TakeReadyAsync(host.Clock.UtcNow.AddHours(1), 10);

        Assert.Equal(prontos.OrderBy(p => p.CreatedAtUtc).Select(p => p.Id), prontos.Select(p => p.Id));
    }

    [Fact]
    public void Espera_entre_tentativas_cresce_e_respeita_o_teto()
    {
        var gerador = new Random(42);

        var primeira = RetryBackoff.For(0, gerador);
        var terceira = RetryBackoff.For(3, gerador);
        var enorme = RetryBackoff.For(50, gerador);

        Assert.True(primeira < terceira);
        Assert.True(enorme <= RetryBackoff.Max * 1.2);
        Assert.True(primeira >= RetryBackoff.First);
    }
}
