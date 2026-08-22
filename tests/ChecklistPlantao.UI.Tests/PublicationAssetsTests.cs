using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ChecklistPlantao.UI.Tests;

public sealed partial class PublicationAssetsTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Pagina_publica_aponta_para_artefatos_estaveis_sem_script_ou_email()
    {
        var index = File.ReadAllText(Path.Combine(Root, "site", "index.html"));
        var privacy = File.ReadAllText(Path.Combine(Root, "site", "privacidade.html"));

        Assert.Contains("releases/latest/download/MedicMark-Android.apk", index, StringComparison.Ordinal);
        Assert.Contains("releases/latest/download/MedicMark-Windows-x64.zip", index, StringComparison.Ordinal);
        Assert.Contains("releases/latest/download/SHA256SUMS.txt", index, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mailto:", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mailto:", privacy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Android_bloqueia_backup_e_trafego_sem_tls()
    {
        var manifestPath = Path.Combine(
            Root,
            "src",
            "ChecklistPlantao.Client",
            "Platforms",
            "Android",
            "AndroidManifest.xml");
        var networkPath = Path.Combine(
            Root,
            "src",
            "ChecklistPlantao.Client",
            "Platforms",
            "Android",
            "Resources",
            "xml",
            "network_security_config.xml");

        var manifest = XDocument.Load(manifestPath);
        XNamespace android = "http://schemas.android.com/apk/res/android";
        var application = Assert.Single(manifest.Root!.Elements("application"));

        Assert.Equal("false", application.Attribute(android + "allowBackup")?.Value);
        Assert.Contains("cleartextTrafficPermitted=\"false\"", File.ReadAllText(networkPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Workflows_fixas_as_actions_em_commits()
    {
        var workflows = Directory.GetFiles(Path.Combine(Root, ".github", "workflows"), "*.yml")
            .Select(File.ReadAllText)
            .ToArray();

        Assert.NotEmpty(workflows);
        Assert.All(workflows, workflow => Assert.DoesNotMatch(MovingActionTag(), workflow));
    }

    [Fact]
    public void Release_windows_usa_rid_portatil_sem_contaminar_os_outros_targets_maui()
    {
        var script = File.ReadAllText(Path.Combine(Root, "deploy", "release", "publish-release.ps1"));
        var project = File.ReadAllText(Path.Combine(
            Root,
            "src",
            "ChecklistPlantao.Client",
            "ChecklistPlantao.Client.csproj"));

        Assert.Contains("-p:RuntimeIdentifierOverride=win-x64", script, StringComparison.Ordinal);
        Assert.Contains("-p:WindowsAppSDKSelfContained=true", script, StringComparison.Ordinal);
        Assert.Contains("-p:SelfContained=true", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'-r', 'win-x64'", script, StringComparison.Ordinal);
        Assert.Contains("SelectSingleNode('/Project/PropertyGroup/ApplicationDisplayVersion')", script, StringComparison.Ordinal);
        Assert.Contains("<RuntimeIdentifier>$(RuntimeIdentifierOverride)</RuntimeIdentifier>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_android_le_a_versao_sem_incluir_espacos_de_outros_property_groups()
    {
        var script = File.ReadAllText(Path.Combine(Root, "deploy", "mobile", "publish-android.ps1"));

        Assert.Contains("SelectSingleNode('/Project/PropertyGroup/ApplicationDisplayVersion')", script, StringComparison.Ordinal);
        Assert.Contains("SelectSingleNode('/Project/PropertyGroup/ApplicationVersion')", script, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"uses:\s+[^\s@]+@v\d", RegexOptions.IgnoreCase)]
    private static partial Regex MovingActionTag();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
    }
}
