using ChecklistPlantao.Domain.Common;
using ChecklistPlantao.Domain.Scheduling;

namespace ChecklistPlantao.Domain.Settings;

/// <summary>
/// Configurações gerais da instituição. São editáveis pelo administrador, sincronizadas para os
/// dispositivos e nunca deduzidas do relógio ou do fuso do servidor.
/// </summary>
public sealed record InstitutionSettings
{
    public const string DefaultTimeZoneId = "America/Sao_Paulo";

    /// <summary>Identificador IANA do fuso usado para exibir horários e agendar notificações.</summary>
    public string TimeZoneId { get; init; } = DefaultTimeZoneId;

    public TimeOnly ShiftStart { get; init; } = new(19, 0);

    public TimeOnly ShiftEnd { get; init; } = new(7, 0);

    /// <summary>Janela de recuperação após o fechamento, antes de apagar os dados operacionais.</summary>
    public TimeSpan RetentionAfterClose { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Por quanto tempo um dispositivo aceita login offline depois da última validação online.</summary>
    public TimeSpan OfflineLoginValidity { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Abre a sessão do plantão automaticamente quando alguém entra no setor sem sessão aberta.</summary>
    public bool AutoOpenSession { get; init; } = true;

    /// <summary>Tentativas de senha offline antes de exigir reconexão ao servidor.</summary>
    public int OfflineLoginMaxAttempts { get; init; } = 5;

    public ShiftWindow ShiftWindow => new(ShiftStart, ShiftEnd);

    public static InstitutionSettings Default { get; } = new();

    public InstitutionSettings Validated()
    {
        if (ShiftStart == ShiftEnd)
        {
            throw new DomainRuleException("O início e o fim do plantão não podem ser iguais.");
        }

        if (RetentionAfterClose < TimeSpan.Zero)
        {
            throw new DomainRuleException("A janela de recuperação não pode ser negativa.");
        }

        if (OfflineLoginValidity <= TimeSpan.Zero)
        {
            throw new DomainRuleException("A validade do acesso offline precisa ser maior que zero.");
        }

        if (OfflineLoginMaxAttempts < 1)
        {
            throw new DomainRuleException("O limite de tentativas offline precisa ser pelo menos 1.");
        }

        DomainRuleException.ThrowIfNullOrWhiteSpace(TimeZoneId, "fuso horário");

        return this;
    }
}

/// <summary>Chaves das configurações persistidas em <c>AppSetting</c>.</summary>
public static class AppSettingKeys
{
    public const string TimeZoneId = "instituicao.fusoHorario";
    public const string ShiftStart = "turno.inicio";
    public const string ShiftEnd = "turno.fim";
    public const string RetentionAfterCloseHours = "retencao.horasAposFechamento";
    public const string OfflineLoginValidityDays = "acessoOffline.validadeDias";
    public const string OfflineLoginMaxAttempts = "acessoOffline.tentativasMaximas";
    public const string AutoOpenSession = "sessao.abrirAutomaticamente";
}

/// <summary>Par chave/valor versionado, sincronizado para os dispositivos.</summary>
public sealed class AppSetting : ISyncVersioned
{
    private AppSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    public AppSetting(string key, string value, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(key, "chave da configuração");

        Id = DeterministicIdFor(key);
        Key = key;
        Value = value;
        Version = 1;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Id derivado da chave para que servidor e clientes cheguem ao mesmo identificador sem
    /// precisar sincronizar um mapeamento à parte.
    /// </summary>
    public static Guid DeterministicIdFor(string key)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }

    public bool SetValue(string value, DateTime nowUtc)
    {
        if (string.Equals(Value, value, StringComparison.Ordinal))
        {
            return false;
        }

        Value = value;
        Version++;
        UpdatedAtUtc = nowUtc;
        return true;
    }
}
