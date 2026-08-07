using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Operations;
using ChecklistPlantao.Contracts.Sync;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Domain.Structure;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Client.Core.Sync;

public sealed record SyncOutcome(bool ServerReached, int Sent, int Applied, int Conflicts, int Rejected, int Received, string? Error)
{
    public static SyncOutcome Unreachable(string? error = null) => new(false, 0, 0, 0, 0, 0, error);
}

/// <summary>
/// Motor de sincronização do dispositivo.
///
/// Ciclo: envia o que está na fila, recebe o que mudou desde o cursor e aplica localmente.
/// Nada aqui bloqueia a interface — a tela já foi atualizada quando o usuário tocou na célula.
///
/// Regras que este componente respeita:
///   • reenviar a mesma operação nunca duplica (o servidor decide pelo OperationId);
///   • conflito resolvido pelo servidor é adotado sem discussão;
///   • falha de envio vira nova tentativa com espera crescente, nunca laço apertado;
///   • se o servidor disser que o cursor é velho demais, refaz-se o bootstrap.
/// </summary>
public sealed class SyncEngine(
    LocalDbContext db,
    OutboxWriter outbox,
    IServerApi api,
    IClock clock,
    ILogger<SyncEngine> logger)
{
    /// <summary>Operações por lote. Suficiente para um plantão inteiro sem estourar a rede.</summary>
    public const int BatchSize = 100;

    public async Task<SyncOutcome> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        if (!api.IsReachable)
        {
            return SyncOutcome.Unreachable();
        }

        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);

        if (!state.BootstrapCompleted)
        {
            var bootstrapped = await BootstrapAsync(cancellationToken).ConfigureAwait(false);

            if (!bootstrapped)
            {
                return SyncOutcome.Unreachable("Não foi possível carregar a configuração do servidor.");
            }
        }

        var push = await PushAsync(cancellationToken).ConfigureAwait(false);
        var received = await PullAsync(cancellationToken).ConfigureAwait(false);

        return push with { Received = received };
    }

    /// <summary>Baixa a configuração completa e a grava localmente. Usado no primeiro uso e na recuperação.</summary>
    public async Task<bool> BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var bootstrap = await api.BootstrapAsync(cancellationToken).ConfigureAwait(false);

        if (bootstrap is null)
        {
            return false;
        }

        var now = clock.UtcNow;

        await ApplyConfigurationAsync(bootstrap, now, cancellationToken).ConfigureAwait(false);

        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        state.MarkBootstrapped(bootstrap.SyncCursor, now);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Bootstrap concluído: {Setores} setor(es), {Leitos} leito(s), {Templates} tipo(s). Cursor {Cursor}.",
            bootstrap.Sectors.Count,
            bootstrap.Beds.Count,
            bootstrap.Templates.Count,
            bootstrap.SyncCursor);

        return true;
    }

    private async Task<SyncOutcome> PushAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var itens = await outbox.TakeReadyAsync(now, BatchSize, cancellationToken).ConfigureAwait(false);

        if (itens.Count == 0)
        {
            return new SyncOutcome(true, 0, 0, 0, 0, 0, null);
        }

        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        var device = await GetDeviceAsync(cancellationToken).ConfigureAwait(false);

        var operacoes = itens
            .Select(i => new SyncOperationDto(
                i.OperationId,
                i.EntityType,
                i.EntityId,
                i.OperationType.ToString(),
                i.Payload,
                i.BaseVersion,
                i.CreatedAtUtc))
            .ToList();

        foreach (var item in itens)
        {
            item.MarkInFlight();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        SyncPushResponse? resposta;

        try
        {
            resposta = await api
                .PushAsync(new SyncPushRequest(device.DeviceId.ToString(), state.Cursor, operacoes), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAllAsync(itens, ex.Message, now, cancellationToken).ConfigureAwait(false);
            return SyncOutcome.Unreachable(ex.Message);
        }

        if (resposta is null)
        {
            await FailAllAsync(itens, "O servidor não respondeu.", now, cancellationToken).ConfigureAwait(false);
            return SyncOutcome.Unreachable();
        }

        var aplicadas = 0;
        var conflitos = 0;
        var rejeitadas = 0;

        var porOperacao = itens.ToDictionary(i => i.OperationId);

        foreach (var resultado in resposta.Results)
        {
            if (!porOperacao.TryGetValue(resultado.OperationId, out var item))
            {
                continue;
            }

            switch (resultado.Status)
            {
                case nameof(SyncOperationStatus.Applied):
                case nameof(SyncOperationStatus.Duplicate):
                case nameof(SyncOperationStatus.NoChange):
                    item.MarkDone();
                    aplicadas++;
                    break;

                case nameof(SyncOperationStatus.Conflict):
                    // O servidor venceu. Adotamos o estado dele e o item sai da fila: insistir
                    // só reenviaria a mesma perda.
                    item.MarkDone();
                    conflitos++;
                    await AdoptServerStateAsync(item.EntityType, resultado.CurrentState, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    item.MarkDone();
                    rejeitadas++;
                    logger.LogWarning("Operação {OperationId} recusada: {Motivo}", resultado.OperationId, resultado.Reason);
                    break;
            }
        }

        await ApplyChangesAsync(resposta.Changes, cancellationToken).ConfigureAwait(false);

        state.AdvanceCursor(resposta.Cursor, clock.UtcNow);

        // A fila só guarda o que ainda não foi resolvido. Manter itens concluídos faria o banco
        // do aparelho crescer sem limite ao longo dos meses.
        db.Outbox.RemoveRange(itens.Where(i => i.Status == OutboxItemStatus.Done));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SyncOutcome(true, itens.Count, aplicadas, conflitos, rejeitadas, resposta.Changes.Count, null);
    }

    private async Task<int> PullAsync(CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);

        SyncPullResponse? resposta;

        try
        {
            resposta = await api.PullAsync(state.Cursor, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falha ao buscar alterações do servidor.");
            return 0;
        }

        if (resposta is null)
        {
            return 0;
        }

        if (resposta.RequiresBootstrap)
        {
            logger.LogInformation("O cursor local ficou para trás do log do servidor. Refazendo o bootstrap.");
            state.RequireBootstrap();
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BootstrapAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        await ApplyChangesAsync(resposta.Changes, cancellationToken).ConfigureAwait(false);

        state.AdvanceCursor(resposta.Cursor, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return resposta.Changes.Count;
    }

    /// <summary>Aplica as alterações vindas do servidor sobre o estado local.</summary>
    private async Task ApplyChangesAsync(IReadOnlyList<ServerChangeDto> changes, CancellationToken cancellationToken)
    {
        foreach (var change in changes)
        {
            if (change.Payload is null)
            {
                continue;
            }

            switch (change.EntityType)
            {
                case SyncEntityTypes.ChecklistEntry:
                    await ApplyEntryAsync(change.Payload, cancellationToken).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.SessionBedMarker:
                    await ApplyMarkerAsync(change.Payload, cancellationToken).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.OperationalSession:
                    await ApplySessionAsync(change.Payload, cancellationToken).ConfigureAwait(false);
                    break;

                case SyncEntityTypes.Sector:
                case SyncEntityTypes.Bed:
                case SyncEntityTypes.ChecklistTemplate:
                case SyncEntityTypes.BedMarkerDefinition:
                case SyncEntityTypes.NotificationConfiguration:
                case SyncEntityTypes.AppSetting:
                    // Alterações de configuração são raras e interdependentes (uma coluna nova
                    // muda o template). Recarregar o bootstrap inteiro é mais simples e seguro
                    // do que aplicar mudanças parciais e arriscar um estado incoerente.
                    await BootstrapAsync(cancellationToken).ConfigureAwait(false);
                    return;

                default:
                    break;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task AdoptServerStateAsync(string entityType, string? payload, CancellationToken cancellationToken)
    {
        if (payload is null)
        {
            return Task.CompletedTask;
        }

        return entityType switch
        {
            SyncEntityTypes.ChecklistEntry => ApplyEntryAsync(payload, cancellationToken),
            SyncEntityTypes.SessionBedMarker => ApplyMarkerAsync(payload, cancellationToken),
            _ => Task.CompletedTask,
        };
    }

    private async Task ApplyEntryAsync(string payload, CancellationToken cancellationToken)
    {
        var dto = SyncJson.Deserialize<ChecklistEntryDto>(payload);

        if (dto is null)
        {
            return;
        }

        var local = await db.ChecklistEntries.FirstOrDefaultAsync(e => e.Id == dto.Id, cancellationToken).ConfigureAwait(false)
            ?? await db.ChecklistEntries.FirstOrDefaultAsync(
                e => e.SessionId == dto.SessionId
                    && e.BedId == dto.BedId
                    && e.ChecklistTemplateId == dto.ChecklistTemplateId
                    && e.ChecklistColumnId == dto.ChecklistColumnId,
                cancellationToken).ConfigureAwait(false);

        if (local is null)
        {
            local = new ChecklistEntry(dto.Id, dto.SessionId, dto.BedId, dto.ChecklistTemplateId, dto.ChecklistColumnId, dto.UpdatedAtUtc);
            db.ChecklistEntries.Add(local);
        }

        // Só adota se a versão do servidor for mais nova. Uma resposta atrasada não pode
        // sobrescrever uma marcação que o usuário acabou de fazer.
        if (dto.Version >= local.Version)
        {
            local.OverwriteFromServer(dto.IsCompleted, dto.Version, dto.UpdatedAtUtc);
        }
    }

    private async Task ApplyMarkerAsync(string payload, CancellationToken cancellationToken)
    {
        var dto = SyncJson.Deserialize<SessionBedMarkerDto>(payload);

        if (dto is null)
        {
            return;
        }

        var local = await db.SessionBedMarkers.FirstOrDefaultAsync(m => m.Id == dto.Id, cancellationToken).ConfigureAwait(false)
            ?? await db.SessionBedMarkers.FirstOrDefaultAsync(
                m => m.SessionId == dto.SessionId && m.BedId == dto.BedId && m.MarkerDefinitionId == dto.MarkerDefinitionId,
                cancellationToken).ConfigureAwait(false);

        if (local is null)
        {
            local = new SessionBedMarker(dto.Id, dto.SessionId, dto.BedId, dto.MarkerDefinitionId, dto.IsSelected, dto.UpdatedAtUtc);
            db.SessionBedMarkers.Add(local);
        }

        if (dto.Version >= local.Version)
        {
            local.OverwriteFromServer(dto.IsSelected, dto.Version, dto.UpdatedAtUtc);
        }
    }

    private async Task ApplySessionAsync(string payload, CancellationToken cancellationToken)
    {
        var dto = SyncJson.Deserialize<OperationalSessionDto>(payload);

        if (dto is null)
        {
            return;
        }

        var local = await db.OperationalSessions
            .Include(s => s.Beds)
            .FirstOrDefaultAsync(s => s.Id == dto.Id, cancellationToken)
            .ConfigureAwait(false);

        if (local is null)
        {
            local = new OperationalSession(dto.Id, dto.SectorId, dto.ServiceDate, dto.StartedAtUtc);
            db.OperationalSessions.Add(local);
        }

        if (dto.Status == nameof(SessionStatus.Closed) && local.IsOpen)
        {
            local.Close(dto.ClosedAtUtc ?? clock.UtcNow);
        }
        else if (dto.Status == nameof(SessionStatus.Open) && !local.IsOpen)
        {
            local.Reopen(clock.UtcNow);
        }
    }

    /// <summary>Substitui a configuração local pela do servidor. Dados operacionais não são tocados.</summary>
    private async Task ApplyConfigurationAsync(BootstrapResponse bootstrap, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await ReplaceAsync(db.Sectors, bootstrap.Sectors.Select(s =>
        {
            var setor = new Sector(s.Id, s.Name, s.Description, s.SortOrder, nowUtc);
            setor.SetShiftOverride(s.ShiftStart, s.ShiftEnd, nowUtc);
            setor.SetActive(s.IsActive, nowUtc);
            return setor;
        }), cancellationToken).ConfigureAwait(false);

        await ReplaceAsync(db.Beds, bootstrap.Beds.Select(b =>
        {
            var leito = new Bed(b.Id, b.SectorId, b.Code, b.Description, b.SortOrder, nowUtc);
            leito.SetActive(b.IsActive, nowUtc);
            return leito;
        }), cancellationToken).ConfigureAwait(false);

        var templatesAntigos = await db.ChecklistTemplates.Include(t => t.Columns).Include(t => t.Sectors).ToListAsync(cancellationToken).ConfigureAwait(false);
        db.ChecklistTemplates.RemoveRange(templatesAntigos);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var dto in bootstrap.Templates)
        {
            var template = new ChecklistTemplate(dto.Id, dto.Name, dto.Code, dto.Description, dto.SortOrder, nowUtc);
            template.SetActive(dto.IsActive, nowUtc);
            template.ReplaceSectors(dto.SectorIds, nowUtc);

            foreach (var coluna in dto.Columns)
            {
                var criada = template.AddColumn(coluna.Id, coluna.DisplayName, coluna.TriggerTime, coluna.SortOrder, nowUtc);
                criada.SetActive(coluna.IsActive, nowUtc);

                if (coluna.TriggerTime.HasValue)
                {
                    criada.ConfigureNotification(
                        coluna.NotificationEnabled,
                        coluna.LeadTimeMinutes,
                        coluna.GracePeriodMinutes,
                        coluna.RepeatIntervalMinutes,
                        coluna.MaximumRepeats,
                        coluna.AllowSnooze,
                        coluna.SnoozeMinutes,
                        nowUtc);
                }
            }

            db.ChecklistTemplates.Add(template);
        }

        await ReplaceAsync(db.BedMarkerDefinitions, bootstrap.Markers.Select(m =>
        {
            var marcador = new BedMarkerDefinition(m.Id, m.Name, m.Code, m.SortOrder, nowUtc);
            marcador.SetActive(m.IsActive, nowUtc);
            return marcador;
        }), cancellationToken).ConfigureAwait(false);

        await ReplaceSettingsAsync(bootstrap.Settings, nowUtc, cancellationToken).ConfigureAwait(false);
        await ReplaceNotificationConfigurationAsync(bootstrap.Notifications, nowUtc, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplaceAsync<T>(DbSet<T> set, IEnumerable<T> novos, CancellationToken cancellationToken)
        where T : class
    {
        set.RemoveRange(await set.ToListAsync(cancellationToken).ConfigureAwait(false));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await set.AddRangeAsync(novos, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplaceSettingsAsync(InstitutionSettingsDto dto, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var settings = new InstitutionSettings
        {
            TimeZoneId = dto.TimeZoneId,
            ShiftStart = dto.ShiftStart,
            ShiftEnd = dto.ShiftEnd,
            RetentionAfterClose = TimeSpan.FromHours(dto.RetentionAfterCloseHours),
            OfflineLoginValidity = TimeSpan.FromDays(dto.OfflineLoginValidityDays),
            OfflineLoginMaxAttempts = dto.OfflineLoginMaxAttempts,
            AutoOpenSession = dto.AutoOpenSession,
        };

        var existentes = await db.AppSettings.ToDictionaryAsync(s => s.Key, s => s, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);

        foreach (var (chave, valor) in Application.Configuration.InstitutionSettingsSerializer.Flatten(settings))
        {
            if (existentes.TryGetValue(chave, out var existente))
            {
                existente.SetValue(valor, nowUtc);
            }
            else
            {
                db.AppSettings.Add(new AppSetting(chave, valor, nowUtc));
            }
        }
    }

    private async Task ReplaceNotificationConfigurationAsync(NotificationConfigurationDto dto, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var atual = await db.NotificationConfigurations.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (atual is null)
        {
            atual = new NotificationConfiguration(nowUtc);
            db.NotificationConfigurations.Add(atual);
        }

        if (!Enum.TryParse<NotificationPriority>(dto.Priority, ignoreCase: true, out var prioridade))
        {
            prioridade = NotificationPriority.High;
        }

        atual.Update(
            dto.SoundEnabled,
            dto.VibrationEnabled,
            prioridade,
            dto.EnabledOnAndroid,
            dto.EnabledOnWindows,
            dto.AllowFullScreenIntent,
            dto.TitleTemplate,
            dto.BodyTemplate,
            nowUtc);
    }

    private async Task FailAllAsync(IReadOnlyList<SyncOutboxItem> itens, string erro, DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var item in itens)
        {
            item.MarkFailed(erro, RetryBackoff.NextAttempt(item.RetryCount, nowUtc));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SyncState> GetStateAsync(CancellationToken cancellationToken)
    {
        var state = await db.SyncState.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (state is null)
        {
            state = new SyncState();
            db.SyncState.Add(state);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return state;
    }

    private async Task<DeviceState> GetDeviceAsync(CancellationToken cancellationToken)
    {
        var device = await db.DeviceState.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (device is null)
        {
            device = new DeviceState();
            db.DeviceState.Add(device);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return device;
    }
}
