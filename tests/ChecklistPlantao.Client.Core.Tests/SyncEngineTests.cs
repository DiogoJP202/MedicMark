using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Client.Core.Tests;

public sealed class SyncEngineTests
{
    private static readonly Guid Sessao = Guid.CreateVersion7();
    private static readonly Guid Leito = Guid.CreateVersion7();
    private static readonly Guid Template = Guid.CreateVersion7();
    private static readonly Guid Coluna = Guid.CreateVersion7();

    [Fact]
    public async Task Sem_servidor_a_sincronizacao_nao_faz_nada_e_a_fila_permanece()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.IsReachable = false;

        await using var db = host.CreateContext();
        await new OutboxWriter(db).ToggleEntryAsync(Sessao, Leito, Template, Coluna, true, host.Clock.UtcNow);

        var resultado = await host.CreateEngine(db).SynchronizeAsync();

        Assert.False(resultado.ServerReached);
        Assert.Equal(1, await db.Outbox.CountAsync());
        Assert.True((await db.ChecklistEntries.SingleAsync()).IsCompleted);
    }

    [Fact]
    public async Task Bootstrap_grava_a_configuracao_completa_localmente()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap(cursor: 7);

        await using var db = host.CreateContext();

        Assert.True(await host.CreateEngine(db).BootstrapAsync());

        Assert.Equal(1, await db.Sectors.CountAsync());
        Assert.Equal(16, await db.Beds.CountAsync());
        Assert.Equal(3, await db.ChecklistTemplates.CountAsync());
        Assert.Equal(10, await db.ChecklistColumns.CountAsync());
        Assert.Equal(3, await db.BedMarkerDefinitions.CountAsync());
        Assert.Equal(7, (await db.SyncState.SingleAsync()).Cursor);
    }

    [Fact]
    public async Task Bootstrap_traz_os_horarios_reais_das_colunas()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap();

        await using var db = host.CreateContext();
        await host.CreateEngine(db).BootstrapAsync();

        var jantar = await db.ChecklistColumns.FirstAsync(c => c.DisplayName == "Jantar");
        var seisHoras = await db.ChecklistColumns.FirstAsync(c => c.DisplayName == "06H");

        Assert.Equal(new TimeOnly(19, 30), jantar.TriggerTime);
        Assert.Equal(new TimeOnly(6, 0), seisHoras.TriggerTime);
        Assert.True(jantar.IsSchedulable);
    }

    [Fact]
    public async Task Envio_bem_sucedido_esvazia_a_fila()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap();

        await using var db = host.CreateContext();
        await new OutboxWriter(db).ToggleEntryAsync(Sessao, Leito, Template, Coluna, true, host.Clock.UtcNow);

        var resultado = await host.CreateEngine(db).SynchronizeAsync();

        Assert.True(resultado.ServerReached);
        Assert.Equal(1, resultado.Applied);
        Assert.Equal(0, await db.Outbox.CountAsync(o => o.Status != OutboxItemStatus.Done));
    }

    [Fact]
    public async Task Reenvio_da_mesma_operacao_nao_duplica()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap();

        await using var db = host.CreateContext();
        await new OutboxWriter(db).ToggleEntryAsync(Sessao, Leito, Template, Coluna, true, host.Clock.UtcNow);

        var operacao = (await db.Outbox.SingleAsync()).OperationId;
        var motor = host.CreateEngine(db);

        await motor.SynchronizeAsync();

        // O aparelho envia de novo (o ACK se perdeu na volta).
        db.Outbox.Add(new Persistence.SyncOutboxItem(
            operacao, SyncEntityTypes.ChecklistEntry, Leito, SyncOperationType.Upsert,
            SyncJson.Serialize(new ChecklistEntryPayload(Sessao, Leito, Template, Coluna, true)),
            0, host.Clock.UtcNow));
        await db.SaveChangesAsync();

        await motor.SynchronizeAsync();

        var duplicadas = host.Api.ReceivedOperations.Count(o => o.OperationId == operacao);
        Assert.Equal(2, duplicadas);

        // Enviada duas vezes, mas o servidor marcou a segunda como duplicada e a fila esvaziou.
        Assert.Equal(0, await db.Outbox.CountAsync(o => o.Status != OutboxItemStatus.Done));
    }

    [Fact]
    public async Task Conflito_faz_o_dispositivo_adotar_o_estado_do_servidor()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap();

        await using var db = host.CreateContext();

        // O usuário desmarcou offline...
        await new OutboxWriter(db).ToggleEntryAsync(Sessao, Leito, Template, Coluna, false, host.Clock.UtcNow);
        var entryId = (await db.ChecklistEntries.SingleAsync()).Id;

        // ...mas outro aparelho já tinha concluído, e o servidor manteve a conclusão.
        host.Api.OperationHandler = operacao => new SyncOperationResultDto(
            operacao.OperationId,
            nameof(SyncOperationStatus.Conflict),
            "A conclusão registrada no servidor prevaleceu.",
            SyncJson.Serialize(new ChecklistEntryDto(entryId, Sessao, Leito, Template, Coluna, true, 9, DateTime.UtcNow)),
            9);

        var resultado = await host.CreateEngine(db).SynchronizeAsync();

        Assert.Equal(1, resultado.Conflicts);

        var entrada = await db.ChecklistEntries.SingleAsync();
        Assert.True(entrada.IsCompleted);
        Assert.Equal(9, entrada.Version);

        // O item saiu da fila: insistir apenas repetiria a perda.
        Assert.Equal(0, await db.Outbox.CountAsync(o => o.Status != OutboxItemStatus.Done));
    }

    [Fact]
    public async Task Falha_de_rede_no_envio_agenda_nova_tentativa_sem_perder_o_item()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap();

        await using var db = host.CreateContext();
        await host.CreateEngine(db).BootstrapAsync();
        await new OutboxWriter(db).ToggleEntryAsync(Sessao, Leito, Template, Coluna, true, host.Clock.UtcNow);

        host.Api.PushException = new HttpRequestException("conexão perdida");

        var resultado = await host.CreateEngine(db).SynchronizeAsync();

        Assert.False(resultado.ServerReached);

        var item = await db.Outbox.SingleAsync();
        Assert.Equal(OutboxItemStatus.Failed, item.Status);
        Assert.Equal(1, item.RetryCount);
        Assert.NotNull(item.NextAttemptAtUtc);
        Assert.Contains("conexão perdida", item.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Alteracao_recebida_do_servidor_e_aplicada_localmente()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap();

        await using var db = host.CreateContext();
        await host.CreateEngine(db).BootstrapAsync();

        var entryId = Guid.CreateVersion7();

        host.Api.PendingChanges.Add(new ServerChangeDto(
            10, SyncEntityTypes.ChecklistEntry, entryId, nameof(SyncChangeType.Updated), 3, DateTime.UtcNow,
            SyncJson.Serialize(new ChecklistEntryDto(entryId, Sessao, Leito, Template, Coluna, true, 3, DateTime.UtcNow))));

        await host.CreateEngine(db).SynchronizeAsync();

        var entrada = await db.ChecklistEntries.SingleAsync();
        Assert.True(entrada.IsCompleted);
        Assert.Equal(3, entrada.Version);
    }

    [Fact]
    public async Task Estado_antigo_do_servidor_nao_sobrescreve_marcacao_mais_nova_do_aparelho()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap();

        await using var db = host.CreateContext();
        await host.CreateEngine(db).BootstrapAsync();

        // O usuário marcou agora: a entrada local está na versão 2.
        await new OutboxWriter(db).ToggleEntryAsync(Sessao, Leito, Template, Coluna, true, host.Clock.UtcNow);
        var entrada = await db.ChecklistEntries.SingleAsync();

        // Chega uma alteração antiga (versão 1) dizendo que estava desmarcado.
        host.Api.PendingChanges.Add(new ServerChangeDto(
            11, SyncEntityTypes.ChecklistEntry, entrada.Id, nameof(SyncChangeType.Updated), 1, DateTime.UtcNow,
            SyncJson.Serialize(new ChecklistEntryDto(entrada.Id, Sessao, Leito, Template, Coluna, false, 1, DateTime.UtcNow))));

        host.Api.OperationHandler = op => new SyncOperationResultDto(op.OperationId, nameof(SyncOperationStatus.Applied), null, null, 2);

        await host.CreateEngine(db).SynchronizeAsync();

        Assert.True((await db.ChecklistEntries.SingleAsync()).IsCompleted);
    }

    [Fact]
    public async Task Cursor_velho_demais_dispara_novo_bootstrap()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap(cursor: 500);

        await using var db = host.CreateContext();
        await host.CreateEngine(db).BootstrapAsync();

        host.Api.RequiresBootstrapOnPull = true;
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap(cursor: 900);

        await host.CreateEngine(db).SynchronizeAsync();

        var estado = await db.SyncState.SingleAsync();
        Assert.True(estado.BootstrapCompleted);
        Assert.Equal(900, estado.Cursor);
    }

    [Fact]
    public async Task Sincronizacao_registra_o_instante_do_ultimo_contato()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap();

        await using var db = host.CreateContext();
        await host.CreateEngine(db).SynchronizeAsync();

        var estado = await db.SyncState.SingleAsync();
        Assert.NotNull(estado.LastSyncAtUtc);
        Assert.NotNull(estado.LastSuccessfulServerContactUtc);
    }

    [Fact]
    public async Task Fila_pendente_atravessa_o_periodo_offline_e_sai_quando_a_conexao_volta()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.Bootstrap = LocalTestHost.BuildBootstrap();

        // Plantão inteiro sem servidor.
        host.Api.IsReachable = false;

        await using (var offline = host.CreateContext())
        {
            var writer = new OutboxWriter(offline);

            for (var i = 0; i < 12; i++)
            {
                await writer.ToggleEntryAsync(Sessao, Guid.CreateVersion7(), Template, Coluna, true, host.Clock.UtcNow.AddMinutes(i));
            }

            await host.CreateEngine(offline).SynchronizeAsync();
            Assert.Equal(12, await offline.Outbox.CountAsync());
        }

        // Aparelho desligado a noite toda; de manhã a rede volta.
        host.Clock.Advance(TimeSpan.FromHours(10));
        host.Api.IsReachable = true;

        await using var online = host.CreateContext();
        var resultado = await host.CreateEngine(online).SynchronizeAsync();

        Assert.True(resultado.ServerReached);
        Assert.Equal(12, resultado.Applied);
        Assert.Equal(0, await online.Outbox.CountAsync(o => o.Status != OutboxItemStatus.Done));
    }
}
