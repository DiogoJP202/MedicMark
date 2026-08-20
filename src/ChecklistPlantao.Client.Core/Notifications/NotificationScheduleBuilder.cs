using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Scheduling;
using ChecklistPlantao.Domain.Structure;

namespace ChecklistPlantao.Client.Core.Notifications;

/// <summary>Estado de uma coluna no plantão, para decidir se ainda vale alertar.</summary>
public sealed record ColumnPendingState(Guid ColumnId, int Pending);

/// <summary>
/// Transforma configuração + estado do plantão na lista concreta de alertas a agendar.
///
/// Duas regras que o enunciado exige e que ficam visíveis aqui:
///   • coluna sem pendência não gera repetição — quem já terminou não é incomodado;
///   • sessão encerrada não gera alerta nenhum.
///
/// Todo o cálculo é puro: recebe o instante e o fuso, não os consulta. É o que torna possível
/// testar a travessia da meia-noite sem depender do relógio da máquina.
/// </summary>
public static class NotificationScheduleBuilder
{
    public static IReadOnlyList<ScheduledNotification> Build(
        ShiftWindow window,
        DateOnly serviceDate,
        Guid sectorId,
        string sectorName,
        ChecklistTemplate template,
        IReadOnlyDictionary<Guid, int> pendingByColumn,
        NotificationConfiguration configuration,
        TimeZoneInfo timeZone,
        DateTime nowUtc,
        bool sessionIsOpen)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(pendingByColumn);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (!sessionIsOpen)
        {
            return [];
        }

        var agendamentos = new List<ScheduledNotification>();
        var agoraLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), timeZone);

        foreach (var coluna in template.ActiveColumnsInOrder)
        {
            var pendentes = pendingByColumn.TryGetValue(coluna.Id, out var valor) ? valor : 0;

            // Coluna já concluída: nada a lembrar. É o cancelamento das repetições exigido
            // pelo item 16 do enunciado, obtido simplesmente não agendando.
            if (pendentes == 0)
            {
                continue;
            }

            foreach (var ocorrencia in NotificationPlanner.Plan(window, serviceDate, coluna))
            {
                if (ocorrencia.FireAtLocal <= agoraLocal)
                {
                    continue;
                }

                agendamentos.Add(Create(ocorrencia, coluna, template, sectorId, sectorName, pendentes, configuration, timeZone));
            }
        }

        return [.. agendamentos.OrderBy(a => a.FireAt)];
    }

    private static ScheduledNotification Create(
        NotificationOccurrence ocorrencia,
        ChecklistColumn coluna,
        ChecklistTemplate template,
        Guid sectorId,
        string sectorName,
        int pendentes,
        NotificationConfiguration configuration,
        TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(ocorrencia.FireAtLocal);
        var disparo = new DateTimeOffset(ocorrencia.FireAtLocal, offset);

        var titulo = NotificationConfiguration.Render(
            configuration.TitleTemplate, template.Name, coluna.DisplayName, sectorName, pendentes);

        var corpo = NotificationConfiguration.Render(
            configuration.BodyTemplate, template.Name, coluna.DisplayName, sectorName, pendentes);

        return new ScheduledNotification(
            ScheduledNotification.BuildId(coluna.Id, ocorrencia.ServiceDate, ocorrencia.Kind, ocorrencia.RepeatIndex),
            disparo,
            titulo,
            corpo,
            sectorId,
            template.Id,
            coluna.Id,
            ocorrencia.ServiceDate,
            ocorrencia.Kind,
            ocorrencia.RepeatIndex,
            coluna.AllowSnooze,
            coluna.SnoozeMinutes);
    }
}
