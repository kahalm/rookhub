using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Welche Liga-Partie der Stapel als Nächstes rechnet (0.665.0, Wunsch 2026-10-05: „analysier im Hintergrund auch alle
/// Partien für LeagueHub — zumindest von Spielern, die aktuell in der Liga mitspielen. Erst die neuesten Partien (2 Jahre
/// zurück), dann immer bevorzugt die Gegner von Schwaz nächste Runde, dann der Rest, und wenn das alles fertig ist erst
/// wieder die Meisterpartien").
/// <list type="bullet">
/// <item>Quelle: die Profile (<see cref="LeaguePlayerProfile.Pgn"/>: Lumbra, Megabase, chess-results, Übertragungen) aller
/// Spieler mit FIDE-ID auf einer Meldeliste der laufenden Saison; nur Partien der letzten <see cref="Years"/> Jahre
/// (Datum im Kopf, ein Jahr allein reicht), nur aus der Grundstellung.</item>
/// <item>Reihenfolge: erst die Spieler der Gegner von Schwaz in der nächsten noch nicht gespielten Runde (je Schwazer
/// Mannschaft), dann alle übrigen — jeweils die neueste Partie zuerst.</item>
/// <item>Dieselbe Partie in zwei Profilen (zwei Ligaspieler gegeneinander) zählt einmal; eine schon gerechnete (eigene
/// Liga-Analyse oder die Analyse einer Vereinspartie mit denselben Zügen) wird übersprungen.</item>
/// </list>
/// Die Liste lebt im Speicher und wird alle <see cref="Refresh"/> neu gebaut (die nächste Runde rückt vor, neue Partien
/// kommen in die Profile).
/// </summary>
public sealed class LeagueAnalysisQueue
{
    public const int Years = 2;
    public static readonly TimeSpan Refresh = TimeSpan.FromHours(1);

    public sealed record Item(string Pgn, string Key, bool Opponent, DateOnly Date);

    private readonly object _gate = new();
    private List<Item> _items = new();
    private DateTime _builtAt = DateTime.MinValue;
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);

    /// <summary>Eine Partie, die sich nicht rechnen ließ (unspielbares PGN), nicht noch einmal anbieten.</summary>
    public void Skip(string key) { lock (_gate) _done.Add(key); }

    /// <summary>Die nächste noch nicht gerechnete Liga-Partie samt ihrem Zug-Schlüssel (kanonische SAN) — oder <c>null</c>.</summary>
    public async Task<(Item Item, string MovesHash)?> NextAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        List<Item> items;
        lock (_gate)
        {
            items = _items;
            if (now - _builtAt < Refresh && items.Count > 0) goto pick;
        }
        items = await BuildAsync(db, now, ct);
        lock (_gate) { _items = items; _builtAt = now; }
    pick:
        foreach (var it in items)
        {
            lock (_gate) if (_done.Contains(it.Key)) continue;
            var plies = GamePlies.Parse(it.Pgn, GameAnalysisDefaults.MaxPlies);
            if (plies is not { } p || p.Plies.Count == 0) { Skip(it.Key); continue; }
            var hash = LeagueClubService.HashOf(p.Plies.Select(x => x.San).ToList());
            var analyzed = await db.GameAnalyses.AnyAsync(a => a.MovesHash == hash && a.Status != GameAnalysisStatus.Failed, ct)
                || await db.GameAnalyses.AnyAsync(a => a.Origin == GameAnalysisOrigin.Club && a.Status != GameAnalysisStatus.Failed
                    && db.LeagueClubGames.Any(c => c.Id == a.LeagueClubGameId && c.MovesHash == hash), ct);
            Skip(it.Key);   // so oder so erledigt: gleich eingereiht oder schon gerechnet
            if (!analyzed) return (it, hash);
        }
        return null;
    }

    /// <summary>Die geordnete Liste bauen (siehe Klassenkommentar).</summary>
    internal static async Task<List<Item>> BuildAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var season = await db.LeagueTournaments.AsNoTracking().MaxAsync(t => (string?)t.Season, ct);
        if (season == null) return new();
        var tnrs = await db.LeagueTournaments.AsNoTracking().Where(t => t.Season == season && t.Stage == "Liga")
            .Select(t => t.Tnr).ToListAsync(ct);
        var players = await db.LeaguePlayers.AsNoTracking().Where(p => tnrs.Contains(p.Tnr) && p.FideId != null && p.FideId != "")
            .Select(p => new { p.Tnr, p.Team, p.FideId }).ToListAsync(ct);
        var opponents = await NextRoundOpponentFidesAsync(db, tnrs, players.Select(p => (p.Tnr, p.Team, p.FideId!)).ToList(), ct);
        var fides = players.Select(p => p.FideId!).Distinct().ToList();

        var cutoff = DateOnly.FromDateTime(now).AddYears(-Years);
        var items = new List<Item>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in fides.Chunk(100))
        {
            var profiles = await db.LeaguePlayerProfiles.AsNoTracking().Where(p => chunk.Contains(p.FideId))
                .Select(p => new { p.FideId, p.Pgn }).ToListAsync(ct);
            foreach (var prof in profiles)
                foreach (var (headers, moveText) in PgnParser.SplitGames(prof.Pgn ?? string.Empty))
                {
                    if (headers.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)) continue;   // Stellungspartie
                    if (DateOf(headers.GetValueOrDefault("Date")) is not { } date || date < cutoff) continue;
                    var sans = PgnParser.ExtractMainlineSans(moveText);
                    if (sans.Count < 2) continue;
                    var key = LeagueClubService.HashOf(sans);
                    if (!seen.Add(key)) continue;
                    items.Add(new Item(PgnOf(headers, moveText), key, opponents.Contains(prof.FideId), date));
                }
        }
        return items.OrderByDescending(i => i.Opponent).ThenByDescending(i => i.Date).ToList();
    }

    /// <summary>
    /// Die FIDE-IDs der Gegner von Schwaz in der nächsten Runde: je Schwazer Mannschaft die kleinste Runde ihrer Begegnungen
    /// ohne Ergebnis, dort der Gegner und dessen Meldeliste.
    /// </summary>
    internal static async Task<HashSet<string>> NextRoundOpponentFidesAsync(AppDbContext db, List<int> tnrs,
        List<(int Tnr, string Team, string Fide)> players, CancellationToken ct)
    {
        var own = LeagueRefresh.OwnTeam;
        var open = await db.LeagueMatches.AsNoTracking()
            .Where(m => tnrs.Contains(m.Tnr) && m.HomePts == null && m.AwayPts == null
                && (m.Home.StartsWith(own) || m.Away.StartsWith(own)))
            .Select(m => new { m.Tnr, m.Round, m.Home, m.Away }).ToListAsync(ct);
        var opponentTeams = open
            .GroupBy(m => (m.Tnr, Team: m.Home.StartsWith(own) ? m.Home : m.Away))
            .Select(g => g.OrderBy(m => m.Round).First())
            .Select(m => (m.Tnr, Opp: m.Home.StartsWith(own) ? m.Away : m.Home))
            .ToHashSet();
        return players.Where(p => opponentTeams.Contains((p.Tnr, p.Team))).Select(p => p.Fide).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>„2025.03.14" → Datum; „2025.??.??" → 1. Jänner 2025; sonst <c>null</c>.</summary>
    internal static DateOnly? DateOf(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var parts = date.Trim().Split('.');
        if (!int.TryParse(parts[0], out var y) || y < 1900) return null;
        var m = parts.Length > 1 && int.TryParse(parts[1], out var mm) && mm is >= 1 and <= 12 ? mm : 1;
        var d = parts.Length > 2 && int.TryParse(parts[2], out var dd) && dd >= 1 && dd <= DateTime.DaysInMonth(y, m) ? dd : 1;
        return new DateOnly(y, m, d);
    }

    private static string PgnOf(Dictionary<string, string> headers, string moveText)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in headers) sb.Append('[').Append(k).Append(" \"").Append(v.Replace("\"", "")).Append("\"]\n");
        sb.Append('\n').Append(moveText.Trim()).Append('\n');
        return sb.ToString();
    }
}
