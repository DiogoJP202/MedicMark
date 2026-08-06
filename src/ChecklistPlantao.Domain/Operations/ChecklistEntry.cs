using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Operations;

/// <summary>
/// Uma célula do checklist: o cruzamento de sessão, leito, tipo de checklist e coluna.
///
/// Deliberadamente NÃO existe campo de usuário aqui. O cliente foi explícito: não quer saber
/// quem marcou. Acrescentar autoria depois seria mudança de requisito, não detalhe técnico.
/// </summary>
public sealed class ChecklistEntry : ISyncVersioned
{
    private ChecklistEntry()
    {
    }

    public ChecklistEntry(
        Guid id,
        Guid sessionId,
        Guid bedId,
        Guid checklistTemplateId,
        Guid checklistColumnId,
        DateTime nowUtc)
    {
        Id = id;
        SessionId = sessionId;
        BedId = bedId;
        ChecklistTemplateId = checklistTemplateId;
        ChecklistColumnId = checklistColumnId;
        IsCompleted = false;
        Version = 1;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid BedId { get; private set; }

    public Guid ChecklistTemplateId { get; private set; }

    public Guid ChecklistColumnId { get; private set; }

    public bool IsCompleted { get; private set; }

    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Aplica o estado desejado. Devolve <c>false</c> quando já estava assim — nesse caso nada
    /// é alterado e a versão não avança, o que torna o reenvio da mesma operação inofensivo.
    /// </summary>
    public bool SetCompletion(bool completed, DateTime nowUtc)
    {
        if (IsCompleted == completed)
        {
            return false;
        }

        IsCompleted = completed;
        Version++;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>Usado pela sincronização para adotar o estado autoritativo vindo do servidor.</summary>
    public void OverwriteFromServer(bool completed, int version, DateTime updatedAtUtc)
    {
        if (version <= 0)
        {
            throw new DomainRuleException("A versão vinda do servidor precisa ser positiva.");
        }

        IsCompleted = completed;
        Version = version;
        UpdatedAtUtc = updatedAtUtc;
    }
}
