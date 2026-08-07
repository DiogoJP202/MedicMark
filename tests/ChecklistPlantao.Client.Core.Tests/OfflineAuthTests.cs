using System.Text;
using ChecklistPlantao.Client.Core.Auth;
using ChecklistPlantao.Client.Core.Persistence;

namespace ChecklistPlantao.Client.Core.Tests;

/// <summary>
/// O acesso offline precisa ser possível sem jamais guardar a senha nem receber o hash do
/// servidor. Estes testes fixam esse contrato.
/// </summary>
public sealed class OfflineAuthTests
{
    private static readonly DateTime Agora = new(2026, 8, 6, 20, 0, 0, DateTimeKind.Utc);

    // Poucas iterações apenas nos testes: a suíte não deve levar minutos.
    private static readonly OfflineAuthOptions Opcoes = new() { Iterations = 1_000 };

    private static LocalCredential Credencial(string senha)
    {
        var (salt, verifier, iteracoes) = OfflineCredentialFactory.Derive(senha, Opcoes);

        return new LocalCredential(
            Guid.CreateVersion7(), "maria", "Maria", salt, verifier, iteracoes, "{}", Agora);
    }

    [Fact]
    public void Senha_correta_e_aceita()
    {
        var credencial = Credencial("Plantao123");

        Assert.True(OfflineCredentialFactory.Verify("Plantao123", credencial));
    }

    [Fact]
    public void Senha_errada_e_recusada()
    {
        var credencial = Credencial("Plantao123");

        Assert.False(OfflineCredentialFactory.Verify("Plantao124", credencial));
        Assert.False(OfflineCredentialFactory.Verify(string.Empty, credencial));
    }

    [Fact]
    public void A_senha_nao_aparece_em_lugar_nenhum_do_registro()
    {
        const string senha = "SenhaSecreta9";
        var credencial = Credencial(senha);

        var bytesDaSenha = Encoding.UTF8.GetBytes(senha);

        Assert.DoesNotContain(senha, Convert.ToBase64String(credencial.Verifier), StringComparison.Ordinal);
        Assert.False(credencial.Verifier.AsSpan().IndexOf(bytesDaSenha) >= 0);
        Assert.False(credencial.Salt.AsSpan().IndexOf(bytesDaSenha) >= 0);
    }

    [Fact]
    public void Cada_dispositivo_gera_sal_e_verificador_diferentes_para_a_mesma_senha()
    {
        var aparelhoA = Credencial("MesmaSenha1");
        var aparelhoB = Credencial("MesmaSenha1");

        Assert.NotEqual(aparelhoA.Salt, aparelhoB.Salt);
        Assert.NotEqual(aparelhoA.Verifier, aparelhoB.Verifier);

        // Ainda assim, cada um valida a própria senha.
        Assert.True(OfflineCredentialFactory.Verify("MesmaSenha1", aparelhoA));
        Assert.True(OfflineCredentialFactory.Verify("MesmaSenha1", aparelhoB));
    }

    [Fact]
    public void Acesso_offline_vale_dentro_do_prazo_e_expira_depois()
    {
        var credencial = Credencial("Plantao123");
        var validade = TimeSpan.FromDays(7);

        Assert.True(credencial.IsOfflineAccessValid(Agora.AddDays(6), validade));
        Assert.True(credencial.IsOfflineAccessValid(Agora.AddDays(7), validade));
        Assert.False(credencial.IsOfflineAccessValid(Agora.AddDays(8), validade));
    }

    [Fact]
    public void Tentativas_seguidas_bloqueiam_o_acesso_local()
    {
        var credencial = Credencial("Plantao123");
        var bloqueio = TimeSpan.FromMinutes(15);

        for (var i = 0; i < 4; i++)
        {
            credencial.RegisterFailure(5, Agora, bloqueio);
        }

        Assert.False(credencial.IsLocked(Agora));

        credencial.RegisterFailure(5, Agora, bloqueio);

        Assert.True(credencial.IsLocked(Agora));
        Assert.True(credencial.IsLocked(Agora.AddMinutes(14)));
        Assert.False(credencial.IsLocked(Agora.AddMinutes(16)));
    }

    [Fact]
    public void Entrada_bem_sucedida_zera_o_contador_de_tentativas()
    {
        var credencial = Credencial("Plantao123");
        credencial.RegisterFailure(5, Agora, TimeSpan.FromMinutes(15));
        credencial.RegisterFailure(5, Agora, TimeSpan.FromMinutes(15));

        credencial.RegisterSuccess();

        Assert.Equal(0, credencial.FailedAttempts);
        Assert.False(credencial.IsLocked(Agora));
    }

    [Fact]
    public void Sincronizacao_renova_a_validade_e_o_snapshot_de_permissoes()
    {
        var credencial = Credencial("Plantao123");
        credencial.RegisterFailure(5, Agora, TimeSpan.FromMinutes(15));

        var depois = Agora.AddDays(3);
        credencial.RefreshFromServer("Maria Silva", "{\"permissoes\":[\"checklist.view\"]}", depois);

        Assert.Equal(depois, credencial.LastServerValidationUtc);
        Assert.Equal("Maria Silva", credencial.DisplayName);
        Assert.Contains("checklist.view", credencial.PermissionsSnapshot, StringComparison.Ordinal);
        Assert.Equal(0, credencial.FailedAttempts);
    }

    [Fact]
    public void Trocar_a_senha_no_servidor_atualiza_o_verificador_local()
    {
        var credencial = Credencial("SenhaAntiga1");

        var (salt, verifier, iteracoes) = OfflineCredentialFactory.Derive("SenhaNova2", Opcoes);
        credencial.UpdateVerifier(salt, verifier, iteracoes);

        Assert.False(OfflineCredentialFactory.Verify("SenhaAntiga1", credencial));
        Assert.True(OfflineCredentialFactory.Verify("SenhaNova2", credencial));
    }
}
