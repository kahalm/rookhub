using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Der Ankuendigungskalender der Chess Federation of Canada (chess.ca) als Quelle.
///
/// <para><b>Kanada ist ein Land, in dem chess-results kaum als Kalender benutzt wird.</b> Der
/// Bestand fuehrt fuer CAN 146 Eintraege, davon 68 kuenftige — der Verbandskalender fuehrt 171
/// kuenftige (gemessen 2026-09-10). Rund hundert Turniere mehr fuer EINEN Abruf; das ist derselbe
/// Fall wie Norwegen, nur eine Stufe milder (dort kannte chess-results NULL kuenftige).</para>
///
/// <para><b>Die Foederation ist FEST.</b> Diese Seite ist ausschliesslich der kanadische Kalender,
/// jeder gelesene Eintrag bekommt <c>"CAN"</c>. Auslands- und Online-Termine hat der Crawler schon
/// aussortiert — und zwar ueber die PROVINZ, nicht ueber den Typ: „FIDE: World CC for People with
/// Disabilities" in Usbekistan traegt dort <c>type=OTB</c> und nur <c>prov=FO</c>.</para>
///
/// <para><b>Der Spielort ist eine Stadt, keine Anschrift.</b> Die Quelle fuehrt weder Strasse noch
/// Postleitzahl — der PLZ-zuerst-Weg des <see cref="GeocodingService"/> greift hier also NIE, es
/// bleibt die Ortsnamen-Aufloesung. Dafuer kommt das Provinz-Kuerzel strukturiert mit und wandert
/// in <see cref="TournamentDirectoryEntry.State"/>; es ist der Rueckfall auf die Regionsmitte, wenn
/// der Ortsname nicht traegt. Kanada steht im Bestand bei 82 % Verortung, der Ortsname allein
/// traegt dort also gut. Ein Ortstext wie „Thornhill / Toronto" nennt ZWEI Orte — die Zerlegung
/// des Geocoders trennt an Schraegstrichen und nimmt den ersten.</para>
///
/// <para><b>Diese Quelle hat KEINE stabile Kennung.</b> <c>oid</c> ist nur die Listenposition;
/// der Crawler bildet die <c>EventId</c> deshalb aus Termin, Ort UND Namen (siehe
/// <c>CfcCalendarService.EventKeyOf</c> — Termin und Ort allein haben 13 Kollisionen, zwei
/// Turniere am selben Tag in derselben Stadt). Hier wird daraus nur noch der Kurzwert, wie bei den
/// uebrigen Quellen ohne eigene Nummer (<see cref="WcuDirectorySweepService.PublicIdOf"/>,
/// <see cref="SchachbundDirectorySweepService.PublicIdOf"/>). Der Kurzwert ist auch die KENNUNG im
/// Herkunftsvermerk: der rohe Schluessel ist laenger als die 60 Zeichen von
/// <see cref="TournamentDirectorySource.ExternalId"/>, und genau daran scheiterten Wales und
/// Deutschland monatelang jede Nacht.</para>
///
/// <para><b>Was diese Quelle nicht liefert</b>: Bedenkzeit, Rundenzahl, Teilnehmerzahl. Die
/// Turnier<b>art</b> bleibt wie bei allen Zusatzquellen <c>Unknown</c>.</para>
/// </summary>
public class CfcDirectorySweepService
{
    /// <summary>Die einzige Foederation, die diese Quelle je liefert.</summary>
    internal const string Federation = "CAN";

    private const int SaveEvery = 25;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<CfcDirectorySweepService> _log;

    public CfcDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<CfcDirectorySweepService> log)
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
        int added = 0, matched = 0, retired = 0, processed = 0;
        // Was DIESER Lauf geliefert hat — Grundlage der Verschwunden-Erkennung unten.
        var delivered = new List<string>();

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.EventId.Length == 0) continue;
                // Geliefert ist geliefert: die Quelle FUEHRT diese Zeile. Ob wir sie lesen
                // koennen, ist eine andere Frage — stand das Eintragen erst hinter den Pruefungen,
                // galt eine Zeile mit unlesbarem Termin als verschwunden und war nach zwei Laeufen
                // abgesagt. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle.
                delivered.Add(PublicIdOf(row.EventId));
                if (row.Start is not { } start || row.Name.Length == 0) continue;

                processed++;
                var publicId = PublicIdOf(row.EventId);
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);
                var match = await ExternalDirectorySource.FindMatchAsync(_db, Federation, start, row.Name,
                    new ExternalDirectorySource.MatchHint(
                        DirectorySourceKind.CanadianChessFederation, publicId, row.Place), ct);

                if (match is not null)
                {
                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.CanadianChessFederation, publicId, row.Url, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("cfc: {PublicId} geht in {Target} auf ({Name})",
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

                var location = HasVenue(row.Place) ? row.Place!.Trim() : null;
                var locationChanged = own.LocationText != location;

                own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
                own.Federation = Federation;
                own.StartDate = start;
                own.EndDate = row.End ?? start;
                own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.LocationText = ExternalDirectorySource.Truncate(location, 300);
                own.State = ExternalDirectorySource.Truncate(row.Province, 100);
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);
                await ExternalDirectorySource.NoteSourceAsync(_db, own,
                    DirectorySourceKind.CanadianChessFederation, publicId, row.Url, now, ct);

                if (location is { Length: > 0 } && (locationChanged || own.Lat is null))
                {
                    var hit = await _geocoding.ResolveAsync(location, own.State, Federation, ct);
                    if (hit is not null && hit.Source != GeoSource.Ambiguous)
                    {
                        own.Lat = hit.Lat;
                        own.Lon = hit.Lon;
                        own.GeoSource = hit.Source;
                        own.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
                    }
                }

                if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "cfc: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "CFC-Kalender: {Read} gelesen, {Added} neu, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, matched, retired);
        // Was die Quelle nicht mehr liefert, wird zurueckgezogen (zwei Laeufe Karenz,
        // Bremse gegen halbe Laeufe — siehe RetireVanishedAsync).
        retired += await ExternalDirectorySource.RetireVanishedAsync(
            _db, DirectorySourceKind.CanadianChessFederation, delivered, now, _log, ct);

        return new ExternalSweepResult(events.Count, added, 0, matched, retired);
    }

    /// <summary>
    /// Ob der Ortstext ein SPIELORT ist. Kanadische Veranstalter tragen bei noch offener
    /// Ausschreibung Platzhalter ein; die bekommen keinen Pin. „Online"-Orte („Zoom",
    /// „lichess.org") kommen hier nicht mehr an, die siebt schon der Crawler ueber die Provinz aus.
    /// </summary>
    internal static bool HasVenue(string? place)
    {
        if (place is not { Length: > 0 }) return false;

        var value = place.Trim();
        return !value.Equals("tbc", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("tba", StringComparison.OrdinalIgnoreCase)
               && !value.Contains("to be announced", StringComparison.OrdinalIgnoreCase)
               && !value.Contains("to follow", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Der Kurzwert des Quellen-Schluessels. Die <c>EventId</c> des Crawlers ist eine Zeile aus
    /// Termin, Ort und Namen und damit deutlich laenger als die 24 Zeichen von
    /// <see cref="TournamentDirectoryEntry.PublicId"/> und die 60 von
    /// <see cref="TournamentDirectorySource.ExternalId"/>.
    /// </summary>
    internal static string PublicIdOf(string key)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim().ToLowerInvariant()));
        return "ca" + Convert.ToHexStringLower(digest)[..12];
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerCfcEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/cfc-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<CfcRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerCfcEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate),
                r.Place, r.Province, r.Url))
            // KEIN Filter auf den Termin — siehe die Begruendung in der Schleife oben.
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record CfcRow(
        string? EventId, string? Name, string? StartDate, string? EndDate,
        string? Place, string? Province, string? Url);

    public sealed record CrawlerCfcEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End,
        string? Place, string? Province, string? Url);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
