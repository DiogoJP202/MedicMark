using System.Net;
using System.Net.Http.Json;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Operations;

namespace ChecklistPlantao.Server.IntegrationTests;

public sealed class SessionConcurrencyTests(ChecklistServerFactory factory) : IClassFixture<ChecklistServerFactory>
{
    [Fact]
    public async Task Duas_aberturas_concorrentes_de_datas_diferentes_resultam_em_sucesso_e_conflito()
    {
        var firstClient = await factory.CreateAdminClientAsync();
        var secondClient = await factory.CreateAdminClientAsync();
        var bootstrap = (await firstClient.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var sectorId = bootstrap.Sectors.Single().Id;

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(
                $"/api/sectors/{sectorId}/sessions",
                new OpenSessionRequest(new DateOnly(2026, 8, 20))),
            secondClient.PostAsJsonAsync(
                $"/api/sectors/{sectorId}/sessions",
                new OpenSessionRequest(new DateOnly(2026, 8, 21))));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    }
}
