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
/// Partien die Stellung nach dem letzten Gegnerzug der Linie erreicht haben, und deren jüngstes Jahr. Hat keine Partie
/// sie erreicht, ist die Linie „nie erreicht" — sie bleibt in der Liste, hinten.</para>
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
    public sealed record Line(string Key, string End, string? StartFen, string Chapter, IReadOnlyList<string> Sans, double Probability,
        int Reached, int? LastYear, bool NeverReached, int Index);

    public sealed record Result(int Games, List<Line> Lines);

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
    public static Result Rank(RepertoireReach.Graph graph, IReadOnlyList<string> chapters, IEnumerable<Game> games)
    {
        var opponentWhite = graph.Color == 'b';
        var relevant = games.Where(g => g.OpponentWhite == opponentWhite).ToList();
        var stats = Count(relevant, graph);

        var lines = new List<Line>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < graph.Mainlines.Count; i++)
        {
            var nodes = graph.Mainlines[i];
            var sans = SansOf(nodes);
            if (sans is null) continue;
            var key = ChessableTrainedLineService.LineKeyFromSans(sans);
            if (!seen.Add(key)) continue;

            var p = 1.0;
            var after = nodes[0];                       // Stellung nach dem letzten Gegnerzug (ohne Gegnerzug: der Start)
            for (var k = 0; k + 1 < nodes.Count; k++)
            {
                var at = nodes[k];
                if (at.UserToMove) continue;
                after = nodes[k + 1];
                if (p == 0) continue;
                var st = stats.GetValueOrDefault(at.Key);
                var n = st?.Next.GetValueOrDefault(after.Key) ?? 0;
                p = st is null || st.Continued == 0 ? 0 : p * n / st.Continued;
            }
            var end = stats.GetValueOrDefault(after.Key);
            var reached = end?.Reached ?? 0;
            var start = nodes[0].Key == RepertoireReach.StandardStartKey ? null : nodes[0].Fen;
            lines.Add(new Line(key, nodes[^1].Key, start, i < chapters.Count ? chapters[i] : "", sans, reached == 0 ? 0 : p,
                reached, end?.LastYear, reached == 0, i));
        }

        lines = lines.OrderByDescending(l => l.Probability).ThenByDescending(l => l.Reached).ThenBy(l => l.Index).ToList();
        return new Result(relevant.Count, lines);
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
