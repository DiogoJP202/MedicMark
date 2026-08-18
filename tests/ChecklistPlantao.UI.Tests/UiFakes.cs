using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.UI.Abstractions;

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
