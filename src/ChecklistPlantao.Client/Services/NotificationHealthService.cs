using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Client.Services;

/// <summary>
/// Lista, em português simples, tudo que impede este aparelho de alertar de forma confiável.
///
/// A regra é uma só e vale para toda a tela: se há qualquer item nesta lista, o aplicativo NÃO
/// diz que as notificações estão em ordem. É o que evita a promessa falsa de "alertas ativos"
/// enquanto uma permissão essencial está negada.
/// </summary>
public sealed class NotificationHealthService(
    INotificationPermissionService permissions,
    ILocalNotificationScheduler scheduler,
    IServiceProvider services) : INotificationHealthService
{
    public async Task<IReadOnlyList<string>> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        var problemas = new List<string>();
        var estado = await permissions.GetAsync(cancellationToken).ConfigureAwait(false);

        if (!estado.NotificationsGranted)
        {
            problemas.Add("A permissão de notificações está negada. Nenhum alerta será exibido.");
        }

        if (estado.ExactAlarmGranted == false)
        {
            problemas.Add("Sem permissão para alarmes exatos: os alertas podem atrasar alguns minutos.");
        }

        if (!estado.BatteryOptimizationIgnored)
        {
            problemas.Add("A economia de bateria está ativa para este aplicativo e pode adiar os alertas.");
        }

        if (scheduler.RequiresAppRunning)
        {
            problemas.Add("Nesta plataforma os alertas só funcionam com o aplicativo aberto, mesmo que minimizado.");
        }

        using var escopo = services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<LocalDbContext>();

        var configuracao = await db.NotificationConfigurations.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (configuracao is not null)
        {
            var plataforma = DeviceInfo.Current.Platform == DevicePlatform.Android
                ? Domain.Notifications.DevicePlatform.Android
                : Domain.Notifications.DevicePlatform.Windows;

            if (!configuracao.IsEnabledFor(plataforma))
            {
                problemas.Add("O administrador desativou as notificações para esta plataforma.");
            }
        }

        var dispositivo = await db.DeviceState.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (dispositivo?.CurrentSectorId is null)
        {
            problemas.Add("Nenhum setor selecionado: não há plantão para alertar.");
        }

        return problemas;
    }
}
