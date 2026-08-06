using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Contracts.Common;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Application.Tests;

public sealed class SessionLifecycleTests
{
    [Fact]
    public async Task Abrir_sessao_inclui_todos_os_leitos_ativos_do_setor()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        var sectorId = await host.OesteSectorIdAsync();

        var result = await host.Sessions.OpenAsync(sectorId, null, user);

        Assert.True(result.IsSuccess);
        var session = await host.Db.OperationalSessions.Include(s => s.Beds).FirstAsync();
        Assert.Equal(16, session.Beds.Count);
        Assert.True(session.IsOpen);
    }

    [Fact]
    public async Task Nao_permite_duas_sessoes_abertas_no_mesmo_setor()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        var sectorId = await host.OesteSectorIdAsync();

        await host.Sessions.OpenAsync(sectorId, null, user);
        var segunda = await host.Sessions.OpenAsync(sectorId, null, user);

        Assert.True(segunda.IsFailure);
        Assert.Equal(ApiErrorCodes.SessionAlreadyOpen, segunda.Error!.Code);
    }

    [Fact]
    public async Task Usuario_sem_acesso_ao_setor_nao_abre_sessao()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var sectorId = await host.OesteSectorIdAsync();
        var user = TestUser.With([Permissions.ChecklistClose], sectors: []);

        var result = await host.Sessions.OpenAsync(sectorId, null, user);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.SectorAccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Sessao_e_criada_automaticamente_quando_a_configuracao_permite()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        var sectorId = await host.OesteSectorIdAsync();

        var state = await host.Sessions.GetCurrentAsync(sectorId, user, createIfMissing: true);

        Assert.True(state.IsSuccess);
        Assert.Equal(nameof(SessionStatus.Open), state.Required.Session.Status);
        Assert.Equal(16, state.Required.ActiveBedIds.Count);
    }

    [Fact]
    public async Task Sem_abertura_automatica_a_consulta_devolve_nao_encontrado()
    {
        using var host = await ApplicationTestHost.CreateAsync(
            settings: InstitutionSettings.Default with { AutoOpenSession = false });

        var user = TestUser.WithEverything();
        var sectorId = await host.OesteSectorIdAsync();

        var state = await host.Sessions.GetCurrentAsync(sectorId, user, createIfMissing: true);

        Assert.True(state.IsFailure);
        Assert.Equal(ApiErrorCodes.NotFound, state.Error!.Code);
    }

    [Fact]
    public async Task Fechar_com_pendencias_exige_confirmacao_explicita()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        var session = await CreateSessionWithOneEntryAsync(host, user, completed: false);

        var semConfirmar = await host.Sessions.CloseAsync(session, confirmWithPending: false, user);
        Assert.True(semConfirmar.IsFailure);
        Assert.Equal(ApiErrorCodes.ValidationFailed, semConfirmar.Error!.Code);

        var confirmando = await host.Sessions.CloseAsync(session, confirmWithPending: true, user);
        Assert.True(confirmando.IsSuccess);
        Assert.Equal(1, confirmando.Required.Overall.Pending);
    }

    [Fact]
    public async Task Fechar_sem_pendencias_dispensa_confirmacao()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        var session = await CreateSessionWithOneEntryAsync(host, user, completed: true);

        var result = await host.Sessions.CloseAsync(session, confirmWithPending: false, user);

        Assert.True(result.IsSuccess);
        Assert.False(result.Required.HasPending);
    }

    [Fact]
    public async Task Sessao_encerrada_nao_aceita_novas_marcacoes()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        var session = await CreateSessionWithOneEntryAsync(host, user, completed: true);
        await host.Sessions.CloseAsync(session, true, user);

        var beds = await host.OesteBedIdsAsync();
        var column = await host.Db.ChecklistColumns.FirstAsync();

        var result = await host.Mutations.ApplyEntryAsync(
            session, beds[0], column.ChecklistTemplateId, column.Id, true, 0, user);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.SessionClosed, result.Error!.Code);
    }

    [Fact]
    public async Task Reiniciar_desfaz_as_marcacoes_e_preserva_as_classificacoes()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        var session = await CreateSessionWithOneEntryAsync(host, user, completed: true);

        var beds = await host.OesteBedIdsAsync();
        var marker = await host.Db.BedMarkerDefinitions.FirstAsync();
        await host.Mutations.ApplyMarkerAsync(session, beds[0], marker.Id, true, 0, user);
        await host.Db.SaveChangesAsync();

        var result = await host.Sessions.ResetAsync(session, user);

        Assert.True(result.IsSuccess);
        Assert.All(await host.Db.ChecklistEntries.ToListAsync(), e => Assert.False(e.IsCompleted));
        Assert.True((await host.Db.SessionBedMarkers.FirstAsync()).IsSelected);
    }

    [Fact]
    public async Task Usuario_sem_permissao_de_encerrar_nao_reinicia()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var admin = TestUser.WithEverything();
        var session = await CreateSessionWithOneEntryAsync(host, admin, completed: true);

        var sectorId = await host.OesteSectorIdAsync();
        var operador = TestUser.With([Permissions.ChecklistView, Permissions.ChecklistUpdate], [sectorId]);

        var result = await host.Sessions.ResetAsync(session, operador);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Retencao_apaga_sessao_encerrada_apos_a_janela_de_recuperacao()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        var session = await CreateSessionWithOneEntryAsync(host, user, completed: true);
        await host.Sessions.CloseAsync(session, true, user);

        host.Clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(0, await host.Sessions.PurgeExpiredAsync());
        Assert.True(await host.Db.OperationalSessions.AnyAsync());

        host.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, await host.Sessions.PurgeExpiredAsync());

        Assert.False(await host.Db.OperationalSessions.AnyAsync());
        Assert.False(await host.Db.ChecklistEntries.AnyAsync());
        Assert.False(await host.Db.SessionBedMarkers.AnyAsync());
    }

    [Fact]
    public async Task Retencao_nunca_apaga_cadastros_nem_configuracoes()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        var session = await CreateSessionWithOneEntryAsync(host, user, completed: true);
        await host.Sessions.CloseAsync(session, true, user);

        host.Clock.Advance(TimeSpan.FromDays(30));
        await host.Sessions.PurgeExpiredAsync();

        Assert.Equal(16, await host.Db.Beds.CountAsync());
        Assert.Equal(1, await host.Db.Sectors.CountAsync());
        Assert.Equal(3, await host.Db.ChecklistTemplates.CountAsync());
        Assert.Equal(3, await host.Db.BedMarkerDefinitions.CountAsync());
        Assert.True(await host.Db.AppSettings.AnyAsync());
    }

    [Fact]
    public async Task Sessao_aberta_nunca_e_apagada_pela_retencao()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        await CreateSessionWithOneEntryAsync(host, user, completed: false);

        host.Clock.Advance(TimeSpan.FromDays(365));

        Assert.Equal(0, await host.Sessions.PurgeExpiredAsync());
        Assert.True(await host.Db.OperationalSessions.AnyAsync());
    }

    [Fact]
    public async Task Nenhuma_marcacao_guarda_referencia_a_usuario()
    {
        using var host = await ApplicationTestHost.CreateAsync();
        var user = TestUser.WithEverything();
        await CreateSessionWithOneEntryAsync(host, user, completed: true);

        var entity = host.Db.Model.FindEntityType(typeof(ChecklistEntry))!;
        var propriedades = entity.GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(propriedades, p => p.Contains("User", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propriedades, p => p.Contains("Usuario", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Guid> CreateSessionWithOneEntryAsync(ApplicationTestHost host, ICurrentUser user, bool completed)
    {
        var sectorId = await host.OesteSectorIdAsync();
        var opened = await host.Sessions.OpenAsync(sectorId, null, user);
        var sessionId = opened.Required.Id;

        var beds = await host.OesteBedIdsAsync();
        var column = await host.Db.ChecklistColumns.OrderBy(c => c.SortOrder).FirstAsync();

        await host.Mutations.ApplyEntryAsync(sessionId, beds[0], column.ChecklistTemplateId, column.Id, completed, 0, user);
        await host.Db.SaveChangesAsync();

        return sessionId;
    }
}
