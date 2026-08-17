using ChecklistPlantao.Client.Core.Auth;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChecklistPlantao.Client.Core.Tests;

/// <summary>
/// Entrar é a única porta do aplicativo. Uma falha de gravação no banco local subia até o topo e
/// derrubava tudo — no aparelho aparecia "o aplicativo precisa ser reiniciado", em toda tentativa,
/// e a causa real (a exceção interna do SQLite) morria junto.
///
/// A regra que estes testes fixam: a entrada nunca lança. Ela recusa, explica e guarda o detalhe
/// técnico separado, para quem vai resolver.
/// </summary>
public sealed class LoginStorageFailureTests
{
    private static ClientSession CreateSession(LocalTestHost host, LocalDbContext db) =>
        new(db,
            host.Api,
            new FakeTokenStore(),
            host.Clock,
            new FakeSettings(),
            new AuthenticatedSessionState(),
            Options.Create(new OfflineAuthOptions { Iterations = 1_000 }),
            NullLogger<ClientSession>.Instance);

    /// <summary>
    /// O servidor foi reconstruído (ou restaurado de um backup) e passou a usar outro identificador
    /// para a mesma pessoa. O nome de usuário é único no banco local, então inserir a credencial
    /// nova colide com a antiga.
    /// </summary>
    private static ServerLoginResult LoginComOutroIdentificador(string userName) =>
        ServerLoginResult.Success(new LoginResponse(
            new TokenPairDto("acesso", DateTime.UtcNow.AddMinutes(30), "renovacao", DateTime.UtcNow.AddDays(7)),
            new AuthenticatedUserDto(Guid.CreateVersion7(), userName, "Administrador", ["checklist.mark"], true, [], [])));

    [Fact]
    public async Task Falha_de_gravacao_local_nao_derruba_a_entrada()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using (var preparo = host.CreateContext())
        {
            preparo.Credentials.Add(new LocalCredential(
                Guid.CreateVersion7(), "admin", "Administrador",
                [1, 2, 3], [4, 5, 6], 1_000, "{}", host.Clock.UtcNow));

            await preparo.SaveChangesAsync();
        }

        host.Api.LoginResult = LoginComOutroIdentificador("admin");

        await using var db = host.CreateContext();
        var sessao = CreateSession(host, db);

        // Antes desta correção, esta linha lançava DbUpdateException.
        var entrou = await sessao.SignInAsync("admin", "Senha12345");

        Assert.False(entrou);
        Assert.False(sessao.IsAuthenticated);
        Assert.NotNull(sessao.LastSignInError);
    }

    [Fact]
    public async Task Falha_de_gravacao_separa_a_mensagem_do_detalhe_tecnico()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using (var preparo = host.CreateContext())
        {
            preparo.Credentials.Add(new LocalCredential(
                Guid.CreateVersion7(), "maria", "Maria", [1], [2], 1_000, "{}", host.Clock.UtcNow));

            await preparo.SaveChangesAsync();
        }

        host.Api.LoginResult = LoginComOutroIdentificador("maria");

        await using var db = host.CreateContext();
        var sessao = CreateSession(host, db);

        Assert.False(await sessao.SignInAsync("maria", "Senha12345"));

        // Para quem está no plantão: o que aconteceu e o que fazer, sem jargão.
        Assert.Contains("neste aparelho", sessao.LastSignInError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLite", sessao.LastSignInError, StringComparison.OrdinalIgnoreCase);

        // Para quem vai resolver: a causa real, incluindo a entidade envolvida.
        Assert.NotNull(sessao.LastSignInErrorDetail);
        Assert.Contains("LocalCredential", sessao.LastSignInErrorDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entrada_bem_sucedida_nao_deixa_detalhe_tecnico_para_tras()
    {
        using var host = await LocalTestHost.CreateAsync();

        // Primeiro uma falha, para sujar o estado.
        await using (var preparo = host.CreateContext())
        {
            preparo.Credentials.Add(new LocalCredential(
                Guid.CreateVersion7(), "admin", "Administrador", [1], [2], 1_000, "{}", host.Clock.UtcNow));

            await preparo.SaveChangesAsync();
        }

        host.Api.LoginResult = LoginComOutroIdentificador("admin");

        await using var db = host.CreateContext();
        var sessao = CreateSession(host, db);

        Assert.False(await sessao.SignInAsync("admin", "Senha12345"));
        Assert.NotNull(sessao.LastSignInErrorDetail);

        // Agora uma tentativa que o servidor recusa: o detalhe anterior não pode sobreviver e
        // confundir o diagnóstico da falha seguinte.
        host.Api.LoginResult = ServerLoginResult.Refused(
            Contracts.Common.ApiErrorCodes.InvalidCredentials, "Usuário ou senha inválidos.");

        Assert.False(await sessao.SignInAsync("admin", "outra"));
        Assert.Equal("Usuário ou senha inválidos.", sessao.LastSignInError);
        Assert.Null(sessao.LastSignInErrorDetail);
    }

    /// <summary>
    /// A espiral observada no aparelho: uma gravação falha, as entidades ficam presas no
    /// rastreador, e como o contexto do MAUI Blazor Hybrid vive enquanto o aplicativo viver,
    /// TODA gravação seguinte falha — inclusive marcar um leito, que nada tem a ver com a
    /// primeira. O aparelho só voltava a funcionar sendo reinstalado.
    /// </summary>
    [Fact]
    public async Task Gravacao_que_falha_nao_deixa_o_contexto_inutilizado()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using (var preparo = host.CreateContext())
        {
            preparo.Credentials.Add(new LocalCredential(
                Guid.CreateVersion7(), "admin", "Administrador", [1], [2], 1_000, "{}", host.Clock.UtcNow));

            await preparo.SaveChangesAsync();
        }

        host.Api.LoginResult = LoginComOutroIdentificador("admin");

        await using var db = host.CreateContext();
        var sessao = CreateSession(host, db);

        Assert.False(await sessao.SignInAsync("admin", "Senha12345"));

        // O rastreador precisa estar limpo: o que estava nele não chegou ao disco.
        Assert.Empty(db.ChangeTracker.Entries());

        // E o MESMO contexto precisa continuar servindo. Aqui está o que o plantão perdia.
        var escritor = new OutboxWriter(db);

        var marcacao = await escritor.ToggleEntryAsync(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            isCompleted: true, host.Clock.UtcNow);

        Assert.True(marcacao.IsCompleted);
        Assert.Equal(1, await escritor.PendingCountAsync());
    }

    /// <summary>
    /// Duas marcações na mesma célula antes de a primeira chegar ao disco. A consulta ao banco
    /// não enxerga o que só existe no rastreador — o mesmo defeito que derrubava a sincronização
    /// em lote no servidor, e que aqui aparecia como "cannot be tracked".
    /// </summary>
    [Fact]
    public async Task Duas_marcacoes_na_mesma_celula_nao_criam_duas_entidades()
    {
        using var host = await LocalTestHost.CreateAsync();

        await using var db = host.CreateContext();
        var escritor = new OutboxWriter(db);

        var sessao = Guid.CreateVersion7();
        var leito = Guid.CreateVersion7();
        var tipo = Guid.CreateVersion7();
        var coluna = Guid.CreateVersion7();

        var primeira = await escritor.ToggleEntryAsync(sessao, leito, tipo, coluna, true, host.Clock.UtcNow);
        var segunda = await escritor.ToggleEntryAsync(sessao, leito, tipo, coluna, false, host.Clock.UtcNow);

        Assert.Equal(primeira.Id, segunda.Id);
        Assert.False(segunda.IsCompleted);
        Assert.Equal(1, await db.ChecklistEntries.CountAsync());
    }

    private sealed class FakeTokenStore : ITokenStore
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task SaveAsync(string accessToken, DateTime accessExpiresAtUtc, string refreshToken, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettings : Application.Abstractions.IInstitutionSettingsProvider
    {
        public Domain.Settings.InstitutionSettings Current => Domain.Settings.InstitutionSettings.Default;

        public ValueTask<Domain.Settings.InstitutionSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);

        public ValueTask ReloadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
