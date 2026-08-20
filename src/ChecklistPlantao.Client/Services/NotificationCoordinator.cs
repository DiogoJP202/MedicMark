using ChecklistPlantao.Client.Core.Notifications;
using Microsoft.Extensions.DependencyInjection;
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
    ILogger<NotificationCoordinator> logger) : IDeviceStartupRescheduler
{
    public async Task RescheduleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var escopo = services.CreateScope();
            var planner = escopo.ServiceProvider.GetRequiredService<LocalNotificationPlanService>();
            var planejados = await planner.BuildAsync(cancellationToken).ConfigureAwait(false);

            await scheduler.CancelAllAsync(cancellationToken).ConfigureAwait(false);
            await scheduler.ScheduleAsync(planejados, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("{Quantidade} alerta(s) reagendado(s) neste dispositivo.", planejados.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Falhar ao reagendar não pode derrubar o aplicativo. O usuário perde alertas, e é
            // exatamente isso que a faixa de saúde das notificações vai denunciar.
            logger.LogError(ex, "Falha ao reagendar as notificações deste dispositivo.");
        }
    }
}
