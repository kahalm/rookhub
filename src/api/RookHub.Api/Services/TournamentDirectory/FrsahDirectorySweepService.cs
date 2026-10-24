using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Der Kalender des rumaenischen Verbands (FRSah, frsah.ro) als Quelle.
///
/// <para><b>Warum diese Quelle.</b> Am 2026-09-09 gemessen: 31 kuenftige Turniere (2026-09-12 bis
/// 2026-11-30), davon rund ein Drittel nicht auf chess-results (dort stehen im selben Zeitraum 73
/// Eintraege fuer ROU). Der Zugewinn ist vor allem STRUKTURELL: die kompletten nationalen
/// Mannschaftsligen (Superliga, Divizia A, die Finalrunden) laufen nie ueber eine
/// Swiss-Manager-Datei und stehen deshalb auch nie auf chess-results.</para>
///
/// <para><b>Derselbe Plugin-Baukasten wie England, aber ohne dessen Sonderfall.</b> FRSah faehrt
/// „The Events Calendar" wie die ECF — anders als dort steht die Spielstaette hier aber schon
/// vollstaendig im Termin selbst (der Crawler-Dienst holt deshalb nur EINEN Endpunkt). Und anders
/// als England liefert diese Quelle KEINE Koordinaten und KEINE gepflegten Schlagworte (keine
/// „Meeting"/„Online"/„Juniors Only"-Aequivalente) — die Verortung geht deshalb den normalen Weg
/// ueber den Ortstext und das Ortslexikon, wie bei den meisten anderen Zusatzquellen.</para>
///
/// <para><b>Die Foederation ist immer ROU.</b> Anders als beim englischen Kalender (der einmal
/// ein GM-Turnier in Frankreich fuehrte) ist dies der Kalender EINES nationalen Verbands ohne
/// beobachteten Auslandsfall — alle 31 gemessenen Turniere liegen in Rumaenien.</para>
///
/// <para><b>Was sie NICHT liefert:</b> Bedenkzeit, Rundenzahl, System und Teilnehmerzahl stehen
/// nur im Fliesstext der Ausschreibung (PDF-Einladungen), nicht strukturiert.</para>
/// </summary>
public class FrsahDirectorySweepService : IDirectorySourceAdapter<FrsahDirectorySweepService.CrawlerFrsahEvent>
{
    private const string Federation = "ROU";

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<FrsahDirectorySweepService> _log;

    public FrsahDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<FrsahDirectorySweepService> log)
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

    DirectorySourceKind IDirectorySourceAdapter<CrawlerFrsahEvent>.Kind => DirectorySourceKind.RomanianChessFederation;
    string IDirectorySourceAdapter<CrawlerFrsahEvent>.LogName => "FRSah";
    string? IDirectorySourceAdapter<CrawlerFrsahEvent>.SourceKey(CrawlerFrsahEvent row) => row.EventId;
    DateOnly? IDirectorySourceAdapter<CrawlerFrsahEvent>.Start(CrawlerFrsahEvent row) => row.Start;
    string IDirectorySourceAdapter<CrawlerFrsahEvent>.Name(CrawlerFrsahEvent row) => row.Name;
    string IDirectorySourceAdapter<CrawlerFrsahEvent>.PublicId(CrawlerFrsahEvent row) => $"ro{row.EventId}";
    string IDirectorySourceAdapter<CrawlerFrsahEvent>.Federation(CrawlerFrsahEvent row) => Federation;
    string? IDirectorySourceAdapter<CrawlerFrsahEvent>.MatchPlace(CrawlerFrsahEvent row) => row.Place;
    string? IDirectorySourceAdapter<CrawlerFrsahEvent>.Url(CrawlerFrsahEvent row) => row.Url;

    async Task<bool> IDirectorySourceAdapter<CrawlerFrsahEvent>.ApplyAsync(
        TournamentDirectoryEntry own, CrawlerFrsahEvent row, DateOnly start, DateTime now, CancellationToken ct)
    {
        var location = row.Place?.Trim();
        var locationChanged = own.LocationText != location;

        own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
        own.Federation = Federation;
        own.StartDate = start;
        own.EndDate = row.End ?? start;
        own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        own.LocationText = ExternalDirectorySource.Truncate(location, 300);
        ExternalSweepRunner.MarkSeen(own, now);
        ExternalDirectorySource.ApplyClassification(own);
        await ExternalDirectorySource.NoteSourceAsync(_db, own,
            DirectorySourceKind.RomanianChessFederation, row.EventId, row.Url, now, ct);

        return await ApplyCoordinatesAsync(own, locationChanged, ct);
    }

    void IDirectorySourceAdapter<CrawlerFrsahEvent>.LogSummary(
        ILogger log, int read, int added, int updated, int matched, int retired) =>
        log.LogInformation(
            "FRSah-Kalender: {Read} gelesen, {Added} neu, {Located} verortet, {Matched} zugeordnet, {Retired} zurueckgezogen",
            read, added, updated, matched, retired);

    /// <summary>
    /// Die Koordinaten ausschliesslich ueber das Ortslexikon setzen — diese Quelle liefert (anders
    /// als der englische Kalender) keine eigenen Koordinaten.
    /// </summary>
    private async Task<bool> ApplyCoordinatesAsync(TournamentDirectoryEntry entry,
        bool locationChanged, CancellationToken ct)
    {
        if (entry.GeoSource == GeoSource.Manual) return false;
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

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerFrsahEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var rows = await _httpClientFactory.GetCrawlerJsonAsync<List<FrsahRow>>(
            $"/api/frsah-calendar?from={from:yyyy-MM-dd}", ct) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerFrsahEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Place, r.Url))
            // KEIN Filter auf den Termin. Eine Zeile ohne lesbaren Termin bleibt in der Liste,
            // weil die Verschwunden-Erkennung sie sonst nicht als GELIEFERT sieht — die Quelle
            // fuehrt sie ja. Ausgesiebt wird sie erst in der Schleife, dort steht sie dann schon
            // in `delivered`. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle:
            // ohne das waeren es reihenweise falsche Absagen nach zwei Naechten.
            .ToList();
    }

    private sealed record FrsahRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Url,
        string? Place, string? City, string? PostalCode, string? Country);

    public sealed record CrawlerFrsahEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Place, string? Url);
}
