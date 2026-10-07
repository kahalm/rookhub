using Chess;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// „Welche Linien meines Repertoires soll ich gegen DIESEN Gegner trainieren?" (Wunsch 2026-10-07) — die Rechnung hinter
/// dem Abschnitt „Trainingslinien" der Spielerkarte (Spielervorbereitung und LeagueHub). Reine Logik ohne DB und HTTP:
/// das Repertoire kommt als <see cref="RepertoireReach.Graph"/> herein, die Partien des Gegners als Zugfolgen.
///
/// <para><b>Rechnung.</b> Gezählt werden nur Partien, in denen der Gegner die ANDERE Farbe als das Repertoire hatte. Je
/// Partie die ersten <see cref="MaxPlies"/> Halbzüge nachgespielt; je erreichter Stellung (Schlüssel wie
/// <see cref="RepertoireReach.Key"/>, Zugumstellungen fallen so von selbst zusammen) zählt, wie oft sie erreicht wurde
/// und in welche Stellung es von dort weiterging. Eine Linie (die Hauptvariante eines Repertoire-Abschnitts — dieselbe
/// Einheit, die der Trainer abfragt) hat die Wahrscheinlichkeit Π (Partien mit dem Gegnerzug der Linie / Partien, die die
/// Stellung erreicht UND dort weitergespielt haben) über alle Gegnerzüge der Linie; eigene Züge zählen 1. Dazu: wie viele
/// Partien die Stellung nach dem letzten Gegnerzug der Linie erreicht haben, und deren jüngstes Jahr.</para>
///
/// <para><b>Auffüllen (Wunsch 2026-10-07: „geh in seiner Partie einen Schritt vor … als ob er eins vorher abgewichen
/// ist").</b> Je Linie zählt, wie viele ihrer Gegnerzüge von vorne weg „getroffen" sind (<see cref="Line.Matched"/>): bis
/// zur ersten Stellung, die der Gegner nie erreicht oder in der er nie den Zug der Linie gespielt hat. Gereiht wird in
/// Stufen: zuerst die voll getroffenen nach Wahrscheinlichkeit, dann die mit EINEM fehlenden Gegnerzug nach der
/// Wahrscheinlichkeit ihres getroffenen Anfangs, dann mit zwei fehlenden … ; je Stufe mehr Partien zuerst, dann die
/// Reihenfolge im Repertoire. Linien, die er gar nicht trifft (<c>Matched = 0</c>), stehen ganz hinten („nie erreicht").</para>
///
/// <para><b>Aufwand.</b> Die Partien laufen als Präfixbaum durch EIN Brett (Zug, Unterbaum, Zug zurück): gemeinsame
/// Anfänge werden einmal gezogen, nicht je Partie.</para>
/// </summary>
public static class OpponentTrainingLines
{
    /// <summary>So viele Halbzüge je Partie zählen — tiefer gehen Repertoire-Linien selten, und danach ist jede Partie
    /// ohnehin ihr eigener Ast.</summary>
    public const int MaxPlies = 40;

    /// <summary>Eine Partie des Gegners: Hauptvariante (englische SAN) ab der Grundstellung, seine Farbe, das Jahr.</summary>
    public sealed record Game(IReadOnlyList<string> Sans, bool OpponentWhite, int? Year);

    /// <summary>Eine Linie des Repertoires, gereiht gegen den Gegner.</summary>
    /// <param name="Key">Linien-Schlüssel wie im Trainer (<c>?line=</c>, <see cref="ChessableTrainedLineService.LineKeyFromSans"/>).</param>
    /// <param name="End">Endstellung (Schlüssel) — wie die Linien-Häufigkeiten von „Häufigste zuerst".</param>
    /// <param name="StartFen">Eigene Startstellung der Linie, <c>null</c> = Grundstellung.</param>
    /// <param name="Index">Stelle im Repertoire (Abschnitt), für eine stabile Reihenfolge.</param>
    /// <param name="Probability">Wahrscheinlichkeit der ganzen Linie (0, sobald ein Gegnerzug fehlt).</param>
    /// <param name="Matched">Gegnerzüge der Linie, die er von vorne weg getroffen hat.</param>
    /// <param name="Missing">Gegnerzüge der Linie, die danach fehlen (0 = voll getroffen).</param>
    /// <param name="PrefixProbability">Wahrscheinlichkeit des getroffenen Anfangs (= <paramref name="Probability"/>, wenn voll).</param>
    /// <param name="PrefixReached">Partien, die die tiefste getroffene Stellung erreicht haben.</param>
    /// <param name="Source">Woher die Wahrscheinlichkeit kommt: <c>own</c> (nur seine Partien), <c>mixed</c>, <c>lichess</c> (nur
    /// Explorer) oder <c>none</c> (eine Gegner-Stellung ohne jede Quelle — dann gilt die Auffüllregel).</param>
    /// <param name="OwnMoves">Gegnerzüge der Linie aus seinen Partien.</param>
    /// <param name="LichessMoves">Gegnerzüge der Linie aus dem Lichess-Explorer (geschätzt).</param>
    /// <param name="LichessFrom">Halbzug (Index in <paramref name="Sans"/>) des ersten geschätzten Gegnerzugs.</param>
    /// <param name="Pending">Eine benötigte Explorer-Stellung kam im Zeitbudget nicht an.</param>
    public sealed record Line(string Key, string End, string? StartFen, string Chapter, IReadOnlyList<string> Sans, double Probability,
        int Reached, int? LastYear, bool NeverReached, int Index, int Matched = 0, int Missing = 0, double PrefixProbability = 0,
        int PrefixReached = 0, string Source = "own", int OwnMoves = 0, int LichessMoves = 0, int? LichessFrom = null, bool Pending = false)
    {
        /// <summary>Stufe der Reihung: 0 = voll getroffen, n = n Gegnerzüge fehlen, ganz hinten = gar nicht getroffen.</summary>
        internal int Tier => NeverReached ? int.MaxValue : Missing;
    }

    public sealed record Result(int Games, List<Line> Lines);

    /// <summary>Schätzung für Lücken (Wunsch 2026-10-07: „wenn gaaaanz wenig games vorhanden sind … nimm lichesspartien"): je
    /// Gegner-Stellung, in der er weniger als <see cref="MinOwn"/> Partien weitergespielt hat, die Zughäufigkeiten des Explorers
    /// (Spieler seiner Stärke) — <see cref="Explorer"/> liefert sie je Stellung oder <c>null</c>; <see cref="Pending"/> = Stellungen,
    /// die gebraucht, aber im Zeitbudget nicht geholt wurden.</summary>
    public sealed record Estimate(Func<RepertoireReach.Node, ExplorerPositionStats?> Explorer, int MinOwn, IReadOnlySet<string> Pending);

    /// <summary>Seine Partien, gezählt je Stellung des Repertoires — einmal, für <see cref="NeedsExplorer"/> und <see cref="Rank(RepertoireReach.Graph, IReadOnlyList{string}, Analysis, Estimate?)"/>.</summary>
    public sealed class Analysis
    {
        internal Analysis(int games, Dictionary<string, PositionStats> stats) { Games = games; Stats = stats; }
        public int Games { get; }
        internal Dictionary<string, PositionStats> Stats { get; }
    }

    /// <summary>Zählt die Partien des Gegners mit der anderen Farbe als <paramref name="graph"/>.</summary>
    public static Analysis Analyze(RepertoireReach.Graph graph, IEnumerable<Game> games)
    {
        var opponentWhite = graph.Color == 'b';
        var relevant = games.Where(g => g.OpponentWhite == opponentWhite).ToList();
        return new Analysis(relevant.Count, Count(relevant, graph));
    }

    /// <summary>Die Gegner-Stellungen der Linien, für die seine Partien nicht reichen (weniger als <paramref name="minOwn"/>
    /// weitergespielt) — genau die braucht die Schätzung vom Explorer; je Stellung einmal.</summary>
    public static List<RepertoireReach.Node> NeedsExplorer(RepertoireReach.Graph graph, Analysis a, int minOwn)
    {
        var need = new Dictionary<string, RepertoireReach.Node>(StringComparer.Ordinal);
        foreach (var nodes in graph.Mainlines)
            for (var k = 0; k + 1 < nodes.Count; k++)
            {
                var at = nodes[k];
                if (at.UserToMove || need.ContainsKey(at.Key)) continue;
                if ((a.Stats.GetValueOrDefault(at.Key)?.Continued ?? 0) < Math.Max(1, minOwn)) need[at.Key] = at;
            }
        return need.Values.ToList();
    }

    /// <summary>Wie oft der Explorer genau den Zug der Linie kennt (Anteil an allen Partien der Stellung); fehlt er in der Liste,
    /// „weniger als eine Partie" wie in <see cref="RepertoireReach"/> — winzig, nicht null.</summary>
    private static double ExplorerShare(ExplorerPositionStats e, RepertoireReach.Node at, string san, string targetKey)
    {
        var hit = e.Moves.FirstOrDefault(m => RepertoireReach.SameSan(m.San, san));
        if (hit is null)
        {
            Chess.ChessBoard? board = null;
            try { board = Chess.ChessBoard.LoadFromFen(at.Fen); } catch { /* ohne Brett nur der SAN-Vergleich */ }
            if (board is not null)
                foreach (var m in e.Moves)
                {
                    try
                    {
                        if (!board.Move(m.San)) continue;
                        var k = RepertoireReach.Key(board.ToFen());
                        board.Cancel();
                        if (k == targetKey) { hit = m; break; }
                    }
                    catch { /* unlesbarer Zug des Explorers */ }
                }
        }
        return hit is not null ? hit.Games / (double)e.Total : 0.5 / e.Total;
    }

    /// <summary>Was je Stellung gezählt wird (über alle Zugfolgen dorthin).</summary>
    internal sealed class PositionStats
    {
        public int Reached;
        /// <summary>Partien, die hier erreicht wurden UND innerhalb von <see cref="MaxPlies"/> weitergingen — der Nenner.</summary>
        public int Continued;
        public int? LastYear;
        /// <summary>Folgestellung (Schlüssel) → Partien.</summary>
        public readonly Dictionary<string, int> Next = new(StringComparer.Ordinal);
    }

    private sealed class Trie
    {
        public int Count;
        public int? LastYear;
        public readonly Dictionary<string, Trie> Children = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Die Linien von <paramref name="graph"/> (alle Abschnitte der Farbe <see cref="RepertoireReach.Graph.Color"/>) gegen
    /// die Partien <paramref name="games"/>, wahrscheinlichste zuerst (dann mehr Partien, dann Reihenfolge im Repertoire).
    /// <paramref name="chapters"/> = Kapitel (<c>[Black]</c>) je Hauptvariante, in derselben Reihenfolge wie
    /// <see cref="RepertoireReach.Graph.Mainlines"/>. Gleiche Linien (gleicher Schlüssel) erscheinen einmal.
    /// </summary>
    public static Result Rank(RepertoireReach.Graph graph, IReadOnlyList<string> chapters, IEnumerable<Game> games) =>
        Rank(graph, chapters, Analyze(graph, games), null);

    /// <summary>
    /// Wie oben; mit <paramref name="estimate"/> KOMBINIERT je Gegner-Stellung: hat er dort mindestens <see cref="Estimate.MinOwn"/>
    /// Partien weitergespielt, zählen seine Partien, sonst der Explorer (mindestens <see cref="RepertoireReach.MinGames"/> Partien).
    /// Wahrscheinlichkeit = Produkt über die Gegnerzüge aus der jeweiligen Quelle. Linien, bei denen eine Stellung keine Quelle hat,
    /// reihen sich wie bisher (Auffüllregel) HINTER allen mit Quelle; diese nach Wahrscheinlichkeit, dann mehr eigene Gegnerzüge,
    /// dann Partien, dann Reihenfolge im Repertoire.
    /// </summary>
    public static Result Rank(RepertoireReach.Graph graph, IReadOnlyList<string> chapters, Analysis analysis, Estimate? estimate)
    {
        var stats = analysis.Stats;

        var lines = new List<Line>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < graph.Mainlines.Count; i++)
        {
            var nodes = graph.Mainlines[i];
            var sans = SansOf(nodes);
            if (sans is null) continue;
            var key = ChessableTrainedLineService.LineKeyFromSans(sans);
            if (!seen.Add(key)) continue;

            var prefix = 1.0;
            var matched = 0;
            var opponentMoves = 0;
            var hit = true;                             // bisher jeder Gegnerzug getroffen
            var after = nodes[0];                       // Stellung nach dem letzten Gegnerzug (ohne Gegnerzug: der Start)
            var deepest = nodes[0];                     // Stellung nach dem letzten GETROFFENEN Gegnerzug
            for (var k = 0; k + 1 < nodes.Count; k++)
            {
                var at = nodes[k];
                if (at.UserToMove) continue;
                opponentMoves++;
                after = nodes[k + 1];
                if (!hit) continue;
                var st = stats.GetValueOrDefault(at.Key);
                var n = st?.Next.GetValueOrDefault(after.Key) ?? 0;
                if (st is null || st.Continued == 0 || n == 0) { hit = false; continue; }
                prefix = prefix * n / st.Continued;
                matched++;
                deepest = after;
            }
            var end = stats.GetValueOrDefault(after.Key);
            var reached = end?.Reached ?? 0;
            var missing = opponentMoves - matched;
            var never = opponentMoves > 0 && matched == 0;
            // Ohne Gegnerzug zählt der Start: wer ihn erreicht, spielt die Linie — sonst ist auch sie „nie erreicht".
            if (opponentMoves == 0 && reached == 0) never = true;
            var prefixReached = stats.GetValueOrDefault(deepest.Key)?.Reached ?? 0;
            var start = nodes[0].Key == RepertoireReach.StandardStartKey ? null : nodes[0].Fen;
            var line = new Line(key, nodes[^1].Key, start, i < chapters.Count ? chapters[i] : "", sans,
                missing == 0 && !never ? prefix : 0, reached, end?.LastYear, never, i, matched, missing, never ? 0 : prefix,
                prefixReached, missing == 0 && !never ? "own" : "none", matched);
            if (estimate is not null) line = Combine(line, nodes, sans, stats, estimate);
            lines.Add(line);
        }

        static IOrderedEnumerable<Line> FillRule(IEnumerable<Line> ls) => ls.OrderBy(l => l.Tier)
            .ThenByDescending(l => l.Missing == 0 ? l.Probability : l.PrefixProbability)
            .ThenByDescending(l => l.Missing == 0 ? l.Reached : l.PrefixReached)
            .ThenByDescending(l => l.Reached)
            .ThenBy(l => l.Index);

        if (estimate is null) return new Result(analysis.Games, FillRule(lines).ToList());
        var sourced = lines.Where(l => l.Source != "none").OrderByDescending(l => l.Probability).ThenByDescending(l => l.OwnMoves)
            .ThenByDescending(l => l.Reached).ThenBy(l => l.Index);
        return new Result(analysis.Games, sourced.Concat(FillRule(lines.Where(l => l.Source == "none"))).ToList());
    }

    /// <summary>Die kombinierte Wahrscheinlichkeit einer Linie (siehe <see cref="Rank(RepertoireReach.Graph, IReadOnlyList{string}, Analysis, Estimate?)"/>).</summary>
    private static Line Combine(Line line, List<RepertoireReach.Node> nodes, List<string> sans, Dictionary<string, PositionStats> stats,
        Estimate estimate)
    {
        var p = 1.0;
        int own = 0, lichess = 0;
        int? from = null;
        bool gap = false, pending = false;
        var minOwn = Math.Max(1, estimate.MinOwn);
        for (var k = 0; k + 1 < nodes.Count; k++)
        {
            var at = nodes[k];
            if (at.UserToMove) continue;
            var after = nodes[k + 1];
            var st = stats.GetValueOrDefault(at.Key);
            if (st is not null && st.Continued >= minOwn)
            {
                p = p * st.Next.GetValueOrDefault(after.Key) / st.Continued;
                own++;
                continue;
            }
            if (estimate.Explorer(at) is { } e && e.Total >= RepertoireReach.MinGames)
            {
                p *= ExplorerShare(e, at, sans[k], after.Key);
                lichess++;
                from ??= k;
                continue;
            }
            gap = true;
            if (estimate.Pending.Contains(at.Key)) pending = true;
        }
        if (gap) return line with { Source = "none", OwnMoves = own, LichessMoves = lichess, LichessFrom = from, Pending = pending };
        var source = lichess == 0 ? "own" : own == 0 && lichess > 0 ? "lichess" : "mixed";
        return line with
        {
            Probability = p, Source = source, OwnMoves = own, LichessMoves = lichess, LichessFrom = from,
            NeverReached = false,
        };
    }

    /// <summary>Die Züge einer Hauptvariante (so, wie das Brett sie schreibt); <c>null</c> = keine.</summary>
    private static List<string>? SansOf(List<RepertoireReach.Node> nodes)
    {
        if (nodes.Count < 2) return null;
        var sans = new List<string>(nodes.Count - 1);
        for (var k = 0; k + 1 < nodes.Count; k++)
        {
            var next = nodes[k + 1];
            var hit = nodes[k].Children.FirstOrDefault(c => ReferenceEquals(c.Child, next));
            if (hit.Child is null) return null;
            sans.Add(hit.San);
        }
        return sans;
    }

    /// <summary>Alle Partien als Präfixbaum, dann EIN Durchlauf mit einem Brett; gezählt wird nur, was im Repertoire vorkommt
    /// (andere Stellungen fragt niemand).</summary>
    internal static Dictionary<string, PositionStats> Count(IReadOnlyList<Game> games, RepertoireReach.Graph graph)
    {
        var root = new Trie();
        foreach (var g in games)
        {
            var t = root;
            Touch(t, g.Year);
            foreach (var san in g.Sans.Take(MaxPlies))
            {
                if (!t.Children.TryGetValue(san, out var c)) t.Children[san] = c = new Trie();
                t = c;
                Touch(t, g.Year);
            }
        }

        var stats = new Dictionary<string, PositionStats>(StringComparer.Ordinal);
        if (root.Count == 0) return stats;
        var board = new ChessBoard();
        Walk(root, RepertoireReach.Key(board.ToFen()));
        return stats;

        void Walk(Trie node, string key)
        {
            var inGraph = graph.Nodes.ContainsKey(key);
            PositionStats? st = null;
            if (inGraph)
            {
                if (!stats.TryGetValue(key, out st)) stats[key] = st = new PositionStats();
                st.Reached += node.Count;
                if (node.LastYear is { } y && (st.LastYear is not { } have || y > have)) st.LastYear = y;
            }
            foreach (var (san, child) in node.Children)
            {
                bool ok;
                try { ok = board.Move(san); }
                catch { ok = false; }
                if (!ok) continue;                     // unlesbarer Zug: die Partie endet hier
                var next = RepertoireReach.Key(board.ToFen());
                if (st is not null)
                {
                    st.Continued += child.Count;
                    st.Next[next] = st.Next.GetValueOrDefault(next) + child.Count;
                }
                Walk(child, next);
                board.Cancel();
            }
        }
    }

    private static void Touch(Trie t, int? year)
    {
        t.Count++;
        if (year is { } y && (t.LastYear is not { } have || y > have)) t.LastYear = y;
    }
}
