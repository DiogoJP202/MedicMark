using ChecklistPlantao.Contracts.Configuration;

namespace ChecklistPlantao.Client.Abstractions;

/// <summary>Como o dispositivo enxerga a rede. São três estados distintos, não um só.</summary>
public enum ConnectivityState
{
    /// <summary>Sem rede alguma.</summary>
    Offline = 0,

    /// <summary>Há rede local, mas o servidor configurado não respondeu.</summary>
    ServerUnreachable = 1,

    /// <summary>Servidor acessível pela rede local, sem internet externa.</summary>
    LocalNetwork = 2,

    /// <summary>Servidor acessível e internet disponível.</summary>
    Online = 3,
}

/// <summary>Estado de sincronização exibido no topo do aplicativo.</summary>
public sealed record SyncStatus(
    ConnectivityState Connectivity,
    int PendingOperations,
    DateTime? LastSyncAtUtc,
    bool IsSyncing,
    string? LastError)
{
    public static SyncStatus Unknown { get; } = new(ConnectivityState.Offline, 0, null, false, null);

    public bool HasPendingWork => PendingOperations > 0;

    /// <summary>
    /// Este aparelho já concluiu ao menos uma sincronização.
    ///
    /// Fila vazia **não** significa sincronizado: um aparelho recém-instalado tem zero pendências
    /// justamente porque nunca fez nada. Dizer "Tudo sincronizado" nesse estado é afirmar o que
    /// não foi medido — a mesma regra que vale para a saúde das notificações.
    /// </summary>
    public bool HasSynced => LastSyncAtUtc is not null;

    /// <summary>Texto principal da faixa. Sem jargão: o plantão não precisa saber o que é cursor.</summary>
    public string Headline => Connectivity switch
    {
        ConnectivityState.Online or ConnectivityState.LocalNetwork when IsSyncing => "Sincronizando…",
        ConnectivityState.Online or ConnectivityState.LocalNetwork when !HasSynced => "Ainda não sincronizado",
        ConnectivityState.Online or ConnectivityState.LocalNetwork when !HasPendingWork => "Tudo sincronizado",
        ConnectivityState.Online or ConnectivityState.LocalNetwork => $"{PendingOperations} alteração(ões) sendo enviada(s)",
        ConnectivityState.ServerUnreachable => "Servidor indisponível",
        _ => "Offline",
    };

    public string Detail => Connectivity switch
    {
        ConnectivityState.Online or ConnectivityState.LocalNetwork when !HasSynced =>
            "Toque para buscar os dados do servidor.",
        ConnectivityState.Online => "Conectado ao servidor.",
        ConnectivityState.LocalNetwork => "Conectado pela rede local.",
        ConnectivityState.ServerUnreachable when HasPendingWork =>
            $"As alterações estão salvas neste dispositivo. {PendingOperations} aguardando envio.",
        ConnectivityState.ServerUnreachable => "As alterações estão salvas neste dispositivo.",
        _ when HasPendingWork => $"{PendingOperations} alteração(ões) aguardando sincronização.",
        _ => "Você pode continuar trabalhando normalmente.",
    };
}

/// <summary>Saúde das notificações neste dispositivo.</summary>
public sealed record NotificationStatus(
    bool PermissionGranted,
    bool? ExactAlarmPermissionGranted,
    bool SoundEnabled,
    bool VibrationEnabled,
    bool BatteryOptimizationIgnored,
    bool SchedulingRequiresAppRunning,
    DateTime? LastTestAtUtc,
    DateTime? NextScheduledLocal,
    IReadOnlyList<string> Problems)
{
    /// <summary>
    /// Verdadeiro só depois que o dispositivo foi realmente consultado.
    ///
    /// Antes disso o aplicativo não sabe nada — e não pode afirmar nada. O estado inicial trazia
    /// "ainda não foi verificado" dentro de <see cref="Problems"/>, então a faixa de alerta subia
    /// em toda abertura de tela dizendo que havia um problema quando o que havia era ausência de
    /// medição. É o mesmo erro que o sistema evita em todo o resto: nunca afirmar o que não foi
    /// verificado.
    /// </summary>
    public bool HasBeenChecked { get; init; }

    public static NotificationStatus Unknown { get; } =
        new(false, null, false, false, false, false, null, null, []) { HasBeenChecked = false };

    /// <summary>
    /// Saudável só quando foi verificado E não há nenhum problema. Nunca dizemos "notificações
    /// ativas" com uma permissão essencial faltando — é o ponto do item 17 do enunciado.
    /// </summary>
    public bool IsHealthy => HasBeenChecked && Problems.Count == 0;

    /// <summary>
    /// Há um problema REAL a mostrar ao usuário. Falso enquanto nada foi medido: ausência de
    /// verificação não é defeito, e alarmar por isso ensina o plantão a ignorar a faixa.
    /// </summary>
    public bool HasProblems => HasBeenChecked && Problems.Count > 0;

    /// <summary>Funciona, mas com ressalva: por exemplo alarme inexato ou app precisa estar aberto.</summary>
    public bool IsDegraded => HasBeenChecked && PermissionGranted && Problems.Count > 0;
}

public sealed record SectorSummary(Guid Id, string Name, int PendingTasks, bool HasOpenSession);

/// <summary>Uma célula da grade: o cruzamento leito × coluna.</summary>
public sealed record ChecklistCell(
    Guid BedId,
    Guid TemplateId,
    Guid ColumnId,
    bool IsCompleted,
    int Version,
    bool IsOverdue);

public sealed record ChecklistRow(Guid BedId, string BedCode, IReadOnlyList<ChecklistCell> Cells, IReadOnlyList<string> MarkerNames)
{
    /// <summary>
    /// Identificadores dos marcadores ativos deste leito, para o filtro do checklist.
    /// Os nomes servem para exibir; filtrar por nome quebraria assim que o administrador
    /// renomeasse "Sondas".
    /// </summary>
    public IReadOnlyList<Guid> MarkerIds { get; init; } = [];

    public int Pending => Cells.Count(c => !c.IsCompleted);

    public bool IsComplete => Pending == 0;
}

public sealed record ChecklistColumnView(Guid ColumnId, string DisplayName, TimeOnly? TriggerTime, int Total, int Completed, bool IsOverdue)
{
    public int Pending => Total - Completed;

    public bool IsComplete => Pending == 0;
}

/// <summary>Tudo o que a tela de checklist precisa para desenhar uma grade.</summary>
public sealed record ChecklistBoard(
    Guid SessionId,
    Guid SectorId,
    string SectorName,
    DateOnly ServiceDate,
    ChecklistTemplateDto Template,
    IReadOnlyList<ChecklistColumnView> Columns,
    IReadOnlyList<ChecklistRow> Rows,
    bool IsSessionOpen)
{
    public int Total => Rows.Sum(r => r.Cells.Count);

    public int Completed => Rows.Sum(r => r.Cells.Count(c => c.IsCompleted));

    public int Pending => Total - Completed;

    public static ChecklistBoard Empty { get; } = new(
        Guid.Empty, Guid.Empty, string.Empty, default, null!, [], [], false);
}

/// <summary>Identidade da sessão atual sem depender da abertura de uma grade de checklist.</summary>
public sealed record CurrentSessionView(Guid Id, Guid SectorId, DateOnly ServiceDate, bool IsOpen);

/// <summary>Pendências agrupadas para a tela de pendências e para as faixas de atraso.</summary>
public sealed record PendingGroup(
    Guid SectorId,
    string SectorName,
    Guid TemplateId,
    string TemplateName,
    Guid ColumnId,
    string ColumnName,
    TimeOnly? TriggerTime,
    bool IsOverdue,
    IReadOnlyList<string> BedCodes)
{
    public int Count => BedCodes.Count;
}

/// <summary>Estado de um marcador de leito na sessão atual.</summary>
public sealed record BedMarkerState(Guid MarkerDefinitionId, string Name, bool IsSelected, int Version);

public sealed record BedMarkersView(Guid BedId, string BedCode, IReadOnlyList<BedMarkerState> Markers);

/// <summary>Resultado de alternar uma célula, já com o que o botão de desfazer precisa.</summary>
public sealed record ToggleOutcome(bool Succeeded, string? Message, ChecklistCell? Cell);
