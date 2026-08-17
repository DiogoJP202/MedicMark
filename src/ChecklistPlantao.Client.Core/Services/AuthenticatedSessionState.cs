using ChecklistPlantao.Domain.Access;

namespace ChecklistPlantao.Client.Core.Services;

/// <summary>
/// Estado de quem está usando o aplicativo — vivo enquanto o aplicativo estiver aberto.
///
/// Existe como SINGLETON de propósito. <see cref="ClientSession"/> depende do banco local, que é
/// por escopo, então a própria sessão precisa ser por escopo também. Guardar o estado de
/// autenticação nos campos dela fazia com que cada escopo tivesse a sua verdade: os serviços
/// singleton (sincronização, reagendamento de notificações) abrem escopos próprios, e qualquer
/// resolução de <c>IAppSession</c> fora do escopo do WebView devolvia uma instância NOVA, não
/// autenticada. O painel então redirecionava para a entrada, e a navegação sumia junto — com o
/// usuário logado o tempo todo.
///
/// Com o estado aqui, o escopo deixa de importar: todos enxergam a mesma sessão.
/// </summary>
public sealed class AuthenticatedSessionState
{
    private readonly Lock _porta = new();

    public bool IsAuthenticated { get; private set; }

    public Guid UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public EffectiveAccess Access { get; private set; } = EffectiveAccess.None;

    /// <summary>Última vez que o servidor confirmou esta credencial. Base da validade offline.</summary>
    public DateTime? LastServerValidationUtc { get; private set; }

    public Guid? CurrentSectorId { get; private set; }

    public string? CurrentSectorName { get; private set; }

    /// <summary>
    /// O evento vive aqui, e não na sessão: quem assina é a interface, cujo escopo pode ser outro
    /// que não o de quem fez a entrada. Assinar na instância errada seria assinar o silêncio.
    /// </summary>
    public event Action? Changed;

    public void SignIn(Guid userId, string userName, string displayName, EffectiveAccess access, DateTime validatedAtUtc)
    {
        lock (_porta)
        {
            IsAuthenticated = true;
            UserId = userId;
            UserName = userName;
            DisplayName = displayName;
            Access = access;
            LastServerValidationUtc = validatedAtUtc;
        }

        Changed?.Invoke();
    }

    public void SignOut()
    {
        lock (_porta)
        {
            IsAuthenticated = false;
            UserId = Guid.Empty;
            UserName = string.Empty;
            DisplayName = string.Empty;
            Access = EffectiveAccess.None;
            LastServerValidationUtc = null;
            CurrentSectorId = null;
            CurrentSectorName = null;
        }

        Changed?.Invoke();
    }

    public void SelectSector(Guid? sectorId, string? sectorName)
    {
        lock (_porta)
        {
            CurrentSectorId = sectorId;
            CurrentSectorName = sectorName;
        }

        Changed?.Invoke();
    }

    /// <summary>Atualiza o nome do setor sem disparar evento — usado ao restaurar a sessão.</summary>
    public void SetSectorNameQuietly(string? sectorName)
    {
        lock (_porta)
        {
            CurrentSectorName = sectorName;
        }
    }

    public void NotifyChanged() => Changed?.Invoke();
}
