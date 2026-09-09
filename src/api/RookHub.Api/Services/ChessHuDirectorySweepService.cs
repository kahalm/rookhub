using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Der Kalender des ungarischen Verbands (chess.hu) als Quelle.
///
/// <para><b>Was diese Quelle bringt, ist VORLAUF.</b> Ab November 2026 fuehrt chess.hu 41
/// Turniere, wo chess-results 5 kennt. Im Rueckblick auf einen abgeschlossenen Zeitraum landen
/// 80 % irgendwann doch dort — die uebrigen 20 % nie. Der Nutzen liegt also weniger im
/// dauerhaften Ueberschuss als darin, dass ein ungarisches Turnier Monate frueher sichtbar wird;
/// holt die Turniersuche es ein, geht der eigene Eintrag in ihrem auf.</para>
///
/// <para><b>Die Quelle ist arm an Feldern, und das ist hier kein Mangel, sondern das Ergebnis
/// einer Messung.</b> Neun Felder: Nummer, Name, Termin, Ort, ein FIDE- und ein Jugend-Merkmal.
/// Die Detailseite traegt zusaetzlich nur das Komitat — und das ist gemessen wertlos: von 52
/// verschiedenen Ortsnamen des Kalenders stehen 46 im Lexikon, davon drei mehrdeutig, und die drei
/// sind es innerhalb DERSELBEN Stadt (mehrere Postleitzahlen). Ein Abruf je Turnier fuer nichts —
/// deshalb bleibt es bei dem einen Abruf, anders als bei chess.sk.</para>
///
/// <para><b>Das Jugend-Merkmal der Quelle wird bewusst nicht uebernommen</b> (Begruendung im
/// Crawler: es steht bei 77 von 107 Turnieren auf „ja", darunter ein Gaensefest). Die Einordnung
/// kommt aus dem Namen wie ueberall.</para>
/// </summary>
public class ChessHuDirectorySweepService
{
    private const int SaveEvery = 50;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<ChessHuDirectorySweepService> _log;

    public ChessHuDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<ChessHuDirectorySweepService> log)
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
                // Geliefert ist geliefert: die Quelle FUEHRT diese Zeile. Ob WIR sie lesen
                // koennen, ist eine andere Frage. Stand das Eintragen erst hinter den Pruefungen,
                // galt eine Zeile mit unlesbarem Termin als verschwunden und war nach zwei Laeufen
                // abgesagt — und ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle.
                delivered.Add(row.EventId);
                if (row.Start is not { } start || row.Name.Length == 0) continue;

                processed++;
                var publicId = $"hu{row.EventId}";
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);
                var match = await ExternalDirectorySource.FindMatchAsync(_db, "HUN", start, row.Name,
                    new ExternalDirectorySource.MatchHint(
                        DirectorySourceKind.HungarianChessFederation, row.EventId, row.Place), ct);

                if (match is not null)
                {
                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.HungarianChessFederation, row.EventId, null, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("chess.hu: {PublicId} geht in {Target} auf ({Name})",
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
                        Federation = "HUN",
                        FirstSeenAt = now,
                    };
                    _db.TournamentDirectoryEntries.Add(own);
                    added++;
                }

                var location = row.HasVenue ? row.Place?.Trim() : null;
                var locationChanged = own.LocationText != location;

                own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
                own.StartDate = start;
                own.EndDate = row.End ?? start;
                own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.LocationText = ExternalDirectorySource.Truncate(location, 300);
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);
                await ExternalDirectorySource.NoteSourceAsync(_db, own,
                    DirectorySourceKind.HungarianChessFederation, row.EventId, null, now, ct);

                // Ohne Spielort keine Koordinaten: „Online" und „Helyszín később" stehen im
                // Orts-Feld, sind aber keine Orte.
                if (location is { Length: > 0 } && (locationChanged || own.Lat is null))
                {
                    var hit = await _geocoding.ResolveAsync(location, null, "HUN", ct);
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
            catch (Exception ex) { _log.LogWarning(ex, "chess.hu: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "chess.hu-Kalender: {Read} gelesen, {Added} neu, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, matched, retired);
        // Was die Quelle nicht mehr liefert, wird zurueckgezogen (zwei Laeufe Karenz,
        // Bremse gegen halbe Laeufe — siehe RetireVanishedAsync).
        retired += await ExternalDirectorySource.RetireVanishedAsync(
            _db, DirectorySourceKind.HungarianChessFederation, delivered, now, ct);

        return new ExternalSweepResult(events.Count, added, 0, matched, retired);
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerChessHuEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/chess-hu-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<ChessHuRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerChessHuEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate),
                r.Place, r.HasVenue))
            // KEIN Filter auf den Termin. Eine Zeile ohne lesbaren Termin bleibt in der Liste,
            // weil die Verschwunden-Erkennung sie sonst nicht als GELIEFERT sieht — die Quelle
            // fuehrt sie ja. Ausgesiebt wird sie erst in der Schleife, dort steht sie dann schon
            // in `delivered`. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle:
            // ohne das waeren es reihenweise falsche Absagen nach zwei Naechten.
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ChessHuRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Place,
        bool HasVenue, bool FideRated);

    public sealed record CrawlerChessHuEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Place, bool HasVenue);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
