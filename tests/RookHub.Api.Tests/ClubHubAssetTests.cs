using System.Text.RegularExpressions;

namespace RookHub.Api.Tests;

/// <summary>
/// Wacht über die eigenen Dateien von ClubHub — dieselbe Klasse Fehler wie bei der Turnierseite und KidHub
/// (<see cref="KidHubAssetTests"/>): <c>public-clubhub/</c> wird ÜBER <c>public/</c> gelegt, eine dort FEHLENDE Datei
/// fällt still auf RookHubs Fassung zurück (Symbol) bzw. lädt gar nicht (Schrift) — ohne Fehler im Build.
/// </summary>
public class ClubHubAssetTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string App => Path.Combine(RepoRoot(), "src", "frontend", "app");
    private static string ClubHubPublic => Path.Combine(App, "public-clubhub");

    [Fact]
    public void IndexHtml_VerweistAufEinEigenesSymbol()
    {
        var html = File.ReadAllText(Path.Combine(App, "src-clubhub", "index.html"));
        var icons = Regex.Matches(html, "<link[^>]+rel=\"icon\"[^>]+href=\"(?<href>[^\"]+)\"").Select(m => m.Groups["href"].Value).ToList();

        Assert.NotEmpty(icons);
        foreach (var href in icons)
            Assert.True(File.Exists(Path.Combine(ClubHubPublic, href.Replace('/', Path.DirectorySeparatorChar))),
                $"src-clubhub/index.html verweist auf {href}, aber public-clubhub/ hat die Datei nicht — es käme RookHubs Symbol");
    }

    [Fact]
    public void JedeSchriftDerGestaltung_LiegtInPublicClubHub()
    {
        var scss = File.ReadAllText(Path.Combine(App, "src-clubhub", "clubhub.scss"));
        var fonts = Regex.Matches(scss, "url\\(\"/(?<path>fonts/[^\"]+)\"\\)").Select(m => m.Groups["path"].Value).Distinct().ToList();

        Assert.True(fonts.Count >= 4, $"Nur {fonts.Count} Schrift-Verweise gefunden — Muster kaputt?");
        foreach (var path in fonts)
            Assert.True(File.Exists(Path.Combine(ClubHubPublic, path.Replace('/', Path.DirectorySeparatorChar))),
                $"clubhub.scss lädt /{path}, aber public-clubhub/ hat die Datei nicht");
    }

    /// <summary>Beide Schriften stehen unter der SIL Open Font License — der Lizenztext reist mit den Dateien.</summary>
    [Theory]
    [InlineData("OFL-ZillaSlab.txt")]
    [InlineData("OFL-AtkinsonHyperlegible.txt")]
    public void DieLizenzDerSchrift_LiegtBei(string file) =>
        Assert.True(File.Exists(Path.Combine(ClubHubPublic, "fonts", file)), $"public-clubhub/fonts/{file} fehlt");
}
