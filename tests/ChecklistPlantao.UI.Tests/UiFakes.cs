using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Client.Abstractions;

namespace ChecklistPlantao.UI.Tests;

/// <summary>Sessão de mentira com acesso configurável, para telas que dependem de permissão.</summary>
internal sealed class StubSession(EffectiveAccess? access = null, bool authenticated = true) : IAppSession
{
    public bool IsAuthenticated { get; } = authenticated;

    public string DisplayName => "Maria";

    public EffectiveAccess Access { get; } = access ?? EffectiveAccess.None;

    public bool PermissionsAreStale => false;

    public DateTime? LastServerValidationUtc => null;

    public Guid? CurrentSectorId => null;

    public string? CurrentSectorName => null;

    public string? LastSignInError => null;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<SectorSummary>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SectorSummary>>([]);

    public Task SelectSectorAsync(Guid sectorId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> SignInAsync(string userName, string password, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class StubSyncStatus : ISyncStatusService
{
    public SyncStatus Current => SyncStatus.Unknown;

    public event Action<SyncStatus>? Changed
    {
        add { }
        remove { }
    }

    public Task SyncNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RefreshConnectivityAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class StubNotificationStatus : INotificationStatusService
{
    public NotificationStatus Current => NotificationStatus.Unknown;

    public event Action<NotificationStatus>? Changed
    {
        add { }
        remove { }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> SendTestNotificationAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task OpenSystemSettingsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Serviço de tema de mentira. <see cref="Efetivo"/> é separado de <see cref="Atual"/> de
/// propósito: com "seguir o aparelho" escolhido, os dois são coisas diferentes, e é justamente
/// nesse caso que o interruptor do menu erraria a posição.
/// </summary>
internal sealed class TemaFalso : ChecklistPlantao.UI.Services.IThemeService
{
    public ChecklistPlantao.UI.Services.ThemeChoice Atual { get; set; }
        = ChecklistPlantao.UI.Services.ThemeChoice.Automatic;

    public ChecklistPlantao.UI.Services.ThemeChoice Efetivo { get; set; }
        = ChecklistPlantao.UI.Services.ThemeChoice.Light;

    public ChecklistPlantao.UI.Services.ThemeChoice? Gravado { get; private set; }

    public Task<ChecklistPlantao.UI.Services.ThemeChoice> GetAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Atual);

    public Task<ChecklistPlantao.UI.Services.ThemeChoice> GetEffectiveAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Efetivo);

    public Task SetAsync(ChecklistPlantao.UI.Services.ThemeChoice choice, CancellationToken cancellationToken = default)
    {
        Gravado = choice;
        Atual = choice;
        Efetivo = choice;
        return Task.CompletedTask;
    }
}
