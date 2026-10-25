using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Der Kalender des slowenischen Verbands (ŠZS) als Quelle.
///
/// <para><b>Das krasseste Verhaeltnis aller geprueften Quellen:</b> 78 kuenftige Turniere gegen
/// <b>7</b> auf chess-results im selben Zeitraum. Und nicht bloss Vorlauf — der Rueckblick auf
/// einen abgeschlossenen Monat zeigt, dass 36 von 88 Eintraegen (41 %) dort NIE erscheinen; ganze
/// woechentliche Serien fehlen strukturell.</para>
///
/// <para><b>Die Postleitzahl steht in der Trefferliste.</b> Das ist der Sonderwert dieser Quelle:
/// der genaueste Weg des <see cref="GeocodingService"/> greift ohne einen einzigen Abruf je
/// Turnier. Uebergeben wird „PLZ Ort" — dieselbe Form, in der chess-results seine Ortstexte
/// schreibt und auf die der PLZ-Weg dort ausgelegt ist.</para>
///
/// <para><b>Abgesagte Turniere werden NICHT angelegt</b> und ein bestehender Eintrag wird
/// zurueckgezogen: die Quelle kennzeichnet Absagen nur im Namen („ODPADE; …"), und ein abgesagtes
/// Turnier im Kalender ist schlechter als keins — man faehrt hin.</para>
/// </summary>
public class SzsDirectorySweepService : IDirectorySourceAdapter<SzsDirectorySweepService.CrawlerSzsEvent>
{
    private const int SaveEvery = 50;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<SzsDirectorySweepService> _log;

    public SzsDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<SzsDirectorySweepService> log)
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

    DirectorySourceKind IDirectorySourceAdapter<CrawlerSzsEvent>.Kind => DirectorySourceKind.SlovenianChessFederation;
    string IDirectorySourceAdapter<CrawlerSzsEvent>.LogName => "SZS";
    int IDirectorySourceAdapter<CrawlerSzsEvent>.SaveEvery => SaveEvery;
    string? IDirectorySourceAdapter<CrawlerSzsEvent>.SourceKey(CrawlerSzsEvent row) => row.EventId.Length == 0 ? null : row.EventId;
    DateOnly? IDirectorySourceAdapter<CrawlerSzsEvent>.Start(CrawlerSzsEvent row) => row.Date;
    string IDirectorySourceAdapter<CrawlerSzsEvent>.Name(CrawlerSzsEvent row) => row.Name;
    string IDirectorySourceAdapter<CrawlerSzsEvent>.PublicId(CrawlerSzsEvent row) => $"sl{row.EventId}";
    // Abgesagt: nichts anlegen, und einen bestehenden Eintrag zurueckziehen. Ein abgesagtes
    // Turnier im Kalender ist schlechter als keins — man faehrt hin.
    bool IDirectorySourceAdapter<CrawlerSzsEvent>.Withdrawn(CrawlerSzsEvent row) => row.Cancelled;
    string IDirectorySourceAdapter<CrawlerSzsEvent>.Federation(CrawlerSzsEvent row) => "SLO";
    string? IDirectorySourceAdapter<CrawlerSzsEvent>.MatchPlace(CrawlerSzsEvent row) => LocationOf(row);
    string? IDirectorySourceAdapter<CrawlerSzsEvent>.Url(CrawlerSzsEvent row) => null;

    async Task<bool> IDirectorySourceAdapter<CrawlerSzsEvent>.ApplyAsync(
        TournamentDirectoryEntry own, CrawlerSzsEvent row, DateOnly start, DateTime now, CancellationToken ct)
    {
        var location = LocationOf(row);
        var locationChanged = own.LocationText != location;
        own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
        // Die Liste nennt EINEN Tag. Mehrtaegige Turniere stehen dort als eigene Zeile je
        // Tag — Beginn und Ende sind hier also derselbe Tag, und das ist die Wahrheit
        // dieser Quelle, kein fehlendes Feld.
        own.StartDate = start;
        own.EndDate = start;
        own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        own.LocationText = ExternalDirectorySource.Truncate(location, 300);
        ExternalSweepRunner.MarkSeen(own, now);
        ExternalDirectorySource.ApplyClassification(own);
        await ExternalDirectorySource.NoteSourceAsync(_db, own,
            DirectorySourceKind.SlovenianChessFederation, row.EventId, null, now, ct);

        // „Aktualisiert" heisst hier: verortet (wie FRSah/ECF).
        var located = false;
        if (locationChanged || own.Lat is null)
        {
            var hit = await _geocoding.ResolveAsync(own.LocationText, null, "SLO", ct);
            if (hit is not null && hit.Source != GeoSource.Ambiguous)
            {
                own.Lat = hit.Lat;
                own.Lon = hit.Lon;
                own.GeoSource = hit.Source;
                own.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
                located = true;
            }
        }
        return located;
    }

    void IDirectorySourceAdapter<CrawlerSzsEvent>.LogSummary(
        ILogger log, int read, int added, int updated, int matched, int retired) =>
        log.LogInformation(
            "SZS-Kalender: {Read} gelesen, {Added} neu, {Matched} zugeordnet, {Retired} zurueckgezogen",
            read, added, matched, retired);


    /// <summary>
    /// „PLZ Ort" — dieselbe Form, in der chess-results seine Ortstexte schreibt („5020 Salzburg").
    /// Der PLZ-Weg des Geocoders ist darauf ausgelegt und verlangt zusaetzlich, dass der Ortsname
    /// im Text vorkommt; beides steht hier also beieinander.
    /// </summary>
    internal static string? LocationOf(CrawlerSzsEvent row)
    {
        var place = row.Place?.Trim();
        var zip = row.PostalCode?.Trim();

        if (zip is not { Length: > 0 }) return place;
        return place is { Length: > 0 } ? $"{zip} {place}" : zip;
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerSzsEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var rows = await _httpClientFactory.GetCrawlerJsonAsync<List<SzsRow>>(
            $"/api/szs-calendar?from={from:yyyy-MM-dd}", ct) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerSzsEvent(
                r.EventId!, r.Name!, ParseDate(r.Date), r.PostalCode, r.Place, r.Cancelled))
            // KEIN Filter auf den Termin. Eine Zeile ohne lesbaren Termin bleibt in der Liste,
            // weil die Verschwunden-Erkennung sie sonst nicht als GELIEFERT sieht — die Quelle
            // fuehrt sie ja. Ausgesiebt wird sie erst in der Schleife, dort steht sie dann schon
            // in `delivered`. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle:
            // ohne das waeren es reihenweise falsche Absagen nach zwei Naechten.
            .ToList();
    }

    private sealed record SzsRow(
        string? EventId, string? Name, string? Date, string? PostalCode, string? Place, bool Cancelled);

    public sealed record CrawlerSzsEvent(
        string EventId, string Name, DateOnly? Date, string? PostalCode, string? Place, bool Cancelled);
}
