using AngleSharp.Dom;
using Bunit;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Client.Abstractions;
using ChecklistPlantao.UI.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// Uma tela que falha não pode derrubar o aplicativo inteiro.
///
/// Sem a barreira de erro, qualquer exceção de página encerrava o renderizador do Blazor: a
/// interface toda morria, sobrava o aviso cru do host e o link "Recarregar" travava em
/// "Carregando…" — porque recarregar o WebView com o estado quebrado não reinicia nada. Pior: a
/// navegação sumia junto, então não havia como sair da tela quebrada.
/// </summary>
public sealed class ErrorBoundaryTests : BunitContext
{
    private void RegistrarServicos()
    {
        Services.AddSingleton<IAppSession>(new FakeSession());
        Services.AddSingleton<ISyncStatusService>(new FakeSync());
        Services.AddSingleton<INotificationStatusService>(new FakeNotifications());
    }

    private IRenderedComponent<MainLayout> RenderComPaginaQuebrada()
    {
        RegistrarServicos();

        return Render<MainLayout>(p => p.Add(
            l => l.Body,
            (RenderFragment)(builder =>
            {
                builder.OpenComponent<PaginaQueQuebra>(0);
                builder.CloseComponent();
            })));
    }

    /// <summary>
    /// A falha da página é assíncrona, então a barreira só troca o conteúdo num render seguinte.
    /// Esperar por isso é parte do que se está testando.
    /// </summary>
    private static IElement AguardarTelaDeErro(IRenderedComponent<MainLayout> cut) =>
        cut.WaitForElement("[data-testid=page-error]", TimeSpan.FromSeconds(5));

    [Fact]
    public void Falha_de_pagina_e_contida_e_nao_derruba_a_casca()
    {
        var cut = RenderComPaginaQuebrada();

        AguardarTelaDeErro(cut);

        // A barra do topo continua de pé: o aplicativo não morreu junto com a página.
        Assert.NotEmpty(cut.FindAll(".app-topo"));
    }

    [Fact]
    public void Tela_de_erro_oferece_saida_e_nova_tentativa()
    {
        var cut = RenderComPaginaQuebrada();

        var texto = AguardarTelaDeErro(cut).TextContent;

        Assert.Contains("Tentar de novo", texto, StringComparison.Ordinal);
        Assert.Contains("Voltar ao início", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Tela_de_erro_tranquiliza_sobre_as_marcacoes()
    {
        var cut = RenderComPaginaQuebrada();

        // O que o plantão precisa saber primeiro é que não perdeu trabalho.
        Assert.Contains("salvas neste aparelho", AguardarTelaDeErro(cut).TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Detalhe_tecnico_fica_disponivel_mas_recolhido()
    {
        var cut = RenderComPaginaQuebrada();

        AguardarTelaDeErro(cut);

        Assert.NotEmpty(cut.FindAll("details"));
        Assert.NotEmpty(cut.FindAll("[data-testid=page-error-detail]"));
    }

    [Fact]
    public void Navegacao_continua_acessivel_com_a_tela_quebrada()
    {
        var cut = RenderComPaginaQuebrada();

        AguardarTelaDeErro(cut);

        // A saída da tela quebrada precisa continuar visível — é o que faltava.
        Assert.NotEmpty(cut.FindAll("[data-testid=island-nav]"));
        Assert.Contains("Painel", cut.Find("[data-testid=island-nav]").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Página que sempre falha ao carregar.
    ///
    /// A falha é ASSÍNCRONA de propósito: é assim que uma página real quebra — carregando dados
    /// no OnInitializedAsync. Lançar de forma síncrona dentro do lote de renderização corromperia
    /// a árvore antes de a barreira poder agir, e o teste mediria outra coisa.
    /// </summary>
    private sealed class PaginaQueQuebra : ComponentBase
    {
        protected override async Task OnInitializedAsync()
        {
            await Task.Yield();
            throw new InvalidOperationException("Falha proposital do teste.");
        }
    }

    private sealed class FakeSync : ISyncStatusService
    {
        public SyncStatus Current { get; } = SyncStatus.Unknown;

        public event Action<SyncStatus>? Changed;

        public Task SyncNowAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(Current);
            return Task.CompletedTask;
        }

        public Task RefreshConnectivityAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeNotifications : INotificationStatusService
    {
        public NotificationStatus Current { get; } =
            new(true, true, true, true, true, false, null, null, []);

        public event Action<NotificationStatus>? Changed;

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(Current);
            return Task.CompletedTask;
        }

        public Task<bool> SendTestNotificationAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task OpenSystemSettingsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Autenticado, para que a navegação seja renderizada.</summary>
    private sealed class FakeSession : IAppSession
    {
        public bool IsAuthenticated => true;

        public string DisplayName => "Teste";

        public EffectiveAccess Access => EffectiveAccess.None;

        public bool PermissionsAreStale => false;

        public DateTime? LastServerValidationUtc => DateTime.UtcNow;

        public Guid? CurrentSectorId => null;

        public string? CurrentSectorName => "Oeste";

        public string? LastSignInError => null;

        public event Action? Changed;

        public Task<IReadOnlyList<SectorSummary>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SectorSummary>>([]);

        public Task SelectSectorAsync(Guid sectorId, CancellationToken cancellationToken = default)
        {
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public Task<bool> SignInAsync(string userName, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
