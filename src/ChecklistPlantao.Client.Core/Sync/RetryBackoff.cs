namespace ChecklistPlantao.Client.Core.Sync;

/// <summary>
/// Espera crescente entre tentativas de envio.
///
/// Escrito à mão em vez de trazer uma biblioteca de resiliência: são poucas linhas e o
/// requisito é explícito — nada de retry agressivo consumindo bateria ou sobrecarregando o
/// servidor. O jitter evita que todos os aparelhos do plantão voltem a tentar no mesmo instante
/// quando a rede retorna.
/// </summary>
public static class RetryBackoff
{
    public static readonly TimeSpan First = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan Max = TimeSpan.FromMinutes(30);

    public static TimeSpan For(int retryCount, Random? random = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retryCount);

        // Dobra a cada falha, com teto. O expoente é limitado para não estourar o double.
        var expoente = Math.Min(retryCount, 12);
        var segundos = First.TotalSeconds * Math.Pow(2, expoente);
        var espera = TimeSpan.FromSeconds(Math.Min(segundos, Max.TotalSeconds));

        // Jitter de até 20% para dessincronizar os aparelhos.
        var gerador = random ?? Random.Shared;
        var jitter = espera.TotalSeconds * 0.2 * gerador.NextDouble();

        return espera + TimeSpan.FromSeconds(jitter);
    }

    public static DateTime NextAttempt(int retryCount, DateTime nowUtc, Random? random = null) =>
        nowUtc.Add(For(retryCount, random));
}
