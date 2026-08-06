namespace ChecklistPlantao.Domain.Common;

/// <summary>
/// Violação de uma invariante do domínio. Mensagens já em português, prontas para exibição —
/// o domínio não conhece camadas de tradução e o público do sistema é local.
/// </summary>
public sealed class DomainRuleException : Exception
{
    public DomainRuleException(string message)
        : base(message)
    {
    }

    public DomainRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static void ThrowIfNullOrWhiteSpace(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleException($"O campo {fieldName} é obrigatório.");
        }
    }
}
