using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Client.Services;

/// <summary>
/// Recalcula e reprograma todos os alertas do aparelho.
///
/// É chamado depois de sincronizar, ao abrir o aplicativo, ao voltar do segundo plano e — no
/// Android — depois que o aparelho reinicia. Sempre recalcula do zero: os identificadores são
/// estáveis, então o sistema substitui em vez de acumular.
/// </summary>
public sealed class NotificationCoordinator(
    IServiceProvider services,
    ILocalNotificationScheduler scheduler,
    IInstitutionSettingsProvider settings,
    IInstitutionTimeZone timeZone,
    IClock clock,
    ILogger<NotificationCoordinator> logger) : IDeviceStartupRescheduler
{
    public async Task RescheduleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var escopo = services.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<LocalDbContext>();

            var dispositivo = await db.DeviceState.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (dispositivo?.CurrentSectorId is not { } sectorId)
            {
                await scheduler.CancelAllAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var sessao = await db.OperationalSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SectorId == sectorId && s.Status == SessionStatus.Open, cancellationToken)
                .ConfigureAwait(false);

            if (sessao is null)
            {
                // Sem plantão aberto não há o que alertar. Cancelar evita o pior tipo de bug de
                // notificação: o alerta fantasma de um plantão que já acabou.
                await scheduler.CancelAllAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var setor = await db.Sectors.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectorId, cancellationToken).ConfigureAwait(false);
            var configuracoes = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
            var janela = ShiftResolver.For(configuracoes, setor);

            var notificacoes = await db.NotificationConfigurations.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? new NotificationConfiguration(clock.UtcNow);

            var templates = await db.ChecklistTemplates
                .AsNoTracking()
                .Include(t => t.Columns)
                .Include(t => t.Sectors)
                .Where(t => t.IsActive)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var entradas = await db.ChecklistEntries
                .AsNoTracking()
                .Where(e => e.SessionId == sessao.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var leitosAtivos = await db.Beds
                .AsNoTracking()
                .CountAsync(b => b.SectorId == sectorId && b.IsActive, cancellationToken)
                .ConfigureAwait(false);

            var planejados = new List<ScheduledNotification>();

            foreach (var template in templates.Where(t => t.AppliesTo(sectorId)))
            {
                var doTemplate = entradas.Where(e => e.ChecklistTemplateId == template.Id).ToList();
                var pendentes = ContarPendentes(template, doTemplate, leitosAtivos);

                planejados.AddRange(NotificationScheduleBuilder.Build(
                    janela, sessao.ServiceDate, sectorId, setor?.Name ?? string.Empty,
                    template, pendentes, notificacoes, timeZone.TimeZone, clock.UtcNow, sessao.IsOpen));
            }

            await scheduler.CancelAllAsync(cancellationToken).ConfigureAwait(false);
            await scheduler.ScheduleAsync(planejados, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("{Quantidade} alerta(s) reagendado(s) para o setor {SectorId}.", planejados.Count, sectorId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Falhar ao reagendar não pode derrubar o aplicativo. O usuário perde alertas, e é
            // exatamente isso que a faixa de saúde das notificações vai denunciar.
            logger.LogError(ex, "Falha ao reagendar as notificações deste dispositivo.");
        }
    }

    /// <summary>
    /// Pendências por coluna. Células que ainda não existem localmente contam como pendentes —
    /// um leito sem marcação nenhuma é justamente o que precisa de alerta.
    /// </summary>
    private static IReadOnlyDictionary<Guid, int> ContarPendentes(
        Domain.Structure.ChecklistTemplate template,
        IReadOnlyList<ChecklistEntry> entradas,
        int leitosAtivos)
    {
        var resultado = new Dictionary<Guid, int>();

        foreach (var coluna in template.ActiveColumnsInOrder)
        {
            var concluidas = entradas.Count(e => e.ChecklistColumnId == coluna.Id && e.IsCompleted);
            resultado[coluna.Id] = Math.Max(0, leitosAtivos - concluidas);
        }

        return resultado;
    }
}
