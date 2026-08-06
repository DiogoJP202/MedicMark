using ChecklistPlantao.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Infrastructure.Time;

/// <summary>
/// Converte entre UTC e o fuso configurado da instituição.
///
/// O identificador é IANA (por exemplo <c>America/Sao_Paulo</c>). O .NET resolve IANA no Windows
/// desde a versão 8; ainda assim há um caminho de contingência para o identificador do Windows,
/// porque uma instalação com ICU desabilitado não resolveria o nome IANA.
///
/// Lê <see cref="IInstitutionSettingsProvider.Current"/>, que é cache já carregado — nada aqui
/// bloqueia nem faz I/O.
/// </summary>
public sealed class InstitutionTimeZone(IInstitutionSettingsProvider settings, ILogger<InstitutionTimeZone> logger)
    : IInstitutionTimeZone
{
    private static readonly Dictionary<string, string> IanaToWindowsFallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["America/Sao_Paulo"] = "E. South America Standard Time",
        ["America/Bahia"] = "Bahia Standard Time",
        ["America/Fortaleza"] = "SA Eastern Standard Time",
        ["America/Manaus"] = "Central Brazilian Standard Time",
        ["America/Belem"] = "SA Eastern Standard Time",
        ["America/Rio_Branco"] = "SA Pacific Standard Time",
        ["America/Noronha"] = "UTC-02",
    };

    private string? _resolvedFor;
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;

    public TimeZoneInfo TimeZone
    {
        get
        {
            var configured = settings.Current.TimeZoneId;

            if (!string.Equals(configured, _resolvedFor, StringComparison.Ordinal))
            {
                _timeZone = Resolve(configured, logger);
                _resolvedFor = configured;
            }

            return _timeZone;
        }
    }

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    public DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZone);

    /// <summary>
    /// Resolve o fuso pelo identificador, tentando IANA e depois o equivalente do Windows.
    /// Falhando os dois, cai em UTC e registra erro: é preferível o sistema subir com horário
    /// deslocado e o problema visível na tela de diagnóstico a não subir.
    /// </summary>
    public static TimeZoneInfo Resolve(string timeZoneId, ILogger? logger = null)
    {
        if (TryFind(timeZoneId, out var zone))
        {
            return zone;
        }

        if (IanaToWindowsFallback.TryGetValue(timeZoneId, out var windowsId) && TryFind(windowsId, out zone))
        {
            logger?.LogWarning(
                "Fuso {TimeZoneId} resolvido pelo identificador do Windows {WindowsId}. Verifique o suporte a ICU nesta máquina.",
                timeZoneId,
                windowsId);
            return zone;
        }

        logger?.LogError(
            "Fuso {TimeZoneId} não existe neste sistema. Usando UTC — horários e notificações podem ficar deslocados.",
            timeZoneId);

        return TimeZoneInfo.Utc;
    }

    private static bool TryFind(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
