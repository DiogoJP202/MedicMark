using System.Collections.Frozen;

namespace ChecklistPlantao.Domain.Access;

/// <summary>
/// Catálogo de permissões por chave. Não existe "usuário admin" implícito no código:
/// tudo é decidido pela união das permissões dos grupos ativos do usuário.
/// </summary>
public static class Permissions
{
    public const string ChecklistView = "checklist.view";
    public const string ChecklistUpdate = "checklist.update";
    public const string ChecklistClose = "checklist.close";
    public const string SectorSelect = "sector.select";

    public const string AdminUsers = "admin.users";
    public const string AdminGroups = "admin.groups";
    public const string AdminSectors = "admin.sectors";
    public const string AdminBeds = "admin.beds";
    public const string AdminTemplates = "admin.templates";
    public const string AdminNotifications = "admin.notifications";
    public const string AdminDevices = "admin.devices";
    public const string AdminSettings = "admin.settings";

    /// <summary>
    /// Descrições em português exibidas na tela de grupos. A chave é o contrato estável;
    /// a descrição é apenas texto de interface e pode mudar sem impacto.
    /// </summary>
    public static readonly FrozenDictionary<string, string> Catalog = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ChecklistView] = "Visualizar o checklist",
        [ChecklistUpdate] = "Marcar e desmarcar tarefas do checklist",
        [ChecklistClose] = "Abrir, fechar e reiniciar a sessão de plantão",
        [SectorSelect] = "Escolher entre os setores autorizados",
        [AdminUsers] = "Administrar usuários",
        [AdminGroups] = "Administrar grupos e permissões",
        [AdminSectors] = "Administrar setores",
        [AdminBeds] = "Administrar leitos",
        [AdminTemplates] = "Administrar tipos de checklist, colunas e marcadores",
        [AdminNotifications] = "Configurar notificações",
        [AdminDevices] = "Consultar os dispositivos registrados",
        [AdminSettings] = "Alterar configurações gerais do sistema",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlyCollection<string> All => Catalog.Keys;

    public static bool IsKnown(string key) => Catalog.ContainsKey(key);
}
