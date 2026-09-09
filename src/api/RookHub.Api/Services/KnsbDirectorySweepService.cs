using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Der Terminkalender des niederlaendischen Verbands (KNSB, schaakbond.nl) als Quelle.
///
/// <para><b>Ein grosser Zusatz.</b> Am 2026-09-09 gemessen: <b>177 kuenftige Eintraege</b> gegen
/// 12 auf chess-results fuer NED im selben Zeitraum — der Zusatz sind ueberwiegend Klubturniere,
/// die chess-results nie sieht.</para>
///
/// <para><b>Was diese Liste NICHT liefert, und zwar strukturell:</b> Enddatum, Ort, Anschrift,
/// Postleitzahl, Koordinaten, Rundenzahl und Teilnehmerzahl. Diese Angaben stehen (Enddatum/Ort/
/// Anschrift) allenfalls auf der Detailseite (ein Abruf je Turnier, bei 15 s Wartezeit ~45 Minuten
/// fuer alle 177) — bewusst nicht Teil dieser Quelle, siehe <see cref="KnsbCalendarService"/> im
/// Crawler. Jeder eigene Eintrag bekommt deshalb <c>EndDate = StartDate</c> (bei den rund 27 %
/// Turnieren, die die Quelle selbst als "Meerdaags" fuehrt, nachweislich ungenau) und BLEIBT ohne
/// Ort — <see cref="GeocodingService"/> wird hier absichtlich gar nicht injiziert, ein Aufruf waere
/// toter Code.</para>
///
/// <para><b>Der eine Lichtblick: die Bedenkzeit-Klasse steht STRUKTURIERT in der Liste</b> (die
/// "speed"-Taxonomie, bei jedem gemessenen Eintrag genau ein Wert) — anders als bei den meisten
/// Quellen des Projekts muss sie hier nicht aus Freitext geraten werden
/// (<see cref="TournamentSpeedClassifier"/> kommt gar nicht zum Einsatz). Der Crawler liefert sie
/// bereits als Klartext ("Normaalschaak"/"Rapidschaak"/"Snelschaak"), <see cref="SpeedOf"/> bildet
/// nur noch auf den Enum-Wert ab.</para>
///
/// <para><b>Die Kennung ist ein gehashter SLUG, nicht die numerische Post-Id.</b> Die Quelle traegt
/// keine eigene Turniernummer nach aussen; der WordPress-Slug ("zomeravondcompetitie-2027-07-19")
/// ist deterministisch aus Titel und Termin gebildet und damit die verlaesslichere Kennung — bei
/// der Messung standen <c>date</c>/<c>modified</c> ALLER 177 Eintraege auf demselben Tag, ein
/// Hinweis auf einen taeglichen Voll-Reimport, bei dem unklar bleibt, ob die numerische Id
/// erhalten bleibt. Der Slug wird bis zu 81 Zeichen lang (5 von 177 ueber den 60 Zeichen von
/// <see cref="TournamentDirectorySource.ExternalId"/>) — gekuerzt wird deshalb NICHT direkt
/// (zwei Turniere zu einem zu machen ist der teuerste Fehler hier), sondern ueber denselben
/// KURZWERT wie bei chess.cz (<see cref="ChessCzDirectorySweepService.PublicIdOf"/>): ein
/// SHA-256-Praefix, der sowohl <see cref="PublicIdOf"/> ("nl" + Hash, 14 Zeichen) als auch die
/// <see cref="ExternalDirectorySource.NoteSource"/>-Kennung traegt.</para>
///
/// <para><b>Kein "Meeting"-Aequivalent noetig</b> — die Filterkategorie der Quelle
/// ("Schaakkalender") liefert schon reine Turnier-Ankuendigungen, siehe Crawler-Dienst. Und die
/// Kategorien "Jeugd"/"Senior" werden bewusst NICHT als Alters-/Publikumsmerkmal ausgewertet: sie
/// stehen haeufig GLEICHZEITIG an einem offenen Turnier (43 von 118 "Jeugd"-Eintraegen tragen auch
/// "Senior") und bedeuten "fuer diese Mitgliedschaft zugelassen", nicht "Jugendturnier". Alter,
/// Geschlecht und Liga-Merkmal kommen deshalb wie bei jeder anderen Quelle allein aus dem Namen
/// (<see cref="ExternalDirectorySource.ApplyClassification"/>).</para>
/// </summary>
public class KnsbDirectorySweepService
{
    private const int SaveEvery = 25;

    /// <summary>Es ist der niederlaendische Verbandskalender — jedes Turnier gehoert zu NED.</summary>
    private const string Federation = "NED";

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KnsbDirectorySweepService> _log;

    public KnsbDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        ILogger<KnsbDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    public async Task<ExternalSweepResult> RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await FetchAsync(today, ct);
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var now = DateTime.UtcNow;
        int added = 0, speedResolved = 0, matched = 0, retired = 0, processed = 0;
        // Was DIESER Lauf geliefert hat — Grundlage der Verschwunden-Erkennung unten.
        var delivered = new List<string>();

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Slug.Length == 0 || row.Name.Length == 0) continue;

                processed++;
                var publicId = PublicIdOf(row.Slug);
                var externalId = ShortHash(row.Slug);
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);

                var match = await ExternalDirectorySource.FindMatchAsync(
                    _db, Federation, row.StartDate, row.Name, ct);

                if (match is not null)
                {
                    delivered.Add(externalId);
                    await ExternalDirectorySource.NoteSourceAsync(_db, 
                        match, DirectorySourceKind.DutchChessFederation, externalId, row.Url, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("KNSB: {PublicId} geht in {Target} auf ({Name})",
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

                own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
                own.Federation = Federation;
                own.StartDate = row.StartDate;
                // Kein Enddatum verfuegbar (siehe Klassen-Kommentar) - Start=Ende ist der Fallback
                // fuer eine unbekannte Dauer, wie ueberall im Projekt.
                own.EndDate = row.StartDate;
                own.StartsOnWeekend = row.StartDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                // Bewusst KEIN own.LocationText = null hier: diese Quelle kennt nie einen Ort, aber
                // ein bereits gesetzter (von Hand, oder von chess-results uebernommen, bevor dieser
                // Eintrag zurueckgezogen wurde) soll nicht geloescht werden.
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);

                var speed = SpeedOf(row.Speed);
                if (speed != TournamentSpeed.Unknown)
                {
                    if (own.Speed == TournamentSpeed.Unknown) speedResolved++;
                    own.Speed = speed;
                }

                delivered.Add(externalId);
                await ExternalDirectorySource.NoteSourceAsync(_db, 
                    own, DirectorySourceKind.DutchChessFederation, externalId, row.Url, now, ct);

                if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "KNSB: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "KNSB-Kalender: {Read} gelesen, {Added} neu, {SpeedResolved} mit neu bekannter Bedenkzeit, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, speedResolved, matched, retired);
        // Was die Quelle nicht mehr liefert, wird zurueckgezogen (zwei Laeufe Karenz,
        // Bremse gegen halbe Laeufe — siehe RetireVanishedAsync).
        retired += await ExternalDirectorySource.RetireVanishedAsync(
            _db, DirectorySourceKind.DutchChessFederation, delivered, now, ct);

        return new ExternalSweepResult(events.Count, added, speedResolved, matched, retired);
    }

    /// <summary>Die "speed"-Taxonomie kommt vom Crawler schon als Klartext - hier nur noch die Abbildung auf den Enum-Wert.</summary>
    internal static TournamentSpeed SpeedOf(string? speed) => speed switch
    {
        "Normaalschaak" => TournamentSpeed.Standard,
        "Rapidschaak" => TournamentSpeed.Rapid,
        "Snelschaak" => TournamentSpeed.Blitz,
        _ => TournamentSpeed.Unknown,
    };

    /// <summary>
    /// Aus dem bis zu 81 Zeichen langen Slug einen Schluessel machen, der in die 24 Zeichen von
    /// <see cref="TournamentDirectoryEntry.PublicId"/> passt. Gekuerzt wird NICHT: zwei Turniere zu
    /// einem zu machen ist der teuerste Fehler, den diese Stelle machen kann - siehe
    /// <see cref="ChessCzDirectorySweepService.PublicIdOf"/> fuer dasselbe Muster bei chess.cz.
    /// </summary>
    internal static string PublicIdOf(string slug) => "nl" + ShortHash(slug);

    private static string ShortHash(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexStringLower(digest)[..12];
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerKnsbEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/knsb-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<KnsbRow>>(body, JsonOptions) ?? [];
        var result = new List<CrawlerKnsbEvent>();
        foreach (var r in rows)
        {
            if (r.Slug is not { Length: > 0 } || r.Name is not { Length: > 0 }) continue;
            if (ParseDate(r.StartDate) is not { } start) continue;
            result.Add(new CrawlerKnsbEvent(r.Slug, r.Name, start, r.Url, r.Speed, r.Online));
        }
        return result;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record KnsbRow(
        string? Slug, string? Name, string? StartDate, string? Url, string? Speed, bool Online);

    public sealed record CrawlerKnsbEvent(
        string Slug, string Name, DateOnly StartDate, string? Url, string? Speed, bool Online);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var d) ? d : null;
}
