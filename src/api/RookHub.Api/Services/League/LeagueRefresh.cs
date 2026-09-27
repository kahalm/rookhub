using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Ein Aktualisierungs-Lauf (Knopf „Daten aktualisieren"):
/// 1. laufende Saison je Liga über den Crawler neu holen (art=2/3/16/20) und ersetzen,
/// 2. Ansichten rechnen,
/// 3. chess-results-Partien der wahrscheinlichen Gegner nachladen (älter als <see cref="StaleDays"/> Tage,
///    höchstens <see cref="MaxPlayersPerRun"/> je Lauf — sonst dauert ein Klick eine halbe Stunde),
/// 4. Ansichten erneut rechnen (Partienzahlen haben sich geändert).
/// </summary>
public sealed class LeagueRefresh
{
    public const string CrawlerClient = "LeagueCrawler";
    public const int MaxPlayersPerRun = 40;
    public const double StaleDays = 14;
    public const double MinPlayProbability = 0.15;
    private static readonly HashSet<string> Empty = new(StringComparer.Ordinal) { "Brett nicht besetzt", "spielfrei", "" };
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // Antwort des Crawlers (GET /api/league/{tnr}) — siehe ChessResultsCrawler.DTOs.LeagueDtos
    public sealed record MatchRow(int Round, int? MatchNo, string Home, string Away, double? HomePts, double? AwayPts, string? Date, string? Time, string? Venue);
    public sealed record GameRow(int Round, int MatchNo, int Board, string HomeTeam, string AwayTeam, string HomePlayer, string AwayPlayer,
        string? HomeTitle, string? AwayTitle, string? HomeColor, string Result, double? HomeScore, double? AwayScore, int Forfeit, string? PgnId);
    public sealed record RosterRow(int? StartNr, string? Title, string Name, string? FideId, int? EloI, int? EloN, string? Fed, string Team, int? RosterBoard);
    public sealed record StatsRow(string Team, int? RosterBoard, string Name, double? Points, int? Games, int? EloPerf);
    public sealed record Pages(int Tnr, List<MatchRow> Matches, List<GameRow> Games, Dictionary<int, string?> RoundDates,
        List<RosterRow> Roster, List<StatsRow> Stats);

    private readonly AppDbContext _db;
    private readonly LeagueService _league;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<LeagueRefresh> _log;
    private readonly Func<DateTime> _now;

    public LeagueRefresh(AppDbContext db, LeagueService league, IHttpClientFactory http, ILogger<LeagueRefresh> log, Func<DateTime>? now = null)
    {
        _db = db; _league = league; _http = http; _log = log; _now = now ?? (() => DateTime.UtcNow);
    }

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var season = await _league.CurrentSeasonAsync(ct);
        if (season is null) return "Kein Bestand — zuerst importieren.";
        var client = _http.CreateClient(CrawlerClient);
        var tnrs = await _db.LeagueTournaments.Where(t => t.Season == season).Select(t => t.Tnr).ToListAsync(ct);
        foreach (var tnr in tnrs)
        {
            var pages = await client.GetFromJsonAsync<Pages>($"api/league/{tnr}", Web, ct)
                        ?? throw new InvalidOperationException($"Crawler lieferte nichts für {tnr}");
            await ReplaceAsync(pages, ct);
        }
        await _league.RebuildViewsAsync(ct);
        var stale = await StalePlayersAsync(season, ct);
        var fetched = 0;
        foreach (var fide in stale)
        {
            var pgn = await client.GetStringAsync($"api/league/games/{fide}", ct);
            await MergeGamesAsync(fide, pgn, ct);
            fetched++;
        }
        if (fetched > 0) await _league.RebuildViewsAsync(ct);
        return $"{tnrs.Count} Ligen neu geholt, Partien von {fetched} Spielern nachgeladen";
    }

    /// <summary>Paarungen/Aufstellungen einer Liga ersetzen — samt Abgleich Brettpaarung ↔ Meldeliste (parse.py main).</summary>
    public async Task ReplaceAsync(Pages p, CancellationToken ct)
    {
        var tnr = p.Tnr;
        _db.LeagueRounds.RemoveRange(await _db.LeagueRounds.Where(x => x.Tnr == tnr).ToListAsync(ct));
        _db.LeagueMatches.RemoveRange(await _db.LeagueMatches.Where(x => x.Tnr == tnr).ToListAsync(ct));
        _db.LeagueGames.RemoveRange(await _db.LeagueGames.Where(x => x.Tnr == tnr).ToListAsync(ct));
        _db.LeaguePlayers.RemoveRange(await _db.LeaguePlayers.Where(x => x.Tnr == tnr).ToListAsync(ct));

        var stats = new Dictionary<(string, string), StatsRow>();
        foreach (var s in p.Stats) stats[(s.Team, LeagueNames.NameKey(s.Name))] = s;
        var roster = new Dictionary<(string, string), RosterRow>();
        foreach (var r in p.Roster)
        {
            var key = LeagueNames.NameKey(r.Name);
            roster[(r.Team, key)] = r;
            var st = stats.GetValueOrDefault((r.Team, key));
            _db.LeaguePlayers.Add(new LeaguePlayer
            {
                Tnr = tnr, Team = r.Team, RosterBoard = r.RosterBoard, StartNr = r.StartNr, Title = r.Title, Name = r.Name,
                NameKey = key, FideId = string.IsNullOrEmpty(r.FideId) ? null : r.FideId, EloI = r.EloI, EloN = r.EloN, Fed = r.Fed,
                Points = st?.Points, Games = st?.Games, EloPerf = st?.EloPerf,
            });
        }
        foreach (var (round, date) in p.RoundDates)
            _db.LeagueRounds.Add(new LeagueRound { Tnr = tnr, Round = round, Date = LeagueDates.Parse(date) });
        foreach (var m in p.Matches)
            _db.LeagueMatches.Add(new LeagueMatch
            {
                Tnr = tnr, Round = m.Round, MatchNo = m.MatchNo, Home = m.Home, Away = m.Away, HomePts = m.HomePts, AwayPts = m.AwayPts,
                Date = m.Date, Time = m.Time, Venue = m.Venue is { Length: > 300 } v ? v[..300] : m.Venue,
            });
        foreach (var g in p.Games)
        {
            string? hp = Empty.Contains(g.HomePlayer) ? null : g.HomePlayer;
            string? ap = Empty.Contains(g.AwayPlayer) ? null : g.AwayPlayer;
            var hr = hp is null ? null : roster.GetValueOrDefault((g.HomeTeam, LeagueNames.NameKey(hp)));
            var ar = ap is null ? null : roster.GetValueOrDefault((g.AwayTeam, LeagueNames.NameKey(ap)));
            _db.LeagueGames.Add(new LeagueGame
            {
                Tnr = tnr, Round = g.Round, MatchNo = g.MatchNo, Board = g.Board, HomeTeam = g.HomeTeam, AwayTeam = g.AwayTeam,
                HomePlayer = hp, AwayPlayer = ap, HomeTitle = g.HomeTitle, AwayTitle = g.AwayTitle, HomeColor = g.HomeColor,
                Result = g.Result, HomeScore = g.HomeScore, AwayScore = g.AwayScore, Forfeit = g.Forfeit, PgnId = g.PgnId,
                HomeFide = string.IsNullOrEmpty(hr?.FideId) ? null : hr.FideId, AwayFide = string.IsNullOrEmpty(ar?.FideId) ? null : ar.FideId,
                HomeRb = hr?.RosterBoard, AwayRb = ar?.RosterBoard,
                HomeElo = hr is null ? null : hr.EloI is > 0 ? hr.EloI : hr.EloN,
                AwayElo = ar is null ? null : ar.EloI is > 0 ? ar.EloI : ar.EloN,
            });
        }
        var t = await _db.LeagueTournaments.FindAsync(new object[] { tnr }, ct);
        if (t is not null) t.UpdatedAt = _now();
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>FIDE-IDs der wahrscheinlichen Gegner in offenen Runden, deren chess-results-Partien veraltet sind.</summary>
    public async Task<List<string>> StalePlayersAsync(string season, CancellationToken ct)
    {
        var tnrs = await _db.LeagueTournaments.Where(t => t.Season == season).Select(t => t.Tnr).ToListAsync(ct);
        var best = new Dictionary<string, double>();
        foreach (var json in await _db.LeagueViews.Where(v => tnrs.Contains(v.Tnr)).Select(v => v.Json).ToListAsync(ct))
        {
            var fixtures = JsonNode.Parse(json)?["fixtures"]?.AsObject();
            if (fixtures is null) continue;
            foreach (var (_, fx) in fixtures)
                foreach (var (_, e) in fx!.AsObject())
                {
                    if (e?["status"]?.GetValue<string>() != "open" || e["roster"] is not JsonArray roster) continue;
                    foreach (var r in roster)
                    {
                        var fide = r?["fide"]?.GetValue<string>();
                        var pr = r?["p"]?.GetValue<double>() ?? 0;
                        if (fide is null || pr < MinPlayProbability) continue;
                        best[fide] = Math.Max(best.GetValueOrDefault(fide), pr);
                    }
                }
        }
        var cutoff = _now().AddDays(-StaleDays);
        var fetched = await _db.LeaguePlayerProfiles.Where(x => best.Keys.Contains(x.FideId))
            .ToDictionaryAsync(x => x.FideId, x => x.CrFetchedAt, ct);
        return best.OrderByDescending(kv => kv.Value).Select(kv => kv.Key)
            .Where(f => fetched.GetValueOrDefault(f) is not { } at || at < cutoff)
            .Take(MaxPlayersPerRun).ToList();
    }

    /// <summary>Neu geholte chess-results-Partien in die Spielerkarte einarbeiten.</summary>
    public async Task MergeGamesAsync(string fide, string crPgn, CancellationToken ct)
    {
        var row = await _db.LeaguePlayerProfiles.FindAsync(new object[] { fide }, ct);
        var name = row?.Name;
        if (string.IsNullOrEmpty(name))
            name = await _db.LeaguePlayers.Where(x => x.FideId == fide).OrderByDescending(x => x.Tnr).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "";
        var stored = row is null ? new List<LeagueProfileBuilder.Game>()
            : PgnParser.SplitGameBlocks(row.Pgn).Select(b => new LeagueProfileBuilder.Game(b.Headers, b.Raw.Trim(),
                LeagueProfileBuilder.StoredSource(b.Headers))).ToList();
        var merged = LeagueProfileBuilder.Merge(stored, LeagueProfileBuilder.Parse(crPgn, "chess-results"));
        var (profile, pgn, count) = LeagueProfileBuilder.Build(fide, name, merged);
        if (row is null)
        {
            row = new LeaguePlayerProfile { FideId = fide };
            _db.LeaguePlayerProfiles.Add(row);
        }
        row.Name = name;
        row.GameCount = count;
        row.ProfileJson = profile.ToJsonString();
        row.Pgn = pgn;
        row.CrFetchedAt = _now();
        row.UpdatedAt = _now();
        await _db.SaveChangesAsync(ct);
    }
}
