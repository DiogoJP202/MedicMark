using System.Text.Json;
using Bunit;
using ChecklistPlantao.Client.Abstractions;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.UI.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

public sealed class SessionPageTests : BunitContext
{
    private static readonly Guid SectorId = Guid.CreateVersion7();
    private static readonly Guid SessionId = Guid.CreateVersion7();

    [Fact]
    public void Resumo_exibe_os_tres_estados_e_usa_a_consulta_direta_da_sessao()
    {
        var session = new FakeSession();
        var store = new FakeStore();
        var sync = new FakeSync();
        Register(session, store, sync);

        var cut = Render<SessionPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, cut.FindAll("[data-testid^='summary-template-']").Count);
            Assert.Contains("Pendente", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Parcial", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Concluído", cut.Markup, StringComparison.Ordinal);
            Assert.Single(cut.FindAll("[data-testid^='summary-marker-']"));
        });

        Assert.Equal(1, store.CurrentSessionCalls);
        Assert.Equal(0, store.TemplateCalls);
    }

    [Fact]
    public void Mudancas_recarregam_e_dispose_remove_todas_as_inscricoes()
    {
        var session = new FakeSession();
        var store = new FakeStore();
        var sync = new FakeSync();
        Register(session, store, sync);
        var cut = Render<SessionPage>();
        cut.WaitForAssertion(() => Assert.Equal(1, store.CurrentSessionCalls));

        store.RaiseChanged();
        cut.WaitForAssertion(() => Assert.Equal(2, store.CurrentSessionCalls));
        sync.RaiseChanged();
        cut.WaitForAssertion(() => Assert.Equal(3, store.CurrentSessionCalls));
        session.RaiseChanged();
        cut.WaitForAssertion(() => Assert.Equal(4, store.CurrentSessionCalls));

        cut.Instance.Dispose();
        cut.Dispose();

        Assert.Equal(0, store.SubscriberCount);
        Assert.Equal(0, sync.SubscriberCount);
        Assert.Equal(0, session.SubscriberCount);
    }

    [Fact]
    public void Estado_calculado_nao_altera_o_json_do_progresso()
    {
        var progress = new ProgressDto(10, 4);

        Assert.Equal(ProgressState.Partial, progress.State);
        Assert.DoesNotContain("State", JsonSerializer.Serialize(progress), StringComparison.Ordinal);
    }

    private void Register(FakeSession session, FakeStore store, FakeSync sync)
    {
        Services.AddSingleton<IAppSession>(session);
        Services.AddSingleton<IChecklistStore>(store);
        Services.AddSingleton<ISyncStatusService>(sync);
    }

    private sealed class FakeSession : IAppSession
    {
        private Action? _changed;

        public bool IsAuthenticated => true;
        public string DisplayName => "Maria";
        public EffectiveAccess Access => EffectiveAccess.None;
        public bool PermissionsAreStale => false;
        public DateTime? LastServerValidationUtc => DateTime.UtcNow;
        public Guid? CurrentSectorId => SectorId;
        public string? CurrentSectorName => "Oeste";
        public string? LastSignInError => null;
        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

        public event Action? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public void RaiseChanged() => _changed?.Invoke();

        public Task<IReadOnlyList<SectorSummary>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SectorSummary>>([]);

        public Task SelectSectorAsync(Guid sectorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> SignInAsync(string userName, string password, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSync : ISyncStatusService
    {
        private Action<SyncStatus>? _changed;
        public SyncStatus Current => SyncStatus.Unknown;
        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

        public event Action<SyncStatus>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public void RaiseChanged() => _changed?.Invoke(Current);
        public Task SyncNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshConnectivityAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeStore : IChecklistStore
    {
        private Action? _changed;
        public int CurrentSessionCalls { get; private set; }
        public int TemplateCalls { get; private set; }
        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

        public event Action? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public void RaiseChanged() => _changed?.Invoke();

        public Task<IReadOnlyList<ChecklistTemplateDto>> GetTemplatesAsync(Guid sectorId, CancellationToken cancellationToken = default)
        {
            TemplateCalls++;
            return Task.FromResult<IReadOnlyList<ChecklistTemplateDto>>([]);
        }

        public Task<CurrentSessionView?> GetCurrentSessionAsync(Guid sectorId, CancellationToken cancellationToken = default)
        {
            CurrentSessionCalls++;
            return Task.FromResult<CurrentSessionView?>(new(SessionId, SectorId, new DateOnly(2026, 8, 6), true));
        }

        public Task<SessionSummaryDto> GetSessionSummaryAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SessionSummaryDto(
                new ProgressDto(12, 6),
                [
                    Template("Pendente", new ProgressDto(4, 0)),
                    Template("Parcial", new ProgressDto(4, 2)),
                    Template("Concluído", new ProgressDto(4, 4)),
                ],
                [new MarkerSummaryDto(Guid.CreateVersion7(), "Sondas", ["1148"]) ]));

        private static TemplateSummaryDto Template(string name, ProgressDto progress) =>
            new(Guid.CreateVersion7(), name, progress, []);

        public Task<ChecklistBoard> GetBoardAsync(Guid sectorId, Guid templateId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ToggleOutcome> ToggleCellAsync(ChecklistCell cell, bool isCompleted, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<BedMarkersView> GetBedMarkersAsync(Guid sessionId, Guid bedId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<BedMarkersView>> GetAllBedMarkersAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ToggleOutcome> ToggleMarkerAsync(Guid sessionId, Guid bedId, BedMarkerState marker, bool isSelected, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<PendingGroup>> GetPendingAsync(Guid sectorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> CloseSessionAsync(Guid sessionId, bool confirmWithPending, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<bool> ResetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
