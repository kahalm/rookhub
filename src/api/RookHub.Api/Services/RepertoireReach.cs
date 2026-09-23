using Chess;

namespace RookHub.Api.Services;

/// <summary>
/// „Wie oft landet man in welcher Repertoire-Stellung?" — die Rechnung hinter dem Lochfinder und
/// dem Trainer-Modus „Häufigste zuerst". Reine Logik ohne DB und HTTP: die Explorer-Daten kommen
/// über einen Delegaten herein, damit Tests sie festnageln können.
///
/// <para><b>Modell</b> (übernommen aus Opening Fenix, GPLv3, dort <c>priority_service.py</c>):
/// die Wurzel hat Wahrscheinlichkeit 1. Ist der Nutzer am Zug, teilt sie sich gleichmäßig auf die
/// Repertoire-Züge auf (er spielt einen davon). Ist der Gegner am Zug, verteilt sie sich nach den
/// Explorer-Häufigkeiten. Zugumstellungen summieren sich, weil die Knoten Stellungen sind, keine
/// Zugfolgen.</para>
///
/// <para><b>Loch</b> = ein Gegnerzug in einer Stellung, für die das Repertoire eine Antwort hat
/// (sonst ist es das bewusste Ende einer Linie), der dort mindestens so oft gespielt wird wie die
/// Schwelle und zu einer Stellung führt, die NIRGENDS im Repertoire vorkommt — auch nicht über eine
/// andere Zugfolge.</para>
/// </summary>
public static class RepertoireReach
{
    /// <summary>Stellungen, die man seltener als in 0,02 % der Partien erreicht (1 in 5000), werden
    /// nicht mehr abgefragt — Löcher darunter träfen einen praktisch nie, und jede Abfrage kostet
    /// einen Explorer-Aufruf.</summary>
    public const double MinReach = 0.0002;

    /// <summary>Unter so vielen Partien sind Anteile Zufall: keine Löcher, keine Weitergabe.</summary>
    public const long MinGames = 10;

    /// <summary>Repertoire-Gegnerzüge, die der Explorer gar nicht kennt, bekommen „weniger als eine
    /// Partie" — winzig, aber nicht null, damit ihre Linien im Trainer noch geordnet werden.</summary>
    private const double UnlistedGames = 0.5;

    /// <summary>Stellungs-Schlüssel: Brett, Zugrecht, Rochade — OHNE en passant. Dieselbe Regel wie
    /// <c>RepertoirePositionLookupService.NormalizeKey</c> und <c>normalizeFen</c> in
    /// <c>position-filter.util.ts</c>: der Client rechnet mit chess.js, das das ep-Feld anders setzt
    /// als Gera.Chess, und die Linien-Häufigkeiten werden über genau diesen Schlüssel zugeordnet.</summary>
    public static string Key(string fen)
    {
        var parts = fen.Split(' ');
        return parts.Length >= 3 ? string.Join(' ', parts.Take(3)) : fen;
    }

    public const string StandardStartKey = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq";

    public sealed class Node
    {
        public required string Key { get; init; }
        /// <summary>Volle FEN beim ersten Erreichen — für Explorer-Abfrage und Brett-Anzeige.</summary>
        public required string Fen { get; init; }
        public required bool UserToMove { get; init; }
        /// <summary>Die im Repertoire gespielten Züge (je Zielstellung einmal).</summary>
        public List<(string San, Node Child)> Children { get; } = new();
        public bool HasParent { get; set; }

        // Vom Durchlauf gesetzt:
        public int Depth { get; set; } = int.MaxValue;
        public Node? Parent { get; set; }
        public string? ViaSan { get; set; }
        public double P { get; set; }
        public bool Known { get; set; }
    }

    public sealed class Graph
    {
        public required char Color { get; init; }
        public Dictionary<string, Node> Nodes { get; } = new(StringComparer.Ordinal);
        public List<Node> Starts { get; } = new();
        /// <summary>Je Linie die Stellungen ihrer Hauptvariante, Startstellung zuerst.</summary>
        public List<List<Node>> Mainlines { get; } = new();

        /// <summary>Stellungen mit Gegner am Zug UND Repertoire-Antwort — nur für die braucht es Explorer-Daten.</summary>
        public IEnumerable<Node> OpponentBranches => Nodes.Values.Where(n => !n.UserToMove && n.Children.Count > 0);
    }

    public sealed record Hole(Node Position, ExplorerMoveStat Move, double Share, double Frequency, long PositionGames);

    public sealed class Result
    {
        public List<Hole> Holes { get; } = new();
        /// <summary>Endstellung (Schlüssel) je Linie → Häufigkeit der tiefsten bekannten Stellung.</summary>
        public Dictionary<string, double> LineFrequencies { get; } = new(StringComparer.Ordinal);
        /// <summary>JEDE Repertoire-Stellung mit bekannter Häufigkeit (Schlüssel → 0…1) — für den Baum.</summary>
        public Dictionary<string, double> PositionFrequencies { get; } = new(StringComparer.Ordinal);
        public int Analyzed { get; set; }
        public int Pending { get; set; }
    }

    /// <summary>Baut den Stellungsgraphen aus den Linien EINER Farbe (<c>'w'</c>/<c>'b'</c>).
    /// Linien mit unbrauchbarer <c>[FEN]</c> fallen weg; ein illegaler Zug beendet die Linie dort
    /// (wie in <see cref="RepertoireAnalyzeService"/>).</summary>
    public static Graph Build(IEnumerable<ParsedSection> sections, char color)
    {
        var g = new Graph { Color = color };
        foreach (var section in sections)
        {
            ChessBoard board;
            if (section.StartFen == null) board = new ChessBoard();
            else { try { board = ChessBoard.LoadFromFen(section.StartFen); } catch { continue; } }

            var start = GetOrAdd(g, board);
            if (!g.Starts.Contains(start)) g.Starts.Add(start);
            var mainline = new List<Node> { start };
            Walk(g, board, section.Moves, start, mainline);
            g.Mainlines.Add(mainline);
        }
        return g;
    }

    private static void Walk(Graph g, ChessBoard board, List<PgnMove> moves, Node from, List<Node>? mainline)
    {
        int made = 0;
        var cur = from;
        foreach (var move in moves)
        {
            // Varianten sind Alternativen zu DIESEM Zug — sie zweigen in der Stellung davor ab.
            foreach (var variation in move.Variations)
                Walk(g, board, variation, cur, null);

            bool ok;
            try { ok = board.Move(move.San); }
            catch { ok = false; }
            if (!ok) break;
            made++;

            var san = board.ExecutedMoves.Count > 0 ? board.ExecutedMoves[^1].San : null;
            var next = GetOrAdd(g, board);
            if (!cur.Children.Any(c => ReferenceEquals(c.Child, next)))
                cur.Children.Add((string.IsNullOrEmpty(san) ? move.San : san, next));
            next.HasParent = true;
            mainline?.Add(next);
            cur = next;
        }
        for (int i = 0; i < made; i++) board.Cancel();
    }

    private static Node GetOrAdd(Graph g, ChessBoard board)
    {
        var fen = board.ToFen();
        var key = Key(fen);
        if (g.Nodes.TryGetValue(key, out var n)) return n;
        var side = fen.Split(' ') is { Length: > 1 } p && p[1] == "b" ? 'b' : 'w';
        n = new Node { Key = key, Fen = fen, UserToMove = side == g.Color };
        g.Nodes[key] = n;
        return n;
    }

    /// <summary>
    /// Verteilt die Wahrscheinlichkeit von den Wurzeln aus Schicht für Schicht (nach Zugtiefe),
    /// fragt je Gegner-Stellung die Explorer-Daten ab und sammelt Löcher und Linien-Häufigkeiten.
    /// <paramref name="stats"/> liefert <c>null</c>, wenn die Daten (noch) nicht da sind — die
    /// Stellung zählt dann als offen, und alles darunter bleibt unbekannt.
    /// <para><paramref name="prefetchLayer"/> (optional) bekommt VOR jeder Tiefenschicht die Stellungen,
    /// die darin abgefragt werden — erst dann stehen ihre Wahrscheinlichkeiten fest. Eine schnelle
    /// Quelle holt sie dort auf einmal (parallel); was sie auslässt, fragt <paramref name="stats"/>
    /// einzeln nach.</para>
    /// </summary>
    public static async Task<Result> EvaluateAsync(
        Graph g, Func<Node, Task<ExplorerPositionStats?>> stats, double threshold, CancellationToken ct = default,
        Func<IReadOnlyList<Node>, Task>? prefetchLayer = null, ISet<Node>? onlyFor = null)
    {
        var result = new Result();
        var order = LayerByDepth(g);

        for (int i = 0; i < order.Count;)
        {
            var end = i;
            while (end < order.Count && order[end].Depth == order[i].Depth) end++;
            if (prefetchLayer is not null)
            {
                var wanted = order.Skip(i).Take(end - i).Where(n => NeedsStats(n) && (onlyFor is null || onlyFor.Contains(n))).ToList();
                if (wanted.Count > 0) await prefetchLayer(wanted);
            }
            for (; i < end; i++) await VisitAsync(order[i]);
        }

        async Task VisitAsync(Node node)
        {
            ct.ThrowIfCancellationRequested();
            if (!node.Known || node.Children.Count == 0) return;

            if (node.UserToMove)
            {
                var part = node.P / node.Children.Count;
                foreach (var (_, child) in node.Children) Add(child, part);
                return;
            }

            if (node.P < MinReach) return;
            // Nur ein Ausschnitt gefragt (Baum beim Durchklicken): Gegner-Stellungen außerhalb davon
            // weder abfragen noch als offen zählen — sie tragen zu den gesuchten Stellungen nichts bei.
            if (onlyFor is not null && !onlyFor.Contains(node)) return;
            var st = await stats(node);
            if (st is null) { result.Pending++; return; }
            result.Analyzed++;
            if (st.Total < MinGames) return;

            var reached = new HashSet<Node>();
            ChessBoard? board = null;
            foreach (var m in st.Moves)
            {
                var share = m.Games / (double)st.Total;
                var direct = node.Children.FirstOrDefault(c => SameSan(c.San, m.San)).Child;
                if (direct is not null)
                {
                    if (reached.Add(direct)) Add(direct, node.P * share);
                    continue;
                }
                // Seltener Zug, den das Repertoire nicht spielt: weder Loch noch (spürbar) Weitergabe.
                if (share < threshold) continue;

                board ??= TryLoad(node.Fen);
                var target = board is null ? null : TargetKey(board, m.San);
                if (target is null) continue;
                if (g.Nodes.TryGetValue(target, out var transposed))
                {
                    // Zugumstellung: die Zielstellung steht im Repertoire, nur über eine andere Zugfolge.
                    if (reached.Add(transposed)) Add(transposed, node.P * share);
                    continue;
                }
                result.Holes.Add(new Hole(node, m, share, node.P * share, st.Total));
            }

            // Repertoire-Züge, die der Explorer nicht aufführt (zu selten für seine Liste).
            foreach (var (_, child) in node.Children)
                if (!reached.Contains(child)) Add(child, node.P * UnlistedGames / st.Total);
        }

        foreach (var n in g.Nodes.Values)
            if (n.Known && n.P > 0) result.PositionFrequencies[n.Key] = n.P;

        foreach (var line in g.Mainlines)
        {
            var deepest = line.LastOrDefault(n => n.Known && n.P > 0);
            if (deepest is null) continue;
            var key = line[^1].Key;
            if (!result.LineFrequencies.TryGetValue(key, out var have) || deepest.P > have)
                result.LineFrequencies[key] = deepest.P;
        }
        return result;
    }

    /// <summary>
    /// Die gesuchten Stellungen samt ALLER Stellungen, von denen aus man sie erreicht (auch über
    /// Zugumstellungen) — nur deren Explorer-Daten braucht es für ihre Häufigkeit. Schlüssel, die im
    /// Graphen nicht vorkommen, fallen weg.
    /// </summary>
    public static HashSet<Node> AncestorsOf(Graph g, IEnumerable<string> keys)
    {
        var parents = new Dictionary<Node, List<Node>>();
        foreach (var n in g.Nodes.Values)
            foreach (var (_, child) in n.Children)
            {
                if (!parents.TryGetValue(child, out var list)) parents[child] = list = new List<Node>();
                list.Add(n);
            }
        var seen = new HashSet<Node>();
        var queue = new Queue<Node>();
        foreach (var key in keys)
            if (g.Nodes.TryGetValue(key, out var n) && seen.Add(n)) queue.Enqueue(n);
        while (queue.Count > 0)
            if (parents.TryGetValue(queue.Dequeue(), out var ps))
                foreach (var p in ps)
                    if (seen.Add(p)) queue.Enqueue(p);
        return seen;
    }

    /// <summary>Die Zugfolge von der Wurzel bis zu dieser Stellung (kürzester Weg), dazu die
    /// Startstellung, falls es nicht die Grundstellung ist.</summary>
    public static (string? StartFen, List<string> Sans) PathTo(Node node)
    {
        var sans = new List<string>();
        var cur = node;
        while (cur.Parent is not null)
        {
            sans.Add(cur.ViaSan ?? "?");
            cur = cur.Parent;
        }
        sans.Reverse();
        return (cur.Key == StandardStartKey ? null : cur.Fen, sans);
    }

    /// <summary>Braucht diese Stellung Explorer-Daten? Gegner am Zug, Repertoire-Antwort, oft genug erreicht.</summary>
    private static bool NeedsStats(Node n) => n.Known && !n.UserToMove && n.Children.Count > 0 && n.P >= MinReach;

    private static void Add(Node child, double p)
    {
        child.P += p;
        child.Known = true;
    }

    /// <summary>Wurzeln = Linien-Starts in der Grundstellung oder ohne Vorgänger (Linien mit eigener
    /// Startstellung). Ein Start, den eine andere Linie erreicht, ist KEINE Wurzel — sonst bekäme er
    /// Wahrscheinlichkeit 1, obwohl man ihn nur über die andere Linie erreicht.</summary>
    private static List<Node> LayerByDepth(Graph g)
    {
        var roots = g.Starts.Where(s => s.Key == StandardStartKey || !s.HasParent).ToList();
        if (roots.Count == 0) roots = g.Starts.ToList();   // nur Kreise (Zugwiederholung) — dann eben alle

        var queue = new Queue<Node>();
        foreach (var r in roots)
        {
            r.Depth = 0;
            r.P = 1;
            r.Known = true;
            queue.Enqueue(r);
        }
        var order = new List<Node>();
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            order.Add(n);
            foreach (var (san, child) in n.Children)
            {
                if (child.Depth != int.MaxValue) continue;
                child.Depth = n.Depth + 1;
                child.Parent = n;
                child.ViaSan = san;
                queue.Enqueue(child);
            }
        }
        return order;
    }

    private static ChessBoard? TryLoad(string fen)
    {
        try { return ChessBoard.LoadFromFen(fen); }
        catch { return null; }
    }

    private static string? TargetKey(ChessBoard board, string san)
    {
        try
        {
            if (!board.Move(san)) return null;
            var key = Key(board.ToFen());
            board.Cancel();
            return key;
        }
        catch { return null; }
    }

    /// <summary>SAN-Vergleich ohne Schach-/Matt-Zeichen und mit „O-O" statt „0-0".</summary>
    internal static bool SameSan(string? a, string? b) =>
        a is not null && b is not null && CanonSan(a) == CanonSan(b);

    private static string CanonSan(string san) =>
        san.Trim().TrimEnd('+', '#', '!', '?').Replace("0-0-0", "O-O-O").Replace("0-0", "O-O");
}
