using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Die Benachrichtigungen der chess-results-Turniersuche: geaenderte und vermutlich abgesagte
/// Turniere an ihre Abonnenten, neue Turniere im Umkreis je Suchprofil.
///
/// <para>Herausgezogen aus <see cref="TournamentDirectoryService"/> (Codereview 2026-09-29, Z8).</para>
/// </summary>
public sealed class DirectoryNotifier
{
    /// <summary>Blockgroesse beim Nachladen der neuen Eintraege fuer die Umkreis-Meldung.</summary>
    private const int NotifyLookupBatch = 500;

    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;

    public DirectoryNotifier(AppDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task NotifyChangedAsync(
        List<(TournamentDirectoryEntry Entry, string? OldDate, string? OldLocation)> changed, CancellationToken ct)
    {
        if (changed.Count == 0) return;

        // Ein Abo traegt die chess-results-Nummer; ein Eintrag ohne sie kann keine Abonnenten
        // haben und faellt hier heraus.
        var ids = changed.Select(c => c.Entry.ChessResultsId).Where(id => id is not null)
            .Select(id => id!).ToList();
        var subscribers = await SubscribersByTournamentAsync(ids, ct);

        foreach (var (entry, oldDate, oldLocation) in changed)
        {
            if (entry.ChessResultsId is null) continue;
            if (!subscribers.TryGetValue(entry.ChessResultsId, out var userIds)) continue;

            await _notifications.CreateManyAsync(userIds, NotificationType.TournamentChanged,
                new Dictionary<string, string>
                {
                    ["tournamentName"] = entry.Name,
                    ["oldDate"] = oldDate ?? "",
                    ["newDate"] = DirectoryKeys.FormatRange(entry.StartDate, entry.EndDate) ?? "",
                    ["oldLocation"] = oldLocation ?? "",
                    ["newLocation"] = entry.LocationText ?? "",
                },
                DetailLink(entry.PublicId));
        }
    }

    /// <summary>
    /// „Vermutlich abgesagt" an die Abonnenten — aber nur für Turniere, die noch nicht VORBEI sind. Ein Turnier, das
    /// schon stattgefunden hat und danach von chess-results verschwindet (Veranstalter räumt eine leere Gruppe weg,
    /// legt Gruppen zusammen), ist keine Absage; die Meldung darüber war nur Lärm (gemeldet am 29.09.2026: die
    /// Schachrallye Pradl vom 27.09. kam zwei Tage später als „vermutlich abgesagt"). Aus dem Verzeichnis
    /// verschwindet es trotzdem. Ohne jedes Datum bleibt es bei der Meldung — ob es vorbei ist, weiß niemand.
    /// </summary>
    public async Task NotifyCancelledAsync(List<TournamentDirectoryEntry> removed, DateOnly today, CancellationToken ct)
    {
        removed = removed.Where(e => (e.EndDate ?? e.StartDate) is not { } last || last >= today).ToList();
        if (removed.Count == 0) return;

        var subscribers = await SubscribersByTournamentAsync(
            removed.Select(e => e.ChessResultsId).Where(id => id is not null)
                .Select(id => id!).ToList(), ct);

        foreach (var entry in removed)
        {
            if (entry.ChessResultsId is null) continue;
            if (!subscribers.TryGetValue(entry.ChessResultsId, out var userIds)) continue;

            await _notifications.CreateManyAsync(userIds, NotificationType.TournamentCancelled,
                new Dictionary<string, string>
                {
                    ["tournamentName"] = entry.Name,
                    ["date"] = DirectoryKeys.FormatRange(entry.StartDate, entry.EndDate) ?? "",
                },
                DetailLink(entry.PublicId));
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

        // Die Kandidaten werden in Bloecken geholt. Eine `Contains`-Liste uebersetzt der Provider
        // in inline-Konstanten; beim ERSTEN Lauf nach einem Deploy sind das ueber alle
        // Foederationen zusammen bis zu sechsstellig viele Nummern — ein Statement von mehreren
        // MB, das an `max_allowed_packet` scheitert. Der Wurf kaeme dann NACH allen erfolgreichen
        // Sweeps: die Daten stehen, aber keine einzige Umkreis-Meldung geht raus.
        var candidates = new List<TournamentDirectoryEntry>();
        foreach (var block in newEntryIds.Distinct().Chunk(NotifyLookupBatch))
        {
            candidates.AddRange(await _db.TournamentDirectoryEntries.AsNoTracking()
                .Where(e => block.Contains(e.Id)
                            && e.Lat != null && e.Lon != null
                            && e.RemovedAt == null
                            && e.StartDate != null && e.StartDate >= today)
                .ToListAsync(ct));
        }
        if (candidates.Count == 0) return 0;

        // Was ein Nutzer AUSGEBLENDET hat, wird ihm auch nicht gemeldet. Eine Benachrichtigung
        // ueber ein Turnier, das man weggeklickt hat, ist genau die Art Meldung, die einen dazu
        // bringt, alle abzuschalten. Nur die Nutzer mit meldenden Profilen und nur die
        // Kandidaten-Nummern werden geladen — das sind wenige Zeilen.
        var candidateIds = candidates.Select(e => e.PublicId).ToList();
        var userIds = profiles.Select(p => p.UserId).Distinct().ToList();
        var ignored = (await _db.TournamentDirectoryIgnores.AsNoTracking()
                .Where(i => userIds.Contains(i.UserId) && candidateIds.Contains(i.PublicId))
                .Select(i => new { i.UserId, i.PublicId })
                .ToListAsync(ct))
            .GroupBy(i => i.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PublicId).ToHashSet(StringComparer.Ordinal));

        var notified = 0;
        foreach (var profile in profiles)
        {
            var hidden = ignored.GetValueOrDefault(profile.UserId);
            var matches = candidates
                .Where(e => MatchesProfile(e, profile))
                .Where(e => hidden is null || !hidden.Contains(e.PublicId))
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

    private static string DetailLink(string publicId) => $"/tournaments/calendar?t={publicId}";
}
