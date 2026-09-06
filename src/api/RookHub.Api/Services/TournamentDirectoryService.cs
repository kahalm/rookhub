using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public sealed record DirectorySweepResult(
    string Federation, int Rows, int Added, int Updated, int Changed, int Removed, string? Error = null)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Fuellt und pflegt das Turnierverzeichnis aus der chess-results-Turniersuche.
///
/// Eine Abfrage je Foederation liefert alle Turniere eines Zeitfensters samt Ort, Termin und
/// Teilnehmerzahl - die Einzelturnierseiten werden dafuer NICHT geholt. Was sich geaendert hat,
/// entsteht hier zentral am Verzeichniseintrag und wird erst danach auf die Abonnenten aufgefaechert;
/// ein Schnappschuss je Nutzer waere redundant und wuerde bei mehreren Abonnenten auseinanderlaufen.
/// </summary>
public class TournamentDirectoryService
{
    /// <summary>
    /// Der Datumsfilter der Suche greift auf das ENDdatum. Rueckwaerts genug, dass ein gerade
    /// beendetes Turnier nicht sofort als "verschwunden" gilt.
    /// </summary>
    internal int LookBackDays { get; set; } = 30;
    internal int LookAheadMonths { get; set; } = 18;

    /// <summary>
    /// Erst nach so vielen Sweeps ohne Treffer gilt ein Turnier als abgesagt. Ein einzelner
    /// gescheiterter oder abgeschnittener Sweep wuerde sonst reihenweise Absagen melden.
    /// </summary>
    internal int MissedSweepsUntilRemoved { get; set; } = 2;

    private const int MaxRows = 2000;

    private readonly AppDbContext _db;
    private readonly CrawlerProxyService _crawler;
    private readonly GeocodingService _geocoding;
    private readonly NotificationService _notifications;
    private readonly ILogger<TournamentDirectoryService> _log;

    public TournamentDirectoryService(
        AppDbContext db,
        CrawlerProxyService crawler,
        GeocodingService geocoding,
        NotificationService notifications,
        ILogger<TournamentDirectoryService> log)
    {
        _db = db;
        _crawler = crawler;
        _geocoding = geocoding;
        _notifications = notifications;
        _log = log;
    }

    /// <summary>
    /// Sweept die uebergebenen Foederationen nacheinander und meldet am Ende EINMAL die neuen
    /// Turniere je Suchprofil. Nacheinander, weil der Crawler ohnehin einen prozessweiten
    /// Rate-Limiter hat - parallel wuerde nur die Warteschlange dort waschen.
    /// </summary>
    public async Task<List<DirectorySweepResult>> RunSweepAsync(
        IReadOnlyList<string> federations, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = new List<DirectorySweepResult>();
        var newEntryIds = new List<int>();

        foreach (var federation in federations)
        {
            ct.ThrowIfCancellationRequested();
            var (result, added) = await SweepFederationAsync(federation, today, ct);
            results.Add(result);
            newEntryIds.AddRange(added);
        }

        if (newEntryIds.Count > 0)
            await NotifyNearbyAsync(newEntryIds, today, ct);

        return results;
    }

    /// <summary>
    /// Ein Durchgang fuer eine Foederation. Gibt zusaetzlich die IDs der neu angelegten Eintraege
    /// zurueck, damit die Umkreis-Meldung am Ende ueber alle Foederationen aggregieren kann.
    /// </summary>
    public async Task<(DirectorySweepResult Result, List<int> NewEntryIds)> SweepFederationAsync(
        string federation, DateOnly today, CancellationToken ct = default)
    {
        federation = federation.Trim().ToUpperInvariant();
        var from = today.AddDays(-LookBackDays);
        var to = today.AddMonths(LookAheadMonths);

        var sweep = await _db.TournamentDirectorySweeps.FirstOrDefaultAsync(s => s.Federation == federation, ct);
        if (sweep is null)
        {
            sweep = new TournamentDirectorySweep { Federation = federation };
            _db.TournamentDirectorySweeps.Add(sweep);
        }
        sweep.LastAttemptedAt = DateTime.UtcNow;

        List<CrawlerDirectoryRow> rows;
        try
        {
            var path = $"/api/tournament-search?fed={Uri.EscapeDataString(federation)}" +
                       $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&maxRows={MaxRows}";
            var json = await _crawler.GetAsync(path, ct);
            rows = ParseRows(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fehlgeschlagener Sweep: LastSweptAt bleibt ALT. Damit nimmt die Rotation die
            // Foederation gleich wieder vor - und die Verschwunden-Erkennung setzt nicht auf
            // einer halben Trefferliste auf, die sonst reihenweise Absagen melden wuerde.
            sweep.LastError = Truncate(ex.Message, 500);
            sweep.ConsecutiveFailures++;
            await _db.SaveChangesAsync(ct);
            _log.LogWarning(ex, "Verzeichnis-Sweep {Federation} fehlgeschlagen", federation);
            return (new DirectorySweepResult(federation, 0, 0, 0, 0, 0, ex.Message), []);
        }

        var existing = await _db.TournamentDirectoryEntries
            .Where(e => e.Federation == federation && (e.EndDate == null || e.EndDate >= from))
            .ToListAsync(ct);
        var byId = existing.ToDictionary(e => e.ChessResultsId, StringComparer.Ordinal);

        var now = DateTime.UtcNow;
        var added = new List<TournamentDirectoryEntry>();
        var changed = new List<(TournamentDirectoryEntry Entry, string? OldDate, string? OldLocation)>();
        var updated = 0;

        foreach (var row in rows)
        {
            if (byId.TryGetValue(row.ChessResultsId, out var entry))
            {
                var oldHash = entry.ChangeHash;
                var oldDate = FormatRange(entry.StartDate, entry.EndDate);
                var oldLocation = entry.LocationText;
                var oldLocationText = entry.LocationText;

                Apply(row, entry, now);
                entry.MissedSweeps = 0;
                entry.RemovedAt = null;
                updated++;

                if (oldHash is not null && oldHash != entry.ChangeHash)
                    changed.Add((entry, oldDate, oldLocation));

                // Nur neu verorten, wenn sich der Ortstext wirklich geaendert hat - sonst wuerde
                // jede Nacht der gesamte Bestand durch den Gazetteer laufen.
                if (!string.Equals(oldLocationText, entry.LocationText, StringComparison.Ordinal))
                    await GeocodeAsync(entry, ct);
            }
            else
            {
                entry = new TournamentDirectoryEntry
                {
                    ChessResultsId = row.ChessResultsId,
                    FirstSeenAt = now,
                    CreatedAt = now,
                };
                Apply(row, entry, now);
                await GeocodeAsync(entry, ct);
                _db.TournamentDirectoryEntries.Add(entry);
                added.Add(entry);
            }
        }

        // Nicht mehr geliefert: erst zaehlen, dann (ab MissedSweepsUntilRemoved) als abgesagt melden.
        var seen = rows.Select(r => r.ChessResultsId).ToHashSet(StringComparer.Ordinal);
        var removed = new List<TournamentDirectoryEntry>();
        foreach (var entry in existing.Where(e => e.RemovedAt == null && !seen.Contains(e.ChessResultsId)))
        {
            entry.MissedSweeps++;
            entry.UpdatedAt = now;
            if (entry.MissedSweeps < MissedSweepsUntilRemoved) continue;
            entry.RemovedAt = now;
            removed.Add(entry);
        }

        sweep.LastSweptAt = now;
        sweep.LastRowCount = rows.Count;
        sweep.LastError = null;
        sweep.ConsecutiveFailures = 0;

        await _db.SaveChangesAsync(ct);

        await NotifyChangedAsync(changed, ct);
        await NotifyCancelledAsync(removed, ct);

        _log.LogInformation(
            "Verzeichnis-Sweep {Federation}: {Rows} Zeilen, {Added} neu, {Changed} geaendert, {Removed} abgesagt",
            federation, rows.Count, added.Count, changed.Count, removed.Count);

        return (new DirectorySweepResult(federation, rows.Count, added.Count, updated, changed.Count, removed.Count),
                added.Select(e => e.Id).ToList());
    }

    // ----- Benachrichtigungen ----------------------------------------------

    private async Task NotifyChangedAsync(
        List<(TournamentDirectoryEntry Entry, string? OldDate, string? OldLocation)> changed, CancellationToken ct)
    {
        if (changed.Count == 0) return;

        var ids = changed.Select(c => c.Entry.ChessResultsId).ToList();
        var subscribers = await SubscribersByTournamentAsync(ids, ct);

        foreach (var (entry, oldDate, oldLocation) in changed)
        {
            if (!subscribers.TryGetValue(entry.ChessResultsId, out var userIds)) continue;

            await _notifications.CreateManyAsync(userIds, NotificationType.TournamentChanged,
                new Dictionary<string, string>
                {
                    ["tournamentName"] = entry.Name,
                    ["oldDate"] = oldDate ?? "",
                    ["newDate"] = FormatRange(entry.StartDate, entry.EndDate) ?? "",
                    ["oldLocation"] = oldLocation ?? "",
                    ["newLocation"] = entry.LocationText ?? "",
                },
                DetailLink(entry.ChessResultsId));
        }
    }

    private async Task NotifyCancelledAsync(List<TournamentDirectoryEntry> removed, CancellationToken ct)
    {
        if (removed.Count == 0) return;

        var subscribers = await SubscribersByTournamentAsync(
            removed.Select(e => e.ChessResultsId).ToList(), ct);

        foreach (var entry in removed)
        {
            if (!subscribers.TryGetValue(entry.ChessResultsId, out var userIds)) continue;

            await _notifications.CreateManyAsync(userIds, NotificationType.TournamentCancelled,
                new Dictionary<string, string>
                {
                    ["tournamentName"] = entry.Name,
                    ["date"] = FormatRange(entry.StartDate, entry.EndDate) ?? "",
                },
                DetailLink(entry.ChessResultsId));
        }
    }

    /// <summary>
    /// Eine Meldung je Suchprofil und Lauf, nicht eine je Turnier: ein naechtlicher Sweep legt
    /// hunderte Eintraege an, und ein Umkreis von 100 km faengt davon leicht zwanzig.
    /// </summary>
    public async Task<int> NotifyNearbyAsync(
        IReadOnlyList<int> newEntryIds, DateOnly today, CancellationToken ct = default)
    {
        var profiles = await _db.TournamentSearchProfiles.AsNoTracking()
            .Where(p => p.NotifyNew)
            .ToListAsync(ct);
        if (profiles.Count == 0) return 0;

        var candidates = await _db.TournamentDirectoryEntries.AsNoTracking()
            .Where(e => newEntryIds.Contains(e.Id)
                        && e.Lat != null && e.Lon != null
                        && e.RemovedAt == null
                        && e.StartDate != null && e.StartDate >= today)
            .ToListAsync(ct);
        if (candidates.Count == 0) return 0;

        var notified = 0;
        foreach (var profile in profiles)
        {
            var matches = candidates
                .Where(e => MatchesProfile(e, profile))
                .OrderBy(e => e.StartDate)
                .ToList();
            if (matches.Count == 0) continue;

            await _notifications.CreateAsync(profile.UserId, NotificationType.TournamentNearbyNew,
                new Dictionary<string, string>
                {
                    ["profileName"] = profile.Name,
                    ["count"] = matches.Count.ToString(CultureInfo.InvariantCulture),
                    ["firstName"] = matches[0].Name,
                    ["radiusKm"] = profile.RadiusKm.ToString(CultureInfo.InvariantCulture),
                },
                $"/tournaments/calendar?profile={profile.Id}");
            notified++;
        }
        return notified;
    }

    /// <summary>
    /// Passt ein Eintrag in den Umkreis und die Filter eines Profils? Erst die billige
    /// Bounding-Box, dann die teure Distanz - bei hunderten Kandidaten x Profilen zaehlt das.
    /// </summary>
    internal static bool MatchesProfile(TournamentDirectoryEntry entry, TournamentSearchProfile profile)
    {
        if (entry.Lat is not { } lat || entry.Lon is not { } lon) return false;

        var box = GeoDistance.BoundingBox(profile.Lat, profile.Lon, profile.RadiusKm);
        if (lat < box.MinLat || lat > box.MaxLat || lon < box.MinLon || lon > box.MaxLon) return false;
        if (GeoDistance.Haversine(profile.Lat, profile.Lon, lat, lon) > profile.RadiusKm) return false;

        if (SplitCsv(profile.Federations) is { Count: > 0 } feds
            && (entry.Federation is null || !feds.Contains(entry.Federation, StringComparer.OrdinalIgnoreCase)))
            return false;

        if (SplitCsv(profile.Speeds) is { Count: > 0 } speeds
            && !speeds.Contains(entry.Speed.ToString(), StringComparer.OrdinalIgnoreCase))
            return false;

        if (profile.WeekendOnly && entry.StartDate is { } start
            && start.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            return false;

        if (profile.MinPlayers is { } min && (entry.PlayerCount ?? 0) < min)
            return false;

        return true;
    }

    internal static List<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private async Task<Dictionary<string, List<int>>> SubscribersByTournamentAsync(
        List<string> chessResultsIds, CancellationToken ct)
    {
        var rows = await _db.TournamentSubscriptions.AsNoTracking()
            .Where(s => chessResultsIds.Contains(s.CrawlerTournamentId))
            .Select(s => new { s.CrawlerTournamentId, s.UserId })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.CrawlerTournamentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(r => r.UserId).Distinct().ToList(), StringComparer.Ordinal);
    }

    // ----- Abbildung + Hilfsfunktionen -------------------------------------

    private void Apply(CrawlerDirectoryRow row, TournamentDirectoryEntry entry, DateTime now)
    {
        entry.Name = Truncate(row.Name, 500);
        entry.Federation = Truncate(row.Federation, 3);
        entry.State = Truncate(row.State, 100);
        entry.StartDate = row.StartDate;
        entry.EndDate = row.EndDate;
        entry.StartsOnWeekend = row.StartDate is { } d
            && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        entry.LocationText = Truncate(row.Location, 500);
        entry.TimeControlText = Truncate(row.TimeControl, 300);
        entry.Speed = TournamentSpeedClassifier.Classify(row.TimeControl);
        entry.Organizer = Truncate(row.Organizer, 300);
        entry.Director = Truncate(row.Director, 300);
        entry.ChiefArbiter = Truncate(row.ChiefArbiter, 300);
        entry.Rounds = row.Rounds;
        entry.PlayerCount = row.PlayerCount;
        entry.UpstreamUpdatedAt = row.LastUpdatedApproxUtc;
        entry.ChangeHash = ComputeChangeHash(row.StartDate, row.EndDate, row.Location);
        entry.LastSeenAt = now;
        entry.UpdatedAt = now;
    }

    private async Task GeocodeAsync(TournamentDirectoryEntry entry, CancellationToken ct)
    {
        // Eine von Hand gesetzte Koordinate nie ueberschreiben - sie ist die Korrektur eines
        // Fehlgriffs und wuerde sonst jede Nacht zurueckfallen.
        if (entry.GeoSource == GeoSource.Manual) return;

        var result = await _geocoding.ResolveAsync(entry.LocationText, entry.State, entry.Federation, ct);
        if (result is null)
        {
            entry.Lat = null;
            entry.Lon = null;
            entry.GeoSource = GeoSource.None;
            entry.GeoPlaceName = null;
            return;
        }

        entry.Lat = result.Lat;
        entry.Lon = result.Lon;
        entry.GeoSource = result.Source;
        entry.GeoPlaceName = Truncate(result.PlaceName, 200);
    }

    /// <summary>
    /// Hash ueber genau die Felder, deren Aenderung eine Meldung wert ist: Termin und Spielort.
    /// Teilnehmerzahl, Schiedsrichter oder ein neuer "Last update"-Zeitstempel bleiben bewusst
    /// draussen - sonst meldet jede wachsende Meldeliste eine "Aenderung".
    /// </summary>
    internal static string ComputeChangeHash(DateOnly? start, DateOnly? end, string? location)
    {
        var payload = string.Join('|',
            start?.ToString("yyyy-MM-dd") ?? "",
            end?.ToString("yyyy-MM-dd") ?? "",
            (location ?? "").Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()[..32];
    }

    internal static string? FormatRange(DateOnly? start, DateOnly? end)
    {
        if (start is null && end is null) return null;
        if (start is null) return end!.Value.ToString("yyyy-MM-dd");
        if (end is null || end == start) return start.Value.ToString("yyyy-MM-dd");
        return $"{start.Value:yyyy-MM-dd} - {end.Value:yyyy-MM-dd}";
    }

    private static string DetailLink(string chessResultsId) => $"/tournaments/calendar?t={chessResultsId}";

    private static string Truncate(string? value, int max) =>
        value is null ? "" : value.Length <= max ? value : value[..max];

    // ----- Antwort des Crawlers --------------------------------------------

    internal sealed record CrawlerDirectoryRow(
        string ChessResultsId, string Name, string? Federation, string? State,
        DateOnly? StartDate, DateOnly? EndDate, string? Location, string? TimeControl,
        string? Director, string? Organizer, string? ChiefArbiter,
        int? Rounds, int? PlayerCount, DateTime? LastUpdatedApproxUtc);

    internal static List<CrawlerDirectoryRow> ParseRows(JsonElement json)
    {
        var rows = new List<CrawlerDirectoryRow>();
        if (json.ValueKind != JsonValueKind.Array) return rows;

        foreach (var element in json.EnumerateArray())
        {
            var id = Str(element, "chessResultsId");
            var name = Str(element, "name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;

            rows.Add(new CrawlerDirectoryRow(
                id, name,
                Str(element, "federation"), Str(element, "state"),
                Date(element, "startDate"), Date(element, "endDate"),
                Str(element, "location"), Str(element, "timeControl"),
                Str(element, "director"), Str(element, "organizer"), Str(element, "chiefArbiter"),
                Int(element, "rounds"), Int(element, "playerCount"),
                Timestamp(element, "lastUpdatedApproxUtc")));
        }
        return rows;
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static DateOnly? Date(JsonElement e, string prop) =>
        DateOnly.TryParseExact(Str(e, prop), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;

    private static DateTime? Timestamp(JsonElement e, string prop) =>
        DateTime.TryParse(Str(e, prop), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
}
