using Chess;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// Zugumstellungen in der Vorbereitung (Wunsch 2026-11-26, Prep 331 „Brunner": seine einzige Partie im Zeitraum ist 1.c4 e6 2.Sc3,
/// Schandorffs Kurs hat gegen 1.c4 nur 2.g3/2.e4 — 2.Sc3 deckt er über 2…d5 3.d4 ab, das Damengambit aus den 1.d4-Kapiteln).
/// Eine Linie, die seinen Partien widerspricht, wird nicht mehr ab ihrem Startzug verglichen, sondern ab der ERSTEN späteren
/// Stellung, die er auch erreicht:
/// <list type="bullet">
/// <item>direkt — er hat die Stellung in einer Partie über eine andere Zugfolge erreicht (Stellungs-Schlüssel wie im Graphen);</item>
/// <item>über eine Brücke — seine Partie hat das Repertoire mit SEINEM Zug verlassen (Stellung P, ich am Zug), und von P aus
/// führen ein eigener Zug (und ein Zug von ihm) in die Linie zurück: P + mein Zug (+ sein Zug) = Stellung der Linie.</item>
/// </list>
/// Die Linie beginnt dann an dieser gemeinsamen Stellung (bei der Brücke an P, samt Brückenzügen) — wie eine Linie mit eigener
/// <c>[FEN]</c>. Widerspricht eine Linie schon seinem ERSTEN Zug und gibt es keinen solchen Rückweg, fällt sie weg
/// (er spielt im Zeitraum nur 1.c4 — dann keine 1.e4-Linien). Reine Logik ohne DB.
///
/// <para><b>Aufwand.</b> Brücken werden nur für Stellungen gesucht, an denen er das Repertoire verlassen hat, und nur, wenn eine
/// gesuchte Stellung der Linien höchstens <see cref="MaxDiffSquares"/> Felder von P abweicht (zwei Halbzüge ändern höchstens sechs
/// Felder) — sonst wird gar nicht erst gezogen; höchstens <see cref="MaxBoundaries"/> Stellungen, die häufigsten zuerst.</para>
/// </summary>
internal static class OpponentTranspositions
{
    /// <summary>So viele Ausstiegsstellungen höchstens (die häufigsten zuerst) — Deckel für Gegner mit sehr vielen Partien.</summary>
    public const int MaxBoundaries = 200;

    /// <summary>Zwei Halbzüge ändern höchstens sechs Felder (Rochade vier, dazu ein Zug zwei).</summary>
    public const int MaxDiffSquares = 6;

    /// <summary>Eine Stellung, an der seine Partie das Repertoire mit SEINEM Zug verlassen hat (ich bin am Zug).</summary>
    internal sealed class Boundary
    {
        public required string Key { get; init; }
        public required string Fen { get; init; }
        public int Count;
        public int? LastYear;
    }

    /// <summary>Die Linie neu ab der gemeinsamen Stellung: <see cref="Nodes"/> (beginnt an der gemeinsamen Stellung bzw. an P),
    /// <see cref="From"/> = Index der ersten Stellung der Ursprungslinie, die wieder dabei ist, <see cref="BridgeMoves"/> = Züge
    /// von P bis dorthin (0 = direkt), <see cref="StartReached"/> = seine Partien in der Startstellung.</summary>
    internal sealed record Rebase(List<RepertoireReach.Node> Nodes, int From, int BridgeMoves, int StartReached);

    private sealed record Bridge(RepertoireReach.Node P, int Count, List<RepertoireReach.Node> Path);

    /// <summary>Erster Gegnerzug der Linie, an dem er weitergespielt hat, aber nie mit dem Zug der Linie (Index in
    /// <paramref name="nodes"/>); <c>null</c> = kein Widerspruch (er folgt, oder seine Partien kommen nicht so weit).
    /// <paramref name="first"/> = es ist sein erster Zug der Linie.</summary>
    internal static int? Contradiction(List<RepertoireReach.Node> nodes, Dictionary<string, OpponentTrainingLines.PositionStats> stats,
        out bool first)
    {
        first = true;
        for (var k = 0; k + 1 < nodes.Count; k++)
        {
            if (nodes[k].UserToMove) continue;
            var st = stats.GetValueOrDefault(nodes[k].Key);
            if (st is not { Continued: > 0 }) return null;
            if (st.Next.GetValueOrDefault(nodes[k + 1].Key) == 0) return k;
            first = false;
        }
        return null;
    }

    /// <summary>Je Hauptvariante mit Widerspruch: die neu angesetzte Linie (<paramref name="rebases"/>) oder — Widerspruch schon beim
    /// ersten Zug und kein Rückweg — weglassen (<paramref name="excluded"/>).</summary>
    internal static void Resolve(RepertoireReach.Graph graph, Dictionary<string, OpponentTrainingLines.PositionStats> stats,
        IReadOnlyCollection<Boundary> boundaries, Dictionary<int, Rebase> rebases, HashSet<int> excluded)
    {
        var open = new List<(int Index, int At, bool First)>();
        for (var i = 0; i < graph.Mainlines.Count; i++)
            if (Contradiction(graph.Mainlines[i], stats, out var first) is { } d) open.Add((i, d, first));
        if (open.Count == 0) return;

        // Stellungen hinter einem Widerspruch, die er nicht direkt erreicht hat — nur DIE kommen als Brückenziel infrage
        var wanted = new Dictionary<string, RepertoireReach.Node>(StringComparer.Ordinal);
        foreach (var (i, d, _) in open)
        {
            var nodes = graph.Mainlines[i];
            for (var j = d + 1; j < nodes.Count; j++)
            {
                if ((stats.GetValueOrDefault(nodes[j].Key)?.Reached ?? 0) > 0) break;    // direkt erreicht: weiter hinten egal
                wanted.TryAdd(nodes[j].Key, nodes[j]);
            }
        }
        var bridges = Bridges(graph, boundaries, wanted);

        foreach (var (i, d, first) in open)
        {
            var nodes = graph.Mainlines[i];
            // Vorrang: eine Stellung, die er in einer Partie WIRKLICH erreicht hat (die früheste); sonst die früheste Brücke
            Rebase? hit = null;
            for (var j = d + 1; j + 1 < nodes.Count && hit is null; j++)
                if (stats.GetValueOrDefault(nodes[j].Key) is { Reached: > 0 } st)
                    hit = new Rebase(nodes.Skip(j).ToList(), j, 0, st.Reached);
            for (var j = d + 1; j + 1 < nodes.Count && hit is null; j++)
                if (bridges.TryGetValue(nodes[j].Key, out var b))
                    hit = new Rebase([b.P, .. b.Path, .. nodes.Skip(j + 1)], j, b.Path.Count, b.Count);
            // eine Linie, die danach keinen Zug mehr hat, trainiert nichts
            if (hit is not null && hit.Nodes.Count >= 2) rebases[i] = hit;
            else if (first) excluded.Add(i);
        }
    }

    /// <summary>Ziel-Stellung → Brücke (P, mein Zug [, sein Zug]); je Ziel die häufigste Ausstiegsstellung.</summary>
    private static Dictionary<string, Bridge> Bridges(RepertoireReach.Graph graph, IReadOnlyCollection<Boundary> boundaries,
        Dictionary<string, RepertoireReach.Node> wanted)
    {
        var found = new Dictionary<string, Bridge>(StringComparer.Ordinal);
        if (wanted.Count == 0 || boundaries.Count == 0) return found;
        var targets = wanted.Values.Select(n => (Node: n, Squares: Squares(n.Key))).ToList();
        foreach (var p in boundaries.OrderByDescending(b => b.Count).ThenBy(b => b.Key, StringComparer.Ordinal).Take(MaxBoundaries))
        {
            var ps = Squares(p.Key);
            var near = targets.Where(t => !found.ContainsKey(t.Node.Key) && Diff(ps, t.Squares) <= MaxDiffSquares)
                .ToDictionary(t => t.Node.Key, StringComparer.Ordinal);
            if (near.Count == 0) continue;
            ChessBoard board;
            try { board = ChessBoard.LoadFromFen(p.Fen); } catch { continue; }
            var pNode = new RepertoireReach.Node { Key = p.Key, Fen = p.Fen, UserToMove = true };
            var needReply = near.Values.Any(t => t.Node.UserToMove);
            foreach (var m in Legal(board))
            {
                if (!TryMove(board, m, out var mSan)) continue;
                var xFen = board.ToFen();
                var xKey = RepertoireReach.Key(xFen);
                if (near.Remove(xKey, out var x))                    // Seite am Zug steht im Schlüssel: er ist am Zug
                {
                    pNode.Children.Add((mSan, x.Node));
                    found[xKey] = new Bridge(pNode, p.Count, [x.Node]);
                }
                else if (needReply && !graph.Nodes.ContainsKey(xKey) && Near(Squares(xKey), near.Values, 4))   // ein Zug: ≤ 4 Felder
                {
                    RepertoireReach.Node? xNode = null;
                    foreach (var r in Legal(board))
                    {
                        if (!TryMove(board, r, out var rSan)) continue;
                        var nKey = RepertoireReach.Key(board.ToFen());
                        board.Cancel();
                        if (!near.Remove(nKey, out var hit)) continue;
                        var n = hit.Node;
                        if (xNode is null)
                        {
                            xNode = new RepertoireReach.Node { Key = xKey, Fen = xFen, UserToMove = false };
                            pNode.Children.Add((mSan, xNode));
                        }
                        xNode.Children.Add((rSan, n));
                        found[nKey] = new Bridge(pNode, p.Count, [xNode, n]);
                    }
                }
                board.Cancel();
                if (near.Count == 0) break;
            }
        }
        return found;
    }

    private static Move[] Legal(ChessBoard board)
    {
        try { return board.Moves(); } catch { return []; }
    }

    private static bool TryMove(ChessBoard board, Move move, out string san)
    {
        san = "";
        try
        {
            if (!board.Move(move)) return false;
            san = board.ExecutedMoves[^1].San ?? "";
            if (san.Length > 0) return true;
            board.Cancel();
            return false;
        }
        catch { return false; }
    }

    /// <summary>Figurenstellung (FEN-Feld 1) als 64 Zeichen, '.' = leer.</summary>
    internal static string Squares(string key)
    {
        var placement = key.Split(' ')[0];
        var chars = new char[64];
        var i = 0;
        foreach (var c in placement)
        {
            if (c == '/') continue;
            if (char.IsDigit(c)) for (var n = c - '0'; n > 0 && i < 64; n--) chars[i++] = '.';
            else if (i < 64) chars[i++] = c;
        }
        for (; i < 64; i++) chars[i] = '.';
        return new string(chars);
    }

    private static bool Near(string squares, IEnumerable<(RepertoireReach.Node Node, string Squares)> targets, int max)
    {
        foreach (var t in targets)
            if (t.Node.UserToMove && Diff(squares, t.Squares, max) <= max) return true;
        return false;
    }

    private static int Diff(string a, string b, int max = MaxDiffSquares)
    {
        var n = 0;
        for (var i = 0; i < 64; i++) if (a[i] != b[i] && ++n > max) return n;
        return n;
    }
}
