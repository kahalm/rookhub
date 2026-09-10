using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public sealed record GazetteerImportResult(string Source, int Imported, int Skipped, string? Error = null);

/// <summary>
/// Laedt die GeoNames-Exporte (CC BY 4.0) und fuellt <see cref="GeoPlace"/>. Bewusst NICHT beim
/// Start und nicht ins Image gebacken: die Daten aendern sich selten, sind je nach Laenderauswahl
/// unterschiedlich gross, und ein Deploy soll nicht an einem fremden Download haengen. Ausgeloest
/// wird der Import ueber den Admin-Endpunkt.
///
/// Ein Re-Import ersetzt die Zeilen des jeweiligen Landes komplett - damit ist der Lauf
/// wiederholbar und haelt keine Karteileichen.
/// </summary>
public class GazetteerImportService
{
    /// <summary>Entpackte Groesse, ab der abgebrochen wird - schuetzt vor einer Zip-Bombe.</summary>
    private const long MaxUncompressedBytes = 256L * 1024 * 1024;
    private const int BatchSize = 2000;

    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly ILogger<GazetteerImportService> _log;
    private readonly string _baseUrl;

    public GazetteerImportService(HttpClient http, AppDbContext db,
        ILogger<GazetteerImportService> log, IConfiguration configuration)
    {
        _http = http;
        _db = db;
        _log = log;
        _baseUrl = (configuration["Gazetteer:BaseUrl"] ?? "https://download.geonames.org/export/").TrimEnd('/') + "/";
    }

    /// <summary>Postleitzahlen + daraus abgeleitete Bundesland-Zentroide fuer ein Land.</summary>
    public async Task<GazetteerImportResult> ImportPostalCodesAsync(string iso2, CancellationToken ct = default)
    {
        iso2 = iso2.Trim().ToUpperInvariant();
        if (iso2.Length != 2 || !iso2.All(char.IsAsciiLetterUpper))
            return new GazetteerImportResult(iso2, 0, 0, "Ungueltiger ISO-3166-1-alpha-2-Code.");

        List<string> lines;
        try
        {
            lines = await DownloadZipEntryLinesAsync($"{_baseUrl}zip/{iso2}.zip", $"{iso2}.txt", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or InvalidOperationException)
        {
            _log.LogWarning(ex, "Gazetteer: PLZ-Download fuer {Country} fehlgeschlagen", iso2);
            return new GazetteerImportResult(iso2, 0, 0, ex.Message);
        }

        var places = ParsePostalLines(iso2, lines, out var skipped);

        await ReplaceAsync(places, g => g.Country == iso2 && g.Kind != GeoPlaceKind.City, ct);
        _log.LogInformation("Gazetteer: {Count} Eintraege fuer {Country} importiert ({Skipped} uebersprungen)",
            places.Count, iso2, skipped);
        return new GazetteerImportResult(iso2, places.Count, skipped);
    }

    /// <summary>
    /// Laender, fuer die GeoNames KEINEN Postleitzahl-Datensatz anbietet (am 2026-09-10 geprueft:
    /// <c>/export/zip/&lt;CC&gt;.zip</c> antwortet dort 404). Dort traegt allein die Ortsliste — und
    /// <c>cities15000</c> ist dafuer zu grob: Armenien hat darin 29 Orte, die Mongolei 24,
    /// Georgien 17. Fuer DIESE Laender wird <c>cities1000</c> gelesen, zusammen 7 397 statt 1 693
    /// Orte (Faktor 4,4): Iran 428 → 1 995, Griechenland 118 → 1 132, Vietnam 313 → 905.
    ///
    /// <para><b>Bewusst NICHT weltweit <c>cities1000</c>.</b> Die zehnfach groessere Liste bringt
    /// vor allem gleichnamige KLEINorte, und die laufen in die Mehrdeutigkeitsregel — also in
    /// denselben Topf, der schon 2 590 Eintraege ohne Pin haelt. Fuer Deutschland mit seinen 19
    /// Muenster ist das die falsche Richtung, fuer Kasachstan die richtige.</para>
    ///
    /// <para><b>Und bewusst eine feste Liste</b> statt „alle Laender, fuer die keine PLZ-Zeilen in
    /// der Tabelle stehen": das haenge von der REIHENFOLGE der Importe ab — laeuft der
    /// Staedte-Import vor dem PLZ-Import, waere die Menge eine andere. Eine stille Abhaengigkeit,
    /// die beim Lesen niemand sieht.</para>
    /// </summary>
    internal static readonly IReadOnlySet<string> DenseCityCountries =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AM", "BA", "EG", "GE", "GR", "IL", "IR", "KG", "KZ", "ME", "MN", "NG", "VN",
        };

    /// <summary>
    /// Ortsnamen, die GeoNames unter dem ENGLISCHEN Exonym fuehrt, waehrend die Turniertexte den
    /// einheimischen Namen schreiben. Das Lexikon bekommt dafuer eine ZWEITE Zeile mit denselben
    /// Koordinaten — kein Ersatz, sondern ein zweiter Name fuer denselben Punkt.
    ///
    /// <para><b>Warum eine Zeile und keine zweite Spalte.</b> Die Ortssuche vergleicht an manchen
    /// Stellen nur <see cref="GeoPlace.NameNormalized"/> (die Wortfolgen-Suche und der
    /// Praefix-Weg), an anderen beide Spalten. Eine zusaetzliche ZEILE wirkt auf allen Wegen.
    /// Und sie erzeugt keine neue Mehrdeutigkeit: die beiden Zeilen liegen am selben Punkt,
    /// ihr Abstand ist null.</para>
    ///
    /// <para><b>Jedes Paar ist einzeln nachgemessen</b> (2026-09-10): aufgenommen ist nur, was im
    /// Lexikon unter dem Exonym STEHT und unter dem Endonym FEHLT. Der Rest faellt heraus, und
    /// zwar zu Recht — wo Postleitzahlen importiert sind, tragen deren Ortsnamen das Endonym
    /// ohnehin: „Warszawa" 3 741 Zeilen, „Lisboa" 9 167, „Roma" 74, „Muenchen" 75. Rom, Warschau,
    /// Lissabon, Moskau, Kiew und Sevilla brauchen hier also nichts.</para>
    ///
    /// <para><b>Prag ist der lehrreiche Fall</b>: Tschechien HAT 15 507 Postleitzahl-Zeilen, aber
    /// die Prager heissen „Praha 1" bis „Praha 10" und normalisieren nie auf „praha" — dieselbe
    /// Form, die auch bei „Москва 194" auffiel. Ohne diese Zeile ist die Stadt ueber ihren
    /// eigenen Namen unerreichbar.</para>
    ///
    /// <para><b>Nicht hier hinein gehoert Muenchen.</b> Es sieht wie derselbe Fall aus, ist aber
    /// keiner: die Umschrift-Spalte traegt „muenchen" schon auf 75 Zeilen, der Treffer gelingt.
    /// Die 24 unverorteten Muenchner Turniere scheitern an der Mehrdeutigkeitsregel, weil diese
    /// 75 Zeilen 21,6 km spannen. Eine 76. Zeile aendert daran nichts.</para>
    /// </summary>
    internal static readonly (string Country, string Alias, string Canonical)[] PlaceAliases =
    [
        ("BE", "Bruxelles", "Brussels"),
        ("BE", "Brussel", "Brussels"),
        ("BE", "Antwerpen", "Antwerp"),
        ("BE", "Luik", "Liege"),
        ("CZ", "Praha", "Prague"),
        ("CZ", "Plzen", "Pilsen"),
        ("DK", "Koebenhavn", "Copenhagen"),
        ("DK", "Kobenhavn", "Copenhagen"),
        ("EG", "Al Qahirah", "Cairo"),
        ("GR", "Athina", "Athens"),
        ("GR", "Athinai", "Athens"),
        ("GR", "Peiraias", "Piraeus"),
        ("IR", "Shahrood", "Shahrud"),
        ("NL", "Den Haag", "The Hague"),
    ];

    /// <summary>
    /// Haengt je Eintrag aus <see cref="PlaceAliases"/> eine zweite Zeile an denselben Punkt.
    /// Fehlt der kanonische Ort in der Liste (weil GeoNames ihn umbenannt hat oder er unter der
    /// Einwohnergrenze liegt), passiert nichts — ein Alias ohne Ziel waere eine Zeile ohne
    /// Koordinaten und damit schlimmer als keine.
    /// </summary>
    internal static List<GeoPlace> AddAliasRows(List<GeoPlace> cities)
    {
        var added = new List<GeoPlace>();

        foreach (var (country, aliasName, canonical) in PlaceAliases)
        {
            var canonicalNormalized = GeoTextNormalizer.Normalize(canonical);
            var target = cities.FirstOrDefault(c =>
                string.Equals(c.Country, country, StringComparison.OrdinalIgnoreCase)
                && c.NameNormalized == canonicalNormalized);
            if (target is null) continue;

            added.Add(new GeoPlace
            {
                Country = target.Country,
                PostalCode = null,
                Name = Truncate(aliasName, 200),
                NameNormalized = Truncate(GeoTextNormalizer.Normalize(aliasName), 200),
                NameTranscribed = Truncate(GeoTextNormalizer.NormalizeTranscribed(aliasName, country), 200)!,
                Lat = target.Lat,
                Lon = target.Lon,
                Kind = GeoPlaceKind.City,
                Population = target.Population,
            });
        }

        cities.AddRange(added);
        return cities;
    }

    /// <summary>
    /// Weltweite Ortsliste. Deckt die Foederationen ab, fuer die kein Postleitzahl-Datensatz
    /// importiert ist - dort bleibt die Ortsnamen-Suche der einzige Weg. Grundlage ist
    /// <c>cities15000</c> (~25k Zeilen); fuer die Laender aus <see cref="DenseCityCountries"/>
    /// wird stattdessen die feinere <c>cities1000</c> genommen.
    ///
    /// <para>Scheitert der zweite Abruf, wird der Import NICHT abgebrochen: die grobe Liste ist
    /// besser als keine, und der Aufruf ist wiederholbar.</para>
    /// </summary>
    public async Task<GazetteerImportResult> ImportCitiesAsync(CancellationToken ct = default)
    {
        List<string> lines;
        try
        {
            lines = await DownloadZipEntryLinesAsync($"{_baseUrl}dump/cities15000.zip", "cities15000.txt", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or InvalidOperationException)
        {
            _log.LogWarning(ex, "Gazetteer: Staedte-Download fehlgeschlagen");
            return new GazetteerImportResult("cities15000", 0, 0, ex.Message);
        }

        var places = ParseCityLines(lines, out var skipped);

        try
        {
            var denseLines = await DownloadZipEntryLinesAsync(
                $"{_baseUrl}dump/cities1000.zip", "cities1000.txt", ct);
            var dense = ParseCityLines(denseLines, out var denseSkipped)
                .Where(p => DenseCityCountries.Contains(p.Country)).ToList();
            var before = places.Count;
            places = MergeDenseCities(places, dense);
            _log.LogInformation(
                "Gazetteer: {Dense} feine Orte fuer {Countries} Laender ohne PLZ-Datensatz " +
                "({Skipped} uebersprungen); Gesamtmenge {Before} -> {After}",
                dense.Count, DenseCityCountries.Count, denseSkipped, before, places.Count);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or InvalidOperationException)
        {
            // Kein Abbruch: die grobe Liste steht schon, und ein erneuter Aufruf holt den Rest nach.
            _log.LogWarning(ex, "Gazetteer: cities1000 nicht abrufbar, bleibe bei cities15000");
        }

        var beforeAliases = places.Count;
        places = AddAliasRows(places);
        await ReplaceAsync(places, g => g.Kind == GeoPlaceKind.City, ct);
        _log.LogInformation(
            "Gazetteer: {Count} Staedte importiert ({Skipped} uebersprungen, {Aliases} Namensvarianten)",
            places.Count, skipped, places.Count - beforeAliases);
        return new GazetteerImportResult("cities15000", places.Count, skipped);
    }

    /// <summary>
    /// Setzt die feine Ortsliste an die Stelle der groben — aber nur fuer die Laender, die in ihr
    /// vorkommen. <c>cities1000</c> ist eine Obermenge von <c>cities15000</c>; wuerden beide
    /// zusammengeworfen, staende jeder groessere Ort DOPPELT im Lexikon und die
    /// Mehrdeutigkeitsregel saehe zwei Kandidaten, wo einer gemeint ist.
    /// </summary>
    internal static List<GeoPlace> MergeDenseCities(List<GeoPlace> coarse, List<GeoPlace> dense)
    {
        if (dense.Count == 0) return coarse;

        var replaced = dense.Select(p => p.Country).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = coarse.Where(p => !replaced.Contains(p.Country)).ToList();
        merged.AddRange(dense);
        return merged;
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Zeilen des PLZ-Exports (Tab-getrennt: country, postal, place, admin1name, admin1code,
    /// admin2name, admin2code, admin3name, admin3code, lat, lon, accuracy) in GeoPlace-Zeilen -
    /// plus je Bundesland ein Zentroid aus dem Mittel seiner Postleitzahlen. Die State-Spalte von
    /// chess-results traegt genau diese admin1-Namen ("Salzburg", "Niederoesterreich"), damit
    /// bleibt als letzter Fallback wenigstens die richtige Region.
    /// </summary>
    internal static List<GeoPlace> ParsePostalLines(string iso2, IEnumerable<string> lines, out int skipped)
    {
        var places = new List<GeoPlace>();
        var regionPoints = new Dictionary<string, (double LatSum, double LonSum, int Count)>(StringComparer.Ordinal);
        skipped = 0;

        foreach (var line in lines)
        {
            var f = line.Split('\t');
            if (f.Length < 11) { skipped++; continue; }
            if (!TryParseCoordinate(f[9], out var lat) || !TryParseCoordinate(f[10], out var lon)) { skipped++; continue; }

            var placeName = f[2].Trim();
            var postal = f[1].Trim();
            if (placeName.Length == 0 || postal.Length == 0) { skipped++; continue; }

            places.Add(new GeoPlace
            {
                Country = iso2,
                PostalCode = Truncate(postal, 20),
                Name = Truncate(placeName, 200),
                NameNormalized = Truncate(GeoTextNormalizer.Normalize(placeName), 200),
                NameTranscribed = Truncate(GeoTextNormalizer.NormalizeTranscribed(placeName, iso2), 200)!,
                Lat = lat,
                Lon = lon,
                Kind = GeoPlaceKind.PostalCode,
            });

            var admin1 = f[3].Trim();
            if (admin1.Length > 0)
            {
                var current = regionPoints.GetValueOrDefault(admin1);
                regionPoints[admin1] = (current.LatSum + lat, current.LonSum + lon, current.Count + 1);
            }
        }

        foreach (var (name, agg) in regionPoints)
        {
            places.Add(new GeoPlace
            {
                Country = iso2,
                PostalCode = null,
                Name = Truncate(name, 200),
                NameNormalized = Truncate(GeoTextNormalizer.Normalize(name), 200),
                NameTranscribed = Truncate(GeoTextNormalizer.NormalizeTranscribed(name, iso2), 200)!,
                Lat = agg.LatSum / agg.Count,
                Lon = agg.LonSum / agg.Count,
                Kind = GeoPlaceKind.Region,
            });
        }

        return places;
    }

    /// <summary>
    /// Zeilen des Ortsexports (geonameid, name, asciiname, alternatenames, lat, lon, fclass,
    /// fcode, country, cc2, admin1..4, population, ...) in GeoPlace-Zeilen.
    /// </summary>
    internal static List<GeoPlace> ParseCityLines(IEnumerable<string> lines, out int skipped)
    {
        var places = new List<GeoPlace>();
        skipped = 0;

        foreach (var line in lines)
        {
            var f = line.Split('\t');
            if (f.Length < 15) { skipped++; continue; }
            if (!TryParseCoordinate(f[4], out var lat) || !TryParseCoordinate(f[5], out var lon)) { skipped++; continue; }

            var name = f[1].Trim();
            var country = f[8].Trim().ToUpperInvariant();
            if (name.Length == 0 || country.Length != 2) { skipped++; continue; }

            int.TryParse(f[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out var population);

            places.Add(new GeoPlace
            {
                Country = country,
                PostalCode = null,
                Name = Truncate(name, 200),
                NameNormalized = Truncate(GeoTextNormalizer.Normalize(name), 200),
                NameTranscribed = Truncate(GeoTextNormalizer.NormalizeTranscribed(name, country), 200)!,
                Lat = lat,
                Lon = lon,
                Kind = GeoPlaceKind.City,
                Population = population,
            });
        }

        return places;
    }

    private async Task<List<string>> DownloadZipEntryLinesAsync(string url, string entryName, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"Eintrag {entryName} fehlt im Archiv {url}.");
        if (entry.Length > MaxUncompressedBytes)
            throw new InvalidDataException($"Eintrag {entryName} ist mit {entry.Length} Bytes zu gross.");

        var lines = new List<string>();
        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        long read = 0;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            read += line.Length;
            if (read > MaxUncompressedBytes)
                throw new InvalidDataException($"Eintrag {entryName} ueberschreitet die Groessengrenze.");
            if (line.Length > 0 && !line.StartsWith('#')) lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// Ersetzt die vom Filter erfassten Zeilen durch die neuen. In Baetchen, mit geleertem
    /// Change-Tracker: 25k getrackte Entities machen jeden weiteren SaveChanges quadratisch teuer.
    ///
    /// <para><b>Loeschen und Schreiben liegen in EINER Transaktion.</b> Ohne sie ist das Loeschen
    /// sofort endgueltig, und ein Abbruch zwischen zwei Baetchen (das <c>ct</c> ist
    /// <c>HttpContext.RequestAborted</c> — ein Reverse-Proxy mit 60 s Zeitlimit reicht dafuer)
    /// laesst das Ortslexikon halb geloescht zurueck. Auffallen wuerde das niemandem: der Request
    /// ist ja schon weg, und die Verortung liefert danach einfach stiller weniger Treffer.</para>
    ///
    /// <para>Der InMemory-Provider kennt weder <c>ExecuteDelete</c> noch Transaktionen — deshalb
    /// die Weiche ueber <c>IsRelational()</c>; die Tests liegen auf ParsePostalLines/ParseCityLines,
    /// dieser Pfad wird auf dem Dev-Stack von Hand abgenommen.</para>
    /// </summary>
    private async Task ReplaceAsync(
        List<GeoPlace> places, System.Linq.Expressions.Expression<Func<GeoPlace, bool>> scope, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            _db.GeoPlaces.RemoveRange(_db.GeoPlaces.Where(scope));
            _db.GeoPlaces.AddRange(places);
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Die Transaktion MUSS in der Execution-Strategy laufen: `EnableRetryOnFailure`
        // (Program.cs) schaltet `MySqlRetryingExecutionStrategy` ein, und die verweigert eine
        // selbst geoeffnete Transaktion — bei einem Wiederholversuch waere sonst unklar, ob nur
        // die einzelne Anweisung oder der ganze Block erneut laufen soll. Ohne die Umklammerung
        // scheitert JEDER Import mit „does not support user-initiated transactions": am
        // 2026-09-09 kamen so alle sieben Laender des PLZ-Imports mit 500 zurueck, und die
        // Verortung blieb auf dem Ortsnamen sitzen. Dasselbe Muster wie in
        // <c>AdminService.ClearPuzzlesAsync</c>.
        //
        // Der Block ist wiederholbar: er loescht den ganzen Bereich und fuellt ihn aus
        // <paramref name="places"/> neu — er haengt also nicht davon ab, wie weit ein
        // abgebrochener Versuch gekommen war.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // ExecuteDelete statt RemoveRange: 25k getrackte Entities zu laden, nur um sie zu
            // loeschen, kostet mehr als der Import selbst.
            await _db.GeoPlaces.Where(scope).ExecuteDeleteAsync(ct);

            for (var offset = 0; offset < places.Count; offset += BatchSize)
            {
                _db.GeoPlaces.AddRange(places.Skip(offset).Take(BatchSize));
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
            }

            await tx.CommitAsync(ct);
        });
    }

    private static bool TryParseCoordinate(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
