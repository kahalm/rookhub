using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Vorläufige Zuordnung einer Vereinspartie zur LAUFENDEN Runde (0.832.0, Wunsch 2026-10-24: „er sollte direkt nach dem
/// Einlesen, wenn er es zuordnen kann, die Matchings für die aktuelle Runde schreiben — und später nur bestätigen"). Anlass:
/// zehn Formulare vom Spieltag (Runde 3) — die Bretter standen bei chess-results noch ohne Spieler, die FIDE-Suche
/// (<see cref="LeaguePairingFinder"/>) fand nichts bzw. eine alte Runde des Gegners.
/// <list type="number">
/// <item><b>Begegnung</b>: die Mannschaft des Gegners laut Meldeliste (FIDE-ID, sonst Name) gegen eine eigene Mannschaft
/// (<see cref="LeagueClub.OwnsTeam"/>) in einer Runde mit leeren Brettern; mit Tag die zeitlich nächste (±<see
/// cref="LeaguePairingFinder.DayTolerance"/>), ohne Tag die jüngste Runde ≤ heute. Ist der Gegner unbekannt, reicht der eigene
/// Spieler in der Meldeliste der eigenen Mannschaft. Mehrere gleich gute Begegnungen → keine Zuordnung.</item>
/// <item><b>Brett</b>: die Farbe (Heim hat Weiß an den ungeraden Brettern — Tirol, in den gespielten Runden der TMM 2026/27
/// ausnahmslos so; Bayern an den geraden, <see cref="LeaguePairingFinder.HomeWhiteByRule"/>) und die Reihenfolge der
/// Meldeliste: die übrigen schon zugeordneten Partien derselben Begegnung grenzen das Brett ein (ein Spieler mit kleinerer
/// Meldenummer sitzt weiter vorn — auf beiden Seiten); bleibt mehr als ein Brett, gilt die Schätzung aus den bisherigen Runden
/// (wie viele Mannschaftskollegen mit kleinerer Meldenummer im Schnitt spielten) und das Brett ist <c>Uncertain</c>.</item>
/// <item><b>Gespeichert</b> über <see cref="LeagueGameLinks.Set"/> (Schlüssel Tnr/Runde/Begegnung/Brett) mit
/// <see cref="LeagueClubGame.LeaguePairingProvisional"/>; nach jedem Speichern werden ALLE vorläufigen Partien der Begegnung
/// gemeinsam neu verteilt (<see cref="RebalanceAsync"/>) — mit jeder weiteren Partie wird die Reihenfolge genauer.</item>
/// <item><b>Bestätigen</b> (<see cref="ConfirmAsync"/>, nach jedem Ersetzen einer Liga): stehen am Brett jetzt Spieler und passen
/// sie samt Farben → bestätigt; sitzt der Gegner an einem anderen Brett derselben Begegnung → umgehängt und bestätigt; sonst
/// gelöst (Warnung).</item>
/// </list>
/// </summary>
public sealed class LeagueProvisionalPairing(AppDbContext db)
{
    /// <summary>Ein leeres Brett einer eigenen Begegnung samt Liga-Angaben.</summary>
    public sealed record Open(LeagueGame G, string Season, string League, DateOnly? Date, string? Source);

    /// <summary>Die vorläufige Wahl: Brettpaarung + ob das Brett nur geschätzt ist.</summary>
    public sealed record Pick(int Id, bool Uncertain);

    /// <summary>Was über eine Partie bekannt ist: welche Farbe der eigene Verein hatte, der eigene Spieler (FIDE-ID, auch die
    /// interne hinter „Schwaz"), der Gegner (FIDE-ID und Namensschlüssel), Tag bzw. Jahr; <paramref name="SelfId"/> = die
    /// Vereinspartie selbst (zählt nicht als „schon zugeordnet").</summary>
    public sealed record Game(bool? OwnWhite, string? OwnFide, string? OppFide, string? OppKey, DateOnly? Date, int? Year, int? SelfId = null);

    internal sealed record RosterEntry(string Team, int? Rb, string? Fide, string NameKey);

    /// <summary>Eine schon zugeordnete Vereinspartie einer der Begegnungen.</summary>
    internal sealed record Placed(LeagueClubGame C, Game Info);

    private readonly record struct MatchKey(int Tnr, int Round, int MatchNo);

    /// <summary>Alles für die Wahl auf einmal (eine Übersicht mit vielen Partien fragt nur einmal).</summary>
    public sealed class Context
    {
        internal List<Open> Open { get; init; } = new();
        internal Dictionary<int, List<RosterEntry>> Roster { get; init; } = new();
        /// <summary>Je (Tnr, Mannschaft): je bisher gespielter Runde die Meldenummern der Eingesetzten.</summary>
        internal Dictionary<(int Tnr, string Team), List<(int Round, List<int> Rbs)>> History { get; init; } = new();
        internal List<Placed> Placed { get; init; } = new();
        internal HashSet<string> OwnFides { get; init; } = new();
        internal DateOnly Today { get; init; }
    }

    /// <summary>Wie <see cref="Game"/> aus den Seiten einer Partie: die eigene Seite ist die anonymisierte („Schwaz"), sonst die
    /// mit der FIDE-ID eines eigenen Spielers (nur wenn genau eine Seite passt).</summary>
    public static Game Describe(LeagueClub club, IReadOnlySet<string> ownFides, string white, string? whiteFide, string black,
        string? blackFide, DateOnly? date, int? year, int? selfId = null)
    {
        bool Own(string? f) => !string.IsNullOrEmpty(f) && ownFides.Contains(f);
        bool? ownWhite = club.IsAnon(white) ? true : club.IsAnon(black) ? false
            : Own(whiteFide) && !Own(blackFide) ? true : Own(blackFide) && !Own(whiteFide) ? false : null;
        if (ownWhite is not { } ow) return new Game(null, null, null, null, date, year, selfId);
        var (ownFide, oppName, oppFide) = ow ? (whiteFide, black, blackFide) : (blackFide, white, whiteFide);
        var oppKey = string.IsNullOrWhiteSpace(oppName) || club.IsAnon(oppName) ? null : LeagueNames.NameKey(oppName);
        return new Game(ow, Blank(ownFide), Blank(oppFide), string.IsNullOrEmpty(oppKey) ? null : oppKey, date, year, selfId);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Meldelisten, bisherige Runden und schon zugeordnete Vereinspartien zu den leeren Brettern <paramref name="open"/>.</summary>
    public async Task<Context> LoadAsync(LeagueClub club, List<Open> open, IReadOnlySet<string> ownFides, DateOnly today, CancellationToken ct)
    {
        var tnrs = open.Select(o => o.G.Tnr).Distinct().ToList();
        if (tnrs.Count == 0) return new Context { Today = today };
        var roster = await db.LeaguePlayers.AsNoTracking().Where(p => tnrs.Contains(p.Tnr))
            .Select(p => new { p.Tnr, p.Team, p.RosterBoard, p.FideId, p.NameKey }).ToListAsync(ct);
        var played = await db.LeagueGames.AsNoTracking()
            .Where(g => tnrs.Contains(g.Tnr) && g.Forfeit == 0 && (g.HomeRb != null || g.AwayRb != null))
            .Select(g => new { g.Tnr, g.Round, g.HomeTeam, g.AwayTeam, g.HomeRb, g.AwayRb }).ToListAsync(ct);
        var history = played.SelectMany(g => new[] { (g.Tnr, Team: g.HomeTeam, g.Round, Rb: g.HomeRb), (g.Tnr, Team: g.AwayTeam, g.Round, Rb: g.AwayRb) })
            .Where(x => x.Rb != null)
            .GroupBy(x => (x.Tnr, x.Team))
            .ToDictionary(x => x.Key, x => x.GroupBy(y => y.Round).Select(y => (y.Key, y.Select(z => z.Rb!.Value).ToList())).ToList());
        var matches = open.Select(o => new MatchKey(o.G.Tnr, o.G.Round, o.G.MatchNo)).ToHashSet();
        var rounds = matches.Select(m => m.Round).Distinct().ToList();
        var linked = await db.LeagueClubGames.AsNoTracking()
            .Where(c => c.ClubId == club.Id && c.LeagueTnr != null && tnrs.Contains(c.LeagueTnr.Value) && c.LeagueRound != null
                        && rounds.Contains(c.LeagueRound.Value) && c.LeagueMatchNo != null && c.LeagueBoard != null)
            .ToListAsync(ct);
        var own = ownFides.ToHashSet();
        foreach (var p in roster.Where(p => p.FideId != null && club.OwnsTeam(p.Team))) own.Add(p.FideId!);
        return new Context
        {
            Open = open,
            Roster = roster.GroupBy(p => p.Tnr).ToDictionary(x => x.Key,
                x => x.Select(p => new RosterEntry(p.Team, p.RosterBoard, p.FideId, p.NameKey)).ToList()),
            History = history,
            Placed = linked.Where(c => matches.Contains(new MatchKey(c.LeagueTnr!.Value, c.LeagueRound!.Value, c.LeagueMatchNo!.Value)))
                .Select(c => new Placed(c, Describe(club, own, c.White, c.WhiteFide ?? c.WhiteRealFide, c.Black, c.BlackFide ?? c.BlackRealFide,
                    null, c.Year, c.Id))).ToList(),
            OwnFides = own,
            Today = today,
        };
    }

    /// <summary>Das wahrscheinlichste leere Brett für <paramref name="q"/> — <c>null</c>, wenn die eigene Seite, die Begegnung
    /// oder die Farbe nicht feststeht.</summary>
    public static Pick? Suggest(Context ctx, LeagueClub club, Game q)
    {
        if (q.OwnWhite is null || ctx.Open.Count == 0) return null;
        if (MatchOf(ctx, club, q) is not { } m) return null;
        var boards = ctx.Open.Where(o => Key(o.G) == m).ToList();
        var others = ctx.Placed.Where(p => p.C.Id != q.SelfId && MatchKeyOf(p.C) == m).ToList();
        var placed = Place(ctx, club, boards, new List<Game> { q },
            others.Select(p => (p.Info, p.C.LeagueBoard!.Value)).ToList());
        return placed.TryGetValue(0, out var r) ? new Pick(r.Id, r.Uncertain) : null;
    }

    private static MatchKey Key(LeagueGame g) => new(g.Tnr, g.Round, g.MatchNo);

    private static MatchKey? MatchKeyOf(LeagueClubGame c) =>
        c is { LeagueTnr: { } t, LeagueRound: { } r, LeagueMatchNo: { } m } ? new MatchKey(t, r, m) : null;

    /// <summary>Die Begegnung: Gegner in der Meldeliste der gegnerischen Mannschaft (sonst der eigene Spieler in der eigenen),
    /// Termin passend; die beste eindeutig, sonst keine.</summary>
    private static MatchKey? MatchOf(Context ctx, LeagueClub club, Game q)
    {
        var scored = new List<(MatchKey M, bool Opp, bool Own, int Distance)>();
        foreach (var m in ctx.Open.GroupBy(o => Key(o.G)))
        {
            var first = m.First();
            var homeOwn = club.OwnsTeam(first.G.HomeTeam);
            if (homeOwn == club.OwnsTeam(first.G.AwayTeam)) continue;   // keine eigene Mannschaft bzw. vereinsintern
            var (ownTeam, oppTeam) = homeOwn ? (first.G.HomeTeam, first.G.AwayTeam) : (first.G.AwayTeam, first.G.HomeTeam);
            var roster = ctx.Roster.GetValueOrDefault(m.Key.Tnr) ?? new();
            var opp = Find(roster, oppTeam, q.OppFide, q.OppKey) != null;
            var own = q.OwnFide != null && roster.Any(r => r.Team == ownTeam && r.Fide == q.OwnFide);
            if (!opp && !own) continue;
            if (first.Date is not { } date) continue;
            int distance;
            if (q.Date is { } d)
            {
                distance = Math.Abs(date.DayNumber - d.DayNumber);
                if (distance > LeaguePairingFinder.DayTolerance) continue;
            }
            else
            {
                if (date > ctx.Today) continue;
                if (q.Year is { } y && !LeaguePairingFinder.InSeason(first.Season, y)) continue;
                distance = ctx.Today.DayNumber - date.DayNumber;
            }
            scored.Add((m.Key, opp, own, distance));
        }
        var best = scored.OrderByDescending(s => s.Opp).ThenByDescending(s => s.Own).ThenBy(s => s.Distance).ToList();
        if (best.Count == 0) return null;
        if (best.Count > 1 && (best[1].Opp, best[1].Own, best[1].Distance) == (best[0].Opp, best[0].Own, best[0].Distance)) return null;
        return best[0].M;
    }

    private static RosterEntry? Find(List<RosterEntry> roster, string team, string? fide, string? nameKey) =>
        (fide is null ? null : roster.FirstOrDefault(r => r.Team == team && r.Fide == fide))
        ?? (nameKey is null ? null : roster.FirstOrDefault(r => r.Team == team && r.NameKey == nameKey));

    /// <summary>
    /// Bretter verteilen: <paramref name="movable"/> bekommen je ein Brett (Schlüssel = ihr Index), <paramref name="fixedItems"/>
    /// stehen schon (Brett belegt, ihre Meldenummern grenzen ein). Je Partie die Bretter in ihrer Farbe, eingegrenzt durch die
    /// Reihenfolge der Meldeliste gegenüber allen anderen; die mit den wenigsten Möglichkeiten zuerst; unter mehreren das, das der
    /// Schätzung aus den bisherigen Runden am nächsten liegt.
    /// </summary>
    private static Dictionary<int, (int Id, int Board, bool Uncertain)> Place(Context ctx, LeagueClub club, List<Open> boards,
        List<Game> movable, List<(Game Info, int Board)> fixedItems)
    {
        var result = new Dictionary<int, (int Id, int Board, bool Uncertain)>();
        if (boards.Count == 0) return result;
        var g0 = boards[0].G;
        var homeOwn = club.OwnsTeam(g0.HomeTeam);
        var (ownTeam, oppTeam) = homeOwn ? (g0.HomeTeam, g0.AwayTeam) : (g0.AwayTeam, g0.HomeTeam);
        var roster = ctx.Roster.GetValueOrDefault(g0.Tnr) ?? new();
        var byBoard = boards.GroupBy(b => b.G.Board).ToDictionary(x => x.Key, x => x.First().G.Id);
        var all = byBoard.Keys.OrderBy(b => b).ToList();
        var n = all.Max();
        bool OwnWhiteAt(int board) => LeaguePairingFinder.HomeWhiteByRule(boards[0].Source, board) == homeOwn;
        int? OwnRank(Game x) => x.OwnFide is null ? null : roster.FirstOrDefault(r => r.Team == ownTeam && r.Fide == x.OwnFide)?.Rb;
        int? OppRank(Game x) => Find(roster, oppTeam, x.OppFide, x.OppKey)?.Rb;
        double? Estimate(string team, int? rank)
        {
            if (rank is not { } r) return null;
            var past = (ctx.History.GetValueOrDefault((g0.Tnr, team)) ?? new()).Where(h => h.Round < g0.Round).ToList();
            return past.Count == 0 ? r : 1 + past.Average(h => h.Rbs.Count(x => x < r));
        }

        var items = movable.Select((m, i) => (Index: i, Game: m, Own: OwnRank(m), Opp: OppRank(m))).ToList();
        var fixedRanks = fixedItems.Select(f => (Own: OwnRank(f.Info), Opp: OppRank(f.Info), f.Board)).ToList();
        var taken = fixedItems.Select(f => f.Board).ToHashSet();
        var plans = new List<(int Index, Game Game, List<int> Range, List<int> Colored, double? EOwn, double? EOpp, int? Own)>();
        foreach (var it in items)
        {
            int lo = 1, hi = n;
            void Bound(int? mine, int? other, int? board)
            {
                if (mine is not { } a || other is not { } b || a == b) return;
                if (b < a) lo = Math.Max(lo, board is { } bb ? bb + 1 : lo + 1);
                else hi = Math.Min(hi, board is { } bb ? bb - 1 : hi - 1);
            }
            // erst die festen (Brett bekannt), dann die anderen beweglichen (nur die Zahl zählt)
            foreach (var f in fixedRanks) { Bound(it.Own, f.Own, f.Board); Bound(it.Opp, f.Opp, f.Board); }
            int lowerOwn = 0, higherOwn = 0, lowerOpp = 0, higherOpp = 0;
            foreach (var o in items.Where(o => o.Index != it.Index))
            {
                if (it.Own is { } a && o.Own is { } b && a != b) { if (b < a) lowerOwn++; else higherOwn++; }
                if (it.Opp is { } c && o.Opp is { } d && c != d) { if (d < c) lowerOpp++; else higherOpp++; }
            }
            var lowerFixedOwn = fixedRanks.Count(f => it.Own is { } a && f.Own is { } b && b < a);
            var higherFixedOwn = fixedRanks.Count(f => it.Own is { } a && f.Own is { } b && b > a);
            var lowerFixedOpp = fixedRanks.Count(f => it.Opp is { } a && f.Opp is { } b && b < a);
            var higherFixedOpp = fixedRanks.Count(f => it.Opp is { } a && f.Opp is { } b && b > a);
            lo = Math.Max(lo, 1 + Math.Max(lowerOwn + lowerFixedOwn, lowerOpp + lowerFixedOpp));
            hi = Math.Min(hi, n - Math.Max(higherOwn + higherFixedOwn, higherOpp + higherFixedOpp));
            var colored = all.Where(b => it.Game.OwnWhite is not { } w || OwnWhiteAt(b) == w).ToList();
            var range = colored.Where(b => b >= lo && b <= hi).ToList();
            plans.Add((it.Index, it.Game, range, colored, Estimate(ownTeam, it.Own), Estimate(oppTeam, it.Opp), it.Own));
        }
        foreach (var p in plans.OrderBy(p => p.Range.Count(b => !taken.Contains(b))).ThenBy(p => p.Own ?? int.MaxValue).ThenBy(p => p.Index))
        {
            var free = p.Range.Where(b => !taken.Contains(b)).ToList();
            var relaxed = free.Count == 0;
            if (relaxed) free = p.Colored.Where(b => !taken.Contains(b)).ToList();
            if (free.Count == 0) continue;   // alle Bretter dieser Farbe vergeben — lieber keine als eine doppelte
            double Score(int b) => (p.EOwn is { } e1 ? Math.Abs(b - e1) : 0) + (p.EOpp is { } e2 ? Math.Abs(b - e2) : 0);
            var board = free.OrderBy(Score).ThenBy(b => b).First();
            taken.Add(board);
            result[p.Index] = (byBoard[board], board, relaxed || free.Count > 1);
        }
        return result;
    }

    // ── nach dem Speichern: alle vorläufigen einer Begegnung gemeinsam neu verteilen ─────────────────────────

    /// <summary>
    /// Die vorläufigen Partien der Begegnungen von <paramref name="games"/> (nur Partien mit vorläufiger Zuordnung zählen)
    /// gemeinsam neu verteilen — die bestätigten bleiben stehen. Speichert selbst. → umgehängte Partien.
    /// </summary>
    public async Task<int> RebalanceAsync(LeagueClub club, IEnumerable<LeagueClubGame> games, DateOnly today, CancellationToken ct,
        ILogger? log = null)
    {
        var keys = games.Where(g => g.LeaguePairingProvisional).Select(MatchKeyOf).OfType<MatchKey>().Distinct().ToList();
        if (keys.Count == 0) return 0;
        var tnrs = keys.Select(k => k.Tnr).Distinct().ToList();
        var rounds = keys.Select(k => k.Round).Distinct().ToList();
        var rows = await (from g in db.LeagueGames.AsNoTracking()
                          join t in db.LeagueTournaments.AsNoTracking() on g.Tnr equals t.Tnr
                          join r in db.LeagueRounds.AsNoTracking() on new { g.Tnr, g.Round } equals new { r.Tnr, r.Round } into rs
                          from r in rs.DefaultIfEmpty()
                          where tnrs.Contains(g.Tnr) && rounds.Contains(g.Round)
                          select new { g, t.Season, t.League, t.Grp, t.Source, Date = r == null ? null : r.Date }).ToListAsync(ct);
        var want = keys.ToHashSet();
        var open = rows.Where(x => want.Contains(Key(x.g)) && LeagueGameLinks.IsOpen(x.g))
            .Select(x => new Open(x.g, x.Season, string.IsNullOrEmpty(x.Grp) ? x.League : $"{x.League} {x.Grp}", x.Date, x.Source)).ToList();
        var ctx = await LoadAsync(club, open, new HashSet<string>(), today, ct);
        var placedIds = ctx.Placed.Select(p => p.C.Id).ToList();
        var tracked = await db.LeagueClubGames.Where(c => placedIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var moved = 0;
        foreach (var m in keys)
        {
            var boards = open.Where(o => Key(o.G) == m).ToList();
            var here = ctx.Placed.Where(p => MatchKeyOf(p.C) == m).ToList();
            var mov = here.Where(p => p.C.LeaguePairingProvisional).ToList();
            var fix = here.Where(p => !p.C.LeaguePairingProvisional).Select(p => (p.Info, p.C.LeagueBoard!.Value)).ToList();
            if (mov.Count == 0 || boards.Count == 0) continue;
            var placed = Place(ctx, club, boards, mov.Select(p => p.Info).ToList(), fix);
            foreach (var (i, (id, board, _)) in placed)
            {
                var c = tracked.GetValueOrDefault(mov[i].C.Id);
                if (c is null || c.LeagueBoard == board && c.LeagueGameId == id) continue;
                var from = c.LeagueBoard;
                LeagueGameLinks.Set(c, boards.First(b => b.G.Id == id).G);
                moved++;
                (log ?? NullLogger.Instance).LogInformation("LeagueHub: vorläufige Ligapaarung der Vereinspartie {Id} neu verteilt — Liga {Tnr}, "
                    + "Runde {Round}, Begegnung {MatchNo}: Brett {From} → {To}", c.Id, m.Tnr, m.Round, m.MatchNo, from, board);
            }
        }
        if (moved > 0) await db.SaveChangesAsync(ct);
        return moved;
    }

    // ── nach dem Aktualisieren: bestätigen, umhängen oder lösen ──────────────────────────────────────

    public sealed record ConfirmResult(int Confirmed, int Moved, int Released);

    /// <summary>
    /// Vorläufige Zuordnungen der Liga <paramref name="tnr"/> (<c>null</c> = aller) prüfen, deren Brett jetzt Spieler hat: passen
    /// Spieler und Farben (FIDE-IDs der Partie, auch die interne hinter „Schwaz") → bestätigt; sitzen sie an einem anderen Brett
    /// derselben Begegnung → dorthin umgehängt und bestätigt; sonst Zuordnung gelöst (Warnung). Noch leere Bretter bleiben
    /// vorläufig. Speichert selbst.
    /// </summary>
    public static async Task<ConfirmResult> ConfirmAsync(AppDbContext db, int? tnr, CancellationToken ct, ILogger? log = null)
    {
        log ??= NullLogger.Instance;
        var rows = await db.LeagueClubGames.IgnoreQueryFilters()
            .Where(c => c.LeaguePairingProvisional && c.LeagueTnr != null && c.LeagueRound != null && c.LeagueMatchNo != null && c.LeagueBoard != null)
            .Where(c => tnr == null || c.LeagueTnr == tnr).ToListAsync(ct);
        if (rows.Count == 0) return new ConfirmResult(0, 0, 0);
        var tnrs = rows.Select(c => c.LeagueTnr!.Value).Distinct().ToList();
        var rounds = rows.Select(c => c.LeagueRound!.Value).Distinct().ToList();
        var boards = await db.LeagueGames.AsNoTracking().Where(g => tnrs.Contains(g.Tnr) && rounds.Contains(g.Round)).ToListAsync(ct);
        int confirmed = 0, moved = 0, released = 0;
        foreach (var c in rows)
        {
            var key = LeagueGameLinks.KeyOf(c)!.Value;
            var match = boards.Where(g => g.Tnr == key.Tnr && g.Round == key.Round && g.MatchNo == key.MatchNo).OrderBy(g => g.Id).ToList();
            var at = match.FirstOrDefault(g => g.Board == key.Board);
            if (at is null || LeagueGameLinks.IsOpen(at)) continue;   // Brett (noch) ohne Spieler bzw. nicht (mehr) da: bleibt vorläufig
            var (wf, bf) = (Blank(c.WhiteFide ?? c.WhiteRealFide), Blank(c.BlackFide ?? c.BlackRealFide));
            if (Fits(at, wf, bf))
            {
                LeagueGameLinks.Set(c, at);
                c.LeaguePairingProvisional = false;
                confirmed++;
                log.LogInformation("LeagueHub: vorläufige Ligapaarung der Vereinspartie {Id} bestätigt (Liga {Tnr}, Runde {Round}, "
                    + "Begegnung {MatchNo}, Brett {Board})", c.Id, key.Tnr, key.Round, key.MatchNo, key.Board);
            }
            else if (match.FirstOrDefault(g => g.Board != key.Board && Fits(g, wf, bf)) is { } other)
            {
                LeagueGameLinks.Set(c, other);
                c.LeaguePairingProvisional = false;
                moved++;
                log.LogInformation("LeagueHub: vorläufige Ligapaarung der Vereinspartie {Id} umgehängt und bestätigt (Liga {Tnr}, Runde {Round}, "
                    + "Begegnung {MatchNo}): Brett {From} → {To}", c.Id, key.Tnr, key.Round, key.MatchNo, key.Board, other.Board);
            }
            else
            {
                LeagueGameLinks.Set(c, null);
                released++;
                log.LogWarning("LeagueHub: vorläufige Ligapaarung der Vereinspartie {Id} passt nicht zur Aufstellung (Liga {Tnr}, Runde {Round}, "
                    + "Begegnung {MatchNo}, Brett {Board}) und die Spieler stehen an keinem anderen Brett der Begegnung — Zuordnung gelöst, "
                    + "bitte neu wählen", c.Id, key.Tnr, key.Round, key.MatchNo, key.Board);
            }
        }
        if (confirmed + moved + released > 0) await db.SaveChangesAsync(ct);
        return new ConfirmResult(confirmed, moved, released);
    }

    /// <summary>Passt das (besetzte) Brett zur Partie? Jede bekannte FIDE-ID der Partie sitzt in IHRER Farbe (oder dort ohne
    /// FIDE-ID), mindestens eine trifft.</summary>
    internal static bool Fits(LeagueGame g, string? whiteFide, string? blackFide)
    {
        var homeWhite = g.HomeColor != "s";
        var (w, b) = homeWhite ? (g.HomeFide, g.AwayFide) : (g.AwayFide, g.HomeFide);
        var hits = 0;
        foreach (var (mine, there) in new[] { (whiteFide, w), (blackFide, b) })
        {
            if (mine is null) continue;
            if (mine == there) hits++;
            else if (!string.IsNullOrEmpty(there)) return false;
        }
        return hits > 0;
    }
}
