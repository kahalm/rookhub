using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// SPIEGELTEST der Verzeichnis-Kennung: jede Form, die ein Erzeuger baut, muss
/// <see cref="DirectoryPublicId.IsValid"/> bestehen — sonst antworten Detailseite, Ausblenden und
/// Melden fuer diese Quelle mit 400 (A5-003: 15 Verbandsformen fielen durch das alte Muster).
///
/// <para>Die Erzeuger werden nicht abgeschrieben, sondern GEFUNDEN: die Kurzschluessel-Quellen per
/// Reflection (jede statische <c>PublicIdOf(string)</c>), die Quellen mit eigener Nummer per
/// Quelltext-Scan (<c>publicId = $"…{…}"</c>). Eine neue Quelle faellt damit hier auf, ohne dass
/// jemand an diesen Test denken muss.</para>
/// </summary>
public class DirectoryPublicIdTests
{
    private static string ApiSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = Path.Combine(dir!.FullName, "src", "api", "RookHub.Api");
        Assert.True(Directory.Exists(src), $"Quellordner fehlt: {src}");
        return src;
    }

    /// <summary>Jede statische <c>PublicIdOf(string)</c> im API-Assembly.</summary>
    private static List<MethodInfo> ShortKeyGenerators() =>
        typeof(DirectoryPublicId).Assembly.GetTypes()
            .Select(t => t.GetMethod("PublicIdOf",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, [typeof(string)]))
            .Where(m => m is not null && m.ReturnType == typeof(string))
            .Select(m => m!)
            .ToList();

    [Fact]
    public void JederKurzschluesselErzeuger_liefertEineGueltigeKennung()
    {
        var generators = ShortKeyGenerators();
        // cz, nl, de, wl, ca, no, sc — faellt die Zahl, hat die Suche die Erzeuger verloren.
        Assert.True(generators.Count >= 7,
            $"Nur {generators.Count} PublicIdOf-Erzeuger gefunden: {string.Join(", ", generators.Select(g => g.DeclaringType!.Name))}");

        var slugs = new[]
        {
            "a", "2026-10-10|Hotel Metropole, Llandudno", "10-oktober-open-2026-mit-sehr-langem-slug-der-ueber-sechzig-zeichen-geht",
            "Š Č Ž ÄÖÜ", "  mixed Case  ",
        };
        foreach (var generator in generators)
        {
            foreach (var slug in slugs)
            {
                var id = (string)generator.Invoke(null, [slug])!;
                Assert.True(DirectoryPublicId.IsValid(id),
                    $"{generator.DeclaringType!.Name}.PublicIdOf(\"{slug}\") = \"{id}\" besteht die Pruefung nicht");
            }
        }
    }

    /// <summary>
    /// Die Quellen mit eigener Nummer bauen die Kennung inline (<c>$"ie{row.EventId}"</c>). Ihre
    /// Nummern sind laut Crawler Ziffern; eingesetzt wird fuer jedes Loch eine.
    /// </summary>
    [Fact]
    public void JedeInlineKennungDerQuellen_bestehtDiePruefung()
    {
        var assignment = new Regex(@"(?i)publicId\s*=.*?\$""(?<fmt>[^""]*\{[^""]*)""");
        var formats = new List<(string File, string Format)>();
        foreach (var file in Directory.GetFiles(Path.Combine(ApiSourceRoot(), "Services"), "*.cs"))
        {
            foreach (var line in File.ReadLines(file))
            {
                var m = assignment.Match(line);
                if (m.Success) formats.Add((Path.GetFileName(file), m.Groups["fmt"].Value));
            }
        }

        // ie, hu, sk, en, fr, ro, it, sl, pl (Jahr-Nummer) und k (Ankuendigungskalender).
        Assert.True(formats.Count >= 10,
            $"Nur {formats.Count} Inline-Kennungen gefunden: {string.Join(", ", formats.Select(f => f.Format))}");

        foreach (var (file, format) in formats)
        {
            var sample = Regex.Replace(format, @"\{[^}]*\}", "2026");
            Assert.True(DirectoryPublicId.IsValid(sample),
                $"{file}: Kennung \"{format}\" (Beispiel \"{sample}\") besteht die Pruefung nicht");
        }
    }

    [Theory]
    [InlineData("1474416")]            // chess-results
    [InlineData("1234567890")]         // zehn Ziffern
    [InlineData("f14805")]             // FIDE-Kalender
    [InlineData("k987654")]            // Ankuendigungskalender
    [InlineData("ie12345")]            // Irland
    [InlineData("hu4711")]
    [InlineData("sk5956")]
    [InlineData("en123")]
    [InlineData("fr62345")]
    [InlineData("ro7")]
    [InlineData("it18234")]
    [InlineData("sl905")]
    [InlineData("pl2026-4711")]        // Polen: Jahr, Bindestrich, Nummer
    [InlineData("de0123456789ab")]     // Kurzschluessel: 12 Hex-Zeichen
    [InlineData("cz9f8e7d6c5b4a")]
    public void Gueltig(string id) => Assert.True(DirectoryPublicId.IsValid(id), id);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]                // drei Buchstaben ohne Ziffer
    [InlineData("f")]
    [InlineData("12345678901")]        // elf Ziffern
    [InlineData("fide-14805")]         // ausgeschriebenes Kuerzel
    [InlineData("../etc")]
    [InlineData("ie12345/..")]
    [InlineData("1.2")]
    [InlineData("IE12345")]            // die Erzeuger schreiben klein
    [InlineData("123\n")]
    [InlineData(" 123")]
    [InlineData("pl2026-")]
    [InlineData("de0123456789abc")]    // 13 Hex-Zeichen
    [InlineData("de0123456789ag")]     // kein Hex
    [InlineData("abc123")]             // drei Buchstaben vor der Nummer
    public void Ungueltig(string? id) => Assert.False(DirectoryPublicId.IsValid(id), id ?? "null");

    [Fact]
    public void LaengerAlsDieSpalte_istUngueltig()
    {
        // Das Muster selbst liesse hoechstens 19 Zeichen durch; der Laengendeckel ist die zweite Wache.
        Assert.False(DirectoryPublicId.IsValid(new string('1', DirectoryPublicId.MaxLength + 1)));

        // Der Deckel IST die Spaltenlaenge — aendert sich die eine, muss die andere mit.
        var column = typeof(TournamentDirectoryEntry).GetProperty(nameof(TournamentDirectoryEntry.PublicId))!
            .GetCustomAttribute<MaxLengthAttribute>()!.Length;
        Assert.Equal(DirectoryPublicId.MaxLength, column);
    }
}
