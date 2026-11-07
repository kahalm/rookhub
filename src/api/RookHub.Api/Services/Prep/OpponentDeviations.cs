using Chess;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// „In welchem Zug macht er einen Zug, den ich nicht kenne?" (Wunsch 2026-11-07, „Abweichungen trainieren"): aus dem Repertoire
/// (<see cref="RepertoireReach.Graph"/>, dieselben Quellen wie die Trainingslinien) und den Partien des Gegners alle Stellungen, in
/// denen der Gegner am Zug war und einen Zug gespielt hat, der aus dem Repertoire herausführt. Reine Logik ohne DB und HTTP.
///
/// <para><b>Abweichung.</b> Eine Stellung des Repertoires (Schlüssel wie <see cref="RepertoireReach.Key"/> — Zugumstellungen fallen
/// zusammen), Gegner am Zug, und sein Zug führt in eine Stellung, die das Repertoire NICHT kennt. Führt sein Zug über eine andere
/// Zugfolge zurück ins Repertoire, ist das keine Abweichung. Gleiche Stellung + gleiche Folgestellung über verschiedene Wege =
/// EINE Abweichung (Partien addiert); als Pfad gilt der Weg, den er dorthin am häufigsten genommen hat.</para>
///
/// <para><b>Reihung.</b> Nach Partien mit dem Abweichungszug, dann jüngstes Jahr, dann kürzerer Pfad. Alles stammt aus seinen Partien:
/// „Häufigkeit × Wahrscheinlichkeit, den Pfad zu erreichen" ist mit seinen eigenen Zahlen genau die Zahl der Partien mit der
/// Abweichung (Anteil der Partien, die die Stellung erreichen × Anteil, der dort abweicht) — eine Lichess-Schätzung braucht es
/// nicht, denn eine Abweichung, die er nie gespielt hat, gibt es nicht.</para>
///
/// <para><b>Fortsetzung.</b> Nach dem Abweichungszug je Stellung, in der ER am Zug war, seine Züge mit Partienzahl (bis
/// <see cref="TailPlies"/> Halbzüge nach der Abweichung) — nur seine Züge verzweigen; für die Antworten des Nutzers gibt es keine
/// Vorgabe (der Trainer urteilt per Engine und schlägt die Stellung nach der Antwort hier nach). Dieselbe Tabelle
/// (<see cref="Result.Positions"/>) trägt auch die Stellungen der Pfade: daraus zeigt der Trainer „in seinen Partien n× erreicht".</para>
/// </summary>
public static class OpponentDeviations
{
    /// <summary>So viele Halbzüge nach der Abweichung zählen für die Fortsetzung.</summary>
    public const int TailPlies = 16;

    /// <summary>Höchstens so viele Abweichungen gehen hinaus (der Rest als <c>more</c>).</summary>
    public const int MaxDeviations = 50;

    /// <summary>Eine Abweichung.</summary>
    /// <param name="Path">Halbzüge (englische SAN) ab der Grundstellung bis zur Stellung VOR dem Abweichungszug.</param>
    /// <param name="Fen">Stellung vor dem Abweichungszug.</param>
    /// <param name="San">Der Abweichungszug.</param>
    /// <param name="Games">Partien mit diesem Zug in dieser Stellung.</param>
    /// <param name="Reached">Partien, die die Stellung erreicht haben (über jeden Weg).</param>
    /// <param name="Repertoire">Die Repertoire-Züge an dieser Stellung (für „statt …").</param>
    public sealed record Deviation(IReadOnlyList<string> Path, string Fen, string San, int Games, int Reached, int? LastYear,
        IReadOnlyList<string> Repertoire);

    /// <summary>Partien des Gegners in einer Stellung: erreicht, jüngstes Jahr und seine Züge dort (nur wo ER am Zug war).</summary>
    public sealed class PositionStat
    {
        public int Reached { get; internal set; }
        public int? LastYear { get; internal set; }
        /// <summary>SAN → Partien (nur Stellungen, in denen er am Zug war).</summary>
        public Dictionary<string, int> Moves { get; } = new(StringComparer.Ordinal);
    }

    public sealed record Result(int Games, List<Deviation> Deviations, int Total, Dictionary<string, PositionStat> Positions);

    private sealed class Trie
    {
        public int Count;
        public int? LastYear;
        public readonly Dictionary<string, Trie> Children = new(StringComparer.Ordinal);
    }

    private sealed class Acc
    {
        public required string Fen;
        public required string San;
        public required IReadOnlyList<string> Repertoire;
        public int Games;
        public int? LastYear;
        public int BestPathCount = -1;
        public List<string> Path = [];
    }

    /// <summary>Die Abweichungen gegen <paramref name="graph"/> (Farbe des Nutzers = <see cref="RepertoireReach.Graph.Color"/>; gezählt nur
    /// Partien, in denen der Gegner die andere Farbe hatte), höchstens <paramref name="take"/>.</summary>
    public static Result Find(RepertoireReach.Graph graph, IEnumerable<OpponentTrainingLines.Game> games, int take = MaxDeviations)
    {
        var opponentWhite = graph.Color == 'b';
        var relevant = games.Where(g => g.OpponentWhite == opponentWhite).ToList();
        var root = new Trie();
        foreach (var g in relevant)
        {
            var t = root;
            Touch(t, g.Year);
            foreach (var san in g.Sans.Take(OpponentTrainingLines.MaxPlies))
            {
                if (!t.Children.TryGetValue(san, out var c)) t.Children[san] = c = new Trie();
                t = c;
                Touch(t, g.Year);
            }
        }

        var all = new Dictionary<string, PositionStat>(StringComparer.Ordinal);   // jede Stellung seiner Partien
        var found = new Dictionary<string, Acc>(StringComparer.Ordinal);          // Stellung + Folgestellung → Abweichung
        var tails = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);   // Abweichung → Stellungen ihrer Fortsetzung
        var paths = new Dictionary<string, List<string>>(StringComparer.Ordinal);      // Stellung → Weg (für die Pfad-Stellungen)
        if (root.Count == 0) return new Result(relevant.Count, [], 0, new());

        var board = new ChessBoard();
        var path = new List<string>();
        Walk(root, RepertoireReach.Key(board.ToFen()), null, 0);

        var ranked = found.Values
            .OrderByDescending(a => a.Games).ThenByDescending(a => a.LastYear ?? 0).ThenBy(a => a.Path.Count)
            .ThenBy(a => string.Join(' ', a.Path), StringComparer.Ordinal).ThenBy(a => a.San, StringComparer.Ordinal)
            .ToList();
        var shown = ranked.Take(Math.Max(0, take)).ToList();

        // Hinaus gehen nur die Stellungen, die der Trainer braucht: Pfade der gezeigten Abweichungen + ihre Fortsetzung
        var keep = new HashSet<string>(StringComparer.Ordinal);
        var devs = new List<Deviation>();
        foreach (var a in shown)
        {
            var b = new ChessBoard();
            keep.Add(RepertoireReach.Key(b.ToFen()));
            foreach (var san in a.Path) { b.Move(san); keep.Add(RepertoireReach.Key(b.ToFen())); }
            var id = RepertoireReach.Key(a.Fen) + "|" + a.San;
            if (tails.TryGetValue(id, out var tail)) keep.UnionWith(tail);
            devs.Add(new Deviation(a.Path, a.Fen, a.San, a.Games, all.GetValueOrDefault(RepertoireReach.Key(a.Fen))?.Reached ?? 0,
                a.LastYear, a.Repertoire));
        }
        var positions = keep.Where(all.ContainsKey).ToDictionary(k => k, k => all[k], StringComparer.Ordinal);
        return new Result(relevant.Count, devs, ranked.Count, positions);

        // tailOf: die Abweichung, in deren Fortsetzung wir gerade sind (null = noch keine), depth = Halbzüge seit ihr
        void Walk(Trie node, string key, string? tailOf, int depth)
        {
            if (!all.TryGetValue(key, out var st)) all[key] = st = new PositionStat();
            st.Reached += node.Count;
            if (node.LastYear is { } y && (st.LastYear is not { } have || y > have)) st.LastYear = y;
            if (tailOf is not null) tails[tailOf].Add(key);

            var whiteToMove = FenFields.WhiteToMove(board.ToFen());
            var opponentToMove = whiteToMove == opponentWhite;
            graph.Nodes.TryGetValue(key, out var gNode);
            foreach (var (san, child) in node.Children)
            {
                bool ok;
                try { ok = board.Move(san); }
                catch { ok = false; }
                if (!ok) continue;                     // unlesbarer Zug: die Partie endet hier
                var next = RepertoireReach.Key(board.ToFen());
                if (opponentToMove) st.Moves[san] = st.Moves.GetValueOrDefault(san) + child.Count;
                path.Add(san);

                string? childTail = tailOf;
                var childDepth = depth + 1;
                if (tailOf is null && opponentToMove && gNode is not null && !graph.Nodes.ContainsKey(next))
                {
                    // Abweichung: aus einer Repertoire-Stellung heraus in eine, die das Repertoire nicht kennt
                    var id = key + "|" + san;
                    if (!found.TryGetValue(id, out var acc))
                    {
                        board.Cancel();
                        var fenBefore = board.ToFen();
                        board.Move(san);
                        found[id] = acc = new Acc
                        {
                            Fen = fenBefore, San = san,
                            Repertoire = gNode.Children.Select(c => c.San).Distinct(StringComparer.Ordinal).ToList(),
                        };
                        tails[id] = new HashSet<string>(StringComparer.Ordinal);
                    }
                    acc.Games += child.Count;
                    if (child.LastYear is { } cy && (acc.LastYear is not { } ay || cy > ay)) acc.LastYear = cy;
                    if (child.Count > acc.BestPathCount)
                    {
                        acc.BestPathCount = child.Count;
                        acc.Path = path.Take(path.Count - 1).ToList();
                    }
                    childTail = id;
                    childDepth = 0;
                }
                else if (tailOf is not null && graph.Nodes.ContainsKey(next))
                {
                    childTail = null;                  // über eine Zugumstellung zurück ins Repertoire: keine Fortsetzung mehr
                }

                if (childTail is null || childDepth < TailPlies) Walk(child, next, childTail, childDepth);
                else if (childTail is not null)
                {
                    // letzte gezählte Stellung der Fortsetzung: erreicht ja, weiter nicht
                    if (!all.TryGetValue(next, out var last)) all[next] = last = new PositionStat();
                    last.Reached += child.Count;
                    tails[childTail].Add(next);
                }
                path.RemoveAt(path.Count - 1);
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
