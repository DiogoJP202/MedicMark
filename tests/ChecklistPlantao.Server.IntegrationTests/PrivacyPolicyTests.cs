using System.Net;

namespace ChecklistPlantao.Server.IntegrationTests;

public sealed class PrivacyPolicyTests(ChecklistServerFactory factory) : IClassFixture<ChecklistServerFactory>
{
    [Theory]
    [InlineData("/privacidade")]
    [InlineData("/privacy")]
    public async Task Policy_is_public_html_and_describes_the_real_data_scope(string route)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(route);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Política de Privacidade do Checklist de Plantão", body);
        Assert.Contains("O sistema não cadastra dados de pacientes", body);
        Assert.Contains("Não vendemos dados", body);
        Assert.Contains("Direitos e exclusão", body);
    }
}
