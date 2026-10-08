using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// „Über welche Zugfolgen kommt man am häufigsten in diese Stellung?" (<c>GET /api/explorer/paths</c>, LeagueHub-Zugeditor,
/// Modus „Stellung", 0.723.0). Sucht VORWÄRTS von der Grundstellung durch den Baum des LOKALEN Explorers bis zur Zielstellung
/// (gleiches Brett + gleiche Seite am Zug; Rochaderechte und en passant zählen nicht) — nie online, kein Kontingent.
///
/// <para><b>Suche:</b> beste zuerst. Ein Knoten ist (Stellung, Tiefe) — Zugumstellungen werden je Tiefe EINMAL erweitert, ihre
/// Vorgänger aber alle behalten, damit am Ende jede Zugfolge zählt. Offen ist immer, was die höchste Wahrscheinlichkeit
/// (Produkt der Zuganteile) hat, gewichtet mit einer Untergrenze der noch nötigen Züge (<see cref="Bounds"/>); je Runde gehen die
/// <see cref="Parallelism"/> besten offenen Knoten gleichzeitig an den Explorer. Je Knoten nur Züge mit Anteil ≥ <see cref="MinShare"/>,
/// höchstens die <see cref="MaxBranch"/> häufigsten.</para>
///
/// <para><b>Schranken</b> (alle zulässig, d. h. sie verwerfen nie eine echte Zugfolge innerhalb der Halbzug-Grenze; Umwandlungen sind
/// ausgenommen — in den ersten 30 Halbzügen kommen sie im Explorer praktisch nicht vor): (1) Halbzug-Grenze + Parität (die Ziel-Tiefe
/// hat die Seite am Zug der Zielstellung); (2) Felder-Abstand: jeder Halbzug ändert höchstens 2 Felder (Rochade 4, en passant 3) —
/// abbrechen, wenn <c>diff &gt; 2·Rest + 4</c>; (3) Material kann nur abnehmen; (4) Bauern gehen nicht zurück: jeder Zielbauer
/// braucht einen eigenen Bauern dahinter, Linienwechsel kosten Schlagzüge, und die Schlagzüge müssen sich in der Materialbilanz des
/// Gegners finden; (5) Zug-Untergrenze je Farbe: jedes Zielfeld einer Figur, auf dem sie noch nicht steht, braucht einen eigenen Zug
/// (die Rochade füllt zwei), dazu die Bauernschritte — mehr eigene Züge, als bis zur Grenze übrig sind, geht nicht.</para>
///
/// <para><b>Ende:</b> sobald die zehn besten gefundenen Zugfolgen wahrscheinlicher sind als jeder offene Knoten (dann ist die
/// Rangfolge exakt), wenn nichts mehr offen ist, oder am Budget: höchstens <see cref="MaxQueries"/> Explorer-Abfragen bzw.
/// <see cref="Budget"/> (dann <c>truncated</c>). Antworten liegen 24 h im eigenen Speicher (<see cref="PathCacheTtl"/>, gedeckelt
/// auf <see cref="CacheSizeLimit"/> Stellungen) und zusätzlich eine Stunde unter dem Schlüssel des Lochfinders
/// (<see cref="RepertoireExplorerService.LocalMemoryKey"/>).</para>
///
/// <para><b>Rang:</b> geschätzte Partien genau dieser Zugfolge = <c>Partien(Grundstellung) · Π Anteil</c> (Anteil = Partien des Zugs /
/// Partien der Stellung davor).</para>
/// </summary>
public sealed class ExplorerPathFinder
{
    public const int DefaultMaxPlies = 20;
    public const int MaxPliesLimit = 30;
    public const double MinShare = 0.01;
    public const int MaxBranch = 8;
    public const int MaxPaths = 10;
    public const int Parallelism = 8;

    /// <summary>Höchstens so viele Abfragen an den Explorer je Aufruf (Treffer im Arbeitsspeicher zählen nicht).</summary>
    public int MaxQueries { get; set; } = 400;
    /// <summary>Höchstens so lange sucht ein Aufruf (die nginx/Reverse-Proxy-Grenze liegt bei 60 s).</summary>
    public TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Die Auswahl, in der gesucht wird: Lichess-Partien ab 1600 (darunter hat der lokale Bestand nichts), Blitz bis
    /// klassisch — Fernschach würde die seltenen Theoriezüge überbetonen.</summary>
    public static readonly ExplorerQuery Query = ExplorerQuery.Create(ExplorerQuery.Lichess,
        LocalExplorerClient.LocalRatings, new[] { "blitz", "rapid", "classical" });

    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private readonly Func<string, CancellationToken, Task<ExplorerPositionStats?>>? _fetch;
    private readonly IMemoryCache _memory;
    private readonly IMemoryCache _pathCache;
    private readonly ILogger<ExplorerPathFinder>? _logger;

    /// <summary>Schlüssel des eigenen Zwischenspeichers (keyed Singleton, <see cref="CacheSizeLimit"/>).</summary>
    public const string CacheServiceKey = "explorer-paths";
    /// <summary>Höchstens so viele Stellungen im eigenen Speicher (Size = 1 je Stellung). Eine Stellung sind je nach Zugzahl
    /// grob 2–10 KB (bis 40 Züge), also höchstens ~200 MB, üblich deutlich weniger; eine Suche braucht 70–300 Stellungen.</summary>
    public const long CacheSizeLimit = 20_000;
    /// <summary>Antworten des lokalen Explorers für die Pfadsuche: 24 h (der Lochfinder behält seine 1 h). Die Daten wachsen
    /// nur monatlich, und die ersten Züge sind in jeder Suche dieselben.</summary>
    public static readonly TimeSpan PathCacheTtl = TimeSpan.FromHours(24);

    public ExplorerPathFinder(LocalExplorerClient local, IMemoryCache memory,
        [FromKeyedServices(CacheServiceKey)] IMemoryCache pathCache, ILogger<ExplorerPathFinder> logger)
        : this(local.IsConfigured ? (fen, ct) => local.FetchAsync(fen, Query, ct) : null, memory, logger, pathCache) { }

    /// <summary>Für Tests: ein beliebiger Explorer (<c>null</c> = keiner eingerichtet).</summary>
    internal ExplorerPathFinder(Func<string, CancellationToken, Task<ExplorerPositionStats?>>? fetch, IMemoryCache memory,
        ILogger<ExplorerPathFinder>? logger = null, IMemoryCache? pathCache = null)
    {
        _fetch = fetch;
        _memory = memory;
        _pathCache = pathCache ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = CacheSizeLimit });
        _logger = logger;
    }

    /// <summary>Gibt es einen lokalen Explorer? Ohne ihn ist der Endpunkt ein 400 <c>noLocalExplorer</c>.</summary>
    public bool IsAvailable => _fetch is not null;

    private sealed class Node
    {
        public required Pos Pos { get; init; }
        public required int Depth { get; init; }
        public double Prob;
        public double Priority;
        public ExplorerPositionStats? Stats;
        public bool Expanded;
        public bool Failed;
        public bool IsTarget;
        public readonly List<(Node Parent, string San, string Uci, double Share, string? Opening, string? Eco)> Parents = new();
    }

    /// <summary>Sucht die häufigsten Zugfolgen zur Stellung <paramref name="fen"/>. Ungültige Stellung →
    /// <see cref="ArgumentException"/> (der Controller macht daraus 400 <c>invalidFen</c>).</summary>
    public async Task<ExplorerPathsResultDto> FindAsync(string fen, int? maxPlies, CancellationToken ct)
    {
        if (_fetch is null) throw new InvalidOperationException("Kein lokaler Explorer eingerichtet.");
        var target = Pos.Parse(fen) ?? throw new ArgumentException("Keine gültige Stellung.");
        var limit = Math.Clamp(maxPlies ?? DefaultMaxPlies, 1, MaxPliesLimit);
        var targetParity = target.WhiteToMove ? 0 : 1;
        var maxDepth = limit % 2 == targetParity ? limit : limit - 1;
        var bounds = new Bounds(target, maxDepth);
        var targetBoard = target.BoardKey;

        var clock = Stopwatch.StartNew();
        var dto = new ExplorerPathsResultDto();
        var queries = 0;
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var open = new List<Node>();

        // Zielstellung selbst: Partien + Eröffnungsname (eine Abfrage, auch wenn keine Zugfolge gefunden wird).
        var targetStats = await FetchAsync(target.ToFen(), ct, () => queries++);
        dto.Games = targetStats?.Total ?? 0;

        var root = new Node { Pos = Pos.Parse(StartFen)!, Depth = 0, Prob = 1 };
        root.IsTarget = root.Pos.BoardKey == targetBoard && maxDepth >= 0 && targetParity == 0;
        nodes[root.Pos.SearchKey + "|0"] = root;
        if (!root.IsTarget && bounds.Feasible(root.Pos, 0, out var rootNeed)) { root.Priority = -rootNeed; open.Add(root); }
        var found = new List<Node>();
        if (root.IsTarget) found.Add(root);

        var truncated = false;
        double tenth = 0;   // Schwelle aus den Funden (Cutoff), 0 = noch keiner
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);
        // Fließend statt schichtweise: immer bis zu Parallelism Abfragen unterwegs, jede Antwort wird sofort erweitert und die
        // nächstbeste offene Stellung nachgeschoben — eine langsame Abfrage (HDD, bis Sekunden) hält die anderen nicht auf.
        var inflight = new Dictionary<Task<ExplorerPositionStats?>, Node>();
        while (true)
        {
            while (inflight.Count < Parallelism && open.Count > 0)
            {
                // Ende, wenn die zehn besten Funde jede offene Stellung schlagen — gemessen an deren SCHÄTZUNG (Wahrscheinlichkeit ·
                // ≈0,3 je noch nötigem Zug, = exp(Priority)). Mit der reinen Wahrscheinlichkeit (exakt) liefe fast jede tiefe
                // Stellung ins Budget, weil frühe, häufige Knoten (1.d4) auf dem Papier noch alles erreichen könnten.
                // Die Abfragen, die noch unterwegs sind, zählen dabei NICHT mit (bis 0.723.1 taten sie es): hing eine einzige
                // Abfrage mit hoher Schätzung an der Platte, füllten die übrigen sieben Plätze sich so lange mit immer
                // schlechteren Stellungen, bis das Budget erreicht war (Prod 08.10., Alapin: kalt 243 Abfragen in 20 s). Jetzt
                // wartet die Suche auf sie — ihre Folgestellungen können die Schwelle wieder überschreiten.
                if (tenth > 0 && tenth >= MaxEstimate(open)) break;
                if (queries >= MaxQueries || clock.Elapsed >= Budget) { truncated = true; break; }
                var best = 0;
                for (var i = 1; i < open.Count; i++) if (open[i].Priority > open[best].Priority) best = i;
                var n = open[best];
                open[best] = open[^1];
                open.RemoveAt(open.Count - 1);
                inflight[FetchAsync(n.Pos.ToFen(), budget.Token, () => Interlocked.Increment(ref queries))] = n;
            }
            if (inflight.Count == 0) break;

            var done = await Task.WhenAny(inflight.Keys);
            var node = inflight[done];
            inflight.Remove(done);
            try { node.Stats = await done; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { node.Stats = null; truncated = true; }
            if (node.Stats is null)
            {
                node.Failed = true;
                if (node == root) { dto.Failed = !truncated; break; }
                continue;
            }
            node.Expanded = true;
            if (node.Stats is not { Total: > 0 } stats) continue;
            var changed = false;
            foreach (var m in stats.Moves.Where(m => m.Games > 0).OrderByDescending(m => m.Games).Take(MaxBranch))
            {
                var share = (double)m.Games / stats.Total;
                if (share < MinShare) break;
                var next = node.Pos.Apply(m.Uci);
                if (next is null) continue;
                var depth = node.Depth + 1;
                var key = next.SearchKey + "|" + depth;
                var prob = node.Prob * share;
                var edge = (node, m.San, Pos.StandardUci(node.Pos, m.Uci), share, m.Opening, m.Eco);
                if (nodes.TryGetValue(key, out var known))
                {
                    // Zugumstellung: die Stellung wird nicht noch einmal erweitert, aber der Weg zählt mit.
                    known.Parents.Add(edge);
                    changed = true;
                    if (prob > known.Prob)
                    {
                        known.Priority += Math.Log(prob) - Math.Log(known.Prob);
                        known.Prob = prob;
                    }
                    continue;
                }
                var child = new Node { Pos = next, Depth = depth, Prob = prob };
                child.Parents.Add(edge);
                nodes[key] = child;
                if (depth % 2 == targetParity && next.BoardKey == targetBoard)
                {
                    child.IsTarget = true;
                    found.Add(child);
                    changed = true;
                    continue;
                }
                if (!bounds.Feasible(next, depth, out var need)) continue;
                // Wahrscheinlichkeit, gewichtet mit der Untergrenze der noch nötigen Züge — die Reihenfolge der offenen Knoten.
                // Das Ende prüft dagegen allein die Wahrscheinlichkeit, die eine echte Obergrenze ist.
                child.Priority = Math.Log(prob) - NeedWeight * need;
                open.Add(child);
            }
            if (changed && found.Count > 0) tenth = Cutoff(found, MinShareOfBest);
        }
        // Am Budget abgebrochen: was noch unterwegs ist, wird verworfen (die Abfragen enden mit dem Abbruch).
        if (inflight.Count > 0)
        {
            budget.Cancel();
            try { await Task.WhenAll(inflight.Keys); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            truncated = true;
        }

        dto.Searched = nodes.Values.Count(n => n.Expanded);
        dto.Queries = queries;
        dto.Truncated = truncated;
        var rootTotal = root.Stats?.Total ?? 0;
        var paths = Enumerate(found).Where(p => p.Edges.Count > 0).OrderByDescending(p => p.Prob).Take(MaxPaths).ToList();
        foreach (var p in paths)
        {
            var est = rootTotal * p.Prob;
            dto.Paths.Add(new ExplorerPathDto
            {
                Moves = p.Edges.Select(e => e.San).ToList(),
                Uci = p.Edges.Select(e => e.Uci).ToList(),
                EstGames = (long)Math.Round(est),
                Share = dto.Games > 0 ? Math.Min(1, est / dto.Games) : 0,
            });
        }

        // Nichts gefunden oder kaum bekannt? Dann EINE Abfrage derselben Stellung mit der anderen Seite am Zug — meist ist dann
        // die Seite falsch eingestellt (Prod 08.10.: Italienisch mit „Weiß am Zug" = 135 Partien, mit Schwarz 3 Mio.; die Suche
        // fand dort sogar Tempoverlust-Wege, ein „nichts gefunden" allein hätte es nie gemeldet).
        if ((dto.Games < OtherSideProbeBelow || dto.Paths.Count == 0) && !ct.IsCancellationRequested)
        {
            try
            {
                var other = await FetchAsync(OtherSideFen(target), ct, () => queries++);
                dto.OtherSideGames = other?.Total ?? 0;
                dto.Queries = queries;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger?.LogWarning(ex, "Explorer-Pfade: Gegenprobe mit der anderen Seite am Zug gescheitert");
            }
        }

        if (targetStats?.Opening is { } name) dto.Opening = new ExplorerOpeningDto { Eco = targetStats.Eco, Name = name };
        else if (paths.Count > 0 && paths[0].Edges.LastOrDefault(e => e.Opening is not null) is { Opening: not null } named)
            dto.Opening = new ExplorerOpeningDto { Eco = named.Eco, Name = named.Opening };

        _logger?.LogInformation("Explorer-Pfade: {Found} Zugfolgen, {Queries} Abfragen, {Searched} Stellungen, {Ms} ms{Cut}",
            dto.Paths.Count, queries, dto.Searched, clock.ElapsedMilliseconds, truncated ? " (abgeschnitten)" : "");
        return dto;
    }

    /// <summary>Unter so vielen Partien in der Zielstellung fragt die Suche auch die andere Seite am Zug ab.</summary>
    public const long OtherSideProbeBelow = 1000;

    /// <summary>Dieselbe Stellung mit der anderen Seite am Zug (en passant fällt weg).</summary>
    internal static string OtherSideFen(Pos target)
    {
        var f = target.ToFen().Split(' ');
        f[1] = f[1] == "w" ? "b" : "w";
        if (f.Length > 3) f[3] = "-";
        return string.Join(' ', f);
    }

    /// <summary>Gewicht je noch nötigem Zug in der Reihenfolge der offenen Knoten (ln ≈ 0,3 je Zug).</summary>
    private const double NeedWeight = 1.2;

    private static double MaxEstimate(List<Node> open)
    {
        var max = 0.0;
        foreach (var n in open) max = Math.Max(max, Math.Exp(n.Priority));
        return max;
    }

    /// <summary>Mit zehn Funden: unter diesem Bruchteil der besten Zugfolge lohnt keine weitere Suche (0,1 %).</summary>
    public const double MinRelative = 0.001;

    /// <summary>Mit WENIGER als zehn Funden (0.723.2): eine Zugfolge unter 0,5 % der besten ist für den Nutzer wertlos. Mit
    /// 0,1 % leerte die Suche dort alle machbaren Umwege (Springer raus und zurück …) — gemessen am lokalen Explorer:
    /// Alapin 272 → 140 Abfragen (Weg 6 mit 0,04 % entfällt), Berlin 139 → 90, Wege 1–5 unverändert.</summary>
    public const double DefaultMinShareOfBest = 0.005;
    /// <summary>Für Messungen einstellbar; Vorgabe <see cref="DefaultMinShareOfBest"/>.</summary>
    public double MinShareOfBest { get; set; } = DefaultMinShareOfBest;

    /// <summary>Ab wo eine offene Stellung nichts mehr beitragen kann: mit zehn Funden die Wahrscheinlichkeit der zehntbesten,
    /// mindestens <see cref="MinRelative"/> der besten; mit weniger <paramref name="minShareOfBest"/> der besten (sonst liefe
    /// eine Stellung mit wenigen Zugfolgen immer ins Budget).</summary>
    private static double Cutoff(List<Node> found, double minShareOfBest)
    {
        var best = Enumerate(found).Where(p => p.Edges.Count > 0).Select(p => p.Prob).OrderByDescending(p => p).Take(MaxPaths).ToList();
        if (best.Count == 0) return 0;
        return best.Count < MaxPaths ? best[0] * minShareOfBest : Math.Max(best[^1], best[0] * MinRelative);
    }

    private sealed record PathEdge(string San, string Uci, string? Opening, string? Eco);
    private sealed record Path(List<PathEdge> Edges, double Prob);

    /// <summary>Alle Zugfolgen zu den Zielknoten (rückwärts über alle Vorgänger), höchstens 2000.</summary>
    private static List<Path> Enumerate(List<Node> found)
    {
        var result = new List<Path>();
        void Walk(Node n, List<PathEdge> suffix, double prob)
        {
            if (result.Count >= 2000) return;
            if (n.Parents.Count == 0)
            {
                var edges = new List<PathEdge>(suffix);
                edges.Reverse();
                result.Add(new Path(edges, prob));
                return;
            }
            foreach (var (parent, san, uci, share, opening, eco) in n.Parents)
            {
                suffix.Add(new PathEdge(san, uci, opening, eco));
                Walk(parent, suffix, prob * share);
                suffix.RemoveAt(suffix.Count - 1);
            }
        }
        foreach (var t in found) Walk(t, new List<PathEdge>(), 1);
        return result;
    }

    /// <summary>Schlüssel im eigenen Speicher — eigener Präfix, damit die 24 h nicht mit den 1-h-Einträgen des Lochfinders
    /// (<see cref="RepertoireExplorerService.LocalMemoryKey"/>) verwechselt werden.</summary>
    internal static string PathCacheKey(string fen) => "explorer:paths:" + Query.CachePrefix + RepertoireReach.Key(fen);

    private async Task<ExplorerPositionStats?> FetchAsync(string fen, CancellationToken ct, Action counted)
    {
        var key = PathCacheKey(fen);
        if (_pathCache.TryGetValue<ExplorerPositionStats>(key, out var own) && own is not null) return own;
        // Was der Lochfinder in der letzten Stunde geholt hat, gilt auch hier.
        var shared = RepertoireExplorerService.LocalMemoryKey(Query, RepertoireReach.Key(fen));
        if (_memory.TryGetValue<ExplorerPositionStats>(shared, out var hit) && hit is not null)
        {
            Remember(key, hit);
            return hit;
        }
        counted();
        var stats = await _fetch!(fen, ct);
        if (stats is not null)
        {
            _memory.Set(shared, stats, RepertoireExplorerService.LocalMemoryTtl);
            Remember(key, stats);
        }
        return stats;
    }

    private void Remember(string key, ExplorerPositionStats stats) =>
        _pathCache.Set(key, stats, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = PathCacheTtl });

    /// <summary>Die Schranken (2)–(5) der Klassenbeschreibung gegen EINE Zielstellung.</summary>
    internal sealed class Bounds
    {
        private readonly Pos _target;
        private readonly int _maxDepth;
        private readonly int[] _targetCounts;

        public Bounds(Pos target, int maxDepth)
        {
            _target = target;
            _maxDepth = maxDepth;
            _targetCounts = Counts(target);
        }

        private static int[] Counts(Pos p)
        {
            var c = new int[128];
            foreach (var ch in p.Board) if (ch != '.') c[ch]++;
            return c;
        }

        /// <summary>Kann von <paramref name="cur"/> (nach <paramref name="depth"/> Halbzügen) die Zielstellung bis zur Grenze
        /// noch erreicht werden? <paramref name="need"/> = Untergrenze der noch nötigen Züge beider Farben.</summary>
        public bool Feasible(Pos cur, int depth, out int need)
        {
            need = 0;
            var rest = _maxDepth - depth;
            if (rest < 0) return false;

            var diff = 0;
            for (var i = 0; i < 64; i++) if (cur.Board[i] != _target.Board[i]) diff++;
            if (diff > 2 * rest + 4) return false;

            var counts = Counts(cur);
            foreach (var ch in "PNBRQKpnbrqk") if (counts[ch] < _targetCounts[ch]) return false;
            var whiteMaterial = "PNBRQK".Sum(ch => counts[ch]) - "PNBRQK".Sum(ch => _targetCounts[ch]);
            var blackMaterial = "pnbrqk".Sum(ch => counts[ch]) - "pnbrqk".Sum(ch => _targetCounts[ch]);

            // Eigene Züge bis zur Grenze: wer am Zug ist, hat bei ungeradem Rest einen mehr.
            var whiteMoves = cur.WhiteToMove ? (rest + 1) / 2 : rest / 2;
            var blackMoves = rest - whiteMoves;

            var w = SideNeed(cur, white: true, captures: blackMaterial);
            var b = SideNeed(cur, white: false, captures: whiteMaterial);
            if (w < 0 || b < 0 || w > whiteMoves || b > blackMoves) return false;
            need = w + b;
            return true;
        }

        /// <summary>Untergrenze der Züge einer Farbe, oder -1, wenn die Bauern das Ziel nicht mehr erreichen können
        /// (<paramref name="captures"/> = so viele gegnerische Steine dürfen noch geschlagen werden).</summary>
        private int SideNeed(Pos cur, bool white, int captures)
        {
            var pawn = white ? 'P' : 'p';
            var misplaced = 0;
            var have = new List<int>();
            var want = new List<int>();
            for (var i = 0; i < 64; i++)
            {
                var t = _target.Board[i];
                var c = cur.Board[i];
                if (c == pawn) have.Add(i);
                if (t == '.' || char.IsUpper(t) != white) continue;
                if (t == pawn) want.Add(i);
                else if (c != t) misplaced++;
            }
            if (misplaced > 0 && (white ? cur.CastleK || cur.CastleQ : cur.CastleKk || cur.CastleQq)) misplaced--;   // Rochade füllt zwei
            var pawnMoves = PawnMatching(have, want, white, captures);
            return pawnMoves < 0 ? -1 : misplaced + pawnMoves;
        }

        /// <summary>Jeder Zielbauer bekommt einen eigenen Bauern, der ihn vorwärts erreichen kann (Linienwechsel nur schlagend,
        /// höchstens <paramref name="captures"/> insgesamt). Liefert die kleinste Zahl an Bauernzügen, oder -1.</summary>
        private static int PawnMatching(List<int> have, List<int> want, bool white, int captures)
        {
            if (want.Count == 0) return 0;
            if (want.Count > have.Count) return -1;
            var n = have.Count;
            // dp[mask] = (Züge, Schlagzüge) — minimiert nach Zügen, Schlagzüge als zweite Bedingung; bei höchstens 8 Bauern billig.
            var size = 1 << n;
            var moves = new int[size];
            var caps = new int[size];
            Array.Fill(moves, int.MaxValue);
            moves[0] = 0;
            for (var mask = 0; mask < size; mask++)
            {
                if (moves[mask] == int.MaxValue) continue;
                var k = System.Numerics.BitOperations.PopCount((uint)mask);
                if (k >= want.Count) continue;
                var t = want[k];
                for (var j = 0; j < n; j++)
                {
                    if ((mask & (1 << j)) != 0) continue;
                    var cost = PawnCost(have[j], t, white, out var fileDiff);
                    if (cost < 0) continue;
                    var next = mask | (1 << j);
                    var m = moves[mask] + cost;
                    var c = caps[mask] + fileDiff;
                    if (c > captures) continue;
                    if (m < moves[next] || (m == moves[next] && c < caps[next])) { moves[next] = m; caps[next] = c; }
                }
            }
            var best = int.MaxValue;
            for (var mask = 0; mask < size; mask++)
                if (System.Numerics.BitOperations.PopCount((uint)mask) == want.Count) best = Math.Min(best, moves[mask]);
            return best == int.MaxValue ? -1 : best;
        }

        /// <summary>Kleinste Zahl an Zügen, mit der ein Bauer von <paramref name="from"/> nach <paramref name="to"/> kommt
        /// (Doppelschritt von der Grundreihe), oder -1.</summary>
        private static int PawnCost(int from, int to, bool white, out int fileDiff)
        {
            fileDiff = Math.Abs(from % 8 - to % 8);
            var ranks = white ? to / 8 - from / 8 : from / 8 - to / 8;
            if (ranks < 0 || fileDiff > ranks) return -1;
            if (ranks == 0) return 0;
            var home = white ? from / 8 == 1 : from / 8 == 6;
            return home && ranks - fileDiff >= 2 ? ranks - 1 : ranks;
        }
    }

    /// <summary>Eine Stellung ohne Zugzähler: Brett (a1 = 0 … h8 = 63), Seite am Zug, Rochaderechte, en passant. Bewusst klein —
    /// die Züge kommen legal vom Explorer und müssen nur nachgezogen werden.</summary>
    internal sealed class Pos
    {
        public char[] Board { get; } = new char[64];
        public bool WhiteToMove { get; private set; }
        public bool CastleK { get; private set; }
        public bool CastleQ { get; private set; }
        public bool CastleKk { get; private set; }
        public bool CastleQq { get; private set; }
        public int EnPassant { get; private set; } = -1;

        public string Placement
        {
            get
            {
                var sb = new StringBuilder();
                for (var r = 7; r >= 0; r--)
                {
                    var empty = 0;
                    for (var f = 0; f < 8; f++)
                    {
                        var c = Board[r * 8 + f];
                        if (c == '.') { empty++; continue; }
                        if (empty > 0) { sb.Append(empty); empty = 0; }
                        sb.Append(c);
                    }
                    if (empty > 0) sb.Append(empty);
                    if (r > 0) sb.Append('/');
                }
                return sb.ToString();
            }
        }

        /// <summary>Vergleich mit der Zielstellung: Brett + Seite am Zug.</summary>
        public string BoardKey => Placement + (WhiteToMove ? " w" : " b");

        /// <summary>Schlüssel der Zugumstellung: Brett, Seite, Rochaderechte (die bestimmen, wie es weitergeht).</summary>
        public string SearchKey => BoardKey + " " + Castling;

        private string Castling
        {
            get
            {
                var s = (CastleK ? "K" : "") + (CastleQ ? "Q" : "") + (CastleKk ? "k" : "") + (CastleQq ? "q" : "");
                return s.Length == 0 ? "-" : s;
            }
        }

        public string ToFen() =>
            $"{Placement} {(WhiteToMove ? 'w' : 'b')} {Castling} {(EnPassant < 0 ? "-" : Square(EnPassant))} 0 1";

        private static string Square(int i) => $"{(char)('a' + i % 8)}{(char)('1' + i / 8)}";

        private static int ParseSquare(string s) =>
            s.Length == 2 && s[0] is >= 'a' and <= 'h' && s[1] is >= '1' and <= '8' ? (s[1] - '1') * 8 + (s[0] - 'a') : -1;

        /// <summary>Liest eine FEN (Brett + Seite Pflicht, Rest optional). Prüft: 8×8, je genau ein König, keine Bauern auf der
        /// 1./8. Reihe. Rochaderechte fehlen → aus der Stellung abgeleitet (König und Turm auf den Ausgangsfeldern).</summary>
        public static Pos? Parse(string? fen)
        {
            if (string.IsNullOrWhiteSpace(fen) || fen.Length > 100) return null;
            var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            var p = new Pos();
            var ranks = parts[0].Split('/');
            if (ranks.Length != 8) return null;
            for (var r = 0; r < 8; r++)
            {
                var f = 0;
                foreach (var c in ranks[7 - r])
                {
                    if (c is >= '1' and <= '8')
                    {
                        for (var k = 0; k < c - '0'; k++) { if (f >= 8) return null; p.Board[r * 8 + f++] = '.'; }
                    }
                    else if ("pnbrqkPNBRQK".Contains(c))
                    {
                        if (f >= 8) return null;
                        p.Board[r * 8 + f++] = c;
                    }
                    else return null;
                }
                if (f != 8) return null;
            }
            if (p.Board.Count(c => c == 'K') != 1 || p.Board.Count(c => c == 'k') != 1) return null;
            for (var f = 0; f < 8; f++)
                if (p.Board[f] is 'P' or 'p' || p.Board[56 + f] is 'P' or 'p') return null;
            if (parts[1] is not ("w" or "b")) return null;
            p.WhiteToMove = parts[1] == "w";
            var castling = parts.Length > 2 ? parts[2] : null;
            if (castling is null)
            {
                p.CastleK = p.Board[4] == 'K' && p.Board[7] == 'R';
                p.CastleQ = p.Board[4] == 'K' && p.Board[0] == 'R';
                p.CastleKk = p.Board[60] == 'k' && p.Board[63] == 'r';
                p.CastleQq = p.Board[60] == 'k' && p.Board[56] == 'r';
            }
            else if (castling != "-")
            {
                if (castling.Any(c => !"KQkq".Contains(c))) return null;
                p.CastleK = castling.Contains('K');
                p.CastleQ = castling.Contains('Q');
                p.CastleKk = castling.Contains('k');
                p.CastleQq = castling.Contains('q');
            }
            if (parts.Length > 3 && parts[3] != "-")
            {
                p.EnPassant = ParseSquare(parts[3]);
                if (p.EnPassant < 0) return null;
            }
            return p;
        }

        private Pos Clone()
        {
            var p = new Pos
            {
                WhiteToMove = WhiteToMove, CastleK = CastleK, CastleQ = CastleQ, CastleKk = CastleKk, CastleQq = CastleQq,
            };
            Array.Copy(Board, p.Board, 64);
            return p;
        }

        /// <summary>Der König schlägt den eigenen Turm (so schreibt der Explorer die Rochade: <c>e1h1</c>)?</summary>
        private bool IsCastle(int from, int to) =>
            char.ToUpperInvariant(Board[from]) == 'K'
            && (Math.Abs(to % 8 - from % 8) == 2
                || (Board[to] != '.' && char.ToUpperInvariant(Board[to]) == 'R' && char.IsUpper(Board[to]) == char.IsUpper(Board[from])));

        /// <summary>Die Rochade in der üblichen Schreibweise (<c>e1g1</c> statt <c>e1h1</c>), sonst unverändert.</summary>
        public static string StandardUci(Pos before, string uci)
        {
            if (uci.Length < 4) return uci;
            var from = ParseSquare(uci[..2]);
            var to = ParseSquare(uci.Substring(2, 2));
            if (from < 0 || to < 0 || !before.IsCastle(from, to)) return uci;
            var kingTo = to % 8 > from % 8 ? from - from % 8 + 6 : from - from % 8 + 2;
            return uci[..2] + Square(kingTo);
        }

        /// <summary>Zieht <paramref name="uci"/> nach (ohne Legalitätsprüfung) — oder <c>null</c> bei Unsinn.</summary>
        public Pos? Apply(string uci)
        {
            if (uci.Length is < 4 or > 5) return null;
            var from = ParseSquare(uci[..2]);
            var to = ParseSquare(uci.Substring(2, 2));
            if (from < 0 || to < 0 || Board[from] == '.') return null;
            var piece = Board[from];
            var white = char.IsUpper(piece);
            if (white != WhiteToMove) return null;
            var p = Clone();
            p.WhiteToMove = !WhiteToMove;
            var rankBase = from - from % 8;

            if (IsCastle(from, to))
            {
                var kingside = to % 8 > from % 8;
                var rookFrom = Board[to] != '.' && char.ToUpperInvariant(Board[to]) == 'R' ? to : rankBase + (kingside ? 7 : 0);
                var rook = Board[rookFrom];
                p.Board[from] = '.';
                p.Board[rookFrom] = '.';
                p.Board[rankBase + (kingside ? 6 : 2)] = piece;
                p.Board[rankBase + (kingside ? 5 : 3)] = rook;
            }
            else
            {
                if (char.ToUpperInvariant(piece) == 'P' && from % 8 != to % 8 && Board[to] == '.')
                    p.Board[from - from % 8 + to % 8] = '.';   // en passant: der geschlagene Bauer steht neben dem Startfeld
                p.Board[from] = '.';
                var placed = piece;
                if (uci.Length == 5)
                {
                    var promo = uci[4];
                    if (!"nbrq".Contains(promo)) return null;
                    placed = white ? char.ToUpperInvariant(promo) : promo;
                }
                p.Board[to] = placed;
                if (char.ToUpperInvariant(piece) == 'P' && Math.Abs(to - from) == 16) p.EnPassant = (from + to) / 2;
            }

            if (piece == 'K') { p.CastleK = false; p.CastleQ = false; }
            if (piece == 'k') { p.CastleKk = false; p.CastleQq = false; }
            foreach (var sq in new[] { from, to })
            {
                if (sq == 7) p.CastleK = false;
                if (sq == 0) p.CastleQ = false;
                if (sq == 63) p.CastleKk = false;
                if (sq == 56) p.CastleQq = false;
            }
            return p;
        }
    }
}
