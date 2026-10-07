using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Die feste Ligapaarung einer Vereinspartie (<see cref="LeagueClubGame.LeagueGameId"/>) — stabil über jedes Aktualisieren
/// (0.716.1, gemeldet 2026-10-07: „gefühlt zum dritten Mal zugewiesen, die Zuordnung verschwindet immer wieder").
/// <para>Ursache: <see cref="LeagueRefresh.ReplaceAsync(AppDbContext, LeagueRefresh.Pages, DateTime, CancellationToken)"/>
/// löschte alle <see cref="LeagueGame"/>-Zeilen einer Liga und legte sie neu an — neue Ids, und jede Zuordnung zeigte ins
/// Leere. Die Leser rieten nur unter Partien OHNE Zuordnung, die tote Id fand keine Paarung: die Partie verschwand ganz aus
/// den Paarungen der Runde.</para>
/// Drei Teile:
/// <list type="number">
/// <item><see cref="Merge"/>: Ersetzen erhält die Id je Schlüssel <see cref="Key"/> (Tnr, Runde, Begegnung, Brett) — vorhandene
/// Zeile aktualisieren, neue einfügen, verschwundene löschen. (Runde + Brett allein reichen NICHT: jede Begegnung einer Runde hat
/// ihre Bretter 1…n.)</item>
/// <item>Sicherheitsnetz: die Zuordnung steht zusätzlich als Schlüssel an der Partie (<see cref="Set"/>); nach jedem Ersetzen
/// löst <see cref="RelinkAsync"/> die Id daraus neu auf, und jeder Leser (<see cref="ResolveAsync"/>) nimmt bei toter Id den
/// Schlüssel — gibt es auch den nicht, gilt die Partie als nicht zugeordnet (geraten werden darf; Warnung im Log). Eine Partie
/// verschwindet nie wegen einer toten Id.</item>
/// <item><see cref="HealAsync"/> beim Start: Zuordnungen von vor 0.716.1 bekommen ihren Schlüssel (gültige Id) bzw. werden über
/// <see cref="LeaguePairingFinder"/> neu gefunden (tote Id, genau EIN genauer Treffer) oder geleert.</item>
/// </list>
/// </summary>
public static class LeagueGameLinks
{
    public readonly record struct Key(int Tnr, int Round, int MatchNo, int Board);

    public static Key KeyOf(LeagueGame g) => new(g.Tnr, g.Round, g.MatchNo, g.Board);

    public static Key? KeyOf(LeagueClubGame c) =>
        c is { LeagueTnr: { } t, LeagueRound: { } r, LeagueMatchNo: { } m, LeagueBoard: { } b } ? new Key(t, r, m, b) : null;

    /// <summary>Die Zuordnung setzen (<paramref name="g"/>) bzw. lösen (<c>null</c>) — Id und Schlüssel immer zusammen.</summary>
    public static void Set(LeagueClubGame c, LeagueGame? g)
    {
        c.LeagueGameId = g?.Id;
        (c.LeagueTnr, c.LeagueRound, c.LeagueMatchNo, c.LeagueBoard) = g is null
            ? ((int?)null, (int?)null, (int?)null, (int?)null)
            : (g.Tnr, g.Round, g.MatchNo, g.Board);
    }

    /// <summary>
    /// Brettpaarungen ersetzen, ohne die Ids zu verlieren: je Schlüssel übernimmt die vorhandene Zeile (die kleinste Id zuerst)
    /// die Werte der neuen, übrige neue werden eingefügt, übrige vorhandene gelöscht. <paramref name="existing"/> = die
    /// GETRACKTEN Zeilen des Bereichs, der ersetzt wird. Speichert nicht. <paramref name="incoming"/> wird verändert (Id).
    /// </summary>
    public static void Merge(AppDbContext db, IReadOnlyCollection<LeagueGame> existing, IEnumerable<LeagueGame> incoming)
    {
        var pool = existing.GroupBy(KeyOf).ToDictionary(x => x.Key, x => new Queue<LeagueGame>(x.OrderBy(g => g.Id)));
        foreach (var n in incoming)
        {
            if (pool.TryGetValue(KeyOf(n), out var q) && q.Count > 0)
            {
                var row = q.Dequeue();
                n.Id = row.Id;
                db.Entry(row).CurrentValues.SetValues(n);
            }
            else db.LeagueGames.Add(n);
        }
        foreach (var gone in pool.Values.SelectMany(q => q)) db.LeagueGames.Remove(gone);
    }

    /// <summary>
    /// Die Paarung je Vereinspartie (Schlüssel: <see cref="LeagueClubGame.Id"/>), wie sie HEUTE in <see cref="LeagueGame"/> steht:
    /// die gespeicherte Id, wenn es sie gibt und sie zum Schlüssel passt; sonst die Zeile zum Schlüssel; sonst fehlt die Partie
    /// im Ergebnis (= nicht zugeordnet, raten erlaubt) — eine tote Id meldet eine Warnung.
    /// </summary>
    public static async Task<Dictionary<int, LeagueGame>> ResolveAsync(AppDbContext db, IEnumerable<LeagueClubGame> games,
        CancellationToken ct, ILogger? log = null)
    {
        var linked = games.Where(c => c.LeagueGameId != null || KeyOf(c) != null).DistinctBy(c => c.Id).ToList();
        var result = new Dictionary<int, LeagueGame>();
        if (linked.Count == 0) return result;
        var ids = linked.Where(c => c.LeagueGameId != null).Select(c => c.LeagueGameId!.Value).Distinct().ToList();
        var byId = ids.Count == 0 ? new Dictionary<int, LeagueGame>()
            : await db.LeagueGames.AsNoTracking().Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        var keys = new List<Key>();
        foreach (var c in linked)
            if (KeyOf(c) is { } k && !(c.LeagueGameId is { } id && byId.TryGetValue(id, out var g0) && KeyOf(g0) == k)) keys.Add(k);
        var byKey = await ByKeysAsync(db, keys, ct);
        foreach (var c in linked)
        {
            var k = KeyOf(c);
            LeagueGame? hit = null;
            if (c.LeagueGameId is { } id && byId.TryGetValue(id, out var g) && (k is null || KeyOf(g) == k)) hit = g;
            else if (k is { } kk) hit = byKey.GetValueOrDefault(kk);
            if (hit is not null) result[c.Id] = hit;
            else if (c.LeagueGameId is { } dead)
                (log ?? NullLogger.Instance).LogWarning(
                    "LeagueHub: Vereinspartie {Id} zeigt auf Brettpaarung {LeagueGameId}{Key}, die es nicht mehr gibt — gilt als nicht zugeordnet",
                    c.Id, dead, k is { } x ? $" (Liga {x.Tnr}, Runde {x.Round}, Begegnung {x.MatchNo}, Brett {x.Board})" : "");
        }
        return result;
    }

    /// <summary>Wie <see cref="ResolveAsync"/> für EINE Partie.</summary>
    public static async Task<LeagueGame?> FindAsync(AppDbContext db, LeagueClubGame c, CancellationToken ct, ILogger? log = null) =>
        (await ResolveAsync(db, new[] { c }, ct, log)).GetValueOrDefault(c.Id);

    private static async Task<Dictionary<Key, LeagueGame>> ByKeysAsync(AppDbContext db, IReadOnlyCollection<Key> keys, CancellationToken ct)
    {
        var result = new Dictionary<Key, LeagueGame>();
        if (keys.Count == 0) return result;
        var want = keys.ToHashSet();
        var tnrs = want.Select(k => k.Tnr).Distinct().ToList();
        var rounds = want.Select(k => k.Round).Distinct().ToList();
        var rows = await db.LeagueGames.AsNoTracking().Where(g => tnrs.Contains(g.Tnr) && rounds.Contains(g.Round)).ToListAsync(ct);
        foreach (var g in rows.OrderBy(g => g.Id))
            if (want.Contains(KeyOf(g))) result.TryAdd(KeyOf(g), g);
        return result;
    }

    /// <summary>
    /// Nach dem Ersetzen einer Liga (<paramref name="tnr"/>; <c>null</c> = aller): die Id jeder Zuordnung mit Schlüssel neu aus
    /// dem Schlüssel auflösen — auch archivierte Partien. Gibt es die Paarung nicht (mehr), wird die Id leer, der Schlüssel bleibt
    /// (taucht sie wieder auf, findet der nächste Lauf sie). Speichert selbst. → geänderte Partien.
    /// </summary>
    public static async Task<int> RelinkAsync(AppDbContext db, int? tnr, CancellationToken ct, ILogger? log = null)
    {
        var rows = await db.LeagueClubGames.IgnoreQueryFilters()
            .Where(c => c.LeagueTnr != null && c.LeagueRound != null && c.LeagueMatchNo != null && c.LeagueBoard != null)
            .Where(c => tnr == null || c.LeagueTnr == tnr).ToListAsync(ct);
        if (rows.Count == 0) return 0;
        var byKey = await ByKeysAsync(db, rows.Select(c => KeyOf(c)!.Value).ToList(), ct);
        var changed = 0;
        foreach (var c in rows)
        {
            var id = byKey.GetValueOrDefault(KeyOf(c)!.Value)?.Id;
            if (c.LeagueGameId == id) continue;
            if (id is null)
                (log ?? NullLogger.Instance).LogWarning("LeagueHub: Brettpaarung der Vereinspartie {Id} (Liga {Tnr}, Runde {Round}, "
                    + "Begegnung {MatchNo}, Brett {Board}) gibt es nicht mehr — Zuordnung ruht", c.Id, c.LeagueTnr, c.LeagueRound,
                    c.LeagueMatchNo, c.LeagueBoard);
            c.LeagueGameId = id;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    public sealed record HealResult(int Keyed, int Relinked, int Repaired, int Cleared);

    /// <summary>
    /// Bestand heilen (beim Start, idempotent): (a) Zuordnungen mit toter Id und ohne Schlüssel (von vor 0.716.1) über
    /// <see cref="LeaguePairingFinder"/> neu finden — genau EIN genauer Treffer (<see cref="LeaguePairingFinder.AutoPick"/>),
    /// sonst Zuordnung leeren; (b) gültigen Zuordnungen ohne Schlüssel den Schlüssel nachtragen; dazu (c) alle mit Schlüssel neu
    /// auflösen (<see cref="RelinkAsync"/>). Speichert selbst.
    /// </summary>
    public static async Task<HealResult> HealAsync(AppDbContext db, CancellationToken ct, ILogger? log = null)
    {
        log ??= NullLogger.Instance;
        var relinked = await RelinkAsync(db, null, ct, log);
        var rows = await db.LeagueClubGames.IgnoreQueryFilters()
            .Where(c => c.LeagueGameId != null && (c.LeagueTnr == null || c.LeagueRound == null || c.LeagueMatchNo == null || c.LeagueBoard == null))
            .ToListAsync(ct);
        int keyed = 0, repaired = 0, cleared = 0;
        if (rows.Count == 0) return new HealResult(0, relinked, 0, 0);
        var ids = rows.Select(c => c.LeagueGameId!.Value).Distinct().ToList();
        var live = await db.LeagueGames.AsNoTracking().Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        var finder = new LeaguePairingFinder(db);
        var clubs = await db.LeagueClubs.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        foreach (var c in rows)
        {
            if (live.TryGetValue(c.LeagueGameId!.Value, out var g))
            {
                Set(c, g);
                keyed++;
                continue;
            }
            var date = LeagueClubService.FullDateOf(PgnParser.SplitGames(c.Pgn).FirstOrDefault().Headers?.GetValueOrDefault("Date"));
            var options = await finder.ForAsync(LeaguePairingFinder.QueryOf(c, date), clubs.GetValueOrDefault(c.ClubId), ct);
            var dead = c.LeagueGameId;
            if (LeaguePairingFinder.AutoPick(options) is { } pick && await db.LeagueGames.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pick, ct) is { } found)
            {
                Set(c, found);
                repaired++;
                log.LogWarning("LeagueHub: Vereinspartie {Id} zeigte auf die verschwundene Brettpaarung {Dead} — neu zugeordnet: {Pairing} "
                    + "(Liga {Tnr}, Runde {Round}, Brett {Board})", c.Id, dead, found.Id, found.Tnr, found.Round, found.Board);
            }
            else
            {
                Set(c, null);
                cleared++;
                log.LogWarning("LeagueHub: Vereinspartie {Id} zeigte auf die verschwundene Brettpaarung {Dead}, und es gibt keine "
                    + "eindeutige ({Options} Vorschläge, {Exact} genau) — Zuordnung geleert, bitte neu wählen", c.Id, dead,
                    options.Count, options.Count(o => o.Exact));
            }
        }
        await db.SaveChangesAsync(ct);
        log.LogInformation("LeagueHub: feste Ligapaarungen geheilt — {Keyed} Schlüssel nachgetragen, {Relinked} neu aufgelöst, "
            + "{Repaired} neu zugeordnet, {Cleared} geleert", keyed, relinked, repaired, cleared);
        return new HealResult(keyed, relinked, repaired, cleared);
    }

    /// <summary><see cref="HealAsync"/> beim Start — wirft nie (ein Fehler hier darf den Start nicht kippen).</summary>
    public static async Task HealOnStartupAsync(AppDbContext db, ILogger log, CancellationToken ct = default)
    {
        try { await HealAsync(db, ct, log); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "LeagueHub: Heilung der festen Ligapaarungen gescheitert");
        }
        finally { db.ChangeTracker.Clear(); }
    }
}
