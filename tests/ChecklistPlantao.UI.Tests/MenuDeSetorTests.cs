using Bunit;
using Bunit.TestDoubles;
using ChecklistPlantao.Client.Abstractions;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.UI.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// O setor no canto superior esquerdo.
///
/// Ele já era a informação mais visível do aplicativo — e a única que responde "onde eu estou" —
/// mas trocar de setor exigia ir ao Painel e abrir outra tela. Nome que identifica o contexto e
/// não deixa mudá-lo é rótulo; agora é controle.
/// </summary>
public sealed class MenuDeSetorTests : BunitContext
{
    private static readonly Guid Oeste = Guid.CreateVersion7();
    private static readonly Guid Leste = Guid.CreateVersion7();

    private static SessaoDeSetor Registrar(BunitContext contexto, SessaoDeSetor sessao)
    {
        contexto.Services.AddSingleton<IAppSession>(sessao);
        return sessao;
    }

    /// <summary>Continua sendo o título da página: é o ponto de referência de quem usa leitor de tela.</summary>
    [Fact]
    public void Continua_sendo_o_titulo_da_pagina()
    {
        Registrar(this, new SessaoDeSetor());

        var cut = Render<MenuDeSetor>();

        Assert.Contains("Oeste", cut.Find("h1").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Com_permissao_o_setor_vira_botao()
    {
        Registrar(this, new SessaoDeSetor());

        var cut = Render<MenuDeSetor>();

        Assert.NotNull(cut.Find("[data-testid=sector-menu-toggle]"));
    }

    /// <summary>Um botão que não leva a lugar nenhum é pior que nenhum botão.</summary>
    [Fact]
    public void Sem_permissao_continua_sendo_texto()
    {
        Registrar(this, new SessaoDeSetor { Acesso = EffectiveAccess.None });

        var cut = Render<MenuDeSetor>();

        Assert.Empty(cut.FindAll("[data-testid=sector-menu-toggle]"));
        Assert.Contains("Oeste", cut.Find("h1").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_setor_escolhido_continua_sendo_texto()
    {
        Registrar(this, new SessaoDeSetor { NomeDoSetor = null });

        var cut = Render<MenuDeSetor>();

        Assert.Empty(cut.FindAll("[data-testid=sector-menu-toggle]"));
    }

    [Fact]
    public void Abrir_lista_os_setores_disponiveis()
    {
        Registrar(this, new SessaoDeSetor());
        var cut = Render<MenuDeSetor>();

        cut.Find("[data-testid=sector-menu-toggle]").Click();

        Assert.NotNull(cut.Find($"[data-testid=sector-menu-{Oeste}]"));
        Assert.NotNull(cut.Find($"[data-testid=sector-menu-{Leste}]"));
    }

    /// <summary>
    /// A lista é buscada a cada abertura: as pendências de cada setor mudam, e uma lista velha
    /// aqui faria escolher pelo motivo errado.
    /// </summary>
    [Fact]
    public void Busca_a_lista_a_cada_abertura()
    {
        var sessao = Registrar(this, new SessaoDeSetor());
        var cut = Render<MenuDeSetor>();

        cut.Find("[data-testid=sector-menu-toggle]").Click();
        cut.Find("[data-testid=sector-menu-toggle]").Click();
        cut.Find("[data-testid=sector-menu-toggle]").Click();

        Assert.Equal(2, sessao.Buscas);
    }

    [Fact]
    public void O_setor_atual_nao_se_distingue_so_pela_cor()
    {
        Registrar(this, new SessaoDeSetor());
        var cut = Render<MenuDeSetor>();

        cut.Find("[data-testid=sector-menu-toggle]").Click();

        var atual = cut.Find($"[data-testid=sector-menu-{Oeste}]");
        Assert.Equal("true", atual.GetAttribute("aria-current"));
        Assert.NotNull(atual.QuerySelector(".setor-menu__marca"));
    }

    [Fact]
    public void Escolher_outro_setor_troca_a_sessao()
    {
        var sessao = Registrar(this, new SessaoDeSetor());
        var cut = Render<MenuDeSetor>();

        cut.Find("[data-testid=sector-menu-toggle]").Click();
        cut.Find($"[data-testid=sector-menu-{Leste}]").Click();

        Assert.Equal(Leste, sessao.Escolhido);
    }

    /// <summary>
    /// O checklist aberto é de um modelo do setor ANTERIOR. Ficar nele depois da troca mostraria
    /// uma tela que não existe mais no setor novo.
    /// </summary>
    [Fact]
    public void Trocar_de_setor_volta_ao_painel()
    {
        Registrar(this, new SessaoDeSetor());
        var navegacao = Services.GetRequiredService<BunitNavigationManager>();
        navegacao.NavigateTo("/checklist/algum-modelo");

        var cut = Render<MenuDeSetor>();
        cut.Find("[data-testid=sector-menu-toggle]").Click();
        cut.Find($"[data-testid=sector-menu-{Leste}]").Click();

        Assert.EndsWith("/", navegacao.Uri, StringComparison.Ordinal);
    }

    /// <summary>Tocar no setor em que já se está não pode recarregar o plantão inteiro à toa.</summary>
    [Fact]
    public void Escolher_o_setor_atual_nao_faz_nada()
    {
        var sessao = Registrar(this, new SessaoDeSetor());
        var cut = Render<MenuDeSetor>();

        cut.Find("[data-testid=sector-menu-toggle]").Click();
        cut.Find($"[data-testid=sector-menu-{Oeste}]").Click();

        Assert.Null(sessao.Escolhido);
        Assert.Empty(cut.FindAll("[data-testid=sector-menu]"));
    }

    [Fact]
    public void Com_um_setor_so_o_painel_explica_em_vez_de_listar()
    {
        Registrar(this, new SessaoDeSetor { Setores = [new SectorSummary(Oeste, "Oeste", 0, true)] });
        var cut = Render<MenuDeSetor>();

        cut.Find("[data-testid=sector-menu-toggle]").Click();

        Assert.Empty(cut.FindAll(".setor-menu__item"));
        Assert.Contains("um setor só", cut.Find("[data-testid=sector-menu]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void O_fundo_fecha_o_menu()
    {
        Registrar(this, new SessaoDeSetor());
        var cut = Render<MenuDeSetor>();

        cut.Find("[data-testid=sector-menu-toggle]").Click();
        cut.Find("[data-testid=sector-menu-backdrop]").Click();

        Assert.Empty(cut.FindAll("[data-testid=sector-menu]"));
    }

    private sealed class SessaoDeSetor : IAppSession
    {
        public bool IsAuthenticated => true;

        public string DisplayName => "Maria";

        public EffectiveAccess Acesso { get; init; } = ComTroca();

        public EffectiveAccess Access => Acesso;

        public bool PermissionsAreStale => false;

        public DateTime? LastServerValidationUtc => null;

        public Guid? CurrentSectorId => Oeste;

        public string? NomeDoSetor { get; init; } = "Oeste";

        public string? CurrentSectorName => NomeDoSetor;

        public string? LastSignInError => null;

        public IReadOnlyList<SectorSummary> Setores { get; init; } =
        [
            new(Oeste, "Oeste", 3, true),
            new(Leste, "Leste", 0, true),
        ];

        public int Buscas { get; private set; }

        public Guid? Escolhido { get; private set; }

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<SectorSummary>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default)
        {
            Buscas++;
            return Task.FromResult(Setores);
        }

        public Task SelectSectorAsync(Guid sectorId, CancellationToken cancellationToken = default)
        {
            Escolhido = sectorId;
            return Task.CompletedTask;
        }

        public Task<bool> SignInAsync(string userName, string password, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static EffectiveAccess ComTroca()
        {
            var grupo = new AccessGroup(Guid.CreateVersion7(), "Teste", null, grantsAllSectors: true, DateTime.UnixEpoch);
            grupo.ReplacePermissions([Permissions.SectorSelect], DateTime.UnixEpoch);
            return EffectiveAccess.FromGroups([grupo]);
        }
    }
}
