using System.Text.RegularExpressions;

namespace RookHub.Api.Tests;

/// <summary>
/// Wacht ueber Deployment-/CI-Dateien, die kein Compiler prueft. Alle drei Punkte hier
/// waren echte Fehler: Frontend-Port 80 in den Beispiel-Composes (nginx lauscht als
/// non-root auf 8080 -> toter Stack), ein 9.0.x-SDK-Pin fuer net10-Projekte (lief nur
/// zufaellig ueber das vorinstallierte Runner-SDK) und ein Floating-NuGet-Range.
/// </summary>
public class DeploymentConfigTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Datei fehlt: {relativePath}");
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("compose.yml.example")]
    [InlineData("compose.vpn.example")]
    public void ExampleCompose_MapsFrontendToContainerPort8080(string file)
    {
        var text = ReadRepoFile(file);

        Assert.Contains("${FRONTEND_PORT}:8080", text);
        Assert.DoesNotContain("${FRONTEND_PORT}:80\"", text);
    }

    [Theory]
    [InlineData("compose.yml.example")]
    [InlineData("compose.vpn.example")]
    public void ExampleCompose_CarriesDataProtectionVolumeAndNewerSettings(string file)
    {
        var text = ReadRepoFile(file);

        // Ohne persistentes /keys sind DataProtection-Keys nach jedem Neustart weg.
        Assert.Contains("dataprotection-keys:/keys", text);
        Assert.Contains("Encryption__Key:", text);
        Assert.Contains("App__BaseUrl:", text);
        Assert.Contains("Email__SmtpHost:", text);
        Assert.Contains("Discord__LinkSecret:", text);
    }

    [Fact]
    public void TestWorkflow_PinsDotnet10_AndRunsFrontendSpecs()
    {
        var text = ReadRepoFile(".github/workflows/test.yml");

        Assert.DoesNotContain("dotnet-version: '9.", text);
        Assert.Contains("dotnet-version: '10.0.x'", text);
        // Ohne diesen Schritt liefe im Image-Gate kein einziger Frontend-Spec.
        Assert.Contains("ng test", text);
    }

    [Fact]
    public void AuditWorkflow_ScansAllThreeEcosystems()
    {
        var text = ReadRepoFile(".github/workflows/audit.yml");

        Assert.Contains("--vulnerable", text);
        Assert.Contains("npm audit", text);
        Assert.Contains("pip-audit", text);
        // Muss melden, nicht blockieren — sonst faellt der Release-Pfad auf ein
        // fremdes Advisory herein.
        Assert.Contains("continue-on-error: true", text);
    }

    [Fact]
    public void TwaWorkflow_RefusesToInventTheReleaseTag()
    {
        var text = ReadRepoFile(".github/workflows/android-twa.yml");

        // action-gh-release wuerde einen fehlenden Tag selbst anlegen; fuer den
        // wird dann nie ein :latest-Image gebaut.
        Assert.Contains("git ls-remote --exit-code --tags origin", text);
    }

    [Fact]
    public void ApiProject_HasNoFloatingPackageVersions()
    {
        var text = ReadRepoFile("src/api/RookHub.Api/RookHub.Api.csproj");

        var floating = Regex.Matches(text, "<PackageReference[^>]*Version=\"([^\"]*[*][^\"]*)\"")
            .Select(m => m.Value)
            .ToList();

        Assert.True(floating.Count == 0,
            "Floating-Versionen ziehen bei jedem Restore unbemerkt neue Pakete: "
            + string.Join(", ", floating));
    }

    [Fact]
    public void OperationsScripts_AreShipped()
    {
        // Backup + Log-Retention gab es lange gar nicht — hier festnageln, damit sie
        // nicht wieder still verschwinden.
        Assert.Contains("mariadb-dump", ReadRepoFile("scripts/backup-db.sh"));
        Assert.Contains("_ilm/policy", ReadRepoFile("scripts/es_log_retention.py"));
        Assert.Contains("Restore", ReadRepoFile("docs/backup.md"));
    }
}
