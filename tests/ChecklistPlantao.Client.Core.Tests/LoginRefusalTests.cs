using ChecklistPlantao.Client.Core.Auth;
using ChecklistPlantao.Client.Core.Services;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChecklistPlantao.Client.Core.Tests;

/// <summary>
/// Quando o SERVIDOR recusa a entrada, o motivo dele precisa chegar ao usuário.
///
/// O defeito apareceu no aparelho: a conta ficou bloqueada por tentativas, o servidor respondeu
/// 423 com "Esta conta está temporariamente bloqueada", e o aplicativo descartava a resposta,
/// caía no caminho offline e exibia "Este usuário ainda não entrou neste dispositivo". A pessoa
/// ficava sem saber o que fazer — a informação que resolveria o problema existia e era jogada fora.
/// </summary>
public sealed class LoginRefusalTests
{
    private static ClientSession CreateSession(LocalTestHost host, Persistence.LocalDbContext db) =>
        new(db,
            host.Api,
            new FakeTokenStore(),
            host.Clock,
            new FakeSettings(),
            Options.Create(new OfflineAuthOptions { Iterations = 1_000 }),
            NullLogger<ClientSession>.Instance);

    [Fact]
    public async Task Conta_bloqueada_mostra_o_motivo_do_servidor()
    {
        using var host = await LocalTestHost.CreateAsync();

        host.Api.LoginResult = ServerLoginResult.Refused(
            ApiErrorCodes.AccountLocked,
            "Esta conta está temporariamente bloqueada por excesso de tentativas. Aguarde alguns minutos.");

        await using var db = host.CreateContext();
        var sessao = CreateSession(host, db);

        var entrou = await sessao.SignInAsync("admin", "SenhaQualquer1");

        Assert.False(entrou);
        Assert.Contains("bloqueada", sessao.LastSignInError, StringComparison.OrdinalIgnoreCase);

        // O erro do caminho offline NÃO pode aparecer: o servidor respondeu, e a resposta é dele.
        Assert.DoesNotContain("ainda não entrou neste dispositivo", sessao.LastSignInError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Senha_errada_mostra_o_motivo_do_servidor()
    {
        using var host = await LocalTestHost.CreateAsync();

        host.Api.LoginResult = ServerLoginResult.Refused(ApiErrorCodes.InvalidCredentials, "Usuário ou senha inválidos.");

        await using var db = host.CreateContext();
        var sessao = CreateSession(host, db);

        Assert.False(await sessao.SignInAsync("admin", "errada1"));
        Assert.Equal("Usuário ou senha inválidos.", sessao.LastSignInError);
    }

    [Fact]
    public async Task Usuario_desativado_mostra_o_motivo_do_servidor()
    {
        using var host = await LocalTestHost.CreateAsync();

        host.Api.LoginResult = ServerLoginResult.Refused(
            ApiErrorCodes.AccountInactive, "Este usuário está desativado. Procure o administrador.");

        await using var db = host.CreateContext();
        var sessao = CreateSession(host, db);

        Assert.False(await sessao.SignInAsync("maria", "Senha12345"));
        Assert.Contains("desativado", sessao.LastSignInError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Servidor_inalcancavel_cai_no_caminho_offline()
    {
        using var host = await LocalTestHost.CreateAsync();

        // Sem resposta do servidor: aí sim o acesso offline é a tentativa correta.
        host.Api.LoginResult = ServerLoginResult.Unreachable();

        await using var db = host.CreateContext();
        var sessao = CreateSession(host, db);

        Assert.False(await sessao.SignInAsync("nunca-entrou", "Senha12345"));
        Assert.Contains("primeiro acesso", sessao.LastSignInError, StringComparison.OrdinalIgnoreCase);
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
