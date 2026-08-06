using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Domain.Common;

namespace ChecklistPlantao.Domain.Tests.Access;

public sealed class EffectiveAccessTests
{
    private static readonly Guid Oeste = Guid.CreateVersion7();
    private static readonly Guid Leste = Guid.CreateVersion7();
    private static readonly Guid Norte = Guid.CreateVersion7();

    private static AccessGroup Group(
        string name,
        IEnumerable<string> permissions,
        IEnumerable<Guid> sectors,
        bool grantsAllSectors = false)
    {
        var group = new AccessGroup(Guid.CreateVersion7(), name, null, grantsAllSectors, TestData.NowUtc);
        group.ReplacePermissions(permissions, TestData.NowUtc);
        group.ReplaceSectors(sectors, TestData.NowUtc);
        return group;
    }

    [Fact]
    public void Permissoes_de_varios_grupos_sao_somadas()
    {
        var plantao = Group("Plantão Oeste", [Permissions.ChecklistView, Permissions.ChecklistUpdate], [Oeste]);
        var sondas = Group("Responsáveis por Sondas", [Permissions.ChecklistView, Permissions.ChecklistClose], [Leste]);

        var acesso = EffectiveAccess.FromGroups([plantao, sondas]);

        Assert.True(acesso.Has(Permissions.ChecklistView));
        Assert.True(acesso.Has(Permissions.ChecklistUpdate));
        Assert.True(acesso.Has(Permissions.ChecklistClose));
        Assert.Equal(3, acesso.Permissions.Count);
    }

    [Fact]
    public void Setores_de_varios_grupos_sao_somados()
    {
        var acesso = EffectiveAccess.FromGroups(
        [
            Group("A", [Permissions.ChecklistView], [Oeste]),
            Group("B", [Permissions.ChecklistView], [Leste]),
        ]);

        Assert.True(acesso.CanAccessSector(Oeste));
        Assert.True(acesso.CanAccessSector(Leste));
        Assert.False(acesso.CanAccessSector(Norte));
    }

    [Fact]
    public void Grupo_inativo_nao_contribui_com_nada()
    {
        var ativo = Group("Ativo", [Permissions.ChecklistView], [Oeste]);
        var inativo = Group("Inativo", [Permissions.AdminUsers], [Leste]);
        inativo.SetActive(false, TestData.NowUtc);

        var acesso = EffectiveAccess.FromGroups([ativo, inativo]);

        Assert.False(acesso.Has(Permissions.AdminUsers));
        Assert.False(acesso.CanAccessSector(Leste));
    }

    [Fact]
    public void Grupo_com_todos_os_setores_enxerga_setor_criado_depois()
    {
        var admin = Group("Administradores", Permissions.All, [], grantsAllSectors: true);

        var acesso = EffectiveAccess.FromGroups([admin]);
        var setorNovo = Guid.CreateVersion7();

        Assert.True(acesso.GrantsAllSectors);
        Assert.True(acesso.CanAccessSector(setorNovo));
    }

    [Fact]
    public void Usuario_sem_grupo_nao_tem_nada()
    {
        var acesso = EffectiveAccess.FromGroups([]);

        Assert.Empty(acesso.Permissions);
        Assert.False(acesso.CanAccessSector(Oeste));
        Assert.False(acesso.Has(Permissions.ChecklistView));
    }

    [Fact]
    public void Filtro_de_setores_respeita_o_acesso_total()
    {
        var restrito = EffectiveAccess.FromGroups([Group("A", [Permissions.ChecklistView], [Oeste])]);
        var total = EffectiveAccess.FromGroups([Group("B", [Permissions.ChecklistView], [], grantsAllSectors: true)]);
        Guid[] setores = [Oeste, Leste, Norte];

        Assert.Single(restrito.FilterSectors(setores, id => id));
        Assert.Equal(3, total.FilterSectors(setores, id => id).Count());
    }

    [Fact]
    public void HasAny_encontra_qualquer_uma_das_permissoes()
    {
        var acesso = EffectiveAccess.FromGroups([Group("A", [Permissions.ChecklistUpdate], [Oeste])]);

        Assert.True(acesso.HasAny(Permissions.AdminUsers, Permissions.ChecklistUpdate));
        Assert.False(acesso.HasAny(Permissions.AdminUsers, Permissions.AdminGroups));
    }

    [Fact]
    public void Permissao_desconhecida_e_recusada()
    {
        var group = new AccessGroup(Guid.CreateVersion7(), "Teste", null, false, TestData.NowUtc);

        var erro = Assert.Throws<DomainRuleException>(
            () => group.ReplacePermissions(["checklist.view", "permissao.inventada"], TestData.NowUtc));

        Assert.Contains("permissao.inventada", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Substituir_permissoes_remove_as_que_sairam()
    {
        var group = Group("A", [Permissions.ChecklistView, Permissions.ChecklistUpdate], [Oeste]);

        group.ReplacePermissions([Permissions.ChecklistView], TestData.NowUtc);

        Assert.Equal([Permissions.ChecklistView], group.PermissionKeys);
    }

    [Fact]
    public void Catalogo_de_permissoes_descreve_todas_as_chaves()
    {
        Assert.All(Permissions.All, key => Assert.False(string.IsNullOrWhiteSpace(Permissions.Catalog[key])));
        Assert.Equal(12, Permissions.All.Count);
    }
}
