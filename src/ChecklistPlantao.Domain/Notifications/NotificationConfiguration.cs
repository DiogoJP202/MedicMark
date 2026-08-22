using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Notifications;

public enum NotificationPriority
{
    Normal = 0,
    High = 1,
    Urgent = 2,
}

/// <summary>
/// Configurações gerais de notificação, comuns a todas as colunas. O que é específico de uma
/// coluna (horário, tolerância, repetição) fica na própria <c>ChecklistColumn</c>.
/// </summary>
public sealed class NotificationConfiguration : ISyncVersioned
{
    /// <summary>
    /// Só existe uma configuração global. O Id é fixo para que servidor e clientes convirjam
    /// sem negociar identificador.
    /// </summary>
    public static readonly Guid SingletonId = new("6d9f0b6c-6f1f-4a2d-9d3b-0d2f5d5a9c11");

    private NotificationConfiguration()
    {
        TitleTemplate = string.Empty;
        BodyTemplate = string.Empty;
    }

    public NotificationConfiguration(DateTime nowUtc)
    {
        Id = SingletonId;
        SoundEnabled = true;
        VibrationEnabled = true;
        Priority = NotificationPriority.High;
        EnabledOnAndroid = true;
        EnabledOnWindows = true;
        AllowFullScreenIntent = false;
        TitleTemplate = DefaultTitleTemplate;
        BodyTemplate = DefaultBodyTemplate;
        Version = 1;
        UpdatedAtUtc = nowUtc;
    }

    public const string DefaultTitleTemplate = "ATENÇÃO — {checklist} {coluna}";
    public const string DefaultBodyTemplate = "{pendentes} leito(s) ainda pendente(s) no setor {setor}.";

    public Guid Id { get; private set; }

    public bool SoundEnabled { get; private set; }

    public bool VibrationEnabled { get; private set; }

    public NotificationPriority Priority { get; private set; }

    public bool EnabledOnAndroid { get; private set; }

    public bool EnabledOnWindows { get; private set; }

    /// <summary>
    /// Alerta em tela cheia no Android. Desligado por padrão: exige permissão especial, é
    /// intrusivo e as lojas restringem o uso. Ver docs/NOTIFICATIONS.md.
    /// </summary>
    public bool AllowFullScreenIntent { get; private set; }

    /// <summary>Modelo do título. Marcadores aceitos: {checklist}, {coluna}, {setor}, {pendentes}.</summary>
    public string TitleTemplate { get; private set; }

    public string BodyTemplate { get; private set; }

    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public bool IsEnabledFor(DevicePlatform platform) => platform switch
    {
        // EnabledOnAndroid é mantido no contrato e no banco por compatibilidade. Na interface
        // ele representa o canal móvel, compartilhado por Android e iPhone.
        DevicePlatform.Android or DevicePlatform.Ios => EnabledOnAndroid,
        DevicePlatform.Windows => EnabledOnWindows,
        _ => false,
    };

    public void Update(
        bool soundEnabled,
        bool vibrationEnabled,
        NotificationPriority priority,
        bool enabledOnAndroid,
        bool enabledOnWindows,
        bool allowFullScreenIntent,
        string titleTemplate,
        string bodyTemplate,
        DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(titleTemplate, "modelo do título");
        DomainRuleException.ThrowIfNullOrWhiteSpace(bodyTemplate, "modelo da mensagem");

        SoundEnabled = soundEnabled;
        VibrationEnabled = vibrationEnabled;
        Priority = priority;
        EnabledOnAndroid = enabledOnAndroid;
        EnabledOnWindows = enabledOnWindows;
        AllowFullScreenIntent = allowFullScreenIntent;
        TitleTemplate = titleTemplate.Trim();
        BodyTemplate = bodyTemplate.Trim();
        Version++;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Substitui os marcadores do modelo. Valores desconhecidos ficam como estão — nunca lança,
    /// porque uma notificação com texto imperfeito é melhor do que notificação nenhuma.
    /// </summary>
    public static string Render(string template, string checklistName, string columnName, string sectorName, int pendingCount) =>
        template
            .Replace("{checklist}", checklistName, StringComparison.OrdinalIgnoreCase)
            .Replace("{coluna}", columnName, StringComparison.OrdinalIgnoreCase)
            .Replace("{setor}", sectorName, StringComparison.OrdinalIgnoreCase)
            .Replace("{pendentes}", pendingCount.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
}
