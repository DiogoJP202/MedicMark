using System.Net;
using System.Net.Http.Json;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Common;
using ChecklistPlantao.Contracts.Configuration;

namespace ChecklistPlantao.Server.IntegrationTests;

/// <summary>
/// Cadastro de estrutura pela API de verdade.
///
/// Existe por causa de um defeito relatado em uso real: salvar uma coluna respondia HTTP 500.
/// A regra de nome único valia só na criação, então a edição ia colidir com o índice do banco,
/// e nada traduzia essa exceção — o administrador via "erro inesperado" sem saber o que corrigir.
/// </summary>
public sealed class AdminStructureTests(ChecklistServerFactory factory) : IClassFixture<ChecklistServerFactory>
{
    [Fact]
    public async Task Renomear_coluna_para_nome_de_outra_ativa_responde_conflito_e_nao_erro_do_servidor()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (gelo, coluna22) = await ColunaDoGeloAsync(admin, "22H");

        var response = await admin.PutAsJsonAsync(
            $"/api/admin/templates/{gelo.Id}/columns/{coluna22.Id}",
            Requisicao(coluna22, displayName: "20H"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problema = await response.Content.ReadFromJsonAsync<ProblemaComCodigo>();
        Assert.Equal(ApiErrorCodes.DuplicateValue, problema!.Codigo);
        Assert.Contains("20H", problema.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Criar_coluna_com_nome_ja_usado_responde_conflito()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (gelo, _) = await ColunaDoGeloAsync(admin, "20H");

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/templates/{gelo.Id}/columns",
            new SaveColumnRequest("20H", new TimeOnly(21, 0), 99, true, true, 0, 15, 10, 3, true, 5, 0));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Criar coluna nova, com nome inédito e horário — o caminho que o usuário relatou travado.</summary>
    [Fact]
    public async Task Criar_coluna_nova_com_horario_funciona()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (gelo, _) = await ColunaDoGeloAsync(admin, "20H");

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/templates/{gelo.Id}/columns",
            new SaveColumnRequest($"T{DateTime.UtcNow.Ticks % 100000}", new TimeOnly(3, 0), 95, true, true, 0, 15, 10, 3, true, 5, 0));

        var criada = await HttpAssert.ReadAsync<ChecklistColumnDto>(response);
        Assert.Equal(new TimeOnly(3, 0), criada.TriggerTime);
    }

    /// <summary>
    /// Coluna sem horário com "Notificar" ligado é recusada pelo domínio. A tela marca "Notificar"
    /// por padrão, então quem cria uma coluna sem hora bate exatamente aqui.
    /// </summary>
    [Fact]
    public async Task Criar_coluna_sem_horario_com_notificacao_ligada_e_recusada()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (gelo, _) = await ColunaDoGeloAsync(admin, "20H");

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/templates/{gelo.Id}/columns",
            new SaveColumnRequest($"S{DateTime.UtcNow.Ticks % 100000}", null, 96, true, NotificationEnabled: true, 0, 15, 10, 3, true, 5, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Sem horário e sem notificação é uma coluna legítima — só não alerta.</summary>
    [Fact]
    public async Task Criar_coluna_sem_horario_e_sem_notificacao_funciona()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (gelo, _) = await ColunaDoGeloAsync(admin, "20H");

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/templates/{gelo.Id}/columns",
            new SaveColumnRequest($"N{DateTime.UtcNow.Ticks % 100000}", null, 97, true, NotificationEnabled: false, 0, 15, 10, 3, true, 5, 0));

        await HttpAssert.EnsureSuccessAsync(response);
    }

    /// <summary>O caminho feliz, para o teste acima não passar por acidente com tudo quebrado.</summary>
    [Fact]
    public async Task Alterar_o_horario_de_uma_coluna_funciona()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (gelo, coluna) = await ColunaDoGeloAsync(admin, "04H");

        var response = await admin.PutAsJsonAsync(
            $"/api/admin/templates/{gelo.Id}/columns/{coluna.Id}",
            Requisicao(coluna, triggerTime: new TimeOnly(4, 30)));

        var salva = await HttpAssert.ReadAsync<ChecklistColumnDto>(response);

        Assert.Equal(new TimeOnly(4, 30), salva.TriggerTime);
        Assert.True(salva.Version > coluna.Version);
    }

    /// <summary>
    /// Editar o tipo não pode apagar a restrição de setores: lista vazia significa "vale para
    /// todos os setores", então enviá-la por engano descadastra silenciosamente o que o
    /// administrador havia configurado.
    /// </summary>
    [Fact]
    public async Task Editar_o_tipo_preserva_os_setores_enviados()
    {
        var admin = await factory.CreateAdminClientAsync();
        var bootstrap = (await admin.GetFromJsonAsync<BootstrapResponse>("/api/bootstrap"))!;
        var setor = bootstrap.Sectors[0].Id;

        var criado = await HttpAssert.ReadAsync<ChecklistTemplateDto>(
            await admin.PostAsJsonAsync("/api/admin/templates", new SaveTemplateRequest(
                $"Restrito {Guid.CreateVersion7():N}"[..20], null, 90, true, [setor], 0)));

        Assert.Contains(setor, criado.SectorIds);

        var renomeado = await HttpAssert.ReadAsync<ChecklistTemplateDto>(
            await admin.PutAsJsonAsync($"/api/admin/templates/{criado.Id}", new SaveTemplateRequest(
                criado.Name + " editado", null, criado.SortOrder, true, criado.SectorIds, criado.Version)));

        Assert.Contains(setor, renomeado.SectorIds);
    }

    private static SaveColumnRequest Requisicao(
        ChecklistColumnDto coluna,
        string? displayName = null,
        TimeOnly? triggerTime = null) =>
        new(
            displayName ?? coluna.DisplayName,
            triggerTime ?? coluna.TriggerTime,
            coluna.SortOrder,
            coluna.IsActive,
            coluna.NotificationEnabled,
            coluna.LeadTimeMinutes,
            coluna.GracePeriodMinutes,
            coluna.RepeatIntervalMinutes,
            coluna.MaximumRepeats,
            coluna.AllowSnooze,
            coluna.SnoozeMinutes,
            coluna.Version);

    private static async Task<(ChecklistTemplateDto Template, ChecklistColumnDto Column)> ColunaDoGeloAsync(
        HttpClient admin,
        string nomeDaColuna)
    {
        var templates = (await admin.GetFromJsonAsync<List<ChecklistTemplateDto>>("/api/admin/templates"))!;
        var gelo = templates.First(t => t.Code == "GELO");

        return (gelo, gelo.Columns.First(c => c.DisplayName == nomeDaColuna));
    }

    /// <summary>O código estável do erro viaja em <c>extensions.codigo</c>, fora do ProblemDetails padrão.</summary>
    private sealed record ProblemaComCodigo(string Detail, string Codigo);
}
