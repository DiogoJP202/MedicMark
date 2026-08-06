namespace ChecklistPlantao.Domain.Sync;

/// <summary>Decisão do servidor sobre uma operação recebida do cliente.</summary>
public enum MergeDecision
{
    /// <summary>A alteração é aplicada e a versão avança.</summary>
    Apply = 0,

    /// <summary>O servidor já está no estado pedido: sucesso idempotente, nada muda.</summary>
    AlreadyInDesiredState = 1,

    /// <summary>
    /// Conflito: a operação é descartada e o estado atual do servidor volta para o cliente,
    /// que adota esse estado.
    /// </summary>
    KeepServerState = 2,
}

/// <summary>
/// Regras de convergência da sincronização. Estão no domínio, e não no servidor, porque o
/// cliente precisa das mesmas regras para exibir o resultado sem inventar comportamento próprio.
/// </summary>
public static class MergePolicies
{
    /// <summary>
    /// Marcação do checklist: "conclusão vence".
    ///
    /// Marcar é sempre aceito — é a operação que o plantão realmente precisa que não se perca.
    /// Desmarcar só é aceito quando o cliente estava vendo a versão atual; uma operação antiga,
    /// feita offline, nunca apaga silenciosamente uma conclusão mais nova.
    /// </summary>
    public static MergeDecision ResolveChecklistEntry(
        bool serverIsCompleted,
        int serverVersion,
        bool requestedIsCompleted,
        int baseVersion)
    {
        if (serverIsCompleted == requestedIsCompleted)
        {
            return MergeDecision.AlreadyInDesiredState;
        }

        if (requestedIsCompleted)
        {
            return MergeDecision.Apply;
        }

        // A partir daqui: o cliente quer desmarcar algo que no servidor está concluído.
        return baseVersion >= serverVersion ? MergeDecision.Apply : MergeDecision.KeepServerState;
    }

    /// <summary>
    /// Classificações de leito e configurações administrativas: versão manda.
    /// Não há "vencedor" preferencial — uma alteração baseada em versão antiga é recusada e o
    /// cliente recebe o estado mais recente para reapresentar ao usuário.
    /// </summary>
    public static MergeDecision ResolveVersioned(int serverVersion, int baseVersion, bool wouldChangeState)
    {
        if (!wouldChangeState)
        {
            return MergeDecision.AlreadyInDesiredState;
        }

        return baseVersion >= serverVersion ? MergeDecision.Apply : MergeDecision.KeepServerState;
    }
}
