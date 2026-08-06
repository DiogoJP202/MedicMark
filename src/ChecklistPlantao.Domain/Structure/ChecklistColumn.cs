using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Structure;

/// <summary>
/// Coluna (momento de execução) de um tipo de checklist. "20H", "Jantar", "PM" são nomes exibidos;
/// <see cref="TriggerTime"/> é a hora real usada para agendar a notificação.
/// </summary>
public sealed class ChecklistColumn : ISyncVersioned
{
    private ChecklistColumn()
    {
        DisplayName = string.Empty;
    }

    internal ChecklistColumn(Guid id, Guid templateId, string displayName, TimeOnly? triggerTime, int sortOrder, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(displayName, "nome da coluna");

        Id = id;
        ChecklistTemplateId = templateId;
        DisplayName = displayName.Trim();
        TriggerTime = triggerTime;
        SortOrder = sortOrder;
        IsActive = true;
        NotificationEnabled = triggerTime.HasValue;
        GracePeriodMinutes = DefaultGracePeriodMinutes;
        RepeatIntervalMinutes = DefaultRepeatIntervalMinutes;
        MaximumRepeats = DefaultMaximumRepeats;
        AllowSnooze = true;
        SnoozeMinutes = DefaultSnoozeMinutes;
        Version = 1;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public const int DefaultGracePeriodMinutes = 15;
    public const int DefaultRepeatIntervalMinutes = 10;
    public const int DefaultMaximumRepeats = 3;
    public const int DefaultSnoozeMinutes = 5;

    public Guid Id { get; private set; }

    public Guid ChecklistTemplateId { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>
    /// Hora real da coluna no fuso da instituição. Nulo significa coluna sem horário —
    /// aparece no checklist mas nunca gera notificação.
    /// </summary>
    public TimeOnly? TriggerTime { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public bool NotificationEnabled { get; private set; }

    /// <summary>Minutos de antecedência opcionais: avisa antes da hora cheia.</summary>
    public int LeadTimeMinutes { get; private set; }

    /// <summary>Tolerância antes de considerar a coluna atrasada e começar a repetir o alerta.</summary>
    public int GracePeriodMinutes { get; private set; }

    public int RepeatIntervalMinutes { get; private set; }

    public int MaximumRepeats { get; private set; }

    public bool AllowSnooze { get; private set; }

    public int SnoozeMinutes { get; private set; }

    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Só faz sentido agendar quando a coluna está ativa, tem hora e a notificação está ligada.
    /// A tela de configuração usa isto para explicar por que uma coluna não alerta.
    /// </summary>
    public bool IsSchedulable => IsActive && NotificationEnabled && TriggerTime.HasValue;

    public void Update(string displayName, TimeOnly? triggerTime, int sortOrder, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(displayName, "nome da coluna");

        DisplayName = displayName.Trim();
        TriggerTime = triggerTime;
        SortOrder = sortOrder;

        if (!triggerTime.HasValue)
        {
            NotificationEnabled = false;
        }

        Touch(nowUtc);
    }

    public void ConfigureNotification(
        bool enabled,
        int leadTimeMinutes,
        int gracePeriodMinutes,
        int repeatIntervalMinutes,
        int maximumRepeats,
        bool allowSnooze,
        int snoozeMinutes,
        DateTime nowUtc)
    {
        if (enabled && !TriggerTime.HasValue)
        {
            throw new DomainRuleException("Defina o horário da coluna antes de habilitar a notificação.");
        }

        ThrowIfNegative(leadTimeMinutes, "antecedência");
        ThrowIfNegative(gracePeriodMinutes, "tolerância");
        ThrowIfNegative(maximumRepeats, "quantidade máxima de repetições");

        if (maximumRepeats > 0 && repeatIntervalMinutes <= 0)
        {
            throw new DomainRuleException("O intervalo de repetição precisa ser maior que zero quando há repetições.");
        }

        if (allowSnooze && snoozeMinutes <= 0)
        {
            throw new DomainRuleException("Os minutos do adiamento precisam ser maiores que zero.");
        }

        NotificationEnabled = enabled;
        LeadTimeMinutes = leadTimeMinutes;
        GracePeriodMinutes = gracePeriodMinutes;
        RepeatIntervalMinutes = repeatIntervalMinutes;
        MaximumRepeats = maximumRepeats;
        AllowSnooze = allowSnooze;
        SnoozeMinutes = snoozeMinutes;
        Touch(nowUtc);
    }

    public void SetActive(bool isActive, DateTime nowUtc)
    {
        if (IsActive == isActive)
        {
            return;
        }

        IsActive = isActive;
        Touch(nowUtc);
    }

    private static void ThrowIfNegative(int value, string fieldName)
    {
        if (value < 0)
        {
            throw new DomainRuleException($"O campo {fieldName} não pode ser negativo.");
        }
    }

    private void Touch(DateTime nowUtc)
    {
        Version++;
        UpdatedAtUtc = nowUtc;
    }
}
