using System.Net.Http.Json;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Sync;

namespace ChecklistPlantao.Server.IntegrationTests;

/// <summary>
/// Várias operações para a MESMA célula num único lote.
///
/// É o caso normal de quem trabalha offline: marcar, perceber o engano, desmarcar e marcar de
/// novo. Todas as operações vão na mesma remessa quando a rede volta.
///
/// O defeito apareceu em aparelho: a busca por entrada existente consultava o banco, e a entrada
/// criada por uma operação anterior do mesmo lote ainda não estava gravada. Uma segunda entrada
/// era criada para a mesma célula e o SaveChanges do lote inteiro estourava
/// "UNIQUE constraint failed: Marcacoes..." — devolvendo 500 e derrubando TODA a sincronização,
/// inclusive as marcações de outras células que estavam corretas.
/// </summary>
public sealed class BatchSameCellTests(ChecklistServerFactory factory) : IClassFixture<ChecklistServerFactory>
{
    private const string DeviceId = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task Marcar_desmarcar_e_marcar_a_mesma_celula_no_mesmo_lote_nao_derruba_a_sincronizacao()
    {
        var client = await factory.CreateAdminClientAsync();
        var (sessionId, bedId, templateId, columnId) = await LoadCellAsync(client, bedIndex: 0);

        var operacoes = new[]
        {
            Operacao(sessionId, bedId, templateId, columnId, isCompleted: true, baseVersion: 0),
            Operacao(sessionId, bedId, templateId, columnId, isCompleted: false, baseVersion: 0),
            Operacao(sessionId, bedId, templateId, columnId, isCompleted: true, baseVersion: 0),
        };

        var resposta = await client.PostAsJsonAsync("/api/sync/push", new SyncPushRequest(DeviceId, 0, operacoes));

        // Antes da correção, isto era 500.
        await HttpAssert.EnsureSuccessAsync(resposta);

        var conteudo = (await resposta.Content.ReadFromJsonAsync<SyncPushResponse>())!;
        Assert.Equal(3, conteudo.Results.Count);
        Assert.DoesNotContain(conteudo.Results, r => r.Status == nameof(SyncOperationStatus.Rejected));

        // Uma única entrada para a célula, e o estado final é o da última operação.
        var estado = await client.GetFromJsonAsync<SessionStateDto>($"/api/sessions/{sessionId}/checklists");
        var entradas = estado!.Entries.Where(e => e.BedId == bedId && e.ChecklistColumnId == columnId).ToList();

        Assert.Single(entradas);
        Assert.True(entradas[0].IsCompleted);
    }

    [Fact]
    public async Task Lote_com_celula_repetida_nao_impede_as_demais_celulas_de_sincronizar()
    {
        var client = await factory.CreateAdminClientAsync();
        var (sessionId, primeiroLeito, templateId, columnId) = await LoadCellAsync(client, bedIndex: 1);
        var bootstrap = (await client.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var segundoLeito = bootstrap.Beds[2].Id;

        var operacoes = new[]
        {
            Operacao(sessionId, primeiroLeito, templateId, columnId, true, 0),
            Operacao(sessionId, primeiroLeito, templateId, columnId, false, 0),
            Operacao(sessionId, segundoLeito, templateId, columnId, true, 0),
        };

        var resposta = await client.PostAsJsonAsync("/api/sync/push", new SyncPushRequest(DeviceId, 0, operacoes));
        await HttpAssert.EnsureSuccessAsync(resposta);

        var estado = await client.GetFromJsonAsync<SessionStateDto>($"/api/sessions/{sessionId}/checklists");

        // A célula de outro leito precisa ter sido gravada: uma repetição no lote não pode
        // custar o trabalho de um plantão inteiro.
        var outra = estado!.Entries.Single(e => e.BedId == segundoLeito && e.ChecklistColumnId == columnId);
        Assert.True(outra.IsCompleted);
    }

    [Fact]
    public async Task Varias_classificacoes_do_mesmo_leito_no_mesmo_lote_convivem()
    {
        var client = await factory.CreateAdminClientAsync();
        var (sessionId, bedId, _, _) = await LoadCellAsync(client, bedIndex: 3);
        var bootstrap = (await client.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var marcador = bootstrap.Markers[0].Id;

        var operacoes = new[]
        {
            MarcadorOperacao(sessionId, bedId, marcador, isSelected: true),
            MarcadorOperacao(sessionId, bedId, marcador, isSelected: false),
            MarcadorOperacao(sessionId, bedId, marcador, isSelected: true),
        };

        var resposta = await client.PostAsJsonAsync("/api/sync/push", new SyncPushRequest(DeviceId, 0, operacoes));
        await HttpAssert.EnsureSuccessAsync(resposta);

        var estado = await client.GetFromJsonAsync<SessionStateDto>($"/api/sessions/{sessionId}/checklists");
        var marcadores = estado!.Markers.Where(m => m.BedId == bedId && m.MarkerDefinitionId == marcador).ToList();

        Assert.Single(marcadores);
        Assert.True(marcadores[0].IsSelected);
    }

    private static SyncOperationDto Operacao(Guid sessionId, Guid bedId, Guid templateId, Guid columnId, bool isCompleted, int baseVersion) =>
        new(Guid.CreateVersion7(),
            SyncEntityTypes.ChecklistEntry,
            Guid.CreateVersion7(),
            nameof(SyncOperationType.Upsert),
            SyncJson.Serialize(new ChecklistEntryPayload(sessionId, bedId, templateId, columnId, isCompleted)),
            baseVersion,
            DateTime.UtcNow);

    private static SyncOperationDto MarcadorOperacao(Guid sessionId, Guid bedId, Guid markerId, bool isSelected) =>
        new(Guid.CreateVersion7(),
            SyncEntityTypes.SessionBedMarker,
            Guid.CreateVersion7(),
            nameof(SyncOperationType.Upsert),
            SyncJson.Serialize(new SessionBedMarkerPayload(sessionId, bedId, markerId, isSelected)),
            0,
            DateTime.UtcNow);

    private static async Task<(Guid SessionId, Guid BedId, Guid TemplateId, Guid ColumnId)> LoadCellAsync(HttpClient client, int bedIndex)
    {
        var bootstrap = (await client.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var setor = bootstrap.Sectors[0];
        var estado = (await client.GetFromJsonAsync<SessionStateDto>($"/api/sectors/{setor.Id}/sessions/current"))!;
        var template = bootstrap.Templates.Single(t => t.Code == "GELO");

        return (estado.Session.Id, bootstrap.Beds[bedIndex].Id, template.Id, template.Columns[0].Id);
    }
}
