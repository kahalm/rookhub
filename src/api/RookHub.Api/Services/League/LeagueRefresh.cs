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
    /// <summary>Der eigene Verein (der Nutzer spielt für SK Schwaz): dessen Gegner bekommen die Plätze zuerst —
    /// stale_players.py sortierte genauso („Gegner von Schwaz zuerst, dann nach Einsatzchance").</summary>
    public const string OwnTeam = "Schwaz";
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
    private readonly LigamanagerSource? _ligamanager;

    public LeagueRefresh(AppDbContext db, LeagueService league, IHttpClientFactory http, ILogger<LeagueRefresh> log, Func<DateTime>? now = null,
        LigamanagerSource? ligamanager = null)
    {
        _db = db; _league = league; _http = http; _log = log; _now = now ?? (() => DateTime.UtcNow); _ligamanager = ligamanager;
    }

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var season = await _league.CurrentSeasonAsync(ct);
        if (season is null) return "Kein Bestand — zuerst importieren.";
        var client = _http.CreateClient(CrawlerClient);
        var leagues = await _db.LeagueTournaments.Where(t => t.Season == season)
            .Select(t => new { t.Tnr, t.Source, t.SourceRef }).ToListAsync(ct);
        var tnrs = leagues.Select(t => t.Tnr).ToList();
        // Eine Liga bzw. ein Spieler, der gerade nicht zu holen ist, hält den Rest NICHT auf (parse.py: „FEHLT",
        // weiter). Ohne das bräche jeder Lauf an derselben Stelle ab — die Reihenfolge ist fest —, die Ligen
        // dahinter und alle Spielerkarten blieben für immer stehen, und die Ansichten würden nicht neu gerechnet.
        var failedLeagues = new List<int>();
        foreach (var (tnr, source, sourceRef) in leagues.Select(t => (t.Tnr, t.Source, t.SourceRef)))
        {
            try
            {
                // Ligen aus dem bayerischen Ligamanager (2026-10-07) holt ihr eigener Leser, nicht der chess-results-Crawler —
                // unter ihrer Nummer gibt es bei chess-results ein ganz anderes (oder gar kein) Turnier.
                if (source == LigamanagerSource.Source)
                {
                    if (_ligamanager is null || LigamanagerSource.LeagueRef.Parse(sourceRef) is not { } lref)
                        throw new InvalidOperationException($"Liga {tnr}: Ligamanager-Leser fehlt oder Quelle „{sourceRef}“ unlesbar");
                    // Nummer und Quelle müssen zusammenpassen (Tnr = TnrOffset + Liga-Id) — sonst schriebe der Import unter
                    // einer ANDEREN Nummer und die Liga stünde doppelt da (Altbestand ohne Versatz, siehe LegacyTnrsAsync).
                    if (LigamanagerSource.LigamanagerIdOf(tnr) != lref.Id)
                        throw new InvalidOperationException($"Liga {tnr}: Nummer passt nicht zur Quelle „{sourceRef}“ "
                            + $"(erwartet {LigamanagerSource.TnrOf(lref.Id)}) — neu einspielen und die alte Zeile löschen");
                    await _ligamanager.ImportAsync(lref, dryRun: false, ct, rebuildViews: false);
                    continue;
                }
                var pages = await client.GetFromJsonAsync<Pages>($"api/league/{tnr}", Web, ct)
                            ?? throw new InvalidOperationException($"Crawler lieferte nichts für {tnr}");
                if (pages.Tnr != tnr) throw new InvalidOperationException($"Crawler lieferte Liga {pages.Tnr} statt {tnr}");
                await ReplaceAsync(pages, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "LeagueHub: Liga {Tnr} nicht aktualisiert", tnr);
                failedLeagues.Add(tnr);
            }
            finally
            {
                // Ein gescheitertes SaveChanges ließe seine Löschungen/Zeilen im Tracker — der nächste Aufruf
                // schriebe sie mit. Und die erfolgreichen braucht danach niemand mehr getrackt.
                _db.ChangeTracker.Clear();
            }
        }
        if (tnrs.Count > 0 && failedLeagues.Count == tnrs.Count)
            throw new InvalidOperationException($"Keine Liga zu holen ({string.Join(", ", failedLeagues)}) — ist der Crawler erreichbar?");
        await _league.RebuildViewsAsync(ct);
        var stale = await StalePlayersAsync(season, ct);
        int fetched = 0, failedPlayers = 0;
        foreach (var fide in stale)
        {
            try
            {
                var pgn = await client.GetStringAsync($"api/league/games/{fide}", ct);
                await MergeGamesAsync(fide, pgn, ct);
                fetched++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "LeagueHub: Partien von {Fide} nicht nachgeladen", fide);
                failedPlayers++;
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
        if (fetched > 0) await _league.RebuildViewsAsync(ct);
        var msg = $"{tnrs.Count - failedLeagues.Count} Ligen neu geholt, Partien von {fetched} Spielern nachgeladen";
        if (failedLeagues.Count > 0) msg += $"; nicht aktualisiert: Liga {string.Join(", ", failedLeagues)}";
        if (failedPlayers > 0) msg += $"; {failedPlayers} Spieler ohne Partien";
        return msg;
    }

    /// <summary>Paarungen/Aufstellungen einer Liga ersetzen — samt Abgleich Brettpaarung ↔ Meldeliste (parse.py main).</summary>
    public Task ReplaceAsync(Pages p, CancellationToken ct) => ReplaceAsync(_db, p, _now(), ct);

    /// <summary>Wie oben, ohne Dienst — auch für andere Quellen (<see cref="LigamanagerSource"/>), die dieselben Zwischenformen
    /// liefern. Speichert selbst (ein <c>SaveChanges</c>; in einer offenen Transaktion des Aufrufers läuft es darin).</summary>
    public static async Task ReplaceAsync(AppDbContext db, Pages p, DateTime now, CancellationToken ct)
    {
        var tnr = p.Tnr;
        // Vier leere Seiten sind keine Liga, sondern eine Fehl- oder Drosselseite von chess-results (200, aber ohne
        // Tabellen) — ersetzt würde damit der ganze Bestand der Liga durch nichts.
        if (p.Matches.Count == 0 && p.Games.Count == 0 && p.Roster.Count == 0)
            throw new InvalidOperationException($"Crawler lieferte für Liga {tnr} leere Seiten — der Bestand bleibt.");
        // Dasselbe gilt je Seite: der Crawler holt die vier nacheinander, eine Drosselseite mitten im Lauf kommt als
        // leere Liste an — ersetzt würde z. B. die ganze Meldeliste durch nichts (keine Prognosen, Teilen-Links 404).
        // Leer bleiben darf eine Seite nur, wenn der Bestand der Liga dafür ebenfalls leer ist (Saisonbeginn).
        var lost = new List<string>();
        if (p.Matches.Count == 0 && await db.LeagueMatches.AnyAsync(x => x.Tnr == tnr, ct)) lost.Add("art=2");
        if (p.Games.Count == 0 && await db.LeagueGames.AnyAsync(x => x.Tnr == tnr, ct)) lost.Add("art=3");
        if (p.Roster.Count == 0 && await db.LeaguePlayers.AnyAsync(x => x.Tnr == tnr, ct)) lost.Add("art=16");
        if (p.Stats.Count == 0 && await db.LeaguePlayers.AnyAsync(x => x.Tnr == tnr && (x.Points != null || x.Games != null || x.EloPerf != null), ct))
            lost.Add("art=20");
        if (lost.Count > 0)
            throw new InvalidOperationException($"Crawler lieferte für Liga {tnr} leere Seite(n) {string.Join(", ", lost)} bei vorhandenem Bestand — der Bestand bleibt.");
        db.LeagueRounds.RemoveRange(await db.LeagueRounds.Where(x => x.Tnr == tnr).ToListAsync(ct));
        db.LeagueMatches.RemoveRange(await db.LeagueMatches.Where(x => x.Tnr == tnr).ToListAsync(ct));
        db.LeagueGames.RemoveRange(await db.LeagueGames.Where(x => x.Tnr == tnr).ToListAsync(ct));
        db.LeaguePlayers.RemoveRange(await db.LeaguePlayers.Where(x => x.Tnr == tnr).ToListAsync(ct));

        var stats = new Dictionary<(string, string), StatsRow>();
        foreach (var s in p.Stats) stats[(s.Team, LeagueNames.NameKey(s.Name))] = s;
        var roster = new Dictionary<(string, string), RosterRow>();
        foreach (var r in p.Roster)
        {
            var key = LeagueNames.NameKey(r.Name);
            roster[(r.Team, key)] = r;
            var st = stats.GetValueOrDefault((r.Team, key));
            db.LeaguePlayers.Add(new LeaguePlayer
            {
                Tnr = tnr, Team = r.Team, RosterBoard = r.RosterBoard, StartNr = r.StartNr, Title = r.Title, Name = r.Name,
                NameKey = key, FideId = string.IsNullOrEmpty(r.FideId) ? null : r.FideId, EloI = r.EloI, EloN = r.EloN, Fed = r.Fed,
                Points = st?.Points, Games = st?.Games, EloPerf = st?.EloPerf,
            });
        }
        foreach (var (round, date) in p.RoundDates)
            db.LeagueRounds.Add(new LeagueRound { Tnr = tnr, Round = round, Date = LeagueDates.Parse(date) });
        foreach (var m in p.Matches)
            db.LeagueMatches.Add(new LeagueMatch
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
            db.LeagueGames.Add(new LeagueGame
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
        var t = await db.LeagueTournaments.FindAsync(new object[] { tnr }, ct);
        if (t is not null) t.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>FIDE-IDs der wahrscheinlichen Gegner in offenen Runden, deren chess-results-Partien veraltet sind —
    /// Gegner des eigenen Vereins zuerst, dann nach Einsatzchance.</summary>
    public async Task<List<string>> StalePlayersAsync(string season, CancellationToken ct)
    {
        var tnrs = await _db.LeagueTournaments.Where(t => t.Season == season).Select(t => t.Tnr).ToListAsync(ct);
        // Rang je Spieler: (0 = Gegner des eigenen Vereins, sonst 1; dann höchste Einsatzchance) — der kleinste zählt.
        var best = new Dictionary<string, (int Own, double P)>();
        foreach (var json in await _db.LeagueViews.Where(v => tnrs.Contains(v.Tnr)).Select(v => v.Json).ToListAsync(ct))
        {
            var fixtures = JsonNode.Parse(json)?["fixtures"]?.AsObject();
            if (fixtures is null) continue;
            foreach (var (team, fx) in fixtures)
                foreach (var (_, e) in fx!.AsObject())
                {
                    if (e?["status"]?.GetValue<string>() != "open" || e["roster"] is not JsonArray roster) continue;
                    var own = team == OwnTeam ? 0 : 1;
                    foreach (var r in roster)
                    {
                        var fide = r?["fide"]?.GetValue<string>();
                        var pr = r?["p"]?.GetValue<double>() ?? 0;
                        if (fide is null || pr < MinPlayProbability) continue;
                        if (!best.TryGetValue(fide, out var cur) || own < cur.Own || (own == cur.Own && pr > cur.P))
                            best[fide] = (own, pr);
                    }
                }
        }
        var cutoff = _now().AddDays(-StaleDays);
        var ids = best.Keys.ToList();
        // Projektion: sonst käme je Kandidat das ganze Pgn mit (die Selektoren laufen erst im Client).
        var fetched = await _db.LeaguePlayerProfiles.Where(x => ids.Contains(x.FideId))
            .Select(x => new { x.FideId, x.CrFetchedAt })
            .ToDictionaryAsync(x => x.FideId, x => x.CrFetchedAt, ct);
        return best.OrderBy(kv => kv.Value.Own).ThenByDescending(kv => kv.Value.P).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .Where(f => fetched.GetValueOrDefault(f) is not { } at || at < cutoff)
            .Take(MaxPlayersPerRun).ToList();
    }

    /// <summary>Neu geholte chess-results-Partien in die Spielerkarte einarbeiten (Vereinspartien bleiben dabei —
    /// <see cref="LeagueProfileStore"/>).</summary>
    public async Task MergeGamesAsync(string fide, string crPgn, CancellationToken ct)
    {
        var now = _now();
        await new LeagueProfileStore(_db).RebuildAsync(fide, ct, LeagueProfileBuilder.Parse(crPgn, "chess-results"), now, now);
        await _db.SaveChangesAsync(ct);
    }
}
