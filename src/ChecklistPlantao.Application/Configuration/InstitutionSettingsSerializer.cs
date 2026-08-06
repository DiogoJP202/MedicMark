using System.Globalization;
using ChecklistPlantao.Domain.Common;
using ChecklistPlantao.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Application.Configuration;

/// <summary>
/// Conversão entre <see cref="InstitutionSettings"/> e os pares chave/valor persistidos.
///
/// Existe um único lugar com esta tradução porque servidor e cliente precisam concordar
/// exatamente sobre o formato de cada valor — um "19:00" lido como "19:00:00" em um dos lados
/// deslocaria todo o agendamento.
/// </summary>
public static class InstitutionSettingsSerializer
{
    public const string TimeFormat = "HH:mm";

    public static IReadOnlyDictionary<string, string> Flatten(InstitutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppSettingKeys.TimeZoneId] = settings.TimeZoneId,
            [AppSettingKeys.ShiftStart] = settings.ShiftStart.ToString(TimeFormat, CultureInfo.InvariantCulture),
            [AppSettingKeys.ShiftEnd] = settings.ShiftEnd.ToString(TimeFormat, CultureInfo.InvariantCulture),
            [AppSettingKeys.RetentionAfterCloseHours] = ((int)settings.RetentionAfterClose.TotalHours).ToString(CultureInfo.InvariantCulture),
            [AppSettingKeys.OfflineLoginValidityDays] = ((int)settings.OfflineLoginValidity.TotalDays).ToString(CultureInfo.InvariantCulture),
            [AppSettingKeys.OfflineLoginMaxAttempts] = settings.OfflineLoginMaxAttempts.ToString(CultureInfo.InvariantCulture),
            [AppSettingKeys.AutoOpenSession] = settings.AutoOpenSession ? "true" : "false",
        };
    }

    /// <summary>
    /// Um valor inválido não derruba o carregamento: registra aviso e mantém o padrão daquele
    /// campo. Subir com uma configuração parcialmente padrão é melhor do que não subir.
    /// </summary>
    public static InstitutionSettings Materialize(IReadOnlyDictionary<string, string> stored, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var defaults = InstitutionSettings.Default;

        var settings = new InstitutionSettings
        {
            TimeZoneId = ReadString(stored, AppSettingKeys.TimeZoneId, defaults.TimeZoneId),
            ShiftStart = ReadTime(stored, AppSettingKeys.ShiftStart, defaults.ShiftStart, logger),
            ShiftEnd = ReadTime(stored, AppSettingKeys.ShiftEnd, defaults.ShiftEnd, logger),
            RetentionAfterClose = TimeSpan.FromHours(
                ReadInt(stored, AppSettingKeys.RetentionAfterCloseHours, (int)defaults.RetentionAfterClose.TotalHours, logger)),
            OfflineLoginValidity = TimeSpan.FromDays(
                ReadInt(stored, AppSettingKeys.OfflineLoginValidityDays, (int)defaults.OfflineLoginValidity.TotalDays, logger)),
            OfflineLoginMaxAttempts = ReadInt(stored, AppSettingKeys.OfflineLoginMaxAttempts, defaults.OfflineLoginMaxAttempts, logger),
            AutoOpenSession = ReadBool(stored, AppSettingKeys.AutoOpenSession, defaults.AutoOpenSession, logger),
        };

        try
        {
            return settings.Validated();
        }
        catch (DomainRuleException ex)
        {
            logger?.LogError(ex, "Configurações inválidas. Usando os valores padrão.");
            return defaults;
        }
    }

    private static string ReadString(IReadOnlyDictionary<string, string> stored, string key, string fallback) =>
        stored.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static TimeOnly ReadTime(IReadOnlyDictionary<string, string> stored, string key, TimeOnly fallback, ILogger? logger)
    {
        if (!stored.TryGetValue(key, out var raw))
        {
            return fallback;
        }

        if (TimeOnly.TryParseExact(raw, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        Warn(logger, key, raw, fallback);
        return fallback;
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> stored, string key, int fallback, ILogger? logger)
    {
        if (!stored.TryGetValue(key, out var raw))
        {
            return fallback;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        Warn(logger, key, raw, fallback);
        return fallback;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string> stored, string key, bool fallback, ILogger? logger)
    {
        if (!stored.TryGetValue(key, out var raw))
        {
            return fallback;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        Warn(logger, key, raw, fallback);
        return fallback;
    }

    private static void Warn<T>(ILogger? logger, string key, string raw, T fallback) =>
        logger?.LogWarning("Configuração {Chave} com valor inválido ({Valor}). Usando {Padrao}.", key, raw, fallback);
}
