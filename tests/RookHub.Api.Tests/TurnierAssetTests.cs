using System.Text.Json;
using System.Text.RegularExpressions;

namespace RookHub.Api.Tests;

/// <summary>
/// Wacht ueber die Symbole der Turnierseite — etwas, das kein Compiler und kein Karma-Test
/// sieht.
///
/// <para><b>Der echte Fehler, der das hier ausgeloest hat</b> (gefunden 2026-09-07): das
/// Manifest der Turnierseite verwies auf `icons/icon-192.png` und `icons/icon-512.png`, aber
/// `public-turnier/` enthielt ueberhaupt keine Symbole. Der Build legt
/// `public-turnier/` UEBER `public/` (siehe `angular.json`), und was dort fehlt, faellt
/// still auf RookHubs Symbol zurueck. Die Turnierseite trug deshalb monatelang das falsche
/// Logo — ohne Fehler, ohne Warnung, ohne 404: die Datei WAR da, nur die falsche.</para>
///
/// <para>Genau diese Klasse Fehler kann nur ein Test finden, der die Dateien auf der Platte
/// gegen ihre Verweise haelt.</para>
/// </summary>
public class TurnierAssetTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string TurnierPublic =>
        Path.Combine(RepoRoot(), "src", "frontend", "app", "public-turnier");

    /// <summary>
    /// Jedes im Manifest genannte Symbol muss in `public-turnier/` LIEGEN. Liegt es nur in
    /// `public/`, zeigt die Turnierseite RookHubs Logo — der Fehler von oben.
    /// </summary>
    [Fact]
    public void Manifest_VerweistNurAufEigeneDateien()
    {
        var manifest = Path.Combine(TurnierPublic, "manifest.webmanifest");
        Assert.True(File.Exists(manifest), "manifest.webmanifest der Turnierseite fehlt");

        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        var icons = doc.RootElement.GetProperty("icons").EnumerateArray().ToList();
        Assert.NotEmpty(icons);

        foreach (var icon in icons)
        {
            var src = icon.GetProperty("src").GetString();
            Assert.False(string.IsNullOrWhiteSpace(src));
            var path = Path.Combine(TurnierPublic, src!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                $"Manifest nennt {src}, aber die Datei fehlt in public-turnier/ — der Build "
                + "liefert dann RookHubs Symbol aus.");
        }
    }

    /// <summary>
    /// Beide Groessen, beide Zwecke. `maskable` ist nicht optional: ohne ein Symbol mit
    /// Sicherheitsabstand beschneidet Android das normale und schneidet dem Turm die Krone ab.
    /// </summary>
    [Theory]
    [InlineData("icons/icon-192.png")]
    [InlineData("icons/icon-512.png")]
    [InlineData("icons/icon-192-maskable.png")]
    [InlineData("icons/icon-512-maskable.png")]
    [InlineData("icons/apple-touch-icon.png")]
    [InlineData("favicon.ico")]
    [InlineData("og-image.png")]
    public void JedesGebrauchteSymbol_LiegtInPublicTurnier(string relative)
    {
        var path = Path.Combine(TurnierPublic, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{relative} fehlt in public-turnier/");
        Assert.True(new FileInfo(path).Length > 500, $"{relative} ist verdaechtig klein");
    }

    /// <summary>
    /// Das Manifest muss BEIDE Zwecke abdecken — `any` und `maskable`.
    /// </summary>
    [Fact]
    public void Manifest_HatBeideZwecke()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(TurnierPublic, "manifest.webmanifest")));
        var purposes = doc.RootElement.GetProperty("icons").EnumerateArray()
            .Select(i => i.GetProperty("purpose").GetString())
            .ToList();

        Assert.Contains("any", purposes);
        Assert.Contains("maskable", purposes);
    }

    /// <summary>
    /// Die Turnierseite hat KEINE Vektorfassung. Bliebe ein `icon.svg`-Verweis im HTML stehen,
    /// lieferte der Build RookHubs SVG aus — dieselbe stille Verwechslung wie oben, nur ueber
    /// einen anderen Weg.
    /// </summary>
    [Fact]
    public void IndexHtml_VerweistNichtAufEinNichtVorhandenesSvg()
    {
        var index = Path.Combine(
            RepoRoot(), "src", "frontend", "app", "src-turnier", "index.html");
        var html = File.ReadAllText(index);

        // Nur ein echter VERWEIS zaehlt, nicht jede Nennung: im HTML steht ein Kommentar, der
        // erklaert, warum es diesen Verweis NICHT gibt — eine reine Textsuche schlug darauf an.
        var referenced = Regex.IsMatch(html, "href\\s*=\\s*[\"']icons/icon\\.svg[\"']");
        if (referenced)
        {
            Assert.True(File.Exists(Path.Combine(TurnierPublic, "icons", "icon.svg")),
                "src-turnier/index.html verweist auf icons/icon.svg, aber public-turnier/ hat "
                + "keines — ausgeliefert wuerde RookHubs SVG.");
        }
    }

    /// <summary>
    /// Die Designvorlagen bleiben im Repo: die ausgelieferten Symbole sind ABLEITUNGEN, und ohne
    /// die Vorlagen liesse sich eine Groesse nicht nachziehen (siehe `public-turnier/ASSETS.md`).
    /// </summary>
    [Theory]
    [InlineData("Designer.png")]
    [InlineData("Designer2.png")]
    [InlineData("Designer3.png")]
    public void Designvorlage_LiegtImRepo(string name)
    {
        var path = Path.Combine(RepoRoot(), "design", name);
        Assert.True(File.Exists(path), $"design/{name} fehlt — Vorlage der Turnier-Symbole");
    }
}
