using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Der Kalender des englischen Verbands (ECF) als Quelle.
///
/// <para><b>Eine der groessten Luecken im Bestand.</b> Am 2026-09-08 gemessen: 256 kuenftige
/// Turniere bis Juli 2027, davon <b>222 (86 %) nicht auf chess-results</b> — dort stehen im
/// selben Zeitraum 143. Der Vergleich ist hier besonders belastbar, weil beide Seiten wirklich
/// abgefragt wurden.</para>
///
/// <para><b>Die einzige Quelle, die KOORDINATEN mitbringt.</b> Fuer 168 der 256 Turniere kommen
/// sie fertig aus dem Spielstaetten-Endpunkt; dort wird nichts aufgeloest und nichts geraten, und
/// der Eintrag traegt <see cref="GeoSource.SourceProvided"/>. Die uebrigen gehen den normalen Weg
/// ueber den Ortstext — deren Spielstaette hat keine hinterlegten Koordinaten, wohl aber die
/// Anschrift im Namen.</para>
///
/// <para><b>Die Schlagworte der Quelle sind gepflegt, und drei davon werden ausgewertet:</b>
/// „Meeting" ist eine Sitzung und kein Turnier (4 von 256), „Online" hat keinen Spielort (28) und
/// „Juniors Only" ist eine verlaessliche Jugend-Angabe (50). Solche Felder gibt es bei keiner
/// anderen Quelle — dort muss alles aus dem Namen erschlossen werden.</para>
///
/// <para><b>Was sie NICHT liefert:</b> Bedenkzeit, Rundenzahl, System und Teilnehmerzahl. Die
/// stehen nur im Fliesstext der Ausschreibung, und der ist Werbetext.</para>
/// </summary>
public class EcfDirectorySweepService
{
    private const int SaveEvery = 25;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<EcfDirectorySweepService> _log;

    public EcfDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<EcfDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _log = log;
    }

    public async Task<ExternalSweepResult> RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await FetchAsync(today, ct);
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var now = DateTime.UtcNow;
        int added = 0, located = 0, matched = 0, retired = 0, processed = 0;
        // Was DIESER Lauf geliefert hat — Grundlage der Verschwunden-Erkennung unten.
        var delivered = new List<string>();

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Start is not { } start || row.Name.Length == 0 || row.EventId.Length == 0)
                    continue;

                processed++;
                var publicId = $"en{row.EventId}";
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);

                // „Meeting" ist eine Sitzung des Verbands, kein Turnier.
                if (row.IsMeeting)
                {
                    if (own is not null && own.RemovedAt is null)
                    {
                        own.RemovedAt = now;
                        retired++;
                    }
                    continue;
                }

                var federation = FederationOf(row.Country);
                var match = await ExternalDirectorySource.FindMatchAsync(_db, federation, start, row.Name, ct);

                if (match is not null)
                {
                    delivered.Add(row.EventId);
                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.EnglishChessFederation, row.EventId, row.Url, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("ECF: {PublicId} geht in {Target} auf ({Name})",
                            own!.PublicId, match.PublicId, match.Name);
                    }
                    if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
                    continue;
                }

                if (own is null)
                {
                    own = new TournamentDirectoryEntry
                    {
                        PublicId = publicId,
                        ChessResultsId = null,
                        FirstSeenAt = now,
                    };
                    _db.TournamentDirectoryEntries.Add(own);
                    added++;
                }

                var location = row.Online ? null : row.Place?.Trim();
                var locationChanged = own.LocationText != location;

                own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
                own.Federation = federation;
                own.StartDate = start;
                own.EndDate = row.End ?? start;
                own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.LocationText = ExternalDirectorySource.Truncate(location, 300);
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);
                ApplyYouthMark(own, row.YouthOnly);
                delivered.Add(row.EventId);
                await ExternalDirectorySource.NoteSourceAsync(_db, own,
                    DirectorySourceKind.EnglishChessFederation, row.EventId, row.Url, now, ct);

                if (await ApplyCoordinatesAsync(own, row, locationChanged, ct)) located++;

                if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "ECF: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "ECF-Kalender: {Read} gelesen, {Added} neu, {Located} verortet, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, located, matched, retired);
        // Was die Quelle nicht mehr liefert, wird zurueckgezogen (zwei Laeufe Karenz,
        // Bremse gegen halbe Laeufe — siehe RetireVanishedAsync).
        retired += await ExternalDirectorySource.RetireVanishedAsync(
            _db, DirectorySourceKind.EnglishChessFederation, delivered, now, ct);

        return new ExternalSweepResult(events.Count, added, located, matched, retired);
    }

    /// <summary>
    /// Die Koordinaten setzen — mit der Angabe der Quelle, wenn es eine gibt, sonst ueber das
    /// Ortslexikon.
    ///
    /// <para>Eine mitgelieferte Koordinate schlaegt JEDEN Lexikon-Treffer: sie meint die
    /// Spielstaette, ein Lexikon-Treffer bestenfalls die Stadtmitte. Sie wird deshalb auch dann
    /// gesetzt, wenn schon ein Pin steht — es sei denn, jemand hat ihn von Hand gesetzt.</para>
    /// </summary>
    private async Task<bool> ApplyCoordinatesAsync(TournamentDirectoryEntry entry,
        CrawlerEcfEvent row, bool locationChanged, CancellationToken ct)
    {
        // Ein Online-Turnier hat keinen Spielort und bekommt keinen Pin.
        if (row.Online) return false;
        if (entry.GeoSource == GeoSource.Manual) return false;

        if (row.Lat is { } lat && row.Lon is { } lon)
        {
            entry.Lat = lat;
            entry.Lon = lon;
            entry.GeoSource = GeoSource.SourceProvided;
            entry.GeoPlaceName = ExternalDirectorySource.Truncate(row.City ?? row.Place, 200);
            return true;
        }

        if (entry.LocationText is not { Length: > 0 }) return false;
        if (!locationChanged && entry.Lat is not null) return false;

        var hit = await _geocoding.ResolveAsync(entry.LocationText, null, entry.Federation, ct);
        if (hit is null || hit.Source == GeoSource.Ambiguous) return false;

        entry.Lat = hit.Lat;
        entry.Lon = hit.Lon;
        entry.GeoSource = hit.Source;
        entry.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
        return true;
    }

    /// <summary>
    /// Das Schlagwort „Juniors Only" ist eine verlaessliche Angabe der Quelle — aber nur, wo der
    /// Name nichts Genaueres sagt. „U14 Championship" traegt seine Klasse selbst.
    /// </summary>
    private static void ApplyYouthMark(TournamentDirectoryEntry entry, bool youth)
    {
        if (youth && entry.AgeGroups == TournamentAgeGroups.None)
            entry.AgeGroups = TournamentAgeGroups.YouthUnspecified;
    }

    /// <summary>
    /// Die Foederation aus dem Land der Spielstaette. Ohne Angabe gilt England — es ist der
    /// englische Kalender. Nennt die Quelle ein ANDERES Land, bekommt der Eintrag KEINE
    /// Foederation: „ENG" waere dann nachweislich falsch, und eine falsche Angabe ist schlechter
    /// als keine (am 2026-09-08 betraf das genau ein Turnier, ein GM-Turnier in Frankreich).
    /// </summary>
    internal static string? FederationOf(string? country)
    {
        var value = country?.Trim();
        if (value is not { Length: > 0 }) return "ENG";

        return UnitedKingdom.Contains(value) ? "ENG" : null;
    }

    private static readonly HashSet<string> UnitedKingdom = new(StringComparer.OrdinalIgnoreCase)
    {
        "United Kingdom", "England", "UK", "GB", "Great Britain",
    };

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerEcfEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/ecf-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<EcfRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerEcfEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Place, r.City,
                r.Country, r.Lat, r.Lon, r.Url, r.Categories ?? []))
            .Where(e => e.Start is not null)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record EcfRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Url,
        string? Place, string? City, string? PostalCode, string? Country, double? Lat, double? Lon,
        List<string>? Categories, string? Website);

    public sealed record CrawlerEcfEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Place, string? City,
        string? Country, double? Lat, double? Lon, string? Url, List<string> Categories)
    {
        /// <summary>Eine Sitzung des Verbands, kein Turnier.</summary>
        public bool IsMeeting => Has("Meeting");

        /// <summary>Kein Spielort — die Quelle fuehrt das als eigenes Schlagwort.</summary>
        public bool Online => Has("Online");

        /// <summary>Reines Jugendturnier laut Quelle.</summary>
        public bool YouthOnly => Has("Juniors Only");

        private bool Has(string category) =>
            Categories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
    }

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
