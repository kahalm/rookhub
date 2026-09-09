using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;

namespace RookHub.Api.Services;

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
public class FrsahDirectorySweepService
{
    private const int SaveEvery = 25;
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
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var now = DateTime.UtcNow;
        int added = 0, located = 0, matched = 0, retired = 0, processed = 0;

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Start is not { } start || row.Name.Length == 0 || row.EventId.Length == 0)
                    continue;

                processed++;
                var publicId = $"ro{row.EventId}";
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);
                var match = await ExternalDirectorySource.FindMatchAsync(_db, Federation, start, row.Name, ct);

                if (match is not null)
                {
                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.RomanianChessFederation, row.EventId, row.Url, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("FRSah: {PublicId} geht in {Target} auf ({Name})",
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
                        Federation = Federation,
                        FirstSeenAt = now,
                    };
                    _db.TournamentDirectoryEntries.Add(own);
                    added++;
                }

                var location = row.Place?.Trim();
                var locationChanged = own.LocationText != location;

                own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
                own.Federation = Federation;
                own.StartDate = start;
                own.EndDate = row.End ?? start;
                own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.LocationText = ExternalDirectorySource.Truncate(location, 300);
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);
                await ExternalDirectorySource.NoteSourceAsync(_db, own,
                    DirectorySourceKind.RomanianChessFederation, row.EventId, row.Url, now, ct);

                if (await ApplyCoordinatesAsync(own, locationChanged, ct)) located++;

                if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "FRSah: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "FRSah-Kalender: {Read} gelesen, {Added} neu, {Located} verortet, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, located, matched, retired);
        return new ExternalSweepResult(events.Count, added, located, matched, retired);
    }

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
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/frsah-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<FrsahRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerFrsahEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Place, r.Url))
            .Where(e => e.Start is not null)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record FrsahRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Url,
        string? Place, string? City, string? PostalCode, string? Country);

    public sealed record CrawlerFrsahEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Place, string? Url);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
