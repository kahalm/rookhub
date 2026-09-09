using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;

namespace RookHub.Api.Services;

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
public class SzsDirectorySweepService
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
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var now = DateTime.UtcNow;
        int added = 0, updated = 0, matched = 0, retired = 0, processed = 0;
        // Was DIESER Lauf geliefert hat — Grundlage der Verschwunden-Erkennung unten.
        var delivered = new List<string>();

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Date is not { } date || row.Name.Length == 0 || row.EventId.Length == 0)
                    continue;

                processed++;
                var publicId = $"sl{row.EventId}";
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);

                // Abgesagt: nichts anlegen, und einen bestehenden Eintrag zurueckziehen. Ein
                // abgesagtes Turnier im Kalender ist schlechter als keins — man faehrt hin.
                if (row.Cancelled)
                {
                    if (own is not null && own.RemovedAt is null)
                    {
                        own.RemovedAt = now;
                        retired++;
                    }
                    continue;
                }

                var match = await ExternalDirectorySource.FindMatchAsync(_db, "SLO", date, row.Name, ct);
                if (match is not null)
                {
                    delivered.Add(row.EventId);
                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.SlovenianChessFederation, row.EventId, null, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("SZS: {PublicId} geht in {Target} auf ({Name})",
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
                        Federation = "SLO",
                        FirstSeenAt = now,
                    };
                    _db.TournamentDirectoryEntries.Add(own);
                    added++;
                }

                var location = LocationOf(row);
                var locationChanged = own.LocationText != location;
                own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
                // Die Liste nennt EINEN Tag. Mehrtaegige Turniere stehen dort als eigene Zeile je
                // Tag — Beginn und Ende sind hier also derselbe Tag, und das ist die Wahrheit
                // dieser Quelle, kein fehlendes Feld.
                own.StartDate = date;
                own.EndDate = date;
                own.StartsOnWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.LocationText = ExternalDirectorySource.Truncate(location, 300);
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);
                delivered.Add(row.EventId);
                await ExternalDirectorySource.NoteSourceAsync(_db, own,
                    DirectorySourceKind.SlovenianChessFederation, row.EventId, null, now, ct);

                if (locationChanged || own.Lat is null)
                {
                    var hit = await _geocoding.ResolveAsync(own.LocationText, null, "SLO", ct);
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
            catch (Exception ex) { _log.LogWarning(ex, "SZS: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "SZS-Kalender: {Read} gelesen, {Added} neu, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, matched, retired);
        // Was die Quelle nicht mehr liefert, wird zurueckgezogen (zwei Laeufe Karenz,
        // Bremse gegen halbe Laeufe — siehe RetireVanishedAsync).
        retired += await ExternalDirectorySource.RetireVanishedAsync(
            _db, DirectorySourceKind.SlovenianChessFederation, delivered, now, ct);

        return new ExternalSweepResult(events.Count, added, updated, matched, retired);
    }

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
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/szs-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<SzsRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerSzsEvent(
                r.EventId!, r.Name!, ParseDate(r.Date), r.PostalCode, r.Place, r.Cancelled))
            .Where(e => e.Date is not null)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SzsRow(
        string? EventId, string? Name, string? Date, string? PostalCode, string? Place, bool Cancelled);

    public sealed record CrawlerSzsEvent(
        string EventId, string Name, DateOnly? Date, string? PostalCode, string? Place, bool Cancelled);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
