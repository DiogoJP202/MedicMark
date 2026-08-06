using System.Net;
using System.Net.Http.Json;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Sync;

namespace ChecklistPlantao.Server.IntegrationTests;

/// <summary>
/// Os cenários que sustentam o modo offline: idempotência do envio, convergência de conflito e
/// avanço do cursor.
/// </summary>
public sealed class SyncTests(ChecklistServerFactory factory) : IClassFixture<ChecklistServerFactory>
{
    private const string DeviceId = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task Bootstrap_traz_os_dados_da_folha_de_papel()
    {
        var client = await factory.CreateAdminClientAsync();

        var bootstrap = (await client.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;

        Assert.Equal("Oeste", Assert.Single(bootstrap.Sectors).Name);
        Assert.Equal(16, bootstrap.Beds.Count);
        Assert.Equal(3, bootstrap.Templates.Count);
        Assert.Equal(3, bootstrap.Markers.Count);
        Assert.Equal("America/Sao_Paulo", bootstrap.Settings.TimeZoneId);

        var gelo = bootstrap.Templates.Single(t => t.Code == "GELO");
        Assert.Equal(["20H", "22H", "00H", "02H", "04H", "06H"], gelo.Columns.Select(c => c.DisplayName));
        Assert.All(gelo.Columns, c => Assert.NotNull(c.TriggerTime));

        var glicemia = bootstrap.Templates.Single(t => t.Code == "GLICEMIA");
        Assert.Equal(new TimeOnly(19, 30), glicemia.Columns.Single(c => c.DisplayName == "Jantar").TriggerTime);
        Assert.Equal(new TimeOnly(7, 0), glicemia.Columns.Single(c => c.DisplayName == "Café").TriggerTime);
    }

    [Fact]
    public async Task Mesma_operacao_enviada_duas_vezes_nao_e_aplicada_duas_vezes()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);

        var operationId = Guid.CreateVersion7();
        var request = BuildPush(contexto, contexto.Beds[0].Id, isCompleted: true, baseVersion: 0, operationId);

        var primeiro = await PushAsync(client, request);
        var segundo = await PushAsync(client, request);

        Assert.Equal(nameof(SyncOperationStatus.Applied), primeiro.Results[0].Status);
        Assert.Equal(nameof(SyncOperationStatus.Duplicate), segundo.Results[0].Status);

        var estado = await client.GetFromJsonAsync<SessionStateDto>($"/api/sessions/{contexto.SessionId}/checklists");
        var marcacoes = estado!.Entries.Where(e => e.BedId == contexto.Beds[0].Id && e.ChecklistColumnId == contexto.Column.Id).ToList();
        Assert.Single(marcacoes);
    }

    [Fact]
    public async Task Desmarcar_com_versao_antiga_nao_apaga_conclusao_mais_nova()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);
        var bed = contexto.Beds[1].Id;

        var marcar = await PushAsync(client, BuildPush(contexto, bed, isCompleted: true, baseVersion: 0, Guid.CreateVersion7()));
        var versaoAtual = marcar.Results[0].CurrentVersion!.Value;

        // Dispositivo que ficou offline vendo uma versão anterior tenta desmarcar.
        var desmarcar = await PushAsync(client, BuildPush(contexto, bed, isCompleted: false, baseVersion: versaoAtual - 1, Guid.CreateVersion7()));

        Assert.Equal(nameof(SyncOperationStatus.Conflict), desmarcar.Results[0].Status);

        var estadoAtual = SyncJson.Deserialize<ChecklistEntryDto>(desmarcar.Results[0].CurrentState!)!;
        Assert.True(estadoAtual.IsCompleted);
    }

    [Fact]
    public async Task Desmarcar_com_versao_atual_e_aceito()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);
        var bed = contexto.Beds[2].Id;

        var marcar = await PushAsync(client, BuildPush(contexto, bed, isCompleted: true, baseVersion: 0, Guid.CreateVersion7()));
        var versaoAtual = marcar.Results[0].CurrentVersion!.Value;

        var desmarcar = await PushAsync(client, BuildPush(contexto, bed, isCompleted: false, baseVersion: versaoAtual, Guid.CreateVersion7()));

        Assert.Equal(nameof(SyncOperationStatus.Applied), desmarcar.Results[0].Status);
    }

    [Fact]
    public async Task Marcar_o_que_ja_esta_marcado_nao_altera_a_versao()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);
        var bed = contexto.Beds[3].Id;

        var primeiro = await PushAsync(client, BuildPush(contexto, bed, isCompleted: true, baseVersion: 0, Guid.CreateVersion7()));
        var segundo = await PushAsync(client, BuildPush(contexto, bed, isCompleted: true, baseVersion: 0, Guid.CreateVersion7()));

        Assert.Equal(nameof(SyncOperationStatus.NoChange), segundo.Results[0].Status);
        Assert.Equal(primeiro.Results[0].CurrentVersion, segundo.Results[0].CurrentVersion);
    }

    [Fact]
    public async Task Pull_incremental_devolve_apenas_o_que_veio_depois_do_cursor()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);

        var cursorInicial = (await client.GetFromJsonAsync<SyncPullResponse>("/api/sync/pull?since=0"))!.Cursor;

        await PushAsync(client, BuildPush(contexto, contexto.Beds[4].Id, isCompleted: true, baseVersion: 0, Guid.CreateVersion7()));

        var incremental = (await client.GetFromJsonAsync<SyncPullResponse>($"/api/sync/pull?since={cursorInicial}"))!;

        Assert.NotEmpty(incremental.Changes);
        Assert.True(incremental.Cursor > cursorInicial);
        Assert.All(incremental.Changes, c => Assert.True(c.Sequence > cursorInicial));

        var vazio = (await client.GetFromJsonAsync<SyncPullResponse>($"/api/sync/pull?since={incremental.Cursor}"))!;
        Assert.Empty(vazio.Changes);
    }

    [Fact]
    public async Task Alteracao_no_pull_ja_traz_o_estado_atual_da_entidade()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);
        var cursor = (await client.GetFromJsonAsync<SyncPullResponse>("/api/sync/pull?since=0"))!.Cursor;

        await PushAsync(client, BuildPush(contexto, contexto.Beds[5].Id, isCompleted: true, baseVersion: 0, Guid.CreateVersion7()));

        var pull = (await client.GetFromJsonAsync<SyncPullResponse>($"/api/sync/pull?since={cursor}"))!;
        var alteracao = pull.Changes.First(c => c.EntityType == SyncEntityTypes.ChecklistEntry);

        Assert.NotNull(alteracao.Payload);
        var entry = SyncJson.Deserialize<ChecklistEntryDto>(alteracao.Payload!)!;
        Assert.True(entry.IsCompleted);
        Assert.Equal(contexto.Beds[5].Id, entry.BedId);
    }

    [Fact]
    public async Task Cliente_nao_pode_alterar_configuracao_pelo_canal_de_sincronizacao()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);

        var operacao = new SyncOperationDto(
            Guid.CreateVersion7(),
            SyncEntityTypes.Sector,
            contexto.SectorId,
            nameof(SyncOperationType.Upsert),
            "{}",
            0,
            DateTime.UtcNow);

        var resposta = await PushAsync(client, new SyncPushRequest(DeviceId, 0, [operacao]));

        Assert.Equal(nameof(SyncOperationStatus.Rejected), resposta.Results[0].Status);
    }

    [Fact]
    public async Task Payload_invalido_e_recusado_sem_derrubar_o_lote()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);

        var invalida = new SyncOperationDto(
            Guid.CreateVersion7(),
            SyncEntityTypes.ChecklistEntry,
            Guid.CreateVersion7(),
            nameof(SyncOperationType.Upsert),
            "isso não é json",
            0,
            DateTime.UtcNow);

        var valida = BuildPush(contexto, contexto.Beds[6].Id, isCompleted: true, baseVersion: 0, Guid.CreateVersion7()).Operations[0];

        var resposta = await PushAsync(client, new SyncPushRequest(DeviceId, 0, [invalida, valida]));

        Assert.Equal(nameof(SyncOperationStatus.Rejected), resposta.Results[0].Status);
        Assert.Equal(nameof(SyncOperationStatus.Applied), resposta.Results[1].Status);
    }

    [Fact]
    public async Task Marcacao_em_leito_de_outro_setor_e_recusada()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);

        var operacao = new SyncOperationDto(
            Guid.CreateVersion7(),
            SyncEntityTypes.ChecklistEntry,
            Guid.CreateVersion7(),
            nameof(SyncOperationType.Upsert),
            SyncJson.Serialize(new ChecklistEntryPayload(contexto.SessionId, Guid.CreateVersion7(), contexto.Template.Id, contexto.Column.Id, true)),
            0,
            DateTime.UtcNow);

        var resposta = await PushAsync(client, new SyncPushRequest(DeviceId, 0, [operacao]));

        Assert.Equal(nameof(SyncOperationStatus.Rejected), resposta.Results[0].Status);
    }

    [Fact]
    public async Task Classificacoes_de_leito_sao_registradas_e_aparecem_no_resumo()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);
        var bed = contexto.Beds[7];
        var sondas = contexto.Markers.Single(m => m.Code == "SONDAS");

        var resposta = await client.PutAsJsonAsync(
            $"/api/sessions/{contexto.SessionId}/beds/{bed.Id}/markers/{sondas.Id}",
            new UpdateBedMarkerRequest(IsSelected: true, BaseVersion: 0, OperationId: Guid.CreateVersion7()));

        resposta.EnsureSuccessStatusCode();

        var resumo = (await client.GetFromJsonAsync<SessionSummaryDto>($"/api/sessions/{contexto.SessionId}/summary"))!;
        var grupo = resumo.Markers.Single(m => m.MarkerDefinitionId == sondas.Id);

        Assert.Contains(bed.Code, grupo.BedCodes);
    }

    [Fact]
    public async Task Um_leito_pode_ter_varias_classificacoes_ao_mesmo_tempo()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);
        var bed = contexto.Beds[8];

        foreach (var marker in contexto.Markers)
        {
            var resposta = await client.PutAsJsonAsync(
                $"/api/sessions/{contexto.SessionId}/beds/{bed.Id}/markers/{marker.Id}",
                new UpdateBedMarkerRequest(true, 0, Guid.CreateVersion7()));

            resposta.EnsureSuccessStatusCode();
        }

        var resumo = (await client.GetFromJsonAsync<SessionSummaryDto>($"/api/sessions/{contexto.SessionId}/summary"))!;

        Assert.All(resumo.Markers, m => Assert.Contains(bed.Code, m.BedCodes));
    }

    [Fact]
    public async Task Marcacao_direta_com_versao_defasada_devolve_409_com_o_estado_atual()
    {
        var client = await factory.CreateAdminClientAsync();
        var contexto = await LoadContextAsync(client);
        var bed = contexto.Beds[9].Id;

        var marcar = await client.PutAsJsonAsync(
            EntryPath(contexto, bed),
            new UpdateChecklistEntryRequest(true, 0, Guid.CreateVersion7()));

        marcar.EnsureSuccessStatusCode();
        var estado = (await marcar.Content.ReadFromJsonAsync<ChecklistEntryDto>())!;

        var conflito = await client.PutAsJsonAsync(
            EntryPath(contexto, bed),
            new UpdateChecklistEntryRequest(false, estado.Version - 1, Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.Conflict, conflito.StatusCode);
        var atual = (await conflito.Content.ReadFromJsonAsync<ChecklistEntryDto>())!;
        Assert.True(atual.IsCompleted);
    }

    private string EntryPath(SyncContext contexto, Guid bedId) =>
        $"/api/sessions/{contexto.SessionId}/beds/{bedId}/templates/{contexto.Template.Id}/columns/{contexto.Column.Id}";

    private static async Task<SyncPushResponse> PushAsync(HttpClient client, SyncPushRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/sync/push", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SyncPushResponse>())!;
    }

    private static SyncPushRequest BuildPush(SyncContext contexto, Guid bedId, bool isCompleted, int baseVersion, Guid operationId)
    {
        var payload = new ChecklistEntryPayload(contexto.SessionId, bedId, contexto.Template.Id, contexto.Column.Id, isCompleted);

        return new SyncPushRequest(DeviceId, 0,
        [
            new SyncOperationDto(
                operationId,
                SyncEntityTypes.ChecklistEntry,
                Guid.CreateVersion7(),
                nameof(SyncOperationType.Upsert),
                SyncJson.Serialize(payload),
                baseVersion,
                DateTime.UtcNow),
        ]);
    }

    private static async Task<SyncContext> LoadContextAsync(HttpClient client)
    {
        var bootstrap = (await client.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var sector = bootstrap.Sectors[0];
        var state = (await client.GetFromJsonAsync<SessionStateDto>($"/api/sectors/{sector.Id}/sessions/current"))!;
        var template = bootstrap.Templates.Single(t => t.Code == "GELO");

        return new SyncContext(sector.Id, state.Session.Id, bootstrap.Beds, template, template.Columns[0], bootstrap.Markers);
    }

    private sealed record SyncContext(
        Guid SectorId,
        Guid SessionId,
        IReadOnlyList<BedDto> Beds,
        ChecklistTemplateDto Template,
        ChecklistColumnDto Column,
        IReadOnlyList<BedMarkerDefinitionDto> Markers);
}
