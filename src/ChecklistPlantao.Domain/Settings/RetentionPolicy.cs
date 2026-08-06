using ChecklistPlantao.Domain.Operations;

namespace ChecklistPlantao.Domain.Settings;

/// <summary>
/// Política de retenção dos dados operacionais.
///
/// O cliente não quer histórico permanente de checklist. Depois de fechada, a sessão fica
/// disponível por uma janela curta — para o caso de alguém ter encerrado por engano — e então
/// suas marcações e classificações são apagadas. Cadastros (usuários, grupos, setores, leitos,
/// templates, configurações) nunca são tocados por esta política.
/// </summary>
public sealed record RetentionPolicy(TimeSpan RecoveryWindow)
{
    public static RetentionPolicy Default { get; } = new(TimeSpan.FromHours(24));

    /// <summary>Limpeza imediata: fechou, apagou. Opção disponível ao administrador.</summary>
    public static RetentionPolicy Immediate { get; } = new(TimeSpan.Zero);

    public static RetentionPolicy From(InstitutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new RetentionPolicy(settings.RetentionAfterClose);
    }

    /// <summary>Instante em que os dados da sessão podem ser apagados, ou nulo se ela ainda está aberta.</summary>
    public DateTime? PurgeAtUtc(OperationalSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.ClosedAtUtc?.Add(RecoveryWindow);
    }

    public bool IsPurgeable(OperationalSession session, DateTime nowUtc)
    {
        var purgeAt = PurgeAtUtc(session);
        return purgeAt.HasValue && nowUtc >= purgeAt.Value;
    }

    /// <summary>Ainda dá para reabrir? Verdadeiro enquanto a janela de recuperação não expirou.</summary>
    public bool IsRecoverable(OperationalSession session, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Status == SessionStatus.Closed && !IsPurgeable(session, nowUtc);
    }
}
