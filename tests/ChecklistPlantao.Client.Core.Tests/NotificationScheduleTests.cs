using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Scheduling;
using ChecklistPlantao.Domain.Structure;

namespace ChecklistPlantao.Client.Core.Tests;

/// <summary>
/// O agendamento é o que faz o alerta chegar. Estes testes fixam as regras que o plantão sente:
/// coluna concluída não incomoda, sessão encerrada não alerta, e a madrugada cai no dia certo.
/// </summary>
public sealed class NotificationScheduleTests
{
    private static readonly ShiftWindow Noturno = new(new TimeOnly(19, 0), new TimeOnly(7, 0));
    private static readonly DateOnly ServiceDate = new(2026, 8, 6);
    private static readonly Guid Setor = Guid.CreateVersion7();
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static readonly DateTime AgoraUtc = new(2026, 8, 6, 19, 5, 0, DateTimeKind.Utc);

    private static NotificationConfiguration Config() => new(AgoraUtc);

    private static (ChecklistTemplate Template, ChecklistColumn Vinte, ChecklistColumn DuasDaManha) BuildGelo()
    {
        var template = new ChecklistTemplate(Guid.CreateVersion7(), "Gelo", "GELO", null, 10, AgoraUtc);
        var vinte = template.AddColumn(Guid.CreateVersion7(), "20H", new TimeOnly(20, 0), 10, AgoraUtc);
        var duas = template.AddColumn(Guid.CreateVersion7(), "02H", new TimeOnly(2, 0), 20, AgoraUtc);

        foreach (var coluna in new[] { vinte, duas })
        {
            coluna.ConfigureNotification(true, 0, 15, 10, 2, true, 5, AgoraUtc);
        }

        return (template, vinte, duas);
    }

    [Fact]
    public void Coluna_pendente_gera_alerta_da_hora_e_as_repeticoes()
    {
        var (template, vinte, duas) = BuildGelo();

        var agendamentos = NotificationScheduleBuilder.Build(
            Noturno, ServiceDate, Setor, "Oeste", template,
            new Dictionary<Guid, int> { [vinte.Id] = 8, [duas.Id] = 16 },
            Config(), Utc, AgoraUtc, sessionIsOpen: true);

        // Duas colunas × (1 alerta + 2 repetições).
        Assert.Equal(6, agendamentos.Count);
        Assert.Equal(agendamentos.OrderBy(a => a.FireAt).Select(a => a.Id), agendamentos.Select(a => a.Id));
    }

    [Fact]
    public void Coluna_ja_concluida_nao_gera_nenhum_alerta()
    {
        var (template, vinte, duas) = BuildGelo();

        var agendamentos = NotificationScheduleBuilder.Build(
            Noturno, ServiceDate, Setor, "Oeste", template,
            new Dictionary<Guid, int> { [vinte.Id] = 0, [duas.Id] = 4 },
            Config(), Utc, AgoraUtc, sessionIsOpen: true);

        Assert.All(agendamentos, a => Assert.Equal(duas.Id, a.ColumnId));
    }

    [Fact]
    public void Sessao_encerrada_cancela_tudo()
    {
        var (template, vinte, _) = BuildGelo();

        var agendamentos = NotificationScheduleBuilder.Build(
            Noturno, ServiceDate, Setor, "Oeste", template,
            new Dictionary<Guid, int> { [vinte.Id] = 5 },
            Config(), Utc, AgoraUtc, sessionIsOpen: false);

        Assert.Empty(agendamentos);
    }

    [Fact]
    public void Coluna_da_madrugada_e_agendada_para_o_dia_seguinte()
    {
        var (template, _, duas) = BuildGelo();

        var agendamentos = NotificationScheduleBuilder.Build(
            Noturno, ServiceDate, Setor, "Oeste", template,
            new Dictionary<Guid, int> { [duas.Id] = 3 },
            Config(), Utc, AgoraUtc, sessionIsOpen: true);

        var principal = agendamentos.First(a => a.Kind == NotificationOccurrenceKind.Due);

        Assert.Equal(new DateTime(2026, 8, 7, 2, 0, 0), principal.FireAt.DateTime);
    }

    [Fact]
    public void Alertas_ja_passados_nao_sao_reagendados()
    {
        var (template, vinte, duas) = BuildGelo();

        // 23h: as 20H já passaram, as 02H ainda vêm.
        var agora = new DateTime(2026, 8, 6, 23, 0, 0, DateTimeKind.Utc);

        var agendamentos = NotificationScheduleBuilder.Build(
            Noturno, ServiceDate, Setor, "Oeste", template,
            new Dictionary<Guid, int> { [vinte.Id] = 2, [duas.Id] = 2 },
            Config(), Utc, agora, sessionIsOpen: true);

        Assert.All(agendamentos, a => Assert.Equal(duas.Id, a.ColumnId));
    }

    [Fact]
    public void Identificador_do_alerta_e_estavel_para_o_reagendamento_substituir_em_vez_de_duplicar()
    {
        var (template, vinte, _) = BuildGelo();
        var pendentes = new Dictionary<Guid, int> { [vinte.Id] = 5 };

        var primeira = NotificationScheduleBuilder.Build(Noturno, ServiceDate, Setor, "Oeste", template, pendentes, Config(), Utc, AgoraUtc, true);
        var segunda = NotificationScheduleBuilder.Build(Noturno, ServiceDate, Setor, "Oeste", template, pendentes, Config(), Utc, AgoraUtc, true);

        Assert.Equal(primeira.Select(a => a.Id), segunda.Select(a => a.Id));
    }

    [Fact]
    public void Texto_do_alerta_usa_os_modelos_configurados()
    {
        var (template, vinte, _) = BuildGelo();

        var agendamento = NotificationScheduleBuilder.Build(
            Noturno, ServiceDate, Setor, "Oeste", template,
            new Dictionary<Guid, int> { [vinte.Id] = 8 },
            Config(), Utc, AgoraUtc, true).First();

        Assert.Contains("Gelo", agendamento.Title, StringComparison.Ordinal);
        Assert.Contains("20H", agendamento.Title, StringComparison.Ordinal);
        Assert.Contains("8", agendamento.Body, StringComparison.Ordinal);
        Assert.Contains("Oeste", agendamento.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Deep_link_leva_ao_checklist_e_a_coluna_corretos()
    {
        var (template, vinte, _) = BuildGelo();

        var agendamento = NotificationScheduleBuilder.Build(
            Noturno, ServiceDate, Setor, "Oeste", template,
            new Dictionary<Guid, int> { [vinte.Id] = 1 },
            Config(), Utc, AgoraUtc, true).First();

        Assert.Equal($"/checklist/{template.Id}/{vinte.Id}", agendamento.DeepLink);
    }

    [Fact]
    public void Coluna_sem_horario_nunca_entra_no_agendamento()
    {
        var template = new ChecklistTemplate(Guid.CreateVersion7(), "Glicemia", "GLICEMIA", null, 20, AgoraUtc);
        var livre = template.AddColumn(Guid.CreateVersion7(), "Livre", null, 10, AgoraUtc);

        var agendamentos = NotificationScheduleBuilder.Build(
            Noturno, ServiceDate, Setor, "Oeste", template,
            new Dictionary<Guid, int> { [livre.Id] = 10 },
            Config(), Utc, AgoraUtc, true);

        Assert.Empty(agendamentos);
    }

    [Fact]
    public void Contagem_de_pendencias_agrupa_por_coluna()
    {
        var sessao = Guid.CreateVersion7();
        var template = Guid.CreateVersion7();
        var colunaA = Guid.CreateVersion7();
        var colunaB = Guid.CreateVersion7();

        var entradas = new List<Domain.Operations.ChecklistEntry>
        {
            new(Guid.CreateVersion7(), sessao, Guid.CreateVersion7(), template, colunaA, AgoraUtc),
            new(Guid.CreateVersion7(), sessao, Guid.CreateVersion7(), template, colunaA, AgoraUtc),
            new(Guid.CreateVersion7(), sessao, Guid.CreateVersion7(), template, colunaB, AgoraUtc),
        };

        entradas[0].SetCompletion(true, AgoraUtc);

        var pendentes = NotificationScheduleBuilder.CountPending(entradas);

        Assert.Equal(1, pendentes[colunaA]);
        Assert.Equal(1, pendentes[colunaB]);
    }
}
