using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Domain.Access;

namespace ChecklistPlantao.Server.IntegrationTests;

/// <summary>
/// A interface esconde botões; o servidor recusa. Estes testes atacam os endpoints diretamente,
/// sem passar por tela nenhuma.
/// </summary>
public sealed class AuthorizationTests(ChecklistServerFactory factory) : IClassFixture<ChecklistServerFactory>
{
    [Fact]
    public async Task Usuario_sem_permissao_administrativa_nao_acessa_o_cadastro_de_usuarios()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (client, _) = await CreateRestrictedUserAsync(admin, "sem-admin", sectorAccess: true);

        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_sem_acesso_ao_setor_nao_consulta_a_sessao_daquele_setor()
    {
        var admin = await factory.CreateAdminClientAsync();
        var bootstrap = (await admin.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var oeste = bootstrap.Sectors[0];

        var (client, _) = await CreateRestrictedUserAsync(admin, "sem-setor", sectorAccess: false);

        var response = await client.GetAsync($"/api/sectors/{oeste.Id}/sessions/current");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_com_acesso_ao_setor_consulta_a_sessao_normalmente()
    {
        var admin = await factory.CreateAdminClientAsync();
        var bootstrap = (await admin.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var oeste = bootstrap.Sectors[0];

        var (client, _) = await CreateRestrictedUserAsync(admin, "com-setor", sectorAccess: true);

        var response = await client.GetAsync($"/api/sectors/{oeste.Id}/sessions/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_sem_permissao_de_encerrar_nao_fecha_a_sessao()
    {
        var admin = await factory.CreateAdminClientAsync();
        var bootstrap = (await admin.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var oeste = bootstrap.Sectors[0];

        var estado = await admin.GetFromJsonAsync<Contracts.Operations.SessionStateDto>($"/api/sectors/{oeste.Id}/sessions/current");
        var (client, _) = await CreateRestrictedUserAsync(admin, "sem-fechar", sectorAccess: true);

        var response = await client.PostAsJsonAsync(
            $"/api/sessions/{estado!.Session.Id}/close",
            new Contracts.Operations.CloseSessionRequest(true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_so_devolve_os_setores_que_o_usuario_enxerga()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (client, _) = await CreateRestrictedUserAsync(admin, "bootstrap-restrito", sectorAccess: false);

        var bootstrap = await client.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap");

        Assert.NotNull(bootstrap);
        Assert.Empty(bootstrap.Sectors);
        Assert.Empty(bootstrap.Beds);
    }

    [Fact]
    public async Task Usuario_desativado_nao_consegue_mais_entrar()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (_, user) = await CreateRestrictedUserAsync(admin, "sera-desativado", sectorAccess: true);

        var atualizado = await admin.PutAsJsonAsync(
            $"/api/admin/users/{user.Id}",
            new UpdateUserRequest(user.DisplayName, IsActive: false, user.GroupIds, user.Version));

        atualizado.EnsureSuccessStatusCode();

        var login = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login",
            new Contracts.Auth.LoginRequest("sera-desativado", "Senha12345", null, null));

        // O Identity aplica bloqueio ao desativar, então a resposta é "conta bloqueada".
        Assert.True(
            login.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Locked or HttpStatusCode.Forbidden,
            $"Esperado 401/403/423, veio {(int)login.StatusCode}.");
    }

    /// <summary>Cria um usuário de plantão com permissões mínimas e devolve um cliente já autenticado.</summary>
    private async Task<(HttpClient Client, AppUserDto User)> CreateRestrictedUserAsync(
        HttpClient admin,
        string userName,
        bool sectorAccess)
    {
        var bootstrap = (await admin.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var sectorIds = sectorAccess ? bootstrap.Sectors.Select(s => s.Id).ToList() : [];

        var groupResponse = await admin.PostAsJsonAsync("/api/admin/groups", new SaveGroupRequest(
            Name: $"Grupo {userName}",
            Description: null,
            IsActive: true,
            GrantsAllSectors: false,
            Permissions: [Permissions.ChecklistView, Permissions.ChecklistUpdate],
            SectorIds: sectorIds,
            BaseVersion: 0));

        var group = await HttpAssert.ReadAsync<AccessGroupDto>(groupResponse);

        var userResponse = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(
            userName, $"Usuário {userName}", "Senha12345", [group.Id]));

        var user = await HttpAssert.ReadAsync<AppUserDto>(userResponse);

        var client = factory.CreateClient();
        var login = await ChecklistServerFactory.LoginAsync(client, userName, "Senha12345");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Tokens.AccessToken);

        return (client, user);
    }
}
