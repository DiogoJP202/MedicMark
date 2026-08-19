using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Scheduling;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Domain.Structure;
using ChecklistPlantao.Client.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Client.Core.Services;

/// <summary>
/// Implementa o <see cref="IChecklistStore"/> lendo e escrevendo SEMPRE no banco local.
///
/// A interface nunca espera o servidor: marcar grava localmente e enfileira. É o que garante que
/// o toque responda igual com ou sem rede.
/// </summary>
public sealed class LocalChecklistStore : IChecklistStore
{
    private readonly IDbContextFactory<LocalDbContext>? _contextos;
    private readonly LocalDbContext? _emprestado;
    private readonly OutboxWriter outbox;
    private readonly IServerApi api;
    private readonly IClock clock;
    private readonly IInstitutionTimeZone timeZone;
    private readonly IInstitutionSettingsProvider settings;
    private readonly ILogger<LocalChecklistStore> logger;

    /// <summary>
    /// Modo normal: um contexto por operação. Abrir o quadro, marcar e listar classificações são
    /// unidades de trabalho independentes. Ver docs/DECISIONS.md (D-021).
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public LocalChecklistStore(
        IDbContextFactory<LocalDbContext> contextos,
        OutboxWriter outbox,
        IServerApi api,
        IClock clock,
        IInstitutionTimeZone timeZone,
        IInstitutionSettingsProvider settings,
        ILogger<LocalChecklistStore> logger)
        : this(outbox, api, clock, timeZone, settings, logger) => _contextos = contextos;

    /// <summary>Modo emprestado: trabalha num contexto já aberto, de quem o criou.</summary>
    public LocalChecklistStore(
        LocalDbContext db,
        OutboxWriter outbox,
        IServerApi api,
        IClock clock,
        IInstitutionTimeZone timeZone,
        IInstitutionSettingsProvider settings,
        ILogger<LocalChecklistStore> logger)
        : this(outbox, api, clock, timeZone, settings, logger) => _emprestado = db;

    private LocalChecklistStore(
        OutboxWriter outbox,
        IServerApi api,
        IClock clock,
        IInstitutionTimeZone timeZone,
        IInstitutionSettingsProvider settings,
        ILogger<LocalChecklistStore> logger)
    {
        this.outbox = outbox;
        this.api = api;
        this.clock = clock;
        this.timeZone = timeZone;
        this.settings = settings;
        this.logger = logger;
    }

    public event Action? Changed;

    /// <summary>Devolve o contexto a usar e se ele é nosso (e portanto precisa ser descartado).</summary>
    private async Task<(LocalDbContext Db, bool Meu)> AbrirAsync(CancellationToken cancellationToken)
    {
        if (_emprestado is not null)
        {
            return (_emprestado, false);
        }

        return (await _contextos!.CreateDbContextAsync(cancellationToken).ConfigureAwait(false), true);
    }

    private static ValueTask FecharAsync(LocalDbContext db, bool meu) =>
        meu ? db.DisposeAsync() : ValueTask.CompletedTask;

    public async Task<IReadOnlyList<ChecklistTemplateDto>> GetTemplatesAsync(Guid sectorId, CancellationToken cancellationToken = default)
    {
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await GetTemplatesAsync(db, sectorId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<ChecklistTemplateDto>> GetTemplatesAsync(
        LocalDbContext db,
        Guid sectorId,
        CancellationToken cancellationToken)
    {
        var templates = await db.ChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Columns)
            .Include(t => t.Sectors)
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. templates.Where(t => t.AppliesTo(sectorId)).Select(ConfigurationQueryService.Map)];
    }

    public async Task<ChecklistBoard> GetBoardAsync(Guid sectorId, Guid templateId, CancellationToken cancellationToken = default)
    {
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await GetBoardAsync(db, sectorId, templateId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    private async Task<ChecklistBoard> GetBoardAsync(
        LocalDbContext db,
        Guid sectorId,
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var sessao = await EnsureSessionAsync(db, sectorId, cancellationToken).ConfigureAwait(false);

        if (sessao is null)
        {
            return ChecklistBoard.Empty;
        }

        var template = await db.ChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Columns)
            .Include(t => t.Sectors)
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            .ConfigureAwait(false);

        if (template is null)
        {
            return ChecklistBoard.Empty;
        }

        var setor = await db.Sectors.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectorId, cancellationToken).ConfigureAwait(false);

        var leitos = await db.Beds
            .AsNoTracking()
            .Where(b => b.SectorId == sectorId && b.IsActive)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var entradas = await db.ChecklistEntries
            .AsNoTracking()
            .Where(e => e.SessionId == sessao.Id && e.ChecklistTemplateId == templateId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var marcadoresPorLeito = await LoadMarkerNamesAsync(db, sessao.Id, cancellationToken).ConfigureAwait(false);

        var configuracoes = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
        var janela = ShiftResolver.For(configuracoes, setor);
        var agoraLocal = timeZone.ToLocal(clock.UtcNow);

        var colunas = template.ActiveColumnsInOrder.ToList();
        var porCelula = entradas.ToDictionary(e => (e.BedId, e.ChecklistColumnId));

        var linhas = new List<ChecklistRow>(leitos.Count);

        foreach (var leito in leitos)
        {
            var celulas = colunas.Select(coluna =>
            {
                var atrasada = IsOverdue(janela, sessao.ServiceDate, coluna, agoraLocal);

                return porCelula.TryGetValue((leito.Id, coluna.Id), out var entrada)
                    ? new ChecklistCell(leito.Id, templateId, coluna.Id, entrada.IsCompleted, entrada.Version, atrasada)
                    : new ChecklistCell(leito.Id, templateId, coluna.Id, false, 0, atrasada);
            }).ToList();

            var doLeito = marcadoresPorLeito.TryGetValue(leito.Id, out var marcados) ? marcados : [];

            linhas.Add(new ChecklistRow(
                leito.Id,
                leito.Code,
                celulas,
                [.. doLeito.Select(m => m.Nome)])
            {
                // Os Ids acompanham os nomes para o filtro do checklist funcionar por
                // identificador — renomear "Sondas" no painel não pode quebrar o filtro.
                MarkerIds = [.. doLeito.Select(m => m.Id)],
            });
        }

        var visaoColunas = colunas.Select(coluna => new ChecklistColumnView(
            coluna.Id,
            coluna.DisplayName,
            coluna.TriggerTime,
            linhas.Count,
            linhas.Count(l => l.Cells.Any(c => c.ColumnId == coluna.Id && c.IsCompleted)),
            IsOverdue(janela, sessao.ServiceDate, coluna, agoraLocal))).ToList();

        return new ChecklistBoard(
            sessao.Id,
            sectorId,
            setor?.Name ?? string.Empty,
            sessao.ServiceDate,
            ConfigurationQueryService.Map(template),
            visaoColunas,
            linhas,
            sessao.IsOpen);
    }

    public async Task<ToggleOutcome> ToggleCellAsync(ChecklistCell cell, bool isCompleted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cell);

        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        OperationalSession? sessao;

        try
        {
            sessao = await db.OperationalSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Status == SessionStatus.Open, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }

        if (sessao is null)
        {
            return new ToggleOutcome(false, "Não há plantão aberto para marcar.", null);
        }

        try
        {
            var entrada = await outbox
                .ToggleEntryAsync(sessao.Id, cell.BedId, cell.TemplateId, cell.ColumnId, isCompleted, clock.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            Changed?.Invoke();

            return new ToggleOutcome(true, null, cell with { IsCompleted = entrada.IsCompleted, Version = entrada.Version });
        }
        catch (DbUpdateException ex)
        {
            // Falha local irrecuperável é o único caso em que a interface reverte.
            logger.LogError(ex, "Não foi possível gravar a marcação no banco local.");
            return new ToggleOutcome(false, "Não foi possível salvar neste dispositivo.", null);
        }
    }

    public async Task<BedMarkersView> GetBedMarkersAsync(Guid sessionId, Guid bedId, CancellationToken cancellationToken = default)
    {
        var todos = await GetAllBedMarkersAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return todos.FirstOrDefault(b => b.BedId == bedId) ?? new BedMarkersView(bedId, string.Empty, []);
    }

    public async Task<IReadOnlyList<BedMarkersView>> GetAllBedMarkersAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await GetAllBedMarkersAsync(db, sessionId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<BedMarkersView>> GetAllBedMarkersAsync(
        LocalDbContext db,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var sessao = await db.OperationalSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (sessao is null)
        {
            return [];
        }

        var definicoes = await db.BedMarkerDefinitions
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var leitos = await db.Beds
            .AsNoTracking()
            .Where(b => b.SectorId == sessao.SectorId && b.IsActive)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var marcados = await db.SessionBedMarkers
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var porChave = marcados.ToDictionary(m => (m.BedId, m.MarkerDefinitionId));

        return
        [
            .. leitos.Select(leito => new BedMarkersView(
                leito.Id,
                leito.Code,
                [.. definicoes.Select(definicao => porChave.TryGetValue((leito.Id, definicao.Id), out var estado)
                    ? new BedMarkerState(definicao.Id, definicao.Name, estado.IsSelected, estado.Version)
                    : new BedMarkerState(definicao.Id, definicao.Name, false, 0))]))
        ];
    }

    public async Task<ToggleOutcome> ToggleMarkerAsync(
        Guid sessionId,
        Guid bedId,
        BedMarkerState marker,
        bool isSelected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marker);

        try
        {
            await outbox
                .ToggleMarkerAsync(sessionId, bedId, marker.MarkerDefinitionId, isSelected, clock.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            Changed?.Invoke();
            return new ToggleOutcome(true, null, null);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Não foi possível gravar a classificação no banco local.");
            return new ToggleOutcome(false, "Não foi possível salvar neste dispositivo.", null);
        }
    }

    public async Task<IReadOnlyList<PendingGroup>> GetPendingAsync(Guid sectorId, CancellationToken cancellationToken = default)
    {
        var templates = await GetTemplatesAsync(sectorId, cancellationToken).ConfigureAwait(false);
        var grupos = new List<PendingGroup>();

        foreach (var template in templates)
        {
            var board = await GetBoardAsync(sectorId, template.Id, cancellationToken).ConfigureAwait(false);

            if (board.SessionId == Guid.Empty)
            {
                continue;
            }

            foreach (var coluna in board.Columns.Where(c => !c.IsComplete))
            {
                var leitos = board.Rows
                    .Where(l => l.Cells.Any(c => c.ColumnId == coluna.ColumnId && !c.IsCompleted))
                    .Select(l => l.BedCode)
                    .ToList();

                grupos.Add(new PendingGroup(
                    sectorId, board.SectorName, template.Id, template.Name,
                    coluna.ColumnId, coluna.DisplayName, coluna.TriggerTime, coluna.IsOverdue, leitos));
            }
        }

        return grupos;
    }

    /// <summary>
    /// Resumo do plantão. Prefere o servidor quando ele responde — ele é a fonte autoritativa —
    /// e cai no cálculo local quando não há rede.
    /// </summary>
    public async Task<SessionSummaryDto> GetSessionSummaryAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (api.IsReachable)
        {
            var doServidor = await api.GetSummaryAsync(sessionId, cancellationToken).ConfigureAwait(false);

            if (doServidor is not null)
            {
                return doServidor;
            }
        }

        return await BuildLocalSummaryAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CloseSessionAsync(Guid sessionId, bool confirmWithPending, CancellationToken cancellationToken = default)
    {
        // Encerrar exige o servidor: é decisão de plantão, com efeito para todos os aparelhos.
        // Fechar só localmente criaria divergência entre dispositivos.
        if (!api.IsReachable)
        {
            return false;
        }

        var ok = await api.CloseSessionAsync(sessionId, confirmWithPending, cancellationToken).ConfigureAwait(false);

        if (ok)
        {
            var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var sessao = await db.OperationalSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken).ConfigureAwait(false);

                if (sessao is { IsOpen: true })
                {
                    sessao.Close(clock.UtcNow);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await FecharAsync(db, meu).ConfigureAwait(false);
            }

            Changed?.Invoke();
        }

        return ok;
    }

    public async Task<bool> ResetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!api.IsReachable)
        {
            return false;
        }

        var ok = await api.ResetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (ok)
        {
            var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var entradas = await db.ChecklistEntries.Where(e => e.SessionId == sessionId).ToListAsync(cancellationToken).ConfigureAwait(false);

                foreach (var entrada in entradas)
                {
                    entrada.SetCompletion(false, clock.UtcNow);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await FecharAsync(db, meu).ConfigureAwait(false);
            }

            Changed?.Invoke();
        }

        return ok;
    }

    /// <summary>
    /// Garante uma sessão local para o setor. Se o servidor estiver acessível, adota a dele;
    /// caso contrário cria uma local para o plantão poder começar mesmo sem rede.
    /// </summary>
    private async Task<OperationalSession?> EnsureSessionAsync(LocalDbContext db, Guid sectorId, CancellationToken cancellationToken)
    {
        var local = await db.OperationalSessions
            .Include(s => s.Beds)
            .FirstOrDefaultAsync(s => s.SectorId == sectorId && s.Status == SessionStatus.Open, cancellationToken)
            .ConfigureAwait(false);

        if (api.IsReachable)
        {
            var doServidor = await api.GetCurrentSessionAsync(sectorId, cancellationToken).ConfigureAwait(false);

            if (doServidor is not null)
            {
                local = await AdoptServerSessionAsync(db, doServidor, local, cancellationToken).ConfigureAwait(false);
                return local;
            }
        }

        if (local is not null)
        {
            return local;
        }

        var configuracoes = await settings.GetAsync(cancellationToken).ConfigureAwait(false);

        if (!configuracoes.AutoOpenSession)
        {
            return null;
        }

        var setor = await db.Sectors.FirstOrDefaultAsync(s => s.Id == sectorId, cancellationToken).ConfigureAwait(false);

        if (setor is null)
        {
            return null;
        }

        var janela = ShiftResolver.For(configuracoes, setor);
        var serviceDate = janela.ServiceDateFor(timeZone.ToLocal(clock.UtcNow));

        // Id determinístico: se o servidor criar a sessão do mesmo setor e data, os dois lados
        // convergem para o mesmo identificador em vez de duplicar o plantão.
        var id = Domain.Common.DeterministicGuid.From($"session:{sectorId}:{serviceDate:yyyy-MM-dd}");

        var sessao = new OperationalSession(id, sectorId, serviceDate, clock.UtcNow);

        var leitos = await db.Beds
            .Where(b => b.SectorId == sectorId && b.IsActive)
            .OrderBy(b => b.SortOrder)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var leito in leitos)
        {
            sessao.AddBed(leito);
        }

        db.OperationalSessions.Add(sessao);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Sessão local criada offline para o setor {SectorId} na data {ServiceDate}.", sectorId, serviceDate);

        return sessao;
    }

    private static async Task<OperationalSession> AdoptServerSessionAsync(
        LocalDbContext db,
        SessionStateDto estado,
        OperationalSession? local,
        CancellationToken cancellationToken)
    {
        if (local is null || local.Id != estado.Session.Id)
        {
            local = db.OperationalSessions.Local.FirstOrDefault(s => s.Id == estado.Session.Id)
                ?? await db.OperationalSessions
                    .Include(s => s.Beds)
                    .FirstOrDefaultAsync(s => s.Id == estado.Session.Id, cancellationToken)
                    .ConfigureAwait(false);

            if (local is null)
            {
                local = new OperationalSession(
                    estado.Session.Id, estado.Session.SectorId, estado.Session.ServiceDate, estado.Session.StartedAtUtc);

                db.OperationalSessions.Add(local);
            }
        }

        foreach (var bedId in estado.ActiveBedIds)
        {
            local.AddBed(bedId);
        }

        foreach (var dto in estado.Entries)
        {
            // O rastreador ANTES do banco. Uma entidade adicionada momentos atrás nesta mesma
            // unidade de trabalho ainda não existe em disco: a consulta não a encontra, o código
            // adiciona outra com a mesma chave, e o EF recusa com "cannot be tracked because
            // another instance with the same key value is already being tracked".
            var entrada = db.ChecklistEntries.Local.FirstOrDefault(e => e.Id == dto.Id)
                ?? await db.ChecklistEntries.FirstOrDefaultAsync(e => e.Id == dto.Id, cancellationToken).ConfigureAwait(false);

            if (entrada is null)
            {
                entrada = new ChecklistEntry(dto.Id, dto.SessionId, dto.BedId, dto.ChecklistTemplateId, dto.ChecklistColumnId, dto.UpdatedAtUtc);
                db.ChecklistEntries.Add(entrada);
            }

            if (dto.Version >= entrada.Version)
            {
                entrada.OverwriteFromServer(dto.IsCompleted, dto.Version, dto.UpdatedAtUtc);
            }
        }

        foreach (var dto in estado.Markers)
        {
            var marcador = db.SessionBedMarkers.Local.FirstOrDefault(m => m.Id == dto.Id)
                ?? await db.SessionBedMarkers.FirstOrDefaultAsync(m => m.Id == dto.Id, cancellationToken).ConfigureAwait(false);

            if (marcador is null)
            {
                marcador = new SessionBedMarker(dto.Id, dto.SessionId, dto.BedId, dto.MarkerDefinitionId, dto.IsSelected, dto.UpdatedAtUtc);
                db.SessionBedMarkers.Add(marcador);
            }

            if (dto.Version >= marcador.Version)
            {
                marcador.OverwriteFromServer(dto.IsSelected, dto.Version, dto.UpdatedAtUtc);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return local;
    }

    /// <summary>Marcadores ativos de cada leito na sessão, com Id e nome.</summary>
    private static async Task<Dictionary<Guid, List<(Guid Id, string Nome)>>> LoadMarkerNamesAsync(
        LocalDbContext db,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var definicoes = await db.BedMarkerDefinitions
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder)
            .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken)
            .ConfigureAwait(false);

        var selecionados = await db.SessionBedMarkers
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId && m.IsSelected)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return selecionados
            .Where(m => definicoes.ContainsKey(m.MarkerDefinitionId))
            .GroupBy(m => m.BedId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(m => (Id: m.MarkerDefinitionId, Nome: definicoes[m.MarkerDefinitionId])).ToList());
    }

    private async Task<SessionSummaryDto> BuildLocalSummaryAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await BuildLocalSummaryAsync(db, sessionId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    private static async Task<SessionSummaryDto> BuildLocalSummaryAsync(
        LocalDbContext db,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var sessao = await db.OperationalSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken).ConfigureAwait(false);

        if (sessao is null)
        {
            return new SessionSummaryDto(new ProgressDto(0, 0), [], []);
        }

        var entradas = await db.ChecklistEntries.AsNoTracking().Where(e => e.SessionId == sessionId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var templates = await db.ChecklistTemplates.AsNoTracking().Include(t => t.Columns).Include(t => t.Sectors).ToListAsync(cancellationToken).ConfigureAwait(false);
        var marcadores = await db.SessionBedMarkers.AsNoTracking().Where(m => m.SessionId == sessionId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var definicoes = await db.BedMarkerDefinitions.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        var codigos = await db.Beds
            .AsNoTracking()
            .Where(b => b.SectorId == sessao.SectorId)
            .ToDictionaryAsync(b => b.Id, b => b.Code, cancellationToken)
            .ConfigureAwait(false);

        var resumo = SessionSummaryCalculator.Build(
            entradas, templates.Where(t => t.AppliesTo(sessao.SectorId)), marcadores, definicoes, codigos);

        return new SessionSummaryDto(
            new ProgressDto(resumo.Overall.Total, resumo.Overall.Completed),
            [.. resumo.Templates.Select(t => new TemplateSummaryDto(
                t.TemplateId, t.TemplateName, new ProgressDto(t.Progress.Total, t.Progress.Completed),
                [.. t.Columns.Select(c => new ColumnSummaryDto(c.ColumnId, c.ColumnName, c.TriggerTime, new ProgressDto(c.Progress.Total, c.Progress.Completed)))]))],
            [.. resumo.Markers.Select(m => new MarkerSummaryDto(m.MarkerDefinitionId, m.MarkerName, m.BedCodes))]);
    }

    private static bool IsOverdue(ShiftWindow window, DateOnly serviceDate, ChecklistColumn column, DateTime nowLocal) =>
        NotificationPlanner.OverdueThreshold(window, serviceDate, column) is { } limite && nowLocal > limite;
}
