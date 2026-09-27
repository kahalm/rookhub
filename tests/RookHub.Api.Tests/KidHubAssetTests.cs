using System.Text.Json;
using System.Text.RegularExpressions;

namespace RookHub.Api.Tests;

/// <summary>
/// Wacht ueber die Symbole von KidHub (Kinderseite) — dieselbe Klasse Fehler wie bei der
/// Turnierseite (<see cref="TurnierAssetTests"/>): `public-kidhub/` wird UEBER `public/` gelegt,
/// und was dort fehlt, liefert der Build still in RookHubs Fassung aus. Kein Compiler und kein
/// Karma-Test sieht das; nur ein Blick auf die Platte.
/// </summary>
public class KidHubAssetTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string KidHubPublic =>
        Path.Combine(RepoRoot(), "src", "frontend", "app", "public-kidhub");

    [Fact]
    public void Manifest_VerweistNurAufEigeneDateien()
    {
        var manifest = Path.Combine(KidHubPublic, "manifest.webmanifest");
        Assert.True(File.Exists(manifest), "manifest.webmanifest von KidHub fehlt");

        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        Assert.Equal("KidHub", doc.RootElement.GetProperty("name").GetString());
        var icons = doc.RootElement.GetProperty("icons").EnumerateArray().ToList();
        Assert.NotEmpty(icons);
        foreach (var icon in icons)
        {
            var src = icon.GetProperty("src").GetString();
            var path = Path.Combine(KidHubPublic, src!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Manifest nennt {src}, aber die Datei fehlt in public-kidhub/");
        }

        var purposes = icons.Select(i => i.GetProperty("purpose").GetString()).ToList();
        Assert.Contains("any", purposes);
        Assert.Contains("maskable", purposes);
    }

    [Theory]
    [InlineData("icons/icon-192.png")]
    [InlineData("icons/icon-512.png")]
    [InlineData("icons/icon-192-maskable.png")]
    [InlineData("icons/icon-512-maskable.png")]
    [InlineData("icons/apple-touch-icon.png")]
    [InlineData("favicon.ico")]
    [InlineData("og-image.png")]
    public void JedesGebrauchteSymbol_LiegtInPublicKidHub(string relative)
    {
        var path = Path.Combine(KidHubPublic, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{relative} fehlt in public-kidhub/");
        Assert.True(new FileInfo(path).Length > 500, $"{relative} ist verdaechtig klein");
    }

    /// <summary>Jeder Symbol-Verweis im HTML muss auf eine EIGENE Datei zeigen.</summary>
    [Fact]
    public void IndexHtml_VerweistNurAufEigeneSymbole()
    {
        var html = File.ReadAllText(Path.Combine(RepoRoot(), "src", "frontend", "app", "src-kidhub", "index.html"));
        foreach (Match m in Regex.Matches(html, "<link[^>]+href\\s*=\\s*\"(?<href>(icons/[^\"]+|favicon\\.ico|manifest\\.webmanifest))\""))
        {
            var href = m.Groups["href"].Value;
            Assert.True(File.Exists(Path.Combine(KidHubPublic, href.Replace('/', Path.DirectorySeparatorChar))),
                $"src-kidhub/index.html verweist auf {href}, aber public-kidhub/ hat die Datei nicht");
        }
    }

    /// <summary>Die Vorlagen bleiben im Repo — ohne sie liesse sich keine Groesse nachziehen.</summary>
    [Theory]
    [InlineData("KidHub.png")]
    [InlineData("derive.py")]
    public void Designvorlage_LiegtImRepo(string name) =>
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "design", "kidhub", name)),
            $"design/kidhub/{name} fehlt — Vorlage der KidHub-Symbole");
}
