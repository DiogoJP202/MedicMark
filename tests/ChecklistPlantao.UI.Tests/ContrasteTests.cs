using System.Globalization;
using System.Text.RegularExpressions;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// Contraste MEDIDO da folha de estilo, nos dois temas.
///
/// A mesma tabela de pares vale para o tema claro e para o escuro. É o que impede o escuro de
/// ser julgado por um critério mais frouxo: se um par passa no claro e não no escuro, o build
/// reprova.
///
/// O teste lê a folha de estilo de verdade, embutida como recurso. Uma cópia das cores digitada
/// aqui envelheceria em silêncio na primeira vez que alguém mexesse na paleta.
/// </summary>
public sealed class ContrasteTests
{
    /// <summary>Texto comum. É o mínimo do WCAG AA para texto abaixo de 18,66px em negrito.</summary>
    private const double Texto = 4.5;

    /// <summary>Corpo do aplicativo: o claro entrega 14:1, e não faria sentido o escuro entregar 4,5.</summary>
    private const double Corpo = 7.0;

    /// <summary>Limite de controle, ícone, barra — o que o WCAG chama de conteúdo não textual.</summary>
    private const double Forma = 3.0;

    public static TheoryData<string, string, string, string, double> Pares()
    {
        (string Nome, string Frente, string Fundo, double Minimo)[] tabela =
        [
            // Texto sobre os fundos do aplicativo
            ("texto na página", "--cor-texto", "--cor-fundo", Corpo),
            ("texto no cartão", "--cor-texto", "--cor-superficie", Corpo),
            ("texto na superfície alternativa", "--cor-texto", "--cor-superficie-alt", Corpo),
            ("texto no distintivo", "--cor-texto", "--cor-neutro-suave", Corpo),
            ("texto secundário na página", "--cor-texto-suave", "--cor-fundo", 6.0),
            ("texto secundário no cartão", "--cor-texto-suave", "--cor-superficie", 6.0),
            ("texto secundário na superfície alternativa", "--cor-texto-suave", "--cor-superficie-alt", 6.0),

            // Texto dentro das faixas coloridas — o fundo muda, o texto não
            ("texto na faixa de informação", "--cor-texto", "--cor-primaria-suave", Texto),
            ("texto na faixa de sucesso", "--cor-texto", "--cor-sucesso-suave", Texto),
            ("texto na faixa de alerta", "--cor-texto", "--cor-alerta-suave", Texto),
            ("texto na faixa de erro", "--cor-texto", "--cor-erro-suave", Texto),

            // Texto branco sobre preenchimento
            ("título na barra do topo", "--cor-texto-inverso", "--cor-chrome", Texto),
            ("botão primário", "--cor-texto-inverso", "--cor-primaria", Texto),
            ("botão primário sob o dedo", "--cor-texto-inverso", "--cor-primaria-clara", Texto),
            ("botão de excluir", "--cor-texto-inverso", "--cor-erro-forte", Texto),
            ("botão de sincronizar em alerta", "--cor-texto-inverso", "--cor-alerta-forte", Texto),
            ("toast de desfazer", "--cor-toast-texto", "--cor-toast-fundo", Texto),

            // Traços coloridos
            ("botão de texto", "--cor-primaria-texto", "--cor-superficie", Texto),
            ("botão de texto na página", "--cor-primaria-texto", "--cor-fundo", Texto),
            ("distintivo de sucesso", "--cor-sucesso", "--cor-sucesso-suave", Texto),
            ("distintivo de alerta", "--cor-alerta", "--cor-alerta-suave", Texto),
            ("distintivo de erro", "--cor-erro", "--cor-erro-suave", Texto),
            ("mensagem de erro no campo", "--cor-erro", "--cor-superficie", Texto),
            ("mensagem de erro na página", "--cor-erro", "--cor-fundo", Texto),

            // Formas: limite de controle, anel de foco, barra de progresso
            ("borda de controle no cartão", "--cor-borda-forte", "--cor-superficie", Forma),
            ("borda de controle na página", "--cor-borda-forte", "--cor-fundo", Forma),
            ("borda de controle na superfície alternativa", "--cor-borda-forte", "--cor-superficie-alt", Forma),
            ("borda de controle no distintivo", "--cor-borda-forte", "--cor-neutro-suave", Forma),
            ("contorno do toast", "--cor-borda-forte", "--cor-toast-fundo", Forma),
            ("anel de foco na página", "--cor-foco", "--cor-fundo", Forma),
            ("anel de foco no cartão", "--cor-foco", "--cor-superficie", Forma),
            ("anel de foco na barra do topo", "--cor-texto-inverso", "--cor-chrome", Forma),
            ("caixa marcada sobre o cartão", "--cor-primaria", "--cor-superficie", Forma),
            ("botão de excluir sobre o cartão", "--cor-erro-forte", "--cor-superficie", Forma),
            ("botão primário sobre a página", "--cor-primaria", "--cor-fundo", Forma),
            ("barra de progresso no trilho", "--cor-primaria-texto", "--cor-neutro-suave", Forma),
            ("barra de progresso completa", "--cor-sucesso", "--cor-neutro-suave", Forma),
            ("borda da faixa de informação", "--cor-primaria-texto", "--cor-primaria-suave", Forma),
        ];

        var dados = new TheoryData<string, string, string, string, double>();
        foreach (var (nome, frente, fundo, minimo) in tabela)
        {
            dados.Add("claro", nome, frente, fundo, minimo);
            dados.Add("escuro", nome, frente, fundo, minimo);
        }

        return dados;
    }

    [Theory]
    [MemberData(nameof(Pares))]
    public void Cada_par_de_cor_atinge_o_minimo(string tema, string nome, string frente, string fundo, double minimo)
    {
        var paleta = tema == "claro" ? Paleta.Clara : Paleta.Escura;

        var razao = Contraste(paleta[frente], paleta[fundo]);

        Assert.True(
            razao >= minimo,
            $"tema {tema}: {nome} — {frente} ({paleta[frente]}) sobre {fundo} ({paleta[fundo]}) " +
            $"dá {razao.ToString("F2", CultureInfo.InvariantCulture)}:1, e o mínimo é {minimo}:1.");
    }

    /// <summary>
    /// O botão de sincronizar é um véu branco SOBRE a barra do topo, e não uma cor sólida — o
    /// contraste do texto depende da mistura, não do véu.
    /// </summary>
    [Theory]
    [InlineData("claro")]
    [InlineData("escuro")]
    public void Botao_de_sincronizar_le_sobre_a_barra_do_topo(string tema)
    {
        var paleta = tema == "claro" ? Paleta.Clara : Paleta.Escura;

        foreach (var veu in new[] { "--realce-sutil", "--realce-medio" })
        {
            var fundo = Misturar(paleta[veu], paleta["--cor-chrome"]);

            Assert.True(
                Contraste(paleta["--cor-texto-inverso"], fundo) >= Texto,
                $"tema {tema}: o texto do botão de sincronizar sobre {veu} não atinge 4,5:1.");
        }
    }

    /// <summary>
    /// Os dois blocos escuros — o do aparelho e o da escolha explícita — precisam ser idênticos.
    /// CSS puro não deixa aplicar um mesmo corpo a dois seletores em contextos diferentes, então
    /// a defesa contra o esquecimento é este teste.
    /// </summary>
    [Fact]
    public void Os_dois_blocos_do_tema_escuro_sao_identicos()
    {
        var doAparelho = Folha.Declaracoes(Folha.CorpoDe(":root:not([data-tema=\"claro\"])"));
        var daEscolha = Folha.Declaracoes(Folha.CorpoDe(":root[data-tema=\"escuro\"]"));

        Assert.Equal(daEscolha, doAparelho);
        Assert.NotEmpty(daEscolha);
    }

    /// <summary>
    /// Toda cor do tema claro precisa de uma resposta no escuro. A única exceção está declarada
    /// aqui, com o motivo: o texto inverso só aparece sobre preenchimento escuro, nos dois temas.
    /// </summary>
    [Fact]
    public void Nenhuma_cor_fica_sem_versao_escura()
    {
        var escuro = Folha.Declaracoes(Folha.CorpoDe(":root[data-tema=\"escuro\"]"));

        var esquecidas = Paleta.Clara.Keys
            .Where(t => t.StartsWith("--cor-", StringComparison.Ordinal))
            .Where(t => t != "--cor-texto-inverso")
            .Where(t => !escuro.ContainsKey(t))
            .ToList();

        Assert.Empty(esquecidas);
    }

    /// <summary>
    /// Cor fixa fora dos blocos de token é cor que o tema escuro não alcança. Foi assim que o
    /// toast acabou com o fundo da cor do texto.
    /// </summary>
    [Fact]
    public void Nenhuma_cor_vive_fora_dos_tokens()
    {
        var soltas = Regex.Matches(Folha.SemBlocosDeToken(), @"#[0-9a-fA-F]{3,8}\b|\brgba?\(")
            .Select(m => m.Value)
            .ToList();

        Assert.Empty(soltas);
    }

    // ------------------------------------------------------------------ medição

    private static double Contraste(string a, string b)
    {
        var (la, lb) = (Luminancia(a), Luminancia(b));
        var (maior, menor) = la > lb ? (la, lb) : (lb, la);
        return (maior + 0.05) / (menor + 0.05);
    }

    private static double Luminancia(string cor)
    {
        var (r, g, b, _) = Componentes(cor);
        static double Linear(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));
    }

    /// <summary>Achata um véu translúcido contra o que está atrás dele.</summary>
    private static string Misturar(string frente, string fundo)
    {
        var (fr, fg, fb, alfa) = Componentes(frente);
        var (tr, tg, tb, _) = Componentes(fundo);

        static int Byte(double v) => (int)Math.Round(v * 255);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"#{Byte((fr * alfa) + (tr * (1 - alfa))):x2}{Byte((fg * alfa) + (tg * (1 - alfa))):x2}{Byte((fb * alfa) + (tb * (1 - alfa))):x2}");
    }

    private static (double R, double G, double B, double A) Componentes(string cor)
    {
        cor = cor.Trim();

        if (cor.StartsWith('#'))
        {
            var hex = cor[1..];
            return (Convert.ToInt32(hex[..2], 16) / 255.0,
                    Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0,
                    Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0,
                    1.0);
        }

        var m = Regex.Match(cor, @"rgba?\(\s*(\d+)\s+(\d+)\s+(\d+)\s*/\s*(\d+)%\s*\)");
        Assert.True(m.Success, $"cor em formato não reconhecido: {cor}");

        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 255.0,
                int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 255.0,
                int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) / 255.0,
                int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) / 100.0);
    }

    // ------------------------------------------------------------------ leitura da folha

    private static class Paleta
    {
        public static readonly IReadOnlyDictionary<string, string> Clara =
            Folha.Declaracoes(Folha.CorpoDe(":root"));

        public static readonly IReadOnlyDictionary<string, string> Escura = Combinar(
            Clara,
            Folha.Declaracoes(Folha.CorpoDe(":root[data-tema=\"escuro\"]")));

        private static Dictionary<string, string> Combinar(
            IReadOnlyDictionary<string, string> baseClara,
            IReadOnlyDictionary<string, string> porCima)
        {
            var junta = new Dictionary<string, string>(baseClara, StringComparer.Ordinal);
            foreach (var (chave, valor) in porCima)
            {
                junta[chave] = valor;
            }

            return junta;
        }
    }

    private static class Folha
    {
        private static readonly string SemComentarios =
            Regex.Replace(Ler(), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        private static readonly string[] BlocosDeToken =
        [
            ":root",
            ":root:not([data-tema=\"claro\"])",
            ":root[data-tema=\"escuro\"]",
        ];

        /// <summary>Corpo de um seletor. Nenhum destes blocos tem chaves aninhadas dentro.</summary>
        public static string CorpoDe(string seletor)
        {
            var m = Regex.Match(SemComentarios, Regex.Escape(seletor) + @"\s*\{([^}]*)\}");
            Assert.True(m.Success, $"não há bloco para o seletor {seletor} na folha de estilo.");
            return m.Groups[1].Value;
        }

        public static IReadOnlyDictionary<string, string> Declaracoes(string corpo)
            => Regex.Matches(corpo, @"(--[a-z0-9-]+)\s*:\s*([^;]+);")
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim(), StringComparer.Ordinal);

        /// <summary>A folha sem os blocos que existem para DECLARAR cor.</summary>
        public static string SemBlocosDeToken()
        {
            var resto = SemComentarios;
            foreach (var seletor in BlocosDeToken)
            {
                resto = Regex.Replace(resto, Regex.Escape(seletor) + @"\s*\{[^}]*\}", string.Empty);
            }

            return resto;
        }

        private static string Ler()
        {
            using var fluxo = typeof(ContrasteTests).Assembly.GetManifestResourceStream("design-system.css")
                ?? throw new InvalidOperationException("A folha de estilo não foi embutida no projeto de teste.");
            using var leitor = new StreamReader(fluxo);
            return leitor.ReadToEnd();
        }
    }
}
