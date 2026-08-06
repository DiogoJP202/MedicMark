using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Seeding;

namespace ChecklistPlantao.Domain.Tests.Seeding;

/// <summary>
/// Estes testes travam os dados que vieram da folha de papel. Se alguém mexer no catálogo por
/// engano, o build acusa — mas o administrador continua livre para editar tudo em tempo de execução.
/// </summary>
public sealed class SeedCatalogTests
{
    [Fact]
    public void Setor_inicial_e_o_oeste() =>
        Assert.Equal("Oeste", Assert.Single(SeedCatalog.Sectors).Name);

    [Fact]
    public void Os_dezesseis_leitos_da_folha_estao_presentes()
    {
        string[] esperados =
        [
            "1148", "1150", "1152", "1153", "1154", "1156", "1158", "1160",
            "1161", "1162", "1163", "1164", "1165", "1166", "1167", "1169",
        ];

        Assert.Equal(esperados, SeedCatalog.Beds.Select(b => b.Code));
        Assert.All(SeedCatalog.Beds, b => Assert.Equal("Oeste", b.SectorName));
    }

    [Fact]
    public void Leitos_tem_ordem_crescente_e_sem_empate()
    {
        var ordens = SeedCatalog.Beds.Select(b => b.SortOrder).ToList();

        Assert.Equal(ordens.Distinct().Count(), ordens.Count);
        Assert.Equal(ordens.OrderBy(o => o).ToList(), ordens);
    }

    [Fact]
    public void Os_tres_tipos_de_checklist_estao_presentes() =>
        Assert.Equal(["Gelo", "Glicemia", "SSVV"], SeedCatalog.Templates.Select(t => t.Name));

    [Theory]
    [InlineData("GELO", 6)]
    [InlineData("GLICEMIA", 2)]
    [InlineData("SSVV", 2)]
    public void Cada_tipo_tem_a_quantidade_de_colunas_da_folha(string templateCode, int esperado) =>
        Assert.Equal(esperado, SeedCatalog.Columns.Count(c => c.TemplateCode == templateCode));

    [Theory]
    [InlineData("GELO", "20H", 20, 0)]
    [InlineData("GELO", "06H", 6, 0)]
    [InlineData("GLICEMIA", "Jantar", 19, 30)]
    [InlineData("GLICEMIA", "Café", 7, 0)]
    [InlineData("SSVV", "PM", 20, 0)]
    [InlineData("SSVV", "AM", 6, 0)]
    public void Horarios_confirmados_com_o_cliente_estao_no_catalogo(string template, string coluna, int hora, int minuto)
    {
        var alvo = SeedCatalog.Columns.Single(c => c.TemplateCode == template && c.DisplayName == coluna);

        Assert.Equal(new TimeOnly(hora, minuto), alvo.TriggerTime);
    }

    [Fact]
    public void Toda_coluna_semeada_tem_horario() =>
        Assert.All(SeedCatalog.Columns, c => Assert.NotNull(c.TriggerTime));

    [Fact]
    public void Toda_coluna_semeada_referencia_um_tipo_existente()
    {
        var codigos = SeedCatalog.Templates.Select(t => t.Code).ToHashSet(StringComparer.Ordinal);

        Assert.All(SeedCatalog.Columns, c => Assert.Contains(c.TemplateCode, codigos));
    }

    [Fact]
    public void Marcadores_da_folha_estao_presentes() =>
        Assert.Equal(["C.I.", "Sondas", "Drenos"], SeedCatalog.Markers.Select(m => m.Name));

    [Fact]
    public void Grupo_administradores_tem_todas_as_permissoes_e_todos_os_setores()
    {
        var admin = SeedCatalog.Groups.Single(g => g.Name == SeedCatalog.AdministratorsGroupName);

        Assert.True(admin.GrantsAllSectors);
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), admin.Permissions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Grupo_do_plantao_ve_e_atualiza_apenas_o_oeste()
    {
        var plantao = SeedCatalog.Groups.Single(g => g.Name == SeedCatalog.OesteShiftGroupName);

        Assert.False(plantao.GrantsAllSectors);
        Assert.Equal(["Oeste"], plantao.SectorNames);
        Assert.Contains(Permissions.ChecklistView, plantao.Permissions);
        Assert.Contains(Permissions.ChecklistUpdate, plantao.Permissions);
        Assert.DoesNotContain(Permissions.AdminUsers, plantao.Permissions);
    }

    [Fact]
    public void Identificadores_de_seed_sao_estaveis_entre_execucoes()
    {
        var primeira = SeedCatalog.Beds.Select(b => b.Id).ToList();
        var segunda = SeedCatalog.Beds.Select(b => b.Id).ToList();

        Assert.Equal(primeira, segunda);
        Assert.Equal(primeira.Distinct().Count(), primeira.Count);
    }

    [Fact]
    public void Identificadores_de_seed_nao_colidem_entre_categorias()
    {
        var todos = SeedCatalog.Sectors.Select(s => s.Id)
            .Concat(SeedCatalog.Beds.Select(b => b.Id))
            .Concat(SeedCatalog.Templates.Select(t => t.Id))
            .Concat(SeedCatalog.Columns.Select(c => c.Id))
            .Concat(SeedCatalog.Markers.Select(m => m.Id))
            .Concat(SeedCatalog.Groups.Select(g => g.Id))
            .ToList();

        Assert.Equal(todos.Distinct().Count(), todos.Count);
    }
}
