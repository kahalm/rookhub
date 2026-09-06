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
    /// Weltweite Ortsliste (cities15000, ~25k Zeilen). Deckt die Foederationen ab, fuer die kein
    /// Postleitzahl-Datensatz importiert ist - dort bleibt die Ortsnamen-Suche der einzige Weg.
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

        await ReplaceAsync(places, g => g.Kind == GeoPlaceKind.City, ct);
        _log.LogInformation("Gazetteer: {Count} Staedte importiert ({Skipped} uebersprungen)", places.Count, skipped);
        return new GazetteerImportResult("cities15000", places.Count, skipped);
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
    }

    private static bool TryParseCoordinate(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
