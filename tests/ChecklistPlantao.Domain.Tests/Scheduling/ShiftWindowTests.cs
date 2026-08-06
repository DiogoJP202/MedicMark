using ChecklistPlantao.Domain.Common;
using ChecklistPlantao.Domain.Scheduling;

namespace ChecklistPlantao.Domain.Tests.Scheduling;

public sealed class ShiftWindowTests
{
    private static readonly ShiftWindow Noturno = new(new TimeOnly(19, 0), new TimeOnly(7, 0));
    private static readonly ShiftWindow Diurno = new(new TimeOnly(7, 0), new TimeOnly(19, 0));

    [Fact]
    public void Plantao_noturno_atravessa_a_meia_noite()
    {
        Assert.True(Noturno.CrossesMidnight);
        Assert.Equal(TimeSpan.FromHours(12), Noturno.Duration);
    }

    [Fact]
    public void Plantao_diurno_nao_atravessa_a_meia_noite()
    {
        Assert.False(Diurno.CrossesMidnight);
        Assert.Equal(TimeSpan.FromHours(12), Diurno.Duration);
    }

    [Fact]
    public void Inicio_e_fim_iguais_sao_recusados()
    {
        var mesmaHora = new TimeOnly(19, 0);
        Assert.Throws<DomainRuleException>(() => new ShiftWindow(mesmaHora, mesmaHora));
    }

    [Theory]
    // Depois do início do turno: pertence ao plantão do próprio dia.
    [InlineData(2026, 8, 6, 19, 0, 2026, 8, 6)]
    [InlineData(2026, 8, 6, 20, 0, 2026, 8, 6)]
    [InlineData(2026, 8, 6, 23, 59, 2026, 8, 6)]
    // Madrugada: ainda é o plantão que começou ontem.
    [InlineData(2026, 8, 7, 0, 0, 2026, 8, 6)]
    [InlineData(2026, 8, 7, 2, 0, 2026, 8, 6)]
    [InlineData(2026, 8, 7, 6, 59, 2026, 8, 6)]
    public void Data_de_servico_da_madrugada_pertence_ao_plantao_do_dia_anterior(
        int ano, int mes, int dia, int hora, int minuto,
        int anoEsperado, int mesEsperado, int diaEsperado)
    {
        var instante = new DateTime(ano, mes, dia, hora, minuto, 0, DateTimeKind.Unspecified);

        var serviceDate = Noturno.ServiceDateFor(instante);

        Assert.Equal(new DateOnly(anoEsperado, mesEsperado, diaEsperado), serviceDate);
    }

    [Fact]
    public void Plantao_diurno_usa_sempre_o_proprio_dia()
    {
        var instante = new DateTime(2026, 8, 6, 3, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(new DateOnly(2026, 8, 6), Diurno.ServiceDateFor(instante));
    }

    [Theory]
    // Colunas do Gelo: 20H e 22H caem no dia da sessão...
    [InlineData(20, 0, 2026, 8, 6)]
    [InlineData(22, 0, 2026, 8, 6)]
    // ...e 00H a 06H caem no dia seguinte.
    [InlineData(0, 0, 2026, 8, 7)]
    [InlineData(2, 0, 2026, 8, 7)]
    [InlineData(4, 0, 2026, 8, 7)]
    [InlineData(6, 0, 2026, 8, 7)]
    // Glicemia: Jantar no dia, Café no dia seguinte.
    [InlineData(19, 30, 2026, 8, 6)]
    [InlineData(7, 0, 2026, 8, 7)]
    public void Ocorrencia_da_coluna_resolve_para_o_dia_correto(
        int hora, int minuto, int anoEsperado, int mesEsperado, int diaEsperado)
    {
        var serviceDate = new DateOnly(2026, 8, 6);

        var ocorrencia = Noturno.OccurrenceOf(serviceDate, new TimeOnly(hora, minuto));

        Assert.Equal(new DateTime(anoEsperado, mesEsperado, diaEsperado, hora, minuto, 0, DateTimeKind.Unspecified), ocorrencia);
    }

    [Fact]
    public void Ocorrencias_do_gelo_ficam_em_ordem_cronologica()
    {
        var serviceDate = new DateOnly(2026, 8, 6);
        TimeOnly[] colunas = [new(20, 0), new(22, 0), new(0, 0), new(2, 0), new(4, 0), new(6, 0)];

        var ocorrencias = colunas.Select(c => Noturno.OccurrenceOf(serviceDate, c)).ToList();

        Assert.Equal(ocorrencias.OrderBy(o => o).ToList(), ocorrencias);
    }

    [Theory]
    [InlineData(19, 0, true)]
    [InlineData(23, 0, true)]
    [InlineData(0, 0, true)]
    [InlineData(7, 0, true)]
    [InlineData(7, 1, false)]
    [InlineData(12, 0, false)]
    [InlineData(18, 59, false)]
    public void Contains_identifica_horarios_fora_da_janela(int hora, int minuto, bool esperado) =>
        Assert.Equal(esperado, Noturno.Contains(new TimeOnly(hora, minuto)));

    [Fact]
    public void Inicio_e_fim_do_plantao_sao_instantes_locais_consecutivos()
    {
        var serviceDate = new DateOnly(2026, 8, 6);

        Assert.Equal(new DateTime(2026, 8, 6, 19, 0, 0, DateTimeKind.Unspecified), Noturno.StartOf(serviceDate));
        Assert.Equal(new DateTime(2026, 8, 7, 7, 0, 0, DateTimeKind.Unspecified), Noturno.EndOf(serviceDate));
    }

    [Fact]
    public void Virada_de_mes_e_tratada_como_qualquer_outra()
    {
        var ultimoDia = new DateOnly(2026, 8, 31);

        Assert.Equal(
            new DateTime(2026, 9, 1, 2, 0, 0, DateTimeKind.Unspecified),
            Noturno.OccurrenceOf(ultimoDia, new TimeOnly(2, 0)));
    }

    [Fact]
    public void Virada_de_ano_bissexto_e_tratada_como_qualquer_outra()
    {
        var ultimoDiaDeFevereiro = new DateOnly(2028, 2, 28);

        Assert.Equal(
            new DateTime(2028, 2, 29, 6, 0, 0, DateTimeKind.Unspecified),
            Noturno.OccurrenceOf(ultimoDiaDeFevereiro, new TimeOnly(6, 0)));
    }
}
