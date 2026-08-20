using Bunit;
using ChecklistPlantao.UI.Components;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// O conjunto de ícones.
///
/// Saiu de dentro do <c>IslandNav</c> quando o resto da interface ainda usava glifos de texto —
/// `▲`, `✕`, `✓`, `!` — para a mesma função. Glifo herda a fonte do sistema e desenha diferente
/// entre Android e Windows; o SVG não.
/// </summary>
public sealed class IconeTests : BunitContext
{
    [Fact]
    public void Desenha_svg_e_nao_texto()
    {
        var cut = Render<Icone>(p => p.Add(i => i.Nome, "alerta"));

        Assert.NotNull(cut.Find("svg"));
        Assert.Empty(cut.Find("span").TextContent.Trim());
    }

    /// <summary>Decorativo por definição: o significado vem do texto ao lado, sempre.</summary>
    [Fact]
    public void E_escondido_do_leitor_de_tela()
    {
        var cut = Render<Icone>(p => p.Add(i => i.Nome, "erro"));

        Assert.Equal("true", cut.Find("span").GetAttribute("aria-hidden"));
        Assert.Equal("false", cut.Find("svg").GetAttribute("focusable"));
    }

    /// <summary>A cor acompanha o texto ao redor — é o que faz o mesmo desenho servir em qualquer faixa.</summary>
    [Fact]
    public void Herda_a_cor_do_contexto()
    {
        var cut = Render<Icone>(p => p.Add(i => i.Nome, "check"));

        Assert.Equal("currentColor", cut.Find("svg").GetAttribute("stroke"));
    }

    [Theory]
    [InlineData("painel")]
    [InlineData("checklist")]
    [InlineData("classificacoes")]
    [InlineData("pendencias")]
    [InlineData("plantao")]
    [InlineData("administracao")]
    [InlineData("alerta")]
    [InlineData("erro")]
    [InlineData("info")]
    [InlineData("check")]
    [InlineData("seta")]
    [InlineData("vazio")]
    [InlineData("conectado")]
    [InlineData("rede-local")]
    [InlineData("sem-servidor")]
    [InlineData("offline")]
    [InlineData("sincronizando")]
    public void Todo_nome_do_conjunto_tem_desenho(string nome)
    {
        var desenho = Icone.Desenho(nome);

        Assert.NotEqual(Icone.Desenho("nome-que-nao-existe"), desenho);
        Assert.NotEmpty(desenho);
    }

    /// <summary>
    /// Nome desconhecido vira um círculo, e não exceção: um ícone errado numa faixa de erro não
    /// pode ser o motivo de a tela não abrir.
    /// </summary>
    [Fact]
    public void Nome_desconhecido_nao_quebra_a_tela()
    {
        var cut = Render<Icone>(p => p.Add(i => i.Nome, "isso-nao-existe"));

        Assert.NotNull(cut.Find("svg circle"));
    }

    /// <summary>
    /// Os estados de conexão precisam ser distinguíveis SEM cor — a faixa é lida de relance, e
    /// dois deles ficam sobre fundos de cor parecida.
    /// </summary>
    [Fact]
    public void Estados_de_conexao_tem_desenhos_diferentes()
    {
        string[] estados = ["conectado", "rede-local", "sem-servidor", "offline", "sincronizando"];

        var desenhos = estados.Select(Icone.Desenho).ToList();

        Assert.Equal(desenhos.Count, desenhos.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Classe_do_contexto_chega_ao_elemento()
    {
        var cut = Render<Icone>(p => p
            .Add(i => i.Nome, "alerta")
            .Add(i => i.Class, "faixa__icone"));

        Assert.Contains("faixa__icone", cut.Find("span").ClassName, StringComparison.Ordinal);
    }
}
