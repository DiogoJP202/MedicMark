using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Common;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Application.Tests;

/// <summary>
/// Em configuração administrativa não existe "conclusão vence": versão defasada é recusada e o
/// administrador precisa atualizar a tela.
/// </summary>
public sealed class AdministrationTests
{
    [Fact]
    public async Task Criar_setor_registra_alteracao_para_sincronizacao()
    {
        using var host = await ApplicationTestHost.CreateAsync();

        var result = await host.Structure.SaveSectorAsync(
            null, new SaveSectorRequest("Leste", null, 20, true, null, null, 0));

        Assert.True(result.IsSuccess);
        Assert.True(await host.Db.ChangeLog.AnyAsync(c => c.EntityType == SyncEntityTypes.Sector));
    }

    [Fact]
    public async Task Setor_com_nome_repetido_e_recusado()
    {
        using var host = await ApplicationTestHost.CreateAsync();

        var result = await host.Structure.SaveSectorAsync(
            null, new SaveSectorRequest("Oeste", null, 20, true, null, null, 0));

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.DuplicateValue, result.Error!.Code);
    }

    [Fact]
    public async Task Editar_setor_com_versao_antiga_e_recusado()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var setor = await host.Db.Sectors.FirstAsync();

        // A versão que os DOIS administradores tinham na tela quando começaram a editar.
        var versaoNaTela = setor.Version;

        var primeira = await host.Structure.SaveSectorAsync(
            setor.Id, new SaveSectorRequest("Oeste A", null, 10, true, null, null, versaoNaTela));
        Assert.True(primeira.IsSuccess);

        // Segundo administrador salva sem ter atualizado a tela.
        var segunda = await host.Structure.SaveSectorAsync(
            setor.Id, new SaveSectorRequest("Oeste B", null, 10, true, null, null, versaoNaTela));

        Assert.True(segunda.IsFailure);
        Assert.Equal(ApiErrorCodes.VersionConflict, segunda.Error!.Code);
        Assert.Equal("Oeste A", (await host.Db.Sectors.FirstAsync()).Name);
    }

    [Fact]
    public async Task Leito_com_codigo_repetido_ativo_no_mesmo_setor_e_recusado()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var sectorId = await host.OesteSectorIdAsync();

        var result = await host.Structure.SaveBedAsync(
            null, new SaveBedRequest(sectorId, "1148", null, 999, true, 0));

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.DuplicateValue, result.Error!.Code);
    }

    [Fact]
    public async Task Leito_pode_ser_movido_para_outro_setor()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var leste = (await host.Structure.SaveSectorAsync(null, new SaveSectorRequest("Leste", null, 20, true, null, null, 0))).Required;
        var leito = await host.Db.Beds.OrderBy(b => b.SortOrder).FirstAsync();

        var result = await host.Structure.SaveBedAsync(
            leito.Id, new SaveBedRequest(leste.Id, leito.Code, null, leito.SortOrder, true, leito.Version));

        Assert.True(result.IsSuccess);
        Assert.Equal(leste.Id, result.Required.SectorId);
    }

    [Fact]
    public async Task Alterar_horario_da_coluna_avanca_a_versao_e_entra_no_log()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var template = await host.Db.ChecklistTemplates.Include(t => t.Columns).FirstAsync(t => t.Code == "GLICEMIA");
        var jantar = template.Columns.First(c => c.DisplayName == "Jantar");

        var result = await host.Structure.SaveColumnAsync(template.Id, jantar.Id, new SaveColumnRequest(
            "Jantar", new TimeOnly(18, 45), jantar.SortOrder, true, true, 0, 15, 10, 3, true, 5, jantar.Version));

        Assert.True(result.IsSuccess);
        Assert.Equal(new TimeOnly(18, 45), result.Required.TriggerTime);
        Assert.True(await host.Db.ChangeLog.AnyAsync(c => c.EntityType == SyncEntityTypes.ChecklistColumn));
    }

    [Fact]
    public async Task Habilitar_notificacao_em_coluna_sem_horario_e_recusado()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var template = await host.Db.ChecklistTemplates.Include(t => t.Columns).FirstAsync(t => t.Code == "SSVV");

        var criada = await host.Structure.SaveColumnAsync(template.Id, null, new SaveColumnRequest(
            "Livre", null, 99, true, NotificationEnabled: true, 0, 15, 10, 3, true, 5, 0));

        Assert.True(criada.IsFailure);
        Assert.Equal(ApiErrorCodes.ValidationFailed, criada.Error!.Code);
    }

    [Fact]
    public async Task Coluna_com_nome_repetido_no_mesmo_tipo_e_recusada()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var template = await host.Db.ChecklistTemplates.Include(t => t.Columns).FirstAsync(t => t.Code == "GELO");

        var result = await host.Structure.SaveColumnAsync(template.Id, null, new SaveColumnRequest(
            "20H", new TimeOnly(21, 0), 99, true, true, 0, 15, 10, 3, true, 5, 0));

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.Error!.Code);
    }

    [Fact]
    public async Task Administrador_cria_novo_marcador_alem_de_ci_sondas_e_drenos()
    {
        using var host = await ApplicationTestHost.CreateAsync();

        var result = await host.Structure.SaveMarkerAsync(null, new SaveMarkerRequest("Isolamento", 40, true, 0));

        Assert.True(result.IsSuccess);
        Assert.Equal("ISOLAMENTO", result.Required.Code);
        Assert.Equal(4, await host.Db.BedMarkerDefinitions.CountAsync());
    }

    [Fact]
    public async Task Reordenar_leitos_grava_a_nova_ordem()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var leitos = await host.Db.Beds.OrderBy(b => b.SortOrder).Take(3).ToListAsync();

        var pedido = new ReorderRequest([.. leitos.Select((b, i) => new ReorderItem(b.Id, (3 - i) * 100))]);
        var result = await host.Structure.ReorderAsync(SyncEntityTypes.Bed, pedido);

        Assert.True(result.IsSuccess);
        var recarregados = await host.Db.Beds.Where(b => leitos.Select(l => l.Id).Contains(b.Id)).OrderBy(b => b.SortOrder).ToListAsync();
        Assert.Equal(leitos[2].Id, recarregados[0].Id);
    }

    [Fact]
    public async Task Salvar_configuracoes_gerais_persiste_e_valida()
    {
        using var host = await ApplicationTestHost.CreateAsync();

        var result = await host.System.SaveSettingsAsync(new SaveInstitutionSettingsRequest(
            "America/Sao_Paulo", new TimeOnly(7, 0), new TimeOnly(19, 0), 12, 3, 4, false));

        Assert.True(result.IsSuccess);

        var stored = await host.Db.AppSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        Assert.Equal("07:00", stored[AppSettingKeys.ShiftStart]);
        Assert.Equal("12", stored[AppSettingKeys.RetentionAfterCloseHours]);
        Assert.Equal("false", stored[AppSettingKeys.AutoOpenSession]);
    }

    [Fact]
    public async Task Turno_com_inicio_igual_ao_fim_e_recusado()
    {
        using var host = await ApplicationTestHost.CreateAsync();

        var result = await host.System.SaveSettingsAsync(new SaveInstitutionSettingsRequest(
            "America/Sao_Paulo", new TimeOnly(19, 0), new TimeOnly(19, 0), 24, 7, 5, true));

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.Error!.Code);
    }

    [Fact]
    public async Task Configuracao_de_notificacao_com_versao_antiga_e_recusada()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var atual = await host.System.GetNotificationsAsync();

        var primeira = await host.System.SaveNotificationsAsync(new SaveNotificationConfigurationRequest(
            true, true, "High", true, true, false, "T {coluna}", "B {pendentes}", atual.Version));
        Assert.True(primeira.IsSuccess);

        var segunda = await host.System.SaveNotificationsAsync(new SaveNotificationConfigurationRequest(
            false, false, "Normal", true, true, false, "X", "Y", atual.Version));

        Assert.True(segunda.IsFailure);
        Assert.Equal(ApiErrorCodes.VersionConflict, segunda.Error!.Code);
    }

    [Fact]
    public async Task Prioridade_de_notificacao_desconhecida_e_recusada()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var atual = await host.System.GetNotificationsAsync();

        var result = await host.System.SaveNotificationsAsync(new SaveNotificationConfigurationRequest(
            true, true, "Altissima", true, true, false, "T", "B", atual.Version));

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.Error!.Code);
    }
}
