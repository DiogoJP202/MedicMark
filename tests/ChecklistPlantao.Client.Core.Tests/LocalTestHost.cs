using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Seeding;
using ChecklistPlantao.Domain.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChecklistPlantao.Client.Core.Tests;

internal sealed class TestClock(DateTime start) : IClock
{
    public DateTime UtcNow { get; private set; } = start;

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
}

/// <summary>
/// Servidor de mentira, controlado pelo teste.
///
/// Permite simular exatamente o que interessa: servidor fora do ar, conflito, operação
/// duplicada e log podado — sem subir nada nem depender de rede.
/// </summary>
internal sealed class FakeServerApi : IServerApi
{
    private readonly HashSet<Guid> _processadas = [];

    public bool IsReachable { get; set; } = true;

    public BootstrapResponse? Bootstrap { get; set; }

    public long Cursor { get; set; }

    public List<ServerChangeDto> PendingChanges { get; } = [];

    public bool RequiresBootstrapOnPull { get; set; }

    public int PushCallCount { get; private set; }

    public List<SyncOperationDto> ReceivedOperations { get; } = [];

    /// <summary>Resposta a ser devolvida para uma operação específica, quando o teste quiser forçar conflito.</summary>
    public Func<SyncOperationDto, SyncOperationResultDto>? OperationHandler { get; set; }

    public Exception? PushException { get; set; }

    public Task<ServerProbeResponse?> ProbeAsync(string url, CancellationToken cancellationToken = default) =>
        Task.FromResult<ServerProbeResponse?>(IsReachable
            ? new ServerProbeResponse("ChecklistPlantao", "0.1.0", "Testing", DateTime.UtcNow, "America/Sao_Paulo")
            : null);

    /// <summary>Resposta a ser devolvida no login. Nulo = servidor inalcançável.</summary>
    public ServerLoginResult? LoginResult { get; set; }

    public Task<ServerLoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(LoginResult ?? ServerLoginResult.Unreachable());

    public Task<BootstrapResponse?> BootstrapAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsReachable ? Bootstrap : null);

    public Task<SyncPushResponse?> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default)
    {
        PushCallCount++;

        if (PushException is not null)
        {
            throw PushException;
        }

        if (!IsReachable)
        {
            return Task.FromResult<SyncPushResponse?>(null);
        }

        var resultados = new List<SyncOperationResultDto>();

        foreach (var operacao in request.Operations)
        {
            ReceivedOperations.Add(operacao);

            if (!_processadas.Add(operacao.OperationId))
            {
                resultados.Add(new SyncOperationResultDto(operacao.OperationId, nameof(SyncOperationStatus.Duplicate), null, null, null));
                continue;
            }

            resultados.Add(OperationHandler?.Invoke(operacao)
                ?? new SyncOperationResultDto(operacao.OperationId, nameof(SyncOperationStatus.Applied), null, null, operacao.BaseVersion + 1));
        }

        Cursor += resultados.Count;

        var mudancas = PendingChanges.ToList();
        PendingChanges.Clear();

        return Task.FromResult<SyncPushResponse?>(new SyncPushResponse(resultados, mudancas, Cursor, DateTime.UtcNow));
    }

    public Task<SyncPullResponse?> PullAsync(long since, CancellationToken cancellationToken = default)
    {
        if (!IsReachable)
        {
            return Task.FromResult<SyncPullResponse?>(null);
        }

        var mudancas = PendingChanges.ToList();
        PendingChanges.Clear();

        return Task.FromResult<SyncPullResponse?>(
            new SyncPullResponse(mudancas, Cursor, false, DateTime.UtcNow, RequiresBootstrapOnPull));
    }

    public Task<SessionStateDto?> GetCurrentSessionAsync(Guid sectorId, CancellationToken cancellationToken = default) =>
        Task.FromResult<SessionStateDto?>(null);

    public Task<SessionSummaryDto?> GetSummaryAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<SessionSummaryDto?>(null);

    public Task<bool> CloseSessionAsync(Guid sessionId, bool confirmWithPending, CancellationToken cancellationToken = default) =>
        Task.FromResult(IsReachable);

    public Task<bool> ResetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(IsReachable);

    public Task<DeviceDto?> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<DeviceDto?>(null);

    public Task<DeviceDto?> HeartbeatAsync(DeviceHeartbeatRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<DeviceDto?>(null);
}

/// <summary>
/// Banco local SQLite em arquivo temporário.
///
/// Arquivo e não memória de propósito: os testes precisam poder FECHAR o contexto e abrir outro
/// sobre o mesmo banco, simulando o aplicativo sendo encerrado e reaberto. Com <c>:memory:</c>
/// o banco morreria junto com a conexão e o teste não provaria nada.
/// </summary>
internal sealed class LocalTestHost : IDisposable
{
    private readonly string _arquivo;
    private readonly List<LocalDbContext> _contextos = [];

    private LocalTestHost(string arquivo, TestClock clock)
    {
        _arquivo = arquivo;
        Clock = clock;
        Api = new FakeServerApi();
    }

    public TestClock Clock { get; }

    public FakeServerApi Api { get; }

    public static async Task<LocalTestHost> CreateAsync(DateTime? startUtc = null)
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"checklist-client-{Guid.CreateVersion7():N}.db");
        var host = new LocalTestHost(arquivo, new TestClock(startUtc ?? new DateTime(2026, 8, 6, 23, 0, 0, DateTimeKind.Utc)));

        await using var contexto = host.CreateContext();
        await contexto.Database.EnsureCreatedAsync();

        return host;
    }

    public LocalDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LocalDbContext>()
            .UseSqlite($"Data Source={_arquivo}")
            .Options;

        var contexto = new LocalDbContext(options);
        _contextos.Add(contexto);
        return contexto;
    }

    public SyncEngine CreateEngine(LocalDbContext db) =>
        new(db, new OutboxWriter(db), Api, Clock, NullLogger<SyncEngine>.Instance);

    /// <summary>Bootstrap equivalente ao que o servidor real devolveria com os dados de seed.</summary>
    public static BootstrapResponse BuildBootstrap(long cursor = 1)
    {
        var setores = SeedCatalog.Sectors
            .Select(s => new SectorDto(s.Id, s.Name, null, s.SortOrder, true, null, null, 1))
            .ToList();

        var leitos = SeedCatalog.Beds
            .Select(b => new BedDto(b.Id, SeedCatalog.Sectors[0].Id, b.Code, null, b.SortOrder, true, 1))
            .ToList();

        var templates = SeedCatalog.Templates.Select(t => new ChecklistTemplateDto(
            t.Id, t.Name, t.Code, null, t.SortOrder, true, [],
            [.. SeedCatalog.Columns
                .Where(c => c.TemplateCode == t.Code)
                .Select(c => new ChecklistColumnDto(
                    c.Id, t.Id, c.DisplayName, c.TriggerTime, c.SortOrder, true, true,
                    0, 15, 10, 3, true, 5, 1))],
            1)).ToList();

        var marcadores = SeedCatalog.Markers
            .Select(m => new BedMarkerDefinitionDto(m.Id, m.Name, m.Code, m.SortOrder, true, 1))
            .ToList();

        return new BootstrapResponse(
            new InstitutionSettingsDto("America/Sao_Paulo", new TimeOnly(19, 0), new TimeOnly(7, 0), 24, 7, 5, true),
            new NotificationConfigurationDto(true, true, "High", true, true, false,
                "ATENÇÃO — {checklist} {coluna}", "{pendentes} leito(s) pendente(s) no setor {setor}.", 1),
            [],
            setores,
            leitos,
            templates,
            marcadores,
            cursor,
            DateTime.UtcNow);
    }

    public void Dispose()
    {
        foreach (var contexto in _contextos)
        {
            contexto.Dispose();
        }

        SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_arquivo))
            {
                File.Delete(_arquivo);
            }
        }
        catch (IOException)
        {
            // Arquivo temporário: o sistema limpa depois.
        }
    }
}
