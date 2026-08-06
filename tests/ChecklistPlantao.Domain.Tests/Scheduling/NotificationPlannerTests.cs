using ChecklistPlantao.Domain.Scheduling;

namespace ChecklistPlantao.Domain.Tests.Scheduling;

public sealed class NotificationPlannerTests
{
    private static readonly ShiftWindow Noturno = new(new TimeOnly(19, 0), new TimeOnly(7, 0));
    private static readonly DateOnly ServiceDate = new(2026, 8, 6);

    [Fact]
    public void Coluna_sem_horario_nao_gera_agendamento()
    {
        var template = TestData.Template();
        var coluna = template.Column("Livre", triggerTime: null);

        Assert.Empty(NotificationPlanner.Plan(Noturno, ServiceDate, coluna));
    }

    [Fact]
    public void Coluna_com_notificacao_desligada_nao_gera_agendamento()
    {
        var template = TestData.Template();
        var coluna = template.Column("22H", new TimeOnly(22, 0)).WithNotification(enabled: false);

        Assert.Empty(NotificationPlanner.Plan(Noturno, ServiceDate, coluna));
    }

    [Fact]
    public void Coluna_inativa_nao_gera_agendamento()
    {
        var template = TestData.Template();
        var coluna = template.Column("22H", new TimeOnly(22, 0)).WithNotification();
        coluna.SetActive(false, TestData.NowUtc);

        Assert.Empty(NotificationPlanner.Plan(Noturno, ServiceDate, coluna));
    }

    [Fact]
    public void Plano_tem_o_alerta_da_hora_e_as_repeticoes_apos_a_tolerancia()
    {
        var template = TestData.Template();
        var coluna = template.Column("22H", new TimeOnly(22, 0))
            .WithNotification(gracePeriodMinutes: 15, repeatIntervalMinutes: 10, maximumRepeats: 3);

        var plano = NotificationPlanner.Plan(Noturno, ServiceDate, coluna);

        Assert.Equal(4, plano.Count);
        Assert.Equal(new DateTime(2026, 8, 6, 22, 0, 0), plano[0].FireAtLocal);
        Assert.Equal(NotificationOccurrenceKind.Due, plano[0].Kind);
        Assert.Equal(new DateTime(2026, 8, 6, 22, 15, 0), plano[1].FireAtLocal);
        Assert.Equal(new DateTime(2026, 8, 6, 22, 25, 0), plano[2].FireAtLocal);
        Assert.Equal(new DateTime(2026, 8, 6, 22, 35, 0), plano[3].FireAtLocal);
        Assert.All(plano.Skip(1), o => Assert.Equal(NotificationOccurrenceKind.Reminder, o.Kind));
    }

    [Fact]
    public void Antecedencia_gera_um_alerta_antes_da_hora()
    {
        var template = TestData.Template();
        var coluna = template.Column("Jantar", new TimeOnly(19, 30))
            .WithNotification(leadTimeMinutes: 10, maximumRepeats: 0);

        var plano = NotificationPlanner.Plan(Noturno, ServiceDate, coluna);

        Assert.Equal(2, plano.Count);
        Assert.Equal(NotificationOccurrenceKind.Lead, plano[0].Kind);
        Assert.Equal(new DateTime(2026, 8, 6, 19, 20, 0), plano[0].FireAtLocal);
        Assert.Equal(new DateTime(2026, 8, 6, 19, 30, 0), plano[1].FireAtLocal);
    }

    [Fact]
    public void Coluna_da_madrugada_e_agendada_para_o_dia_seguinte()
    {
        var template = TestData.Template();
        var coluna = template.Column("02H", new TimeOnly(2, 0)).WithNotification(maximumRepeats: 0);

        var plano = NotificationPlanner.Plan(Noturno, ServiceDate, coluna);

        Assert.Equal(new DateTime(2026, 8, 7, 2, 0, 0), Assert.Single(plano).FireAtLocal);
    }

    [Fact]
    public void Repeticoes_da_madrugada_nao_voltam_para_o_dia_anterior()
    {
        var template = TestData.Template();
        var coluna = template.Column("00H", new TimeOnly(0, 0))
            .WithNotification(gracePeriodMinutes: 15, repeatIntervalMinutes: 30, maximumRepeats: 2);

        var plano = NotificationPlanner.Plan(Noturno, ServiceDate, coluna);

        Assert.All(plano, o => Assert.Equal(new DateOnly(2026, 8, 7), DateOnly.FromDateTime(o.FireAtLocal)));
        Assert.Equal(new DateTime(2026, 8, 7, 0, 45, 0), plano[^1].FireAtLocal);
    }

    [Fact]
    public void Proximos_disparos_ignoram_o_que_ja_passou()
    {
        var template = TestData.Template();
        var colunas = new[]
        {
            template.Column("20H", new TimeOnly(20, 0), 10).WithNotification(maximumRepeats: 0),
            template.Column("22H", new TimeOnly(22, 0), 20).WithNotification(maximumRepeats: 0),
            template.Column("02H", new TimeOnly(2, 0), 30).WithNotification(maximumRepeats: 0),
        };

        // 21:00 do dia 6: 20H já passou, 22H e 02H ainda vêm.
        var agora = new DateTime(2026, 8, 6, 21, 0, 0);

        var proximos = NotificationPlanner.UpcomingFor(Noturno, agora, colunas, shiftsAhead: 0);

        Assert.Equal(2, proximos.Count);
        Assert.Equal(new DateTime(2026, 8, 6, 22, 0, 0), proximos[0].FireAtLocal);
        Assert.Equal(new DateTime(2026, 8, 7, 2, 0, 0), proximos[1].FireAtLocal);
    }

    [Fact]
    public void Proximos_disparos_incluem_o_plantao_seguinte_quando_pedido()
    {
        var template = TestData.Template();
        var coluna = template.Column("20H", new TimeOnly(20, 0)).WithNotification(maximumRepeats: 0);

        var agora = new DateTime(2026, 8, 6, 21, 0, 0);

        var proximos = NotificationPlanner.UpcomingFor(Noturno, agora, [coluna], shiftsAhead: 1);

        Assert.Equal(new DateTime(2026, 8, 7, 20, 0, 0), Assert.Single(proximos).FireAtLocal);
    }

    [Fact]
    public void Proximos_disparos_saem_em_ordem_cronologica()
    {
        var template = TestData.Template();
        var colunas = new[]
        {
            template.Column("06H", new TimeOnly(6, 0), 10).WithNotification(maximumRepeats: 1),
            template.Column("20H", new TimeOnly(20, 0), 20).WithNotification(maximumRepeats: 1),
            template.Column("00H", new TimeOnly(0, 0), 30).WithNotification(maximumRepeats: 1),
        };

        var proximos = NotificationPlanner.UpcomingFor(Noturno, new DateTime(2026, 8, 6, 19, 5, 0), colunas, shiftsAhead: 1);

        Assert.Equal(proximos.OrderBy(p => p.FireAtLocal).ToList(), proximos);
    }

    [Fact]
    public void Limite_de_atraso_e_a_hora_da_coluna_mais_a_tolerancia()
    {
        var template = TestData.Template();
        var coluna = template.Column("22H", new TimeOnly(22, 0)).WithNotification(gracePeriodMinutes: 20);

        var limite = NotificationPlanner.OverdueThreshold(Noturno, ServiceDate, coluna);

        Assert.Equal(new DateTime(2026, 8, 6, 22, 20, 0), limite);
    }

    [Fact]
    public void Limite_de_atraso_e_nulo_para_coluna_sem_horario()
    {
        var template = TestData.Template();
        var coluna = template.Column("Livre", triggerTime: null);

        Assert.Null(NotificationPlanner.OverdueThreshold(Noturno, ServiceDate, coluna));
    }
}
