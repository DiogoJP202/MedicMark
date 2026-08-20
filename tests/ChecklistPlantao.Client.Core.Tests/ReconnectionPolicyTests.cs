using ChecklistPlantao.Client.Core.Sync;
using Microsoft.AspNetCore.SignalR.Client;

namespace ChecklistPlantao.Client.Core.Tests;

/// <summary>
/// A política de reconexão do hub existe por causa de um defeito de campo: o
/// <c>WithAutomaticReconnect</c> sem argumentos tenta em 0s, 2s, 10s e 30s e **desiste**. Um
/// servidor fora do ar por mais de um minuto — reinício, atualização, queda de rede no corredor —
/// deixava o aparelho mudo até alguém sair e entrar da conta.
///
/// A medição de cobertura mostrou esta classe em **0%**: ela nasceu para consertar um defeito e
/// nada verificava que continua fazendo isso. O modo de falhar é silencioso — devolver
/// <c>null</c> significa "desisti", e ninguém perceberia até um aparelho parar de receber avisos.
/// </summary>
public sealed class ReconnectionPolicyTests
{
    private static TimeSpan? Espera(long tentativasAnteriores) =>
        new ReconexaoSemDesistir().NextRetryDelay(
            new RetryContext { PreviousRetryCount = tentativasAnteriores, ElapsedTime = TimeSpan.Zero });

    /// <summary>O invariante que dá nome à classe.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(10_000)]
    public void Nunca_desiste(long tentativas)
    {
        Assert.NotNull(Espera(tentativas));
    }

    /// <summary>A primeira tentativa é imediata: a maioria das quedas é um soluço de rede.</summary>
    [Fact]
    public void Primeira_tentativa_e_imediata()
    {
        Assert.Equal(TimeSpan.Zero, Espera(0));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 30)]
    public void Espera_cresce_nas_primeiras_tentativas(long tentativa, int segundos)
    {
        Assert.Equal(TimeSpan.FromSeconds(segundos), Espera(tentativa));
    }

    /// <summary>
    /// Estabiliza em um minuto: insistir mais rápido gastaria bateria sem ganhar nada, e esperar
    /// mais faria o aparelho demorar a voltar quando o servidor voltasse.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(100)]
    public void Estabiliza_em_um_minuto(long tentativa)
    {
        Assert.Equal(TimeSpan.FromMinutes(1), Espera(tentativa));
    }

    /// <summary>A espera nunca encolhe — senão viraria laço apertado sob falha prolongada.</summary>
    [Fact]
    public void Espera_nunca_diminui()
    {
        var anterior = TimeSpan.Zero;

        for (long tentativa = 0; tentativa < 20; tentativa++)
        {
            var atual = Espera(tentativa);

            Assert.NotNull(atual);
            Assert.True(atual >= anterior, $"A espera diminuiu na tentativa {tentativa}.");

            anterior = atual.Value;
        }
    }
}
