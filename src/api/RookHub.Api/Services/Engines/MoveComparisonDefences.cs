using Chess;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using static RookHub.Api.Services.Engines.MoveComparisonBoard;
using static RookHub.Api.Services.Engines.MoveComparisonEval;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Die ANDEREN Verteidigungen in der Widerlegung eines Fehlzugs (0.971.0, Befund an Vereinspartie 245, Fehler 25...Lc8:
/// „der Grund, warum Lc8 nicht funktioniert, ist ein taktischer. Wenn Txc8 Txc8 gehen würde, wäre alles gut, aber dann geht
/// die Springergabel Sd7 — ebenso Txc8 Kxc8 Tc1 Kb8, gleiches Problem. Du ratterst nur die Computerlinie runter.“).
/// Das Modell darf nur Züge nennen, die in den gegebenen Linien stehen — die Nebenwege des Verteidigers standen nirgends,
/// also konnte es sie nicht erklären. Hier werden sie ausgewählt (<see cref="Plan"/>), als Zeilen
/// <see cref="MoveComparisonLineKind.Defence"/> von der Engine gerechnet und als Fakten gelesen (<see cref="Of"/>).
///
/// <para><b>Auswahl</b>: in der besten Antwort-Linie auf den schwächeren Zug an den ersten <see cref="Decisions"/> Zügen des
/// Verteidigers (der Seite, die den Fehler gemacht hat — Halbzug 1 und 3 der Linie) die naheliegenden anderen Züge: steht
/// er im Schach, alle anderen legalen Züge; sonst nur die Züge, die die gerade gezogene Figur des Gegners schlagen (ein
/// Opfer annehmen, anders zurückschlagen). Reihenfolge je Entscheidung: Schlagen der gezogenen Figur, Königszüge, der Rest;
/// darin die billigere Figur zuerst. Unterverwandlungen zählen nicht. Die Entscheidungen kommen abwechselnd an die Reihe,
/// bis der Deckel (<c>ClubMistakeComparisons:DefenceLines</c>, Vorgabe <see cref="DefaultMax"/>) erreicht ist — eine
/// Entscheidung mit vielen Zügen verdrängt die andere nicht.</para>
/// <para><b>Zeile</b>: <see cref="MoveComparisonLine.CandidateUci"/> = der schwächere Zug, <see cref="MoveComparisonLine.ReplyUci"/>
/// = die andere Verteidigung, <see cref="MoveComparisonLine.Ordinal"/> = ihr Halbzug in der Linie, <see cref="MoveComparisonLine.Fen"/>
/// = die Stellung danach. Der Weg dorthin steht nicht in der Zeile: er ist der Anfang der Antwort-Linie des schwächeren
/// Kandidaten, und beim Lesen muss er wieder genau auf <c>Fen</c> führen — sonst zählt die Zeile nicht.</para>
/// </summary>
internal static class MoveComparisonDefences
{
    /// <summary>Höchstens so viele Zusatzlinien je Vergleich (je eine Engine-Rechnung mit einer Linie).</summary>
    public const int DefaultMax = 4;
    /// <summary>So viele Entscheidungen des Verteidigers in der Widerlegung werden angesehen.</summary>
    public const int Decisions = 2;

    private sealed record Alternative(string Uci, string San);

    /// <summary>Eine Entscheidung des Verteidigers in der Widerlegung: die Stellung davor, der Weg dorthin (SAN ab der Stellung
    /// nach dem schwächeren Zug), der Zug der Linie und die naheliegenden anderen in Rangfolge.</summary>
    private sealed record Spot(int Ply, string FenBefore, IReadOnlyList<string> Prefix, string MainSan, bool InCheck,
        int LegalMoves, IReadOnlyList<Alternative> Alternatives);

    /// <summary>Eine andere Verteidigung als Fakt: Zug, Stellung danach, Bewertung aus Sicht des Lesers, die Linie der Engine
    /// (wie der Gegner sie widerlegt) und geprüfte Aussagen über das Feld des ersten Zuges dieser Linie.</summary>
    internal sealed record Defence(string San, string FenAfter, string Eval, IReadOnlyList<string> Line, IReadOnlyList<string> SquareFacts);

    /// <summary>Eine Entscheidung samt ihrer gerechneten anderen Verteidigungen. <see cref="Complete"/> = JEDER andere legale
    /// Zug ist gerechnet (dann darf der Text „hat nur diese Züge“ sagen).</summary>
    internal sealed record Decision(IReadOnlyList<string> Prefix, string FenBefore, string MainSan, bool InCheck, int LegalMoves,
        bool Complete, IReadOnlyList<Defence> Defences);

    /// <summary>Die Zusatzzeilen eines Vergleichs, höchstens <paramref name="max"/> — offen, oder gleich erledigt, wenn die
    /// Stellung danach zu Ende ist. Leer, wenn die Widerlegung keine naheliegende andere Verteidigung hat.</summary>
    internal static List<MoveComparisonLine> Plan(MoveComparison c, int max)
    {
        var lines = new List<MoveComparisonLine>();
        foreach (var weak in MoveComparisonSteps.WeakerCandidates(c).OrderBy(l => l.Ordinal))
        {
            var spots = Spots(weak);
            for (var k = 0; lines.Count < max && spots.Any(s => s.Alternatives.Count > k); k++)
                foreach (var spot in spots)
                {
                    if (lines.Count >= max) break;
                    if (spot.Alternatives.Count <= k) continue;
                    var uci = spot.Alternatives[k].Uci;
                    if (PlayUci(spot.FenBefore, uci) is not { } after) continue;
                    var line = new MoveComparisonLine
                    {
                        Kind = MoveComparisonLineKind.Defence, CandidateUci = weak.CandidateUci, ReplyUci = uci, Ordinal = spot.Ply, Fen = after,
                    };
                    if (Terminal(after) is not null) line.State = MoveComparisonLineState.Done;
                    lines.Add(line);
                }
        }
        return lines;
    }

    /// <summary>Die gerechneten anderen Verteidigungen zum schwächeren Kandidaten als Fakten, je Entscheidung in der
    /// Reihenfolge der Auswahl. Gescheiterte und nicht (mehr) zur Linie passende Zeilen fehlen einfach.</summary>
    internal static List<Decision> Of(MoveComparison c, MoveComparisonLine weak)
    {
        var result = new List<Decision>();
        var rows = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Defence && l.CandidateUci == weak.CandidateUci
            && l.State == MoveComparisonLineState.Done).ToList();
        if (rows.Count == 0) return result;
        var moverWhite = FenFields.WhiteToMove(c.Fen);
        foreach (var spot in Spots(weak))
        {
            var defences = new List<Defence>();
            foreach (var alt in spot.Alternatives)
            {
                var row = rows.FirstOrDefault(r => r.Ordinal == spot.Ply && r.ReplyUci == alt.Uci);
                if (row is null || PlayUci(spot.FenBefore, alt.Uci) is not { } after || !MoveComparisonFromAnalysis.SameFen(after, row.Fen)) continue;
                if (EvalOf(row.Fen, row.ResultJson) is not { } ev) continue;
                var line = Pvs(row.Fen, row.ResultJson).FirstOrDefault()?.Line ?? [];
                defences.Add(new Defence(alt.San, row.Fen, MoverText(ev, moverWhite), line, SquareFacts(c, weak, row.Fen, line)));
            }
            if (defences.Count > 0)
                result.Add(new Decision(spot.Prefix, spot.FenBefore, spot.MainSan, spot.InCheck, spot.LegalMoves,
                    defences.Count == spot.LegalMoves - 1, defences));
        }
        return result;
    }

    /// <summary>Die Entscheidungen des Verteidigers in der besten Antwort-Linie auf den schwächeren Zug.</summary>
    private static List<Spot> Spots(MoveComparisonLine weak)
    {
        var spots = new List<Spot>();
        if (string.IsNullOrEmpty(weak.Fen) || BrokerCandidates.Parse(weak.ResultJson, weak.Fen) is not { Count: > 0 } cands) return spots;
        IReadOnlyList<string> pv = cands[0].Pv is { Count: > 0 } moves ? moves : [cands[0].Uci];
        try
        {
            var board = ChessBoard.LoadFromFen(weak.Fen);
            var prefix = new List<string>();
            string? lastTo = null;
            for (var i = 0; i < pv.Count && spots.Count < Decisions; i++)
            {
                var legal = board.Moves(generateSan: true);
                var main = FindMove(legal, pv[i]);
                if (main?.San is null) break;
                if (i % 2 == 1)
                {
                    var fen = board.ToFen();
                    var inCheck = FenFields.WhiteToMove(fen) ? board.WhiteKingChecked : board.BlackKingChecked;
                    var mainUci = GamePlies.ToUci(main);
                    var target = lastTo;
                    // Unterverwandlungen sind keine eigene Verteidigung.
                    var others = legal.Where(m => m.San is not null && GamePlies.ToUci(m) is var u && u != mainUci && !(u.Length == 5 && u[4] != 'q')).ToList();
                    var near = (inCheck ? others : others.Where(m => Takes(m, target)))
                        .OrderBy(m => Takes(m, target) ? 0 : PieceType(m) == 'k' ? 1 : 2)
                        .ThenBy(m => PieceType(m) switch { 'k' => 0, 'p' => 1, 'n' or 'b' => 3, 'r' => 5, _ => 9 })
                        .ThenBy(m => GamePlies.ToUci(m), StringComparer.Ordinal)
                        .Select(m => new Alternative(GamePlies.ToUci(m), m.San!)).ToList();
                    spots.Add(new Spot(i, fen, prefix.ToList(), main.San, inCheck, others.Count + 1, near));
                }
                prefix.Add(main.San);
                lastTo = main.NewPosition.ToString();
                board.Move(main);
            }
        }
        catch
        {
            // Linie nicht nachzuspielen — was bis hierher gefunden ist, gilt.
        }
        return spots;
    }

    private static bool Takes(Move m, string? square) => square is not null && m.CapturedPiece is not null && m.NewPosition.ToString() == square;

    private static char PieceType(Move m) => char.ToLowerInvariant(m.Piece.Type.AsChar);

    /// <summary>
    /// Geprüfte Aussagen über das Feld, auf das der erste Zug der Widerlegung zieht — der „eigentliche Grund“: (1) es ist das
    /// Feld, das der Fehlzug geräumt hat; (2) nach dem Fehlzug deckten es nur Figuren, die inzwischen nicht mehr dort stehen
    /// (geschlagen oder weggelenkt), und jetzt deckt es der Verteidiger gar nicht mehr. Nur Brettgeometrie
    /// (<see cref="BoardGeometry"/>): „decken“ heißt bestreichen, Fesselungen bleiben außen vor.
    /// </summary>
    private static List<string> SquareFacts(MoveComparison c, MoveComparisonLine weak, string fenAfter, IReadOnlyList<string> line)
    {
        var facts = new List<string>();
        if (line.Count == 0 || weak.CandidateUci.Length < 4 || SanOf(c.Fen, weak.CandidateUci) is not { } weakSan) return facts;
        try
        {
            var board = ChessBoard.LoadFromFen(fenAfter);
            var move = Array.Find(board.Moves(generateSan: true), m => m.San == line[0]);
            if (move is null) return facts;
            var square = move.NewPosition.ToString();
            var defenderWhite = FenFields.WhiteToMove(c.Fen);
            var weakMove = GameRecapService.Numbered(c.Fen, [weakSan]);
            if (weak.CandidateUci[..2] == square && BoardGeometry.FromFen(c.Fen)?.PieceAt(square) is { } left and not '\0')
                facts.Add($"{square} is the square the {ExplanationFacts.PieceName(char.ToLowerInvariant(left))} left with {weakMove}");
            board.Move(move);
            if (BoardGeometry.FromFen(weak.Fen) is { } then && BoardGeometry.FromFen(board.ToFen()) is { } now)
            {
                var covered = then.Attackers(square, defenderWhite);
                if (covered.Count > 0 && covered.All(p => now.PieceAt(p.Square) != p.Piece) && now.Attackers(square, defenderWhite).Count == 0)
                    facts.Add($"after {weakMove} the square {square} was covered only by "
                              + ExplanationFacts.JoinAnd(covered.Select(p => $"the {ExplanationFacts.PieceName(char.ToLowerInvariant(p.Piece))} on {p.Square}").ToList())
                              + $", which {(covered.Count == 1 ? "is" : "are")} no longer there");
            }
        }
        catch
        {
            // Stellung nicht lesbar — dann eben ohne.
        }
        return facts;
    }
}
