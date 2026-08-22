using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChecklistPlantao.Contracts.Auth;
using Microsoft.Extensions.Configuration;

namespace ChecklistPlantao.Server.IntegrationTests;

public sealed class RateLimitTests(ChecklistServerFactory factory) : IClassFixture<ChecklistServerFactory>
{
    [Fact]
    public async Task Login_usa_o_ip_encaminhado_e_isola_ips_diferentes()
    {
        using var limitada = LimitedFactory(loginPerMinute: 1, syncBurst: 100);
        using var mesmoIp = limitada.CreateClient();
        using var outroIp = limitada.CreateClient();

        mesmoIp.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
        outroIp.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.11");

        var primeira = await LoginInvalidoAsync(mesmoIp);
        var segunda = await LoginInvalidoAsync(mesmoIp);
        var outroCliente = await LoginInvalidoAsync(outroIp);

        Assert.Equal(HttpStatusCode.Unauthorized, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, segunda.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, outroCliente.StatusCode);
    }

    [Fact]
    public async Task Sincronizacao_e_limitada_por_dispositivo_e_nao_pelo_ip_compartilhado()
    {
        using var limitada = LimitedFactory(loginPerMinute: 100, syncBurst: 1);
        using var dispositivoA = await AuthenticatedAsync(limitada, "dispositivo-a");
        using var dispositivoB = await AuthenticatedAsync(limitada, "dispositivo-b");

        var primeiraA = await dispositivoA.GetAsync("/api/bootstrap");
        var segundaA = await dispositivoA.GetAsync("/api/bootstrap");
        var primeiraB = await dispositivoB.GetAsync("/api/bootstrap");

        Assert.Equal(HttpStatusCode.OK, primeiraA.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, segundaA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, primeiraB.StatusCode);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> LimitedFactory(
        int loginPerMinute,
        int syncBurst) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:LoginPerMinute"] = loginPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["RateLimit:SyncBurst"] = syncBurst.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["RateLimit:SyncPerMinute"] = "1",
            })));

    private static Task<HttpResponseMessage> LoginInvalidoAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("usuario-inexistente", "SenhaInvalida1", "teste-rate-limit", "Teste"));

    private static async Task<HttpClient> AuthenticatedAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        string deviceId)
    {
        var client = factory.CreateClient();
        var resposta = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                ChecklistServerFactory.AdminUserName,
                ChecklistServerFactory.AdminPassword,
                deviceId,
                "Teste"));

        resposta.EnsureSuccessStatusCode();
        var login = (await resposta.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Tokens.AccessToken);
        return client;
    }
}
