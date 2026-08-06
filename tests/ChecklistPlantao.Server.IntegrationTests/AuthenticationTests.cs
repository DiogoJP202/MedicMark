using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Domain.Access;

namespace ChecklistPlantao.Server.IntegrationTests;

public sealed class AuthenticationTests(ChecklistServerFactory factory) : IClassFixture<ChecklistServerFactory>
{
    [Fact]
    public async Task Administrador_inicial_entra_com_as_credenciais_configuradas()
    {
        var client = factory.CreateClient();

        var login = await ChecklistServerFactory.LoginAsync(client, ChecklistServerFactory.AdminUserName, ChecklistServerFactory.AdminPassword);

        Assert.False(string.IsNullOrWhiteSpace(login.Tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(login.Tokens.RefreshToken));
        Assert.True(login.User.GrantsAllSectors);
        Assert.Equal(Permissions.All.Count, login.User.Permissions.Count);
    }

    [Fact]
    public async Task Senha_errada_devolve_401_sem_revelar_se_o_usuario_existe()
    {
        var client = factory.CreateClient();

        var inexistente = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nao-existe", "SenhaQualquer1", null, null));
        var senhaErrada = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ChecklistServerFactory.AdminUserName, "SenhaErrada1", null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);

        var corpoA = await inexistente.Content.ReadAsStringAsync();
        var corpoB = await senhaErrada.Content.ReadAsStringAsync();
        Assert.Equal(ExtractDetail(corpoA), ExtractDetail(corpoB));
    }

    [Fact]
    public async Task Endpoint_protegido_sem_token_devolve_401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/bootstrap");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_invalido_devolve_401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token.completamente.invalido");

        var response = await client.GetAsync("/api/bootstrap");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_devolve_novo_par_e_invalida_o_anterior()
    {
        var client = factory.CreateClient();
        var login = await ChecklistServerFactory.LoginAsync(client, ChecklistServerFactory.AdminUserName, ChecklistServerFactory.AdminPassword);

        var primeiro = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.Tokens.RefreshToken, null));
        primeiro.EnsureSuccessStatusCode();
        var renovado = (await primeiro.Content.ReadFromJsonAsync<LoginResponse>())!;

        Assert.NotEqual(login.Tokens.RefreshToken, renovado.Tokens.RefreshToken);

        // Reapresentar o refresh já consumido tem de falhar: é o sinal de token roubado.
        var repetido = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.Tokens.RefreshToken, null));
        Assert.Equal(HttpStatusCode.Unauthorized, repetido.StatusCode);
    }

    [Fact]
    public async Task Logout_revoga_o_refresh_token()
    {
        var client = factory.CreateClient();
        var login = await ChecklistServerFactory.LoginAsync(client, ChecklistServerFactory.AdminUserName, ChecklistServerFactory.AdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Tokens.AccessToken);

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(login.Tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.Tokens.RefreshToken, null));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Me_descreve_o_acesso_efetivo_do_usuario_autenticado()
    {
        var client = await factory.CreateAdminClientAsync();

        var me = await client.GetFromJsonAsync<AuthenticatedUserDto>("/api/auth/me");

        Assert.NotNull(me);
        Assert.Equal("admin", me.UserName);
        Assert.Contains(Permissions.AdminUsers, me.Permissions);
    }

    [Fact]
    public async Task Sonda_do_servidor_e_anonima_e_nao_expoe_infraestrutura()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/server-info");
        var corpo = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("ChecklistPlantao", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain(".db", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SigningKey", corpo, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_checks_respondem_sem_autenticacao(string caminho)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(caminho);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string ExtractDetail(string problemJson)
    {
        using var document = System.Text.Json.JsonDocument.Parse(problemJson);
        return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() ?? string.Empty : string.Empty;
    }
}
