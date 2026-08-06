using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Scheduling;
using ChecklistPlantao.Domain.Settings;

namespace ChecklistPlantao.Application.Abstractions;

/// <summary>
/// Fonte do tempo. Existe para que as regras de turno, retenção e notificação sejam testáveis
/// sem depender do relógio da máquina.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>Relógio real. Único ponto do sistema que chama <see cref="DateTime.UtcNow"/>.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Conversão entre UTC (persistência) e o fuso da instituição (exibição e agendamento).
/// Nunca use o fuso do servidor nem o do aparelho para calcular horário operacional.
/// </summary>
public interface IInstitutionTimeZone
{
    TimeZoneInfo TimeZone { get; }

    DateTime ToLocal(DateTime utc);

    DateTime ToUtc(DateTime local);

    DateTime LocalNow(IClock clock) => ToLocal(clock.UtcNow);
}

/// <summary>
/// Acesso às configurações vigentes, já convertidas para o tipo forte do domínio.
///
/// <see cref="Current"/> é síncrono de propósito: componentes que formatam horário são chamados
/// de dentro de render de interface, onde não cabe await. O valor é carregado uma vez na
/// inicialização e recarregado quando o administrador altera algo, então ler o cache é seguro.
/// </summary>
public interface IInstitutionSettingsProvider
{
    /// <summary>Últimas configurações carregadas. Antes do primeiro carregamento vale o padrão.</summary>
    InstitutionSettings Current { get; }

    /// <summary>Garante que o cache foi carregado ao menos uma vez.</summary>
    ValueTask<InstitutionSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Recarrega imediatamente. Chamado após uma alteração administrativa.</summary>
    ValueTask ReloadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Quem está chamando. No servidor vem do JWT; no cliente vem da sessão local.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    string UserName { get; }

    string DisplayName { get; }

    EffectiveAccess Access { get; }
}

/// <summary>
/// Resolve a janela de plantão a aplicar, respeitando a sobreposição por setor.
/// </summary>
public static class ShiftResolver
{
    public static ShiftWindow For(InstitutionSettings settings, Domain.Structure.Sector? sector)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (sector?.ShiftStart is { } start && sector.ShiftEnd is { } end)
        {
            return new ShiftWindow(start, end);
        }

        return settings.ShiftWindow;
    }
}
