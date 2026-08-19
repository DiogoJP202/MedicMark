using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChecklistPlantao.Client.Core.Tests;

/// <summary>
/// Adotar a sessão do servidor sobre o que já existe no aparelho.
///
/// Defeito relatado em campo ao reiniciar o plantão:
///
/// <code>
/// SQLite Error 19: UNIQUE constraint failed: Marcacoes.SessionId, Marcacoes.BedId,
/// Marcacoes.ChecklistTemplateId, Marcacoes.ChecklistColumnId
/// </code>
///
/// A adoção procurava a marcação **só por Id**. Quando a mesma célula tem identificadores
/// diferentes nos dois lados — o aparelho criou a dela offline, o servidor criou a sua — a busca
/// falhava e o código inseria, colidindo com o índice único da chave natural.
///
/// O <c>SyncEngine</c> já procurava pela chave natural; este caminho tinha ficado para trás. Nunca
/// foi exercitado porque o duplo do servidor devolvia sempre uma sessão nula.
/// </summary>
public sealed class AdoptServerSessionTests
{
    private static readonly Guid Setor = Guid.CreateVersion7();
    private static readonly Guid Leito = Guid.CreateVersion7();
    private static readonly Guid Tipo = Guid.CreateVersion7();
    private static readonly Guid Coluna = Guid.CreateVersion7();
    private static readonly Guid Marcador = Guid.CreateVersion7();

    private static LocalChecklistStore CreateStore(LocalTestHost host, Persistence.LocalDbContext db) =>
        new(db,
            new OutboxWriter(db),
            host.Api,
            host.Clock,
            new FakeTimeZone(),
            new FakeSettings(),
            NullLogger<LocalChecklistStore>.Instance);

    /// <summary>Sessão do servidor com UM identificador próprio para a célula e para o marcador.</summary>
    private static SessionStateDto EstadoDoServidor(
        Guid sessaoId,
        Guid idDaMarcacaoNoServidor,
        Guid idDoMarcadorNoServidor,
        DateTime agora) =>
        new(
            new OperationalSessionDto(sessaoId, Setor, DateOnly.FromDateTime(agora), agora, null, nameof(SessionStatus.Open), 1),
            [Leito],
            [new ChecklistEntryDto(idDaMarcacaoNoServidor, sessaoId, Leito, Tipo, Coluna, true, 5, agora)],
            [new SessionBedMarkerDto(idDoMarcadorNoServidor, sessaoId, Leito, Marcador, true, 3, agora)]);

    /// <summary>
    /// O cenário exato do campo: o aparelho já tem a marcação com o Id dele, e o servidor manda a
    /// mesma célula com outro Id.
    /// </summary>
    [Fact]
    public async Task Mesma_celula_com_identificadores_diferentes_nao_duplica()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.IsReachable = true;

        var sessao = Guid.CreateVersion7();
        var agora = host.Clock.UtcNow;

        // O aparelho criou a marcação offline, com identificador próprio.
        await using (var preparo = host.CreateContext())
        {
            await new OutboxWriter(preparo).ToggleEntryAsync(sessao, Leito, Tipo, Coluna, true, agora);
            await new OutboxWriter(preparo).ToggleMarkerAsync(sessao, Leito, Marcador, true, agora);
        }

        // O servidor devolve a MESMA célula com outro identificador.
        host.Api.SessionState = EstadoDoServidor(sessao, Guid.CreateVersion7(), Guid.CreateVersion7(), agora);

        await using var db = host.CreateContext();

        // Antes da correção esta chamada lançava DbUpdateException com violação de unicidade.
        await CreateStore(host, db).GetBoardAsync(Setor, Tipo);

        await using var conferencia = host.CreateContext();

        Assert.Equal(1, await conferencia.ChecklistEntries.CountAsync());
        Assert.Equal(1, await conferencia.SessionBedMarkers.CountAsync());
    }

    /// <summary>O estado do servidor tem de prevalecer, e não apenas deixar de estourar.</summary>
    [Fact]
    public async Task Estado_do_servidor_prevalece_sobre_o_local()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.IsReachable = true;

        var sessao = Guid.CreateVersion7();
        var agora = host.Clock.UtcNow;

        await using (var preparo = host.CreateContext())
        {
            // Local diz "não feito"; o servidor dirá "feito", com versão mais alta.
            await new OutboxWriter(preparo).ToggleEntryAsync(sessao, Leito, Tipo, Coluna, false, agora);
        }

        host.Api.SessionState = EstadoDoServidor(sessao, Guid.CreateVersion7(), Guid.CreateVersion7(), agora);

        await using var db = host.CreateContext();
        await CreateStore(host, db).GetBoardAsync(Setor, Tipo);

        await using var conferencia = host.CreateContext();
        var marcacao = await conferencia.ChecklistEntries.SingleAsync();

        Assert.True(marcacao.IsCompleted);
        Assert.Equal(5, marcacao.Version);
    }

    /// <summary>Quando os identificadores coincidem, nada muda — o caminho normal segue igual.</summary>
    [Fact]
    public async Task Identificadores_iguais_continuam_funcionando()
    {
        using var host = await LocalTestHost.CreateAsync();
        host.Api.IsReachable = true;

        var sessao = Guid.CreateVersion7();
        var agora = host.Clock.UtcNow;

        Guid idLocal;

        await using (var preparo = host.CreateContext())
        {
            var marcacao = await new OutboxWriter(preparo).ToggleEntryAsync(sessao, Leito, Tipo, Coluna, true, agora);
            idLocal = marcacao.Id;
        }

        host.Api.SessionState = EstadoDoServidor(sessao, idLocal, Guid.CreateVersion7(), agora);

        await using var db = host.CreateContext();
        await CreateStore(host, db).GetBoardAsync(Setor, Tipo);

        await using var conferencia = host.CreateContext();

        Assert.Equal(1, await conferencia.ChecklistEntries.CountAsync());
        Assert.Equal(idLocal, (await conferencia.ChecklistEntries.SingleAsync()).Id);
    }

    private sealed class FakeTimeZone : Application.Abstractions.IInstitutionTimeZone
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateTime ToLocal(DateTime utc) => utc;

        public DateTime ToUtc(DateTime local) => local;
    }

    private sealed class FakeSettings : Application.Abstractions.IInstitutionSettingsProvider
    {
        public Domain.Settings.InstitutionSettings Current => Domain.Settings.InstitutionSettings.Default;

        public ValueTask<Domain.Settings.InstitutionSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);

        public ValueTask ReloadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
