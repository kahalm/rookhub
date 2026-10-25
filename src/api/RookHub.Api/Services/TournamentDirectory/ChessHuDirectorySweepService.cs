using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

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
public class ChessHuDirectorySweepService : IDirectorySourceAdapter<ChessHuDirectorySweepService.CrawlerChessHuEvent>
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
        return await ExternalSweepRunner.RunAsync(_db, _log, this, events, ct);
    }

    // ----- Abbildung fuer den gemeinsamen Runner ------------------------------

    DirectorySourceKind IDirectorySourceAdapter<CrawlerChessHuEvent>.Kind => DirectorySourceKind.HungarianChessFederation;
    string IDirectorySourceAdapter<CrawlerChessHuEvent>.LogName => "chess.hu";
    int IDirectorySourceAdapter<CrawlerChessHuEvent>.SaveEvery => SaveEvery;
    string? IDirectorySourceAdapter<CrawlerChessHuEvent>.SourceKey(CrawlerChessHuEvent row) => row.EventId.Length == 0 ? null : row.EventId;
    DateOnly? IDirectorySourceAdapter<CrawlerChessHuEvent>.Start(CrawlerChessHuEvent row) => row.Start;
    string IDirectorySourceAdapter<CrawlerChessHuEvent>.Name(CrawlerChessHuEvent row) => row.Name;
    string IDirectorySourceAdapter<CrawlerChessHuEvent>.PublicId(CrawlerChessHuEvent row) => $"hu{row.EventId}";
    string? IDirectorySourceAdapter<CrawlerChessHuEvent>.Federation(CrawlerChessHuEvent row) => "HUN";
    string? IDirectorySourceAdapter<CrawlerChessHuEvent>.MatchPlace(CrawlerChessHuEvent row) => row.Place;
    string? IDirectorySourceAdapter<CrawlerChessHuEvent>.Url(CrawlerChessHuEvent row) => null;

    async Task<bool> IDirectorySourceAdapter<CrawlerChessHuEvent>.ApplyAsync(
        TournamentDirectoryEntry own, CrawlerChessHuEvent row, DateOnly start, DateTime now, CancellationToken ct)
    {
        var location = row.HasVenue ? row.Place?.Trim() : null;
        var locationChanged = own.LocationText != location;

        own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
        own.StartDate = start;
        own.EndDate = row.End ?? start;
        own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        own.LocationText = ExternalDirectorySource.Truncate(location, 300);
        ExternalSweepRunner.MarkSeen(own, now);
        ExternalDirectorySource.ApplyClassification(own);
        await ExternalDirectorySource.NoteSourceAsync(_db, own,
            DirectorySourceKind.HungarianChessFederation, row.EventId, null, now, ct);

        // Ohne Spielort keine Koordinaten: „Online" und „Helyszín később" stehen im
        // Orts-Feld, sind aber keine Orte. „Aktualisiert" heisst hier: verortet (wie FRSah/ECF).
        return await ExternalSweepRunner.LocateAsync(_geocoding, own, location, null, "HUN", locationChanged, ct);
    }

    void IDirectorySourceAdapter<CrawlerChessHuEvent>.LogSummary(
        ILogger log, int read, int added, int updated, int matched, int retired) =>
        log.LogInformation(
            "chess.hu-Kalender: {Read} gelesen, {Added} neu, {Matched} zugeordnet, {Retired} zurueckgezogen",
            read, added, matched, retired);


    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerChessHuEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var rows = await _httpClientFactory.GetCrawlerJsonAsync<List<ChessHuRow>>(
            $"/api/chess-hu-calendar?from={from:yyyy-MM-dd}", ct) ?? [];
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

    private sealed record ChessHuRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Place,
        bool HasVenue, bool FideRated);

    public sealed record CrawlerChessHuEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Place, bool HasVenue);
}
