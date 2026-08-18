using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Contracts.Common;
using ChecklistPlantao.Contracts.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChecklistPlantao.Server.IntegrationTests;

/// <summary>
/// O cliente HTTP de verdade contra o servidor de verdade.
///
/// Este era o último ponto cego da suíte. Todo teste de cliente trocava <see cref="IServerApi"/>
/// por um duplo, então <see cref="HttpServerApi"/> nunca falava com ninguém: montagem de URL,
/// serialização de <c>TimeOnly</c>, envio do bearer e — o mais importante — a tradução de
/// <c>ProblemDetails</c> na mensagem que a tela exibe passavam sem nenhuma verificação.
/// </summary>
public sealed class ClientAgainstServerTests(ChecklistServerFactory factory) : IClassFixture<ChecklistServerFactory>
{
    [Fact]
    public async Task O_cliente_reconhece_o_servidor()
    {
        var api = await ClienteAutenticadoAsync();

        var resposta = await api.ProbeAsync("http://localhost");

        Assert.NotNull(resposta);
        Assert.Equal("ChecklistPlantao", resposta!.Application);
    }

    [Fact]
    public async Task O_cliente_baixa_o_bootstrap_com_os_dados_semeados()
    {
        var api = await ClienteAutenticadoAsync();

        var bootstrap = await api.BootstrapAsync();

        Assert.NotNull(bootstrap);
        Assert.NotEmpty(bootstrap!.Sectors);
        Assert.NotEmpty(bootstrap.Beds);
        Assert.NotEmpty(bootstrap.Templates);
    }

    /// <summary>Criar coluna pelo caminho completo cliente → servidor — o defeito relatado.</summary>
    [Fact]
    public async Task Criar_coluna_pelo_cliente_funciona()
    {
        var admin = new AdministrationService(await ClienteAutenticadoAsync());
        var gelo = await TemplateGeloAsync(admin);

        var resultado = await admin.SaveColumnAsync(gelo.Id, null, new SaveColumnRequest(
            $"C{DateTime.UtcNow.Ticks % 100000}", new TimeOnly(9, 0), 98, true, true, 0, 15, 10, 3, true, 5, 0));

        Assert.True(resultado.Succeeded, resultado.Message);
    }

    /// <summary>
    /// O horário é <c>TimeOnly</c> e atravessa JSON nos dois sentidos. Se a serialização
    /// estivesse errada, a coluna gravaria com hora trocada — e o agendamento inteiro iria junto.
    /// </summary>
    [Fact]
    public async Task O_horario_da_coluna_sobrevive_a_ida_e_volta()
    {
        var admin = new AdministrationService(await ClienteAutenticadoAsync());
        var gelo = await TemplateGeloAsync(admin);
        var nome = $"H{DateTime.UtcNow.Ticks % 100000}";

        await admin.SaveColumnAsync(gelo.Id, null, new SaveColumnRequest(
            nome, new TimeOnly(2, 45), 97, true, true, 0, 15, 10, 3, true, 5, 0));

        var criada = (await TemplateGeloAsync(admin)).Columns.First(c => c.DisplayName == nome);

        Assert.Equal(new TimeOnly(2, 45), criada.TriggerTime);
    }

    /// <summary>
    /// A recusa precisa chegar à tela como texto legível, com o código estável junto. Sem isto o
    /// administrador vê "Não foi possível salvar" e não descobre que o nome está repetido.
    /// </summary>
    [Fact]
    public async Task Erro_do_servidor_vira_mensagem_legivel_para_a_tela()
    {
        var admin = new AdministrationService(await ClienteAutenticadoAsync());
        var gelo = await TemplateGeloAsync(admin);
        var existente = gelo.Columns.First(c => c.IsActive).DisplayName;

        var resultado = await admin.SaveColumnAsync(gelo.Id, null, new SaveColumnRequest(
            existente, new TimeOnly(23, 0), 96, true, true, 0, 15, 10, 3, true, 5, 0));

        Assert.False(resultado.Succeeded);
        Assert.Equal(ApiErrorCodes.DuplicateValue, resultado.Code);
        Assert.Contains(existente, resultado.Message!, StringComparison.Ordinal);
    }

    /// <summary>Conflito de versão tem tratamento próprio na tela: "atualize e refaça".</summary>
    [Fact]
    public async Task Conflito_de_versao_chega_com_o_codigo_certo()
    {
        var admin = new AdministrationService(await ClienteAutenticadoAsync());
        var gelo = await TemplateGeloAsync(admin);
        var coluna = gelo.Columns.First(c => c.DisplayName == "06H");

        var resultado = await admin.SaveColumnAsync(gelo.Id, coluna.Id, new SaveColumnRequest(
            coluna.DisplayName, coluna.TriggerTime, coluna.SortOrder, true, true, 0, 15, 10, 3, true, 5,
            BaseVersion: coluna.Version + 99));

        Assert.False(resultado.Succeeded);
        Assert.Equal(ApiErrorCodes.VersionConflict, resultado.Code);
    }

    /// <summary>Sem endereço configurado o cliente não tenta a rede — é o modo offline.</summary>
    [Fact]
    public async Task Sem_endereco_configurado_a_administracao_avisa_que_exige_conexao()
    {
        var admin = new AdministrationService(ConstruirApi(new TokenStoreFalso(null), new EnderecoFalso(null)));

        var resultado = await admin.SaveMarkerAsync(null, new SaveMarkerRequest("Teste", 10, true, 0));

        Assert.False(resultado.Succeeded);
        Assert.Contains("conexão", resultado.Message!, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ apoio

    private static async Task<ChecklistTemplateDto> TemplateGeloAsync(AdministrationService admin) =>
        (await admin.GetTemplatesAsync()).First(t => t.Code == "GELO");

    private async Task<HttpServerApi> ClienteAutenticadoAsync()
    {
        var login = await ChecklistServerFactory.LoginAsync(
            factory.CreateClient(),
            ChecklistServerFactory.AdminUserName,
            ChecklistServerFactory.AdminPassword);

        return ConstruirApi(new TokenStoreFalso(login.Tokens.AccessToken), new EnderecoFalso("http://localhost"));
    }

    private HttpServerApi ConstruirApi(ITokenStore tokens, IServerAddressProvider endereco) =>
        new(new FabricaDeClientes(factory), tokens, endereco, NullLogger<HttpServerApi>.Instance);

    /// <summary>Entrega o cliente do servidor em memória, no lugar de um socket de verdade.</summary>
    private sealed class FabricaDeClientes(ChecklistServerFactory factory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => factory.CreateClient();
    }

    private sealed class TokenStoreFalso(string? token) : ITokenStore
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(token);

        public Task SaveAsync(string accessToken, DateTime accessExpiresAtUtc, string refreshToken, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EnderecoFalso(string? url) : IServerAddressProvider
    {
        public string? ServerUrl => url;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(url);
    }
}
