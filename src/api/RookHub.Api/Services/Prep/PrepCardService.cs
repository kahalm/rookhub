using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// Die Spielerkarte der Spielervorbereitung (Phase 2): dieselben Antworten wie die Liga-Karte (Profil, Baum, letzte
/// Partien, PGN), aber für JEDEN Spieler des Partiebestands — Schlüssel ist <see cref="PrepPlayer.Id"/>, nicht jeder hat
/// eine FIDE-ID. Hat er eine, kommen die Quellen von LeagueHub dazu (Liga/chess-results/Vereinspartien über
/// <see cref="LeagueProfileStore.GamesAsync"/>, Online-Partien über <see cref="LeagueProfileStore.OnlineGames"/>, Konten über
/// <see cref="LeagueService.CardAsync"/>) — nichts davon ist hier nachgebaut, auch nicht die Regeln für Minderjährige.
///
/// <para><b>Grenze.</b> Die Partien eines Spielers liegen über die ganze Tabelle verstreut; kalt kostet jede einen
/// Plattenzugriff — gemessen 2026-10-02 am vollen Lumbra-Bestand (10,4 Mio. Partien, 128 MB Puffer wie in Prod): rund
/// 1,4 s Grundkosten plus 2–5 ms je Partie, 500 Partien 1,6–2,5 s, 1000 3,8–6 s, 4000 21 s, 8898 56 s (und warm noch
/// 27 s, weil 128 MB sie nicht fassen). Über die API kalt: Karte mit 500 Partien 1,1–4,4 s, „alle“ 1977 Partien 16 s,
/// 5000 Partien 38 s — bei der beobachteten Streuung zu nah an den 60 s von nginx. Ein größerer Puffer macht nur das
/// WIEDERHOLTE Laden schneller, nicht das erste. Vorgabe sind deshalb die jüngsten <see cref="DefaultLimit"/>, <c>all</c>
/// lädt bis <see cref="DefaultMax"/>. Liga-Partien zählen bei einer Grenze nur ab dem Datum der ältesten geladenen
/// Bestandspartie — ältere wären meist Dubletten von nicht geladenen.</para>
///
/// <para><b>Speicher.</b> Was eine Karte lädt, bleibt <see cref="CacheFor"/> im Speicher (keine eigene Tabelle): Profil,
/// Baum und letzte Partien rechnen danach ohne Datenbank. Die Online-Partien fragt jede Anfrage neu (Filter, wenige).</para>
///
/// <para><b>Namens-Zwilling.</b> Partien ohne FIDE-ID gehören einem eigenen Spieler (Identität = Name). Trägt GENAU EIN
/// FIDE-Spieler denselben Namensschlüssel, bietet seine Karte den Zwilling an (<c>twin</c>, mit Partienzahl) und nimmt
/// dessen Partien nur auf Wunsch dazu — Vorgabe aus, ein gleichnamiger Fremder verfälschte sonst den Eröffnungsbaum.</para>
/// </summary>
public sealed class PrepCardService(AppDbContext db, IMemoryCache cache, LeagueService league, int limit = PrepCardService.DefaultLimit,
    int max = PrepCardService.DefaultMax)
{
    /// <summary>So viele jüngste Bestandspartien lädt eine Karte ohne <c>all</c> (kalt ~2 s); einstellbar über <see cref="LimitKey"/>.</summary>
    public const int DefaultLimit = 500;
    public const string LimitKey = "Prep:CardLimit";
    /// <summary>Höchstens so viele lädt <c>all</c> (kalt ~25 s); einstellbar über <see cref="MaxKey"/>.</summary>
    public const int DefaultMax = 3000;
    public const string MaxKey = "Prep:CardMax";

    public static int LimitFrom(IConfiguration config) =>
        int.TryParse(config[LimitKey], out var n) ? Math.Clamp(n, 100, 50_000) : DefaultLimit;

    public static int MaxFrom(IConfiguration config) =>
        int.TryParse(config[MaxKey], out var n) ? Math.Clamp(n, 100, 100_000) : DefaultMax;

    private int Cap(bool all) => all ? Math.Max(max, limit) : limit;
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    /// <summary>Partien je <c>IN (…)</c>-Abfrage.</summary>
    private const int IdBatch = 1000;

    /// <summary>Eine Partie der Karte — aus dem Bestand (<see cref="PrepId"/>) oder von LeagueHub (<see cref="League"/>).</summary>
    public sealed record CardGame(long? PrepId, int? WhiteId, int? BlackId, string Color, byte Result, int? PlayedOn,
        string Moves, byte Sources, short? WhiteElo, short? BlackElo, int? EventId, string? Round, string? Eco,
        LeagueProfileBuilder.Game? League, bool OwnStart)
    {
        public string Year => PlayedOn is { } d ? (d / 10000).ToString("D4", CultureInfo.InvariantCulture) : "";
        public string Label => League is { } l ? l.Source
            : Sources == (PrepSources.Mega | PrepSources.Lumbra) ? "Mega+Lumbra" : PrepSources.Name(Sources);
    }

    public sealed record Twin(int Id, string Name, int Games);

    /// <summary>Was eine Karte geladen hat (so im Speicher).</summary>
    public sealed record Loaded(PrepPlayer Player, Twin? Twin, bool TwinIncluded, int Limit, List<CardGame> Games,
        int PrepTotal, int PrepLoaded, bool Limited, int? Since);

    // ── Laden ────────────────────────────────────────────────────────────────────────────────────

    public async Task<Loaded?> LoadAsync(int id, bool all, bool twin, CancellationToken ct)
    {
        var key = $"prep:card:{id}:{Cap(all)}:{(twin ? 1 : 0)}";
        if (cache.TryGetValue(key, out Loaded? hit) && hit is not null) return hit;
        var player = await db.PrepPlayers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (player is null) return null;
        var tw = await TwinAsync(player, ct);
        var ids = new List<int> { player.Id };
        if (twin && tw is not null) ids.Add(tw.Id);
        var (rows, more) = await PrepRowsAsync(ids, Cap(all), ct);
        var since = more ? rows.Where(r => r.PlayedOn is not null).Select(r => r.PlayedOn).Min() : null;

        var games = new List<CardGame>(rows.Count);
        foreach (var r in rows)
            games.Add(r with { Color = ids.Contains(r.WhiteId ?? -1) ? "w" : "s" });
        if (player.FideId is { } fide)
            games.AddRange(await LeagueGamesAsync(fide, player.Name, games, more, since, ct));
        games = games.OrderByDescending(g => g.PlayedOn ?? -1).ThenByDescending(g => g.PrepId ?? long.MaxValue).ToList();

        var total = player.Games + (twin && tw is not null ? tw.Games : 0);
        var loaded = new Loaded(player, tw, twin && tw is not null, Cap(all), games, total, rows.Count, more, since);
        cache.Set(key, loaded, CacheFor);
        return loaded;
    }

    /// <summary>Der Namens-Zwilling eines FIDE-Spielers: derselbe Namensschlüssel ohne FIDE-ID — nur, wenn kein ZWEITER
    /// FIDE-Spieler so heißt (sonst wüsste niemand, wessen Partien das sind).</summary>
    public async Task<Twin?> TwinAsync(PrepPlayer p, CancellationToken ct)
    {
        if (p.FideId is null || p.NameKey.Length == 0) return null;
        var same = await db.PrepPlayers.AsNoTracking().Where(x => x.NameKey == p.NameKey)
            .Select(x => new { x.Id, x.Name, x.FideId, x.Games }).Take(20).ToListAsync(ct);
        if (same.Count(x => x.FideId != null) != 1) return null;
        var t = same.FirstOrDefault(x => x.FideId == null && x.Games > 0);
        return t is null ? null : new Twin(t.Id, t.Name, t.Games);
    }

    /// <summary>
    /// Die Bestandspartien dieser Spieler, jüngste zuerst, höchstens <paramref name="max"/>. Erst nur Id + Datum aus den
    /// beiden Indizes (WhiteId/BlackId, PlayedOn — ohne Zeilenzugriff), dann genau die gebrauchten Zeilen.
    /// → (Zeilen, gibt es ältere?).
    /// </summary>
    private async Task<(List<CardGame> Rows, bool More)> PrepRowsAsync(List<int> ids, int? max, CancellationToken ct)
    {
        var keys = new Dictionary<long, int?>();
        foreach (var pid in ids)
        {
            var w = db.PrepGames.AsNoTracking().Where(g => g.WhiteId == pid)
                .OrderByDescending(g => g.PlayedOn).ThenByDescending(g => g.Id).Select(g => new { g.Id, g.PlayedOn });
            var b = db.PrepGames.AsNoTracking().Where(g => g.BlackId == pid)
                .OrderByDescending(g => g.PlayedOn).ThenByDescending(g => g.Id).Select(g => new { g.Id, g.PlayedOn });
            foreach (var x in await (max is { } n ? w.Take(n + 1) : w).ToListAsync(ct)) keys[x.Id] = x.PlayedOn;
            foreach (var x in await (max is { } n2 ? b.Take(n2 + 1) : b).ToListAsync(ct)) keys[x.Id] = x.PlayedOn;
        }
        var ordered = keys.OrderByDescending(k => k.Value ?? -1).ThenByDescending(k => k.Key).Select(k => k.Key).ToList();
        var more = max is { } m && ordered.Count > m;
        if (more) ordered = ordered.Take(max!.Value).ToList();

        var rows = new List<CardGame>(ordered.Count);
        foreach (var chunk in ordered.Chunk(IdBatch))
            rows.AddRange(await db.PrepGames.AsNoTracking().Where(g => chunk.Contains(g.Id))
                .Select(g => new CardGame(g.Id, g.WhiteId, g.BlackId, "", g.Result, g.PlayedOn, g.Moves, g.Sources, g.WhiteElo,
                    g.BlackElo, g.EventId, g.Round, g.Eco, null, false))
                .ToListAsync(ct));
        return (rows, more);
    }

    /// <summary>
    /// Die Partien von LeagueHub (Liga/chess-results/Verein) eines FIDE-Spielers, ohne die, die schon im Bestand stehen:
    /// gleiches Jahr + gleiche Hauptvariante (wie <see cref="LeagueProfileStore.MovesKey"/>) oder — für Abschriften mit
    /// falschem Datum oder fehlenden letzten Zügen — gleiche Farbe, gleicher Gegner-Nachname und dieselben ersten
    /// <see cref="LeagueProfileStore.SameGamePlies"/> Halbzüge (wie <see cref="LeagueProfileStore.SameGameKey"/>).
    /// </summary>
    private async Task<List<CardGame>> LeagueGamesAsync(string fide, string name, List<CardGame> prep, bool limited, int? since,
        CancellationToken ct)
    {
        var (leagueName, leagueGames) = await new LeagueProfileStore(db).GamesAsync(fide, ct);
        if (leagueGames.Count == 0) return [];
        if (leagueName.Length == 0) leagueName = name;

        var byMoves = prep.Select(g => g.Year + "|" + g.Moves).ToHashSet(StringComparer.Ordinal);
        var byStart = prep.Where(g => g.Moves.Count(c => c == ' ') + 1 >= 10)
            .GroupBy(g => g.Color + "|" + FirstPlies(g.Moves, LeagueProfileStore.SameGamePlies), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);

        var candidates = new List<(CardGame Game, string Start)>();
        foreach (var lg in leagueGames)
        {
            var color = LeagueProfileBuilder.ColorOf(lg, fide, leagueName);
            if (color is null) continue;
            var playedOn = PrepPgn.PlayedOn(lg.Headers.TryGetValue("Date", out var d) ? d : null);
            if (limited && (playedOn is null || playedOn < since)) continue;
            var moveText = PgnParser.SplitGames(lg.Raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
            var moves = string.Join(' ', PgnParser.ExtractMainlineSans(moveText));
            var ownStart = lg.Headers.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen) && !PgnParser.IsStartPosition(fen.Trim());
            var game = new CardGame(null, null, null, color, PrepResult.Parse(lg.Headers.GetValueOrDefault("Result")), playedOn, moves, 0,
                null, null, null, null, null, lg, ownStart);
            if (!ownStart && byMoves.Contains(game.Year + "|" + moves)) continue;
            candidates.Add((game, color + "|" + FirstPlies(moves, LeagueProfileStore.SameGamePlies)));
        }

        // Zweiter Schlüssel: nur für Kandidaten mit gleichem Anfang die Gegner-Namen aus dem Bestand holen (wenige).
        var opponents = candidates.Where(c => c.Game.Moves.Count(ch => ch == ' ') + 1 >= 10 && byStart.ContainsKey(c.Start))
            .SelectMany(c => byStart[c.Start]).Select(g => g.Color == "w" ? g.BlackId : g.WhiteId).OfType<int>().Distinct().ToList();
        var names = await NamesAsync(opponents, ct);
        var result = new List<CardGame>();
        foreach (var (game, start) in candidates)
        {
            if (byStart.TryGetValue(start, out var same) && game.Moves.Count(ch => ch == ' ') + 1 >= 10)
            {
                var vs = PrepPgn.Surname(game.League!.Headers.GetValueOrDefault(game.Color == "w" ? "Black" : "White"));
                if (vs.Length > 0 && same.Any(g => (g.Color == "w" ? g.BlackId : g.WhiteId) is { } o
                                                  && names.TryGetValue(o, out var n) && PrepPgn.Surname(n.Name) == vs))
                    continue;
            }
            result.Add(game);
        }
        return result;
    }

    private static string FirstPlies(string moves, int n)
    {
        var at = -1;
        for (var i = 0; i < n; i++)
        {
            at = moves.IndexOf(' ', at + 1);
            if (at < 0) return moves;
        }
        return moves[..at];
    }

    private sealed record NameRow(int Id, string Name, string? FideId);

    private async Task<Dictionary<int, NameRow>> NamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var res = new Dictionary<int, NameRow>();
        foreach (var chunk in ids.Distinct().Chunk(IdBatch))
            foreach (var r in await db.PrepPlayers.AsNoTracking().Where(p => chunk.Contains(p.Id))
                         .Select(p => new NameRow(p.Id, p.Name, p.FideId)).ToListAsync(ct))
                res[r.Id] = r;
        return res;
    }

    private async Task<Dictionary<int, PrepEvent>> EventsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var res = new Dictionary<int, PrepEvent>();
        foreach (var chunk in ids.Distinct().Chunk(IdBatch))
            foreach (var e in await db.PrepEvents.AsNoTracking().Where(e => chunk.Contains(e.Id)).ToListAsync(ct))
                res[e.Id] = e;
        return res;
    }

    // ── Antworten ────────────────────────────────────────────────────────────────────────────────

    private static double? Points(byte result, string color) => result switch
    {
        PrepResult.WhiteWins => color == "w" ? 1 : 0,
        PrepResult.BlackWins => color == "w" ? 0 : 1,
        PrepResult.Draw => 0.5,
        _ => null,
    };

    private static double? OnlinePoints(string result, bool white) => result switch
    {
        "1-0" => white ? 1 : 0,
        "0-1" => white ? 0 : 1,
        "1/2-1/2" => 0.5,
        _ => null,
    };

    private static void Head(JsonObject o, Loaded l)
    {
        o["id"] = l.Player.Id;
        o["fide"] = l.Player.FideId;
        o["name"] = l.Player.Name;
    }

    /// <summary>Kopf jeder Antwort: geladen/gesamt, Grenze, Zwilling.</summary>
    private void Scope(JsonObject o, Loaded l)
    {
        o["games"] = l.PrepTotal;
        o["loaded"] = l.PrepLoaded;
        o["limited"] = l.Limited;
        o["limit"] = l.Limit;
        o["max"] = Cap(true);                    // so viele lädt all=true höchstens — die Seite sagt es dazu
        o["since"] = l.Since is { } s ? DateText(s) : null;
        o["twin"] = l.Twin is { } t ? new JsonObject { ["id"] = t.Id, ["name"] = t.Name, ["games"] = t.Games } : null;
        o["twinIncluded"] = l.TwinIncluded;
    }

    /// <summary>Die Brettpartien nach Filter (Jahre) — Partien ab eigener Stellung zählen wie bei der Liga nicht.</summary>
    private static IEnumerable<CardGame> Board(Loaded l, LeagueProfileStore.TreeFilter filter, DateTime? cutoff) =>
        !filter.Board ? [] : l.Games.Where(g => !g.OwnStart && (cutoff is not { } c || (g.PlayedOn is { } d && d / 10000 >= c.Year)));

    private async Task<List<(string Line, string Result, bool White, DateTime PlayedAt)>> OnlineAsync(Loaded l,
        LeagueProfileStore.TreeFilter filter, DateTime? cutoff, bool? white, CancellationToken ct, string? prefix = null)
    {
        if (!filter.Online || l.Player.FideId is not { } fide) return [];
        var q = LeagueProfileStore.OnlineGames(db, fide, filter, cutoff);
        if (white is { } w) q = q.Where(g => g.White == w);
        if (!string.IsNullOrEmpty(prefix))
        {
            // wie der Baum der Liga: die Zugfolge schon in SQL, nicht jede Online-Partie in den Speicher
            var preSpace = prefix + " ";
            q = q.Where(g => g.Line == prefix || g.Line.StartsWith(preSpace));
        }
        return (await q.Select(g => new { g.Line, g.Result, g.White, g.PlayedAt }).ToListAsync(ct))
            .Select(g => (g.Line, g.Result, g.White, g.PlayedAt)).ToList();
    }

    private static DateTime? Cutoff(LeagueProfileStore.TreeFilter f) => f.Years is { } y ? DateTime.UtcNow.AddYears(-y) : null;

    /// <summary>Die Partien der Karte als Zugfolgen für die Trainingslinien (<see cref="OpponentTrainingLines"/>): die geladenen
    /// Brettpartien (ohne eigene Startstellung) und — nach <paramref name="filter"/> — die Online-Partien; Filter wie Profil und Baum.</summary>
    public async Task<List<OpponentTrainingLines.Game>> TrainingGamesAsync(Loaded l, LeagueProfileStore.TreeFilter filter, CancellationToken ct)
    {
        var cutoff = Cutoff(filter);
        var games = Board(l, filter, cutoff)
            .Select(g => new OpponentTrainingLines.Game(g.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries), g.Color == "w",
                g.PlayedOn is { } d ? d / 10000 : null))
            .ToList();
        foreach (var g in await OnlineAsync(l, filter, cutoff, null, ct))
            games.Add(new(g.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries), g.White, g.PlayedAt.Year));
        return games;
    }

    /// <summary>Eröffnungsprofil — Form wie <see cref="LeagueProfileStore.ProfileAsync"/>.</summary>
    public async Task<JsonObject> ProfileAsync(Loaded l, LeagueProfileStore.TreeFilter filter, CancellationToken ct)
    {
        var cutoff = Cutoff(filter);
        var games = new List<LeagueProfileBuilder.ProfileGame>();
        var years = new List<string>();
        int board = 0, online = 0;
        foreach (var g in Board(l, filter, cutoff))
        {
            board++;
            if (g.Year.Length > 0) years.Add(g.Year);
            games.Add(new(g.Moves.Split(' ', 9, StringSplitOptions.RemoveEmptyEntries).Take(8).ToList(), g.Color, Points(g.Result, g.Color)));
        }
        foreach (var g in await OnlineAsync(l, filter, cutoff, null, ct))
        {
            online++;
            years.Add(g.PlayedAt.Year.ToString(CultureInfo.InvariantCulture));
            games.Add(new(g.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(8).ToList(), g.White ? "w" : "s",
                OnlinePoints(g.Result, g.White)));
        }
        var o = new JsonObject();
        Head(o, l);
        o["n"] = board + online;
        o["board"] = board;
        o["online"] = online;
        o["with_moves"] = games.Count(x => x.Moves.Count > 0);
        o["years"] = years.Count > 0 ? new JsonArray(years.Min(StringComparer.Ordinal), years.Max(StringComparer.Ordinal)) : null;
        LeagueProfileBuilder.AddSections(o, games);
        Scope(o, l);
        return o;
    }

    /// <summary>
    /// Die Karte: das Profil (Vorgabe-Filter: Brettpartien), die Quellen, die letzten Partien und — mit FIDE-ID — die
    /// Online-Konten über <see cref="LeagueService.CardAsync"/>, nie mit <c>reveal</c>. <paramref name="manage"/> (<c>prep.manage</c>):
    /// wie LeagueHub sie einem angemeldeten Nicht-Admin zeigt (auch unsichere, mit Kommentar; Minderjährige nur „es gibt eins");
    /// sonst wie über einen Teilen-Link (nur gesicherte, ohne Kommentar, bei Minderjährigen gar keins).
    /// </summary>
    public async Task<JsonObject> CardAsync(Loaded l, bool manage, CancellationToken ct)
    {
        var o = await ProfileAsync(l, LeagueProfileStore.TreeFilter.Default, ct);
        var src = new JsonObject();
        foreach (var grp in l.Games.Where(g => !g.OwnStart).GroupBy(g => g.Label).OrderByDescending(g => g.Count())) src[grp.Key] = grp.Count();
        o["src"] = src;
        o["firstYear"] = l.Player.FirstYear;
        o["lastYear"] = l.Player.LastYear;
        o["maxElo"] = l.Player.MaxElo;
        o["recent"] = new JsonArray((await RecentEntriesAsync(l, null, ct)).Select(e => (JsonNode)e.Entry).ToArray());
        if (l.Player.FideId is { } fide && await league.CardAsync(fide, onlySure: !manage, ct, reveal: false, prep: true) is { } lc)
        {
            o["accounts"] = lc["accounts"]?.DeepClone() ?? new JsonArray();
            o["online"] = lc["online"]?.DeepClone() ?? 0;
            o["onlineUnsure"] = lc["onlineUnsure"]?.DeepClone() ?? 0;
        }
        else
        {
            o["accounts"] = new JsonArray();
            o["online"] = 0;
            o["onlineUnsure"] = 0;
        }
        return o;
    }

    /// <summary>Eröffnungsbaum ab <paramref name="line"/> — Form und Zählung wie <see cref="LeagueProfileStore.TreeAsync"/>.</summary>
    public async Task<JsonObject> TreeAsync(Loaded l, string color, string? line, LeagueProfileStore.TreeFilter filter, CancellationToken ct)
    {
        color = color is "s" or "b" ? "s" : "w";
        var cutoff = Cutoff(filter);
        var prefix = (line ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(LeagueProfileStore.TreeMaxPlies).ToList();
        var pre = string.Join(' ', prefix);
        var stats = new Dictionary<string, (int N, double Pts, int Scored, string Last)>(StringComparer.Ordinal);
        var order = new List<string>();
        int total = 0, ended = 0, board = 0, online = 0;

        void Count(string moves, double? pts, string year)
        {
            total++;
            var rest = pre.Length == 0 ? moves : moves.Length == pre.Length ? "" : moves[(pre.Length + 1)..];
            if (rest.Length == 0 || prefix.Count >= LeagueProfileStore.TreeMaxPlies) { ended++; return; }
            var sp = rest.IndexOf(' ');
            var next = sp < 0 ? rest : rest[..sp];
            if (!stats.TryGetValue(next, out var st)) { order.Add(next); st = (0, 0, 0, ""); }
            stats[next] = (st.N + 1, st.Pts + (pts ?? 0), st.Scored + (pts is null ? 0 : 1),
                string.CompareOrdinal(year, st.Last) > 0 ? year : st.Last);
        }
        bool Starts(string moves) => pre.Length == 0 || moves == pre || moves.StartsWith(pre + " ", StringComparison.Ordinal);

        foreach (var g in Board(l, filter, cutoff))
        {
            if (g.Color != color || !Starts(g.Moves)) continue;
            board++;
            Count(g.Moves, Points(g.Result, color), g.Year);
        }
        foreach (var g in await OnlineAsync(l, filter, cutoff, color == "w", ct, pre))
        {
            if (!Starts(g.Line)) continue;
            online++;
            Count(g.Line, OnlinePoints(g.Result, g.White), g.PlayedAt.Year.ToString(CultureInfo.InvariantCulture));
        }

        var o = new JsonObject();
        Head(o, l);
        o["color"] = color;
        o["line"] = pre;
        o["total"] = total;
        o["ended"] = ended;
        o["board"] = board;
        o["online"] = online;
        o["moves"] = new JsonArray(order.OrderByDescending(m => stats[m].N).ThenBy(m => order.IndexOf(m))
            .Select(m => (JsonNode)new JsonObject
            {
                ["san"] = m, ["n"] = stats[m].N,
                ["score"] = stats[m].Scored > 0 ? (int)Math.Round(100 * stats[m].Pts / stats[m].Scored, MidpointRounding.ToEven) : null,
                ["last"] = stats[m].Last.Length > 0 ? stats[m].Last : null,
            }).ToArray());
        Scope(o, l);
        return o;
    }

    /// <summary>Die letzten Partien MIT PGN zum Nachspielen — Form wie <see cref="LeagueProfileStore.RecentAsync"/>.</summary>
    public async Task<JsonObject> RecentAsync(Loaded l, string? color, CancellationToken ct)
    {
        var o = new JsonObject();
        Head(o, l);
        o["games"] = new JsonArray((await RecentEntriesAsync(l, color is "w" or "s" ? color : null, ct)).Select(e =>
        {
            e.Entry["pgn"] = e.Game.Raw.Trim() + "\n";
            return (JsonNode)e.Entry;
        }).ToArray());
        return o;
    }

    private async Task<List<(JsonObject Entry, LeagueProfileBuilder.Game Game)>> RecentEntriesAsync(Loaded l, string? color, CancellationToken ct)
    {
        var pick = l.Games.Where(g => color is null || g.Color == color).Take(LeagueProfileBuilder.RecentCount).ToList();
        var pgn = await AsPgnGamesAsync(pick, ct);
        return pick.Select((g, i) => (LeagueProfileBuilder.RecentEntry(pgn[i], g.Color), pgn[i])).ToList();
    }

    /// <summary>Alle geladenen Partien als PGN (Grenze und Zwilling wie die Karte) — der Download je Spieler.</summary>
    public async Task<string> PgnAsync(Loaded l, CancellationToken ct)
    {
        var games = await AsPgnGamesAsync(l.Games, ct);
        return string.Join("\n", games.Select(g => g.Raw.TrimEnd() + "\n"));
    }

    /// <summary>Partien als <see cref="LeagueProfileBuilder.Game"/> (Kopf + PGN): Bestandspartien bekommen Namen, Turnier
    /// und Ort aus ihren Tabellen, Liga-Partien bleiben, wie sie sind.</summary>
    private async Task<List<LeagueProfileBuilder.Game>> AsPgnGamesAsync(IReadOnlyList<CardGame> games, CancellationToken ct)
    {
        var prep = games.Where(g => g.League is null).ToList();
        var names = await NamesAsync(prep.SelectMany(g => new[] { g.WhiteId, g.BlackId }).OfType<int>(), ct);
        var events = await EventsAsync(prep.Select(g => g.EventId).OfType<int>(), ct);
        return games.Select(g => g.League ?? ToPgn(g, names, events)).ToList();
    }

    private static LeagueProfileBuilder.Game ToPgn(CardGame g, Dictionary<int, NameRow> names, Dictionary<int, PrepEvent> events)
    {
        var ev = g.EventId is { } e && events.TryGetValue(e, out var x) ? x : null;
        var white = g.WhiteId is { } w && names.TryGetValue(w, out var wn) ? wn : null;
        var black = g.BlackId is { } b && names.TryGetValue(b, out var bn) ? bn : null;
        var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Event"] = ev?.Name ?? "?", ["Site"] = ev?.Site ?? "?", ["Date"] = g.PlayedOn is { } d ? DateText(d) : "????.??.??",
            ["Round"] = g.Round ?? "?", ["White"] = white?.Name ?? "?", ["Black"] = black?.Name ?? "?",
            ["Result"] = PrepResult.Text(g.Result),
        };
        if (g.WhiteElo is { } we) h["WhiteElo"] = we.ToString(CultureInfo.InvariantCulture);
        if (g.BlackElo is { } be) h["BlackElo"] = be.ToString(CultureInfo.InvariantCulture);
        if (white?.FideId is { } wf) h["WhiteFideId"] = wf;
        if (black?.FideId is { } bf) h["BlackFideId"] = bf;
        if (g.Eco is { } eco) h["ECO"] = eco;
        h["Source"] = g.Sources switch
        {
            PrepSources.Mega => "Megabase",
            PrepSources.Lumbra => "LumbrasGigaBase",
            _ => "Megabase, LumbrasGigaBase",
        };
        var sb = new StringBuilder();
        foreach (var (k, v) in h) sb.Append(PgnWriter.Tag(k, v));
        sb.Append('\n');
        sb.Append(Wrap(PgnWriter.MoveText(g.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries), result: PrepResult.Text(g.Result))));
        sb.Append('\n');
        return new LeagueProfileBuilder.Game(h, sb.ToString(), g.Label);
    }

    /// <summary>Zugtext auf Zeilen von höchstens 79 Zeichen umbrechen (PGN-Export-Format).</summary>
    private static string Wrap(string text)
    {
        var sb = new StringBuilder(text.Length + text.Length / 79);
        var col = 0;
        foreach (var tok in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (col > 0 && col + 1 + tok.Length > 79) { sb.Append('\n'); col = 0; }
            else if (col > 0) { sb.Append(' '); col++; }
            sb.Append(tok);
            col += tok.Length;
        }
        return sb.ToString();
    }

    /// <summary>20240517 → „2024.05.17", 19750000 → „1975.??.??".</summary>
    public static string DateText(int d)
    {
        var y = d / 10000;
        var m = d / 100 % 100;
        var day = d % 100;
        return string.Create(CultureInfo.InvariantCulture,
            $"{y:D4}.{(m == 0 ? "??" : m.ToString("D2", CultureInfo.InvariantCulture))}.{(day == 0 ? "??" : day.ToString("D2", CultureInfo.InvariantCulture))}");
    }
}
