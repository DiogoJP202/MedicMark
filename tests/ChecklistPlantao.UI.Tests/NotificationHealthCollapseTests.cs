using Bunit;
using ChecklistPlantao.UI.Abstractions;
using ChecklistPlantao.UI.Components.Status;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// Recolhimento da faixa de saúde das notificações.
///
/// O item 19 do enunciado proíbe dispensar o aviso PERMANENTEMENTE enquanto o problema existir —
/// não proíbe recolhê-lo. A faixa expandida ocupava um terço da tela do celular e ficava fixa no
/// topo; recolhida, vira uma linha que continua visível e continua levando à correção.
///
/// O invariante que estes testes protegem: <b>recolhida ou não, a faixa nunca desaparece enquanto
/// houver problema</b>.
/// </summary>
public sealed class NotificationHealthCollapseTests : BunitContext
{
    private static NotificationStatus ComProblemas(params string[] problemas) =>
        new(false, false, true, true, false, false, null, null, problemas);

    [Fact]
    public void Recolher_mantem_a_faixa_visivel()
    {
        var cut = Render<NotificationHealthBanner>(p => p
            .Add(b => b.Status, ComProblemas("Permissão de notificações negada.")));

        cut.Find("[data-testid=notification-health-collapse]").Click();

        // O aviso continua na tela — só mudou de forma.
        var faixa = cut.Find("[data-testid=notification-health-banner]");
        Assert.Contains("Permissão de notificações negada.", faixa.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("ATENÇÃO", faixa.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Recolhida_resume_varios_problemas_em_uma_linha()
    {
        var cut = Render<NotificationHealthBanner>(p => p
            .Add(b => b.Status, ComProblemas("Permissão negada.", "Alarmes exatos não permitidos.", "Economia de bateria ativa.")));

        cut.Find("[data-testid=notification-health-collapse]").Click();

        Assert.Contains("3 pendências nas notificações", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Da_para_reabrir_depois_de_recolher()
    {
        var cut = Render<NotificationHealthBanner>(p => p
            .Add(b => b.Status, ComProblemas("Permissão de notificações negada.")));

        cut.Find("[data-testid=notification-health-collapse]").Click();
        cut.Find("[data-testid=notification-health-expand]").Click();

        Assert.Contains("ATENÇÃO", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Recolhida_ainda_oferece_a_correcao()
    {
        var chamou = false;

        var cut = Render<NotificationHealthBanner>(p => p
            .Add(b => b.Status, ComProblemas("Permissão de notificações negada."))
            .Add(b => b.OnFix, () => chamou = true));

        cut.Find("[data-testid=notification-health-collapse]").Click();
        cut.Find("button.botao--texto").Click();

        Assert.True(chamou);
    }

    /// <summary>
    /// O usuário recolheu o aviso que leu. Um problema diferente é informação nova, e volta na
    /// forma completa — senão o recolhimento viraria dispensa permanente pela porta dos fundos.
    /// </summary>
    [Fact]
    public void Problema_novo_reabre_a_faixa()
    {
        var cut = Render<NotificationHealthBanner>(p => p
            .Add(b => b.Status, ComProblemas("Permissão de notificações negada.")));

        cut.Find("[data-testid=notification-health-collapse]").Click();
        Assert.DoesNotContain("ATENÇÃO", cut.Markup, StringComparison.Ordinal);

        cut.Render(p => p
            .Add(b => b.Status, ComProblemas("Permissão de notificações negada.", "Economia de bateria ativa.")));

        Assert.Contains("ATENÇÃO", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Economia de bateria ativa.", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>Sem isso, uma atualização de estado qualquer desfaria o recolhimento a cada ciclo.</summary>
    [Fact]
    public void Os_mesmos_problemas_nao_reabrem_a_faixa()
    {
        var cut = Render<NotificationHealthBanner>(p => p
            .Add(b => b.Status, ComProblemas("Permissão de notificações negada.")));

        cut.Find("[data-testid=notification-health-collapse]").Click();

        cut.Render(p => p.Add(b => b.Status, ComProblemas("Permissão de notificações negada.")));

        Assert.DoesNotContain("ATENÇÃO", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolvido_o_problema_a_faixa_some_por_completo()
    {
        var cut = Render<NotificationHealthBanner>(p => p
            .Add(b => b.Status, ComProblemas("Permissão de notificações negada.")));

        cut.Find("[data-testid=notification-health-collapse]").Click();

        cut.Render(p => p
            .Add(b => b.Status, new NotificationStatus(true, true, true, true, true, false, DateTime.UtcNow, DateTime.Now, [])));

        Assert.Empty(cut.FindAll("[data-testid=notification-health-banner]"));
    }
}
