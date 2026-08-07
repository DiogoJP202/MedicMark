using Bunit;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.UI.Components;
using Microsoft.AspNetCore.Components;

namespace ChecklistPlantao.UI.Tests;

public sealed class PermissionGuardTests : BunitContext
{
    [Fact]
    public void Mostra_o_conteudo_quando_a_permissao_existe()
    {
        var cut = Render<PermissionGuard>(p => p
            .Add(g => g.Permission, Permissions.ChecklistUpdate)
            .Add(g => g.Access, UiTestData.AccessWith(Permissions.ChecklistUpdate))
            .AddChildContent("<button>Marcar</button>"));

        Assert.Contains("Marcar", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Esconde_o_conteudo_quando_a_permissao_falta()
    {
        var cut = Render<PermissionGuard>(p => p
            .Add(g => g.Permission, Permissions.AdminUsers)
            .Add(g => g.Access, UiTestData.AccessWith(Permissions.ChecklistView))
            .AddChildContent("<button>Administrar</button>"));

        Assert.DoesNotContain("Administrar", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Usuario_sem_grupo_nao_ve_nada()
    {
        var cut = Render<PermissionGuard>(p => p
            .Add(g => g.Permission, Permissions.ChecklistView)
            .Add(g => g.Access, EffectiveAccess.None)
            .AddChildContent("<span>Checklist</span>"));

        Assert.DoesNotContain("Checklist", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Esconde_quando_a_permissao_existe_mas_o_setor_nao_e_autorizado()
    {
        var cut = Render<PermissionGuard>(p => p
            .Add(g => g.Permission, Permissions.ChecklistUpdate)
            .Add(g => g.SectorId, Guid.CreateVersion7())
            .Add(g => g.Access, UiTestData.AccessWith(Permissions.ChecklistUpdate))
            .AddChildContent("<span>Grade</span>"));

        Assert.DoesNotContain("Grade", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Mostra_quando_a_permissao_e_o_setor_conferem()
    {
        var cut = Render<PermissionGuard>(p => p
            .Add(g => g.Permission, Permissions.ChecklistUpdate)
            .Add(g => g.SectorId, UiTestData.SectorId)
            .Add(g => g.Access, UiTestData.AccessWith(Permissions.ChecklistUpdate))
            .AddChildContent("<span>Grade</span>"));

        Assert.Contains("Grade", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Exibe_o_conteudo_alternativo_quando_configurado()
    {
        var cut = Render<PermissionGuard>(p => p
            .Add(g => g.Permission, Permissions.ChecklistClose)
            .Add(g => g.Access, UiTestData.AccessWith(Permissions.ChecklistView))
            .Add(g => g.ChildContent, (RenderFragment)(builder => builder.AddMarkupContent(0, "<span>Encerrar</span>")))
            .Add(g => g.Fallback, (RenderFragment)(builder => builder.AddMarkupContent(0, "<p>Sem permissão</p>"))));

        Assert.DoesNotContain("Encerrar", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Sem permissão", cut.Markup, StringComparison.Ordinal);
    }
}
