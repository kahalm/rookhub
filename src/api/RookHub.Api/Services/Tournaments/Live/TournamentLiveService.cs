using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>Eine Runde der verknüpften Übertragung, schon auf die chess-results-Rundennummer umgerechnet.</summary>
public sealed record LiveRoundInfo(int Round, string LichessRoundId, string Name, DateTime? StartsAt, bool Ongoing, bool Finished);

/// <summary>Stand der Verknüpfung eines Turniers mit einer Lichess-Übertragung.</summary>
public sealed record LiveStatus(bool Linked, bool Manual, string? TourId, string? TourName, string? Url,
    IReadOnlyList<LiveRoundInfo> Rounds, int? CurrentRound);

/// <summary>
/// Verknüpfung Turnier ↔ Lichess-Übertragung (Tabelle <c>TournamentBroadcastLinks</c>): lesen (und beim ersten Lesen
/// automatisch suchen, <see cref="BroadcastFinder"/>), von Hand verknüpfen, lösen, chess-results-Runde → Lichess-Runde.
/// Die Runden der Übertragung kommen 60 s gecacht von Lichess (ob eine Runde läuft, ändert sich am Turniertag).
/// </summary>
public sealed class TournamentLiveService
{
    private static readonly TimeSpan TourTtl = TimeSpan.FromSeconds(60);
    /// <summary>So lange hält die letzte gute Rundenliste als Rückfall, wenn Lichess drosselt oder nicht antwortet.</summary>
    private static readonly TimeSpan StaleTtl = TimeSpan.FromHours(12);
    private static string StaleKey(string tourId) => "live-tour-stale:" + tourId;

    private readonly AppDbContext _db;
    private readonly ILiveTournamentData _data;
    private readonly BroadcastFinder _finder;
    private readonly ILichessLiveSource _source;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TournamentLiveService> _logger;

    public TournamentLiveService(AppDbContext db, ILiveTournamentData data, BroadcastFinder finder, ILichessLiveSource source,
        IMemoryCache cache, ILogger<TournamentLiveService> logger)
    {
        _db = db;
        _data = data;
        _finder = finder;
        _source = source;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Stand für die Turnierseite; ohne Verknüpfung wird (gecacht, siehe Finder) gesucht. <c>null</c> = Turnier unbekannt.</summary>
    public async Task<LiveStatus?> GetStatusAsync(string id, CancellationToken ct)
    {
        var link = await _db.TournamentBroadcastLinks.AsNoTracking().FirstOrDefaultAsync(l => l.CrawlerTournamentId == id, ct);
        if (link is null)
        {
            var t = await _data.GetTournamentAsync(id, ct);
            if (t is null) return null;
            var tour = await FindAsync(id, t, ct);
            if (tour is null) return new LiveStatus(false, false, null, null, null, [], null);
            link = await SaveAsync(id, tour, manual: false, ct);
        }
        return await StatusOfAsync(link, ct);
    }

    /// <summary>Stand NUR für ein schon verknüpftes Turnier — ohne Suche bei Lichess (Link-Vorschau: ein Vorschau-Abrufer soll
    /// keine Suche anstoßen). <c>null</c> = nicht verknüpft.</summary>
    public async Task<LiveStatus?> GetLinkedStatusAsync(string id, CancellationToken ct)
    {
        var link = await _db.TournamentBroadcastLinks.AsNoTracking().FirstOrDefaultAsync(l => l.CrawlerTournamentId == id, ct);
        return link is null ? null : await StatusOfAsync(link, ct);
    }

    private async Task<LichessBroadcasts.Tour?> FindAsync(string id, CrTournament t, CancellationToken ct)
    {
        IReadOnlyList<CrPlayer> players;
        try { players = await _data.GetPlayersAsync(id, ct); }
        catch (CrawlerRequestException) { players = []; }
        var ids = players.Select(p => p.FideId).OfType<string>().ToList();
        return await _finder.FindAsync(t.ChessResultsId.Length > 0 ? t : t with { ChessResultsId = id }, ids, DateTime.UtcNow, ct);
    }

    private async Task<TournamentBroadcastLink> SaveAsync(string id, LichessBroadcasts.Tour tour, bool manual, CancellationToken ct)
    {
        var link = await _db.TournamentBroadcastLinks.FirstOrDefaultAsync(l => l.CrawlerTournamentId == id, ct);
        if (link is null) _db.TournamentBroadcastLinks.Add(link = new TournamentBroadcastLink { CrawlerTournamentId = id });
        link.TourId = tour.Id;
        link.TourName = tour.Name.Length > 200 ? tour.Name[..200] : tour.Name;
        link.Manual = manual;
        link.LinkedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (!manual)
        {
            // Zwei Zuschauer haben gleichzeitig gesucht — die andere Zeile gilt.
            _db.ChangeTracker.Clear();
            return await _db.TournamentBroadcastLinks.AsNoTracking().FirstAsync(l => l.CrawlerTournamentId == id, ct);
        }
        return link;
    }

    /// <summary>Von Hand verknüpfen (Turnier-, Runden- oder Partie-Link). Reason: <c>invalidUrl</c>, <c>notFound</c>, <c>tournamentNotFound</c>.</summary>
    public async Task<(LiveStatus? Status, string? Reason)> LinkAsync(string id, string? url, CancellationToken ct)
    {
        if (LichessBroadcasts.IdsOf(url) is not { } ids) return (null, "invalidUrl");
        if (await _data.GetTournamentAsync(id, ct) is null) return (null, "tournamentNotFound");
        var json = ids.TourId is { } tid ? await _source.GetTourAsync(tid, ct) : await _source.GetTourOfRoundAsync(ids.RoundId!, ct);
        if (json is null || LichessBroadcasts.ParseTourDetail(json) is not { } detail) return (null, "notFound");
        _cache.Set("live-tour:" + detail.Tour.Id, detail, TourTtl);
        var link = await SaveAsync(id, detail.Tour, manual: true, ct);
        _logger.LogInformation("Live-Bretter: Turnier {Tournament} von Hand mit Übertragung {Tour} verknüpft", id, detail.Tour.Id);
        return (await StatusOfAsync(link, ct), null);
    }

    /// <summary>Verknüpfung lösen (danach sucht das nächste Lesen wieder automatisch). → gab es eine?</summary>
    public async Task<bool> UnlinkAsync(string id, CancellationToken ct)
    {
        var link = await _db.TournamentBroadcastLinks.FirstOrDefaultAsync(l => l.CrawlerTournamentId == id, ct);
        if (link is null) return false;
        _db.TournamentBroadcastLinks.Remove(link);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Die Lichess-Runde zur chess-results-Runde eines VERKNÜPFTEN Turniers; <c>null</c> = nicht verknüpft / keine solche Runde.</summary>
    public async Task<LiveRoundInfo?> ResolveRoundAsync(string id, int round, CancellationToken ct)
    {
        var link = await _db.TournamentBroadcastLinks.AsNoTracking().FirstOrDefaultAsync(l => l.CrawlerTournamentId == id, ct);
        if (link is null) return null;
        var detail = await DetailAsync(link.TourId, ct);
        return detail is null ? null : RoundsOf(detail).FirstOrDefault(r => r.Round == round);
    }

    private async Task<LiveStatus> StatusOfAsync(TournamentBroadcastLink link, CancellationToken ct)
    {
        var detail = await DetailAsync(link.TourId, ct);
        var rounds = detail is null ? [] : RoundsOf(detail);
        var current = rounds.LastOrDefault(r => r.Ongoing)?.Round
                      ?? rounds.LastOrDefault(r => r.Finished || r.StartsAt is { } s && s <= DateTime.UtcNow)?.Round
                      ?? rounds.FirstOrDefault()?.Round;
        return new LiveStatus(true, link.Manual, link.TourId, detail?.Tour.Name ?? link.TourName,
            $"https://lichess.org/broadcast/-/{link.TourId}", rounds, current);
    }

    private async Task<LichessBroadcasts.TourDetail?> DetailAsync(string tourId, CancellationToken ct)
    {
        var key = "live-tour:" + tourId;
        if (_cache.TryGetValue(key, out LichessBroadcasts.TourDetail? d)) return d;
        try
        {
            var json = await _source.GetTourAsync(tourId, ct);
            d = json is null ? null : LichessBroadcasts.ParseTourDetail(json);
        }
        catch (Exception e) when (e is LichessRateLimitedException or HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogInformation("Live-Bretter: Übertragung {Tour} nicht gelesen ({Error})", tourId, e.Message);
            // Letzte gute Fassung weiter nutzen (Prod 26.11.2026: Lichess-429 → Rundenliste leer → JEDER Strom 404 „not
            // linked“ → Browser verbinden neu → noch mehr Abrufe). Ohne gute Fassung: nicht cachen, später wieder versuchen.
            return _cache.TryGetValue(StaleKey(tourId), out LichessBroadcasts.TourDetail? stale) ? stale : null;
        }
        _cache.Set(key, d, TourTtl);
        if (d is not null) _cache.Set(StaleKey(tourId), d, StaleTtl);
        return d;
    }

    /// <summary>Lichess-Runden → chess-results-Rundennummern (Zahl im Namen, sonst Reihenfolge; siehe <see cref="LichessBroadcasts.RoundFor"/>).</summary>
    public static List<LiveRoundInfo> RoundsOf(LichessBroadcasts.TourDetail detail)
    {
        var rounds = detail.Rounds;
        var numbers = rounds.Select(r => LichessBroadcasts.RoundNumberOf(r.Name)).ToList();
        var byName = numbers.All(n => n is not null) && numbers.Distinct().Count() == numbers.Count;
        return rounds.Select((r, i) => new LiveRoundInfo(byName ? numbers[i]!.Value : i + 1, r.Id, r.Name, r.StartsAt, r.Ongoing, r.Finished))
            .OrderBy(r => r.Round).ToList();
    }
}
