using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Access;

/// <summary>
/// Usuário do sistema. O hash da senha é responsabilidade exclusiva do servidor (ASP.NET Identity)
/// e nunca sai dele — o login offline usa um verificador próprio derivado no dispositivo.
/// </summary>
public sealed class AppUser : ISyncVersioned
{
    private readonly List<UserGroup> _groups = [];

    private AppUser()
    {
        UserName = string.Empty;
        DisplayName = string.Empty;
    }

    public AppUser(Guid id, string userName, string displayName, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(userName, "nome de usuário");
        DomainRuleException.ThrowIfNullOrWhiteSpace(displayName, "nome de exibição");

        Id = id;
        UserName = NormalizeUserName(userName);
        DisplayName = displayName.Trim();
        IsActive = true;
        Version = 1;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string UserName { get; private set; }

    public string DisplayName { get; private set; }

    public bool IsActive { get; private set; }

    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<UserGroup> Groups => _groups;

    public IEnumerable<Guid> GroupIds => _groups.Select(g => g.GroupId);

    /// <summary>
    /// Nome de usuário é comparado sem diferenciar maiúsculas e sem espaços nas pontas.
    /// A unicidade é garantida por índice no banco sobre este valor normalizado.
    /// </summary>
    public static string NormalizeUserName(string userName) => userName.Trim().ToLowerInvariant();

    public void Rename(string displayName, DateTime nowUtc)
    {
        DomainRuleException.ThrowIfNullOrWhiteSpace(displayName, "nome de exibição");

        DisplayName = displayName.Trim();
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

    public void ReplaceGroups(IEnumerable<Guid> groupIds, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(groupIds);

        var desired = groupIds.Distinct().ToList();

        _groups.RemoveAll(g => !desired.Contains(g.GroupId));

        foreach (var groupId in desired.Where(id => !GroupIds.Contains(id)))
        {
            _groups.Add(new UserGroup(Id, groupId));
        }

        Touch(nowUtc);
    }

    private void Touch(DateTime nowUtc)
    {
        Version++;
        UpdatedAtUtc = nowUtc;
    }
}
