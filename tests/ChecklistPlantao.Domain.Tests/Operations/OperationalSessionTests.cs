using ChecklistPlantao.Domain.Common;
using ChecklistPlantao.Domain.Operations;
using ChecklistPlantao.Domain.Settings;

namespace ChecklistPlantao.Domain.Tests.Operations;

public sealed class OperationalSessionTests
{
    private static readonly Guid Setor = Guid.CreateVersion7();
    private static readonly DateOnly ServiceDate = new(2026, 8, 6);

    private static OperationalSession NovaSessao() =>
        new(Guid.CreateVersion7(), Setor, ServiceDate, TestData.NowUtc);

    [Fact]
    public void Sessao_nasce_aberta_e_versionada()
    {
        var sessao = NovaSessao();

        Assert.True(sessao.IsOpen);
        Assert.Equal(SessionStatus.Open, sessao.Status);
        Assert.Equal(1, sessao.Version);
        Assert.Null(sessao.ClosedAtUtc);
    }

    [Fact]
    public void Fechar_registra_o_instante_e_avanca_a_versao()
    {
        var sessao = NovaSessao();
        var fechamento = TestData.NowUtc.AddHours(10);

        sessao.Close(fechamento);

        Assert.Equal(SessionStatus.Closed, sessao.Status);
        Assert.Equal(fechamento, sessao.ClosedAtUtc);
        Assert.Equal(2, sessao.Version);
    }

    [Fact]
    public void Fechar_duas_vezes_e_recusado()
    {
        var sessao = NovaSessao();
        sessao.Close(TestData.NowUtc);

        Assert.Throws<DomainRuleException>(() => sessao.Close(TestData.NowUtc.AddMinutes(1)));
    }

    [Fact]
    public void Reabrir_limpa_o_fechamento()
    {
        var sessao = NovaSessao();
        sessao.Close(TestData.NowUtc);

        sessao.Reopen(TestData.NowUtc.AddMinutes(5));

        Assert.True(sessao.IsOpen);
        Assert.Null(sessao.ClosedAtUtc);
    }

    [Fact]
    public void Leito_repetido_nao_e_adicionado_duas_vezes()
    {
        var sessao = NovaSessao();
        var leito = Guid.CreateVersion7();

        sessao.AddBed(leito);
        sessao.AddBed(leito);

        Assert.Single(sessao.Beds);
    }

    [Fact]
    public void Desativar_leito_fora_da_sessao_e_recusado()
    {
        var sessao = NovaSessao();

        Assert.Throws<DomainRuleException>(
            () => sessao.SetBedActive(Guid.CreateVersion7(), false, TestData.NowUtc));
    }

    [Fact]
    public void Sessao_aberta_nunca_e_apagavel()
    {
        var sessao = NovaSessao();
        var politica = RetentionPolicy.Default;

        Assert.Null(politica.PurgeAtUtc(sessao));
        Assert.False(politica.IsPurgeable(sessao, TestData.NowUtc.AddYears(1)));
    }

    [Fact]
    public void Sessao_fechada_so_e_apagavel_depois_da_janela_de_recuperacao()
    {
        var sessao = NovaSessao();
        var fechamento = TestData.NowUtc;
        sessao.Close(fechamento);
        var politica = RetentionPolicy.Default;

        Assert.False(politica.IsPurgeable(sessao, fechamento.AddHours(23)));
        Assert.True(politica.IsPurgeable(sessao, fechamento.AddHours(24)));
        Assert.True(politica.IsPurgeable(sessao, fechamento.AddHours(25)));
    }

    [Fact]
    public void Recuperavel_enquanto_a_janela_nao_expira()
    {
        var sessao = NovaSessao();
        sessao.Close(TestData.NowUtc);
        var politica = RetentionPolicy.Default;

        Assert.True(politica.IsRecoverable(sessao, TestData.NowUtc.AddHours(1)));
        Assert.False(politica.IsRecoverable(sessao, TestData.NowUtc.AddHours(30)));
    }

    [Fact]
    public void Limpeza_imediata_apaga_assim_que_fecha()
    {
        var sessao = NovaSessao();
        sessao.Close(TestData.NowUtc);

        Assert.True(RetentionPolicy.Immediate.IsPurgeable(sessao, TestData.NowUtc));
    }

    [Fact]
    public void Politica_de_retencao_vem_das_configuracoes()
    {
        var settings = InstitutionSettings.Default with { RetentionAfterClose = TimeSpan.FromHours(6) };

        Assert.Equal(TimeSpan.FromHours(6), RetentionPolicy.From(settings).RecoveryWindow);
    }
}
