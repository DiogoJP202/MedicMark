using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Tests.Structure;

/// <summary>
/// Nome único de coluna dentro de um tipo de checklist.
///
/// A regra existia só na criação. Renomear uma coluna para o nome de outra ativa passava pelo
/// domínio e ia morrer no índice único do banco — uma exceção que ninguém tratava, entregue ao
/// usuário como HTTP 500. Aqui a regra vale para os dois caminhos.
/// </summary>
public sealed class ChecklistTemplateColumnTests
{
    [Fact]
    public void Nao_aceita_criar_coluna_com_nome_de_outra_ativa()
    {
        var template = TestData.Template();
        template.Column("20H", new TimeOnly(20, 0));

        var erro = Assert.Throws<DomainRuleException>(() => template.Column("20H", new TimeOnly(21, 0)));

        Assert.Contains("20H", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nao_aceita_renomear_coluna_para_o_nome_de_outra_ativa()
    {
        var template = TestData.Template();
        template.Column("20H", new TimeOnly(20, 0));
        var coluna22 = template.Column("22H", new TimeOnly(22, 0));

        Assert.Throws<DomainRuleException>(() =>
            template.UpdateColumn(coluna22, "20H", new TimeOnly(22, 0), 20, isActive: true, TestData.NowUtc));
    }

    /// <summary>Sem isto, salvar uma coluna sem mexer no nome acusaria conflito consigo mesma.</summary>
    [Fact]
    public void Aceita_salvar_a_coluna_mantendo_o_proprio_nome()
    {
        var template = TestData.Template();
        var coluna = template.Column("22H", new TimeOnly(22, 0));

        template.UpdateColumn(coluna, "22H", new TimeOnly(23, 0), 20, isActive: true, TestData.NowUtc);

        Assert.Equal(new TimeOnly(23, 0), coluna.TriggerTime);
    }

    /// <summary>A comparação ignora maiúsculas — "20h" e "20H" seriam indistinguíveis na tela.</summary>
    [Fact]
    public void Nome_repetido_so_por_maiusculas_tambem_e_recusado()
    {
        var template = TestData.Template();
        template.Column("20H", new TimeOnly(20, 0));
        var outra = template.Column("22H", new TimeOnly(22, 0));

        Assert.Throws<DomainRuleException>(() =>
            template.UpdateColumn(outra, "20h", new TimeOnly(22, 0), 20, isActive: true, TestData.NowUtc));
    }

    /// <summary>
    /// O índice único do banco é filtrado por ativo (D-016): uma coluna desativada pode repetir o
    /// nome, senão desativar uma coluna travaria o cadastro de outra igual.
    /// </summary>
    [Fact]
    public void Coluna_que_fica_inativa_pode_repetir_o_nome()
    {
        var template = TestData.Template();
        template.Column("20H", new TimeOnly(20, 0));
        var outra = template.Column("22H", new TimeOnly(22, 0));

        template.UpdateColumn(outra, "20H", new TimeOnly(22, 0), 20, isActive: false, TestData.NowUtc);

        Assert.False(outra.IsActive);
        Assert.Equal("20H", outra.DisplayName);
    }

    [Fact]
    public void Nome_livre_depois_que_a_colidente_e_desativada()
    {
        var template = TestData.Template();
        var coluna20 = template.Column("20H", new TimeOnly(20, 0));
        var coluna22 = template.Column("22H", new TimeOnly(22, 0));

        template.UpdateColumn(coluna20, "20H", new TimeOnly(20, 0), 10, isActive: false, TestData.NowUtc);
        template.UpdateColumn(coluna22, "20H", new TimeOnly(22, 0), 20, isActive: true, TestData.NowUtc);

        Assert.Equal("20H", coluna22.DisplayName);
        Assert.True(coluna22.IsActive);
    }

    [Fact]
    public void Nao_aceita_coluna_de_outro_tipo_de_checklist()
    {
        var gelo = TestData.Template();
        var ssvv = TestData.Template("SSVV", "SSVV");
        var colunaDeOutro = ssvv.Column("PM", new TimeOnly(20, 0));

        Assert.Throws<DomainRuleException>(() =>
            gelo.UpdateColumn(colunaDeOutro, "PM", new TimeOnly(20, 0), 10, isActive: true, TestData.NowUtc));
    }

    /// <summary>A versão do template avança: os dispositivos precisam perceber que há o que baixar.</summary>
    [Fact]
    public void Alterar_coluna_avanca_a_versao_do_tipo_e_da_coluna()
    {
        var template = TestData.Template();
        var coluna = template.Column("22H", new TimeOnly(22, 0));

        var versaoTemplate = template.Version;
        var versaoColuna = coluna.Version;

        template.UpdateColumn(coluna, "22H30", new TimeOnly(22, 30), 20, isActive: true, TestData.NowUtc);

        Assert.True(template.Version > versaoTemplate);
        Assert.True(coluna.Version > versaoColuna);
    }
}
