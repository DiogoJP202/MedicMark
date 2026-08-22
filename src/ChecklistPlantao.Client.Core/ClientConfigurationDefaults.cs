using ChecklistPlantao.Client.Core.Sync;

namespace ChecklistPlantao.Client.Core;

/// <summary>
/// Valores fornecidos pelo aplicativo hospedeiro para a primeira configuração do aparelho.
/// Um servidor real salvo pelo usuário continua tendo prioridade sobre estes padrões.
/// </summary>
public sealed class ClientConfigurationDefaults
{
    private readonly HashSet<string> _replacedServerUrls;

    public ClientConfigurationDefaults(
        string? serverUrl = null,
        bool hideServerAddress = false,
        bool requireHttps = false,
        params string[] replacedServerUrls)
    {
        ServerUrl = string.IsNullOrWhiteSpace(serverUrl)
            ? null
            : HttpServerApi.NormalizeUrl(serverUrl);

        _replacedServerUrls = replacedServerUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(HttpServerApi.NormalizeUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        HideServerAddress = hideServerAddress;
        RequireHttps = requireHttps;
    }

    /// <summary>Endereço gravado automaticamente quando o aparelho ainda não possui servidor.</summary>
    public string? ServerUrl { get; }

    /// <summary>Não renderiza o endereço configurado nas telas do aplicativo distribuído.</summary>
    public bool HideServerAddress { get; }

    /// <summary>Exige HTTPS tanto ao testar quanto ao salvar um servidor alternativo.</summary>
    public bool RequireHttps { get; }

    /// <summary>
    /// Endereços de loopback pertencem ao próprio aparelho. Eles eram usados com
    /// <c>adb reverse</c> durante o desenvolvimento e não devem impedir a migração para o servidor
    /// empacotado na distribuição oficial.
    /// </summary>
    public bool ShouldReplace(string? configuredServerUrl)
    {
        if (ServerUrl is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuredServerUrl))
        {
            return true;
        }

        if (!Uri.TryCreate(configuredServerUrl, UriKind.Absolute, out var configuredUri))
        {
            return false;
        }

        return configuredUri.IsLoopback
            || _replacedServerUrls.Contains(HttpServerApi.NormalizeUrl(configuredServerUrl));
    }
}
