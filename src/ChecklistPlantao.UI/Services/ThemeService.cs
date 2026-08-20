using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ChecklistPlantao.UI.Services;

/// <summary>
/// A escolha de tema do APARELHO — não da conta.
///
/// Quem trabalha à noite quer a tela escura mesmo que o celular esteja no modo claro, e quem
/// usa o mesmo login no computador da sala quer a tela clara. Por isso a preferência é do
/// aparelho, e por isso ela precisa valer offline.
/// </summary>
public enum ThemeChoice
{
    /// <summary>Segue o aparelho. É o padrão, e o que cobre a maioria sem ninguém configurar nada.</summary>
    Automatic,
    Light,
    Dark,
}

public interface IThemeService
{
    /// <summary>A escolha em vigor. Devolve <see cref="ThemeChoice.Automatic"/> se nada foi escolhido.</summary>
    Task<ThemeChoice> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// O tema que está VALENDO — nunca <see cref="ThemeChoice.Automatic"/>. Com "seguir o
    /// aparelho" escolhido, devolve o que o aparelho decidiu.
    ///
    /// É o que um interruptor de duas posições precisa saber: mostrar a posição errada é pior
    /// que não ter interruptor.
    /// </summary>
    Task<ThemeChoice> GetEffectiveAsync(CancellationToken cancellationToken = default);

    Task SetAsync(ThemeChoice choice, CancellationToken cancellationToken = default);
}

/// <summary>
/// Guarda a escolha no armazenamento local do WebView e marca o &lt;html&gt;.
///
/// Por que NÃO no banco local, que era o plano original: subir a versão do esquema local
/// APAGA o banco — inclusive a fila de envio. Uma preferência de aparência não pode custar as
/// marcações de um plantão. Ver docs/DECISIONS.md (D-024).
/// </summary>
public sealed class WebViewThemeService(IJSRuntime js, ILogger<WebViewThemeService> logger) : IThemeService
{
    public async Task<ThemeChoice> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var lido = await js.InvokeAsync<string>("temaDoAplicativo.lido", cancellationToken);
            return Interpretar(lido);
        }
        catch (Exception erro) when (erro is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Sem JavaScript disponível (pré-renderização, teste de componente) o tema é o do
            // aparelho. Ler uma preferência de aparência nunca pode derrubar a tela.
            logger.LogDebug(erro, "Não foi possível ler a preferência de tema; assumindo automático.");
            return ThemeChoice.Automatic;
        }
    }

    public async Task<ThemeChoice> GetEffectiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var lido = await js.InvokeAsync<string>("temaDoAplicativo.efetivo", cancellationToken);
            return Interpretar(lido) is ThemeChoice.Dark ? ThemeChoice.Dark : ThemeChoice.Light;
        }
        catch (Exception erro) when (erro is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Sem JavaScript, o tema em vigor é o claro: é o que o CSS entrega sem nenhum atributo.
            logger.LogDebug(erro, "Não foi possível ler o tema em vigor; assumindo claro.");
            return ThemeChoice.Light;
        }
    }

    public async Task SetAsync(ThemeChoice choice, CancellationToken cancellationToken = default)
    {
        try
        {
            await js.InvokeVoidAsync("temaDoAplicativo.definir", cancellationToken, Escrever(choice));
        }
        catch (Exception erro) when (erro is JSException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogDebug(erro, "Não foi possível gravar a preferência de tema.");
        }
    }

    /// <summary>Os nomes atravessam para o JavaScript e para o armazenamento local; ficam em português lá.</summary>
    public static string Escrever(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => "claro",
        ThemeChoice.Dark => "escuro",
        _ => "automatico",
    };

    /// <summary>Qualquer coisa que não seja "claro" ou "escuro" é automático — inclusive lixo gravado por uma versão futura.</summary>
    public static ThemeChoice Interpretar(string? valor) => valor switch
    {
        "claro" => ThemeChoice.Light,
        "escuro" => ThemeChoice.Dark,
        _ => ThemeChoice.Automatic,
    };
}
