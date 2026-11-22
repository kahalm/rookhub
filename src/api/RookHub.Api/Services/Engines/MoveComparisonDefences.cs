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
/// Opfer annehmen, anders zurückschlagen; seit 1.001.1 auch en passant). Reihenfolge je Entscheidung: zuerst die nicht
/// offensichtlichen Züge (seit 1.001.1 rücken grobe Einsteller ohne Schach — <see cref="StaticNet"/> mindestens
/// <see cref="ObviousLossPawns"/> unter dem Zug der Linie — nach hinten), darin Schlagen der gezogenen Figur, Königszüge,
/// der Rest, jeweils die billigere Figur zuerst. Unterverwandlungen zählen nicht. Die Entscheidungen kommen abwechselnd an die Reihe,
/// bis der Deckel (<c>ClubMistakeComparisons:DefenceLines</c>, Vorgabe <see cref="DefaultMax"/>) erreicht ist — eine
/// Entscheidung mit vielen Zügen verdrängt die andere nicht.</para>
/// <para><b>Natürliche Züge</b> (1.003.0, Befund Vereinspartie 259): dazu die ruhigen Züge, die Maia-3 einem Menschen der Stärke
/// des Verteidigers mit mindestens <c>ClubMistakeComparisons:MaiaMinProbability</c> (25 %) zutraut (<see cref="MaiaNaturalMoves"/>),
/// ohne den Zug der Linie. Sie stehen vor den anderen (nach Wahrscheinlichkeit), offensichtliche Einsteller bleiben hinten;
/// derselbe Deckel. Ohne Maia gilt die Auswahl wie vorher.</para>
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

    /// <param name="Natural">Seit 1.003.0: Maia traut den Zug einem Menschen der Stärke des Verteidigers zu
    /// (<see cref="MaiaNaturalMoves"/>), sonst <c>null</c>.</param>
    private sealed record Alternative(string Uci, string San, MaiaNaturalMoves.Natural? Natural = null);

    /// <summary>Eine Entscheidung des Verteidigers in der Widerlegung: die Stellung davor, der Weg dorthin (SAN ab der Stellung
    /// nach dem schwächeren Zug), der Zug der Linie und die naheliegenden anderen in Rangfolge.</summary>
    private sealed record Spot(int Ply, string FenBefore, IReadOnlyList<string> Prefix, string MainSan, bool InCheck,
        int LegalMoves, IReadOnlyList<Alternative> Alternatives, IReadOnlyList<string> MainNext);

    /// <summary>Eine andere Verteidigung als Fakt: Zug, Stellung danach, Bewertung aus Sicht des Lesers, die Linie der Engine
    /// (wie der Gegner sie widerlegt) und geprüfte Aussagen über das Feld des ersten Zuges dieser Linie.</summary>
    /// <param name="ObviousLoss">Seit 1.001.1: die Verteidigung stellt offensichtlich Material ein (<see cref="ObviousLossOf"/>) —
    /// der erste Zug der Engine-Linie, der das tut (z. B. „Bxf4“), sonst <c>null</c>. Solche Verteidigungen erklärt der Text
    /// nicht.</param>
    /// <param name="Natural">Seit 1.003.0: ein natürlicher Zug laut Maia (<see cref="MaiaNaturalMoves"/>) — der Faktenblock
    /// vermerkt „a natural move for a ~1600 player (Maia: 38 %)“.</param>
    internal sealed record Defence(string San, string FenAfter, string Eval, IReadOnlyList<string> Line, IReadOnlyList<string> SquareFacts,
        string? ObviousLoss = null, MaiaNaturalMoves.Natural? Natural = null);

    /// <summary>Eine Entscheidung samt ihrer gerechneten anderen Verteidigungen. <see cref="Complete"/> = JEDER andere legale
    /// Zug ist gerechnet (dann darf der Text „hat nur diese Züge“ sagen).</summary>
    internal sealed record Decision(IReadOnlyList<string> Prefix, string FenBefore, string MainSan, bool InCheck, int LegalMoves,
        bool Complete, IReadOnlyList<Defence> Defences);

    /// <summary>Die Zusatzzeilen eines Vergleichs, höchstens <paramref name="max"/> — offen, oder gleich erledigt, wenn die
    /// Stellung danach zu Ende ist. Leer, wenn die Widerlegung keine naheliegende andere Verteidigung hat.</summary>
    /// <param name="natural">Seit 1.003.0 die natürlichen Züge laut Maia (<see cref="MaiaNaturalMoves"/>); <c>null</c> = keine.</param>
    internal static List<MoveComparisonLine> Plan(MoveComparison c, int max, MaiaNaturalMoves? natural = null)
    {
        var lines = new List<MoveComparisonLine>();
        foreach (var weak in MoveComparisonSteps.WeakerCandidates(c).OrderBy(l => l.Ordinal))
        {
            var spots = Spots(weak, natural);
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
    /// <param name="natural">Wie bei <see cref="Plan"/>. Zeilen, die zur Linie passen, aber (ohne Maia, mit anderer Schwelle)
    /// nicht mehr in der Auswahl stehen, zählen seit 1.003.0 trotzdem — nur ohne Maia-Vermerk.</param>
    internal static List<Decision> Of(MoveComparison c, MoveComparisonLine weak, MaiaNaturalMoves? natural = null)
    {
        var result = new List<Decision>();
        var rows = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Defence && l.CandidateUci == weak.CandidateUci
            && l.State == MoveComparisonLineState.Done).ToList();
        if (rows.Count == 0) return result;
        var moverWhite = FenFields.WhiteToMove(c.Fen);
        foreach (var spot in Spots(weak, natural))
        {
            var defences = new List<Defence>();
            var extra = rows.Where(r => r.Ordinal == spot.Ply && r.ReplyUci is { Length: >= 4 } u && spot.Alternatives.All(a => a.Uci != u)
                    && SanOf(spot.FenBefore, u) is not null)
                .Select(r => new Alternative(r.ReplyUci!, SanOf(spot.FenBefore, r.ReplyUci!)!)).ToList();
            foreach (var alt in spot.Alternatives.Concat(extra))
            {
                var row = rows.FirstOrDefault(r => r.Ordinal == spot.Ply && r.ReplyUci == alt.Uci);
                if (row is null || PlayUci(spot.FenBefore, alt.Uci) is not { } after || !MoveComparisonFromAnalysis.SameFen(after, row.Fen)) continue;
                if (EvalOf(row.Fen, row.ResultJson) is not { } ev) continue;
                var line = Pvs(row.Fen, row.ResultJson).FirstOrDefault()?.Line ?? [];
                var obvious = ObviousLossOf(spot, alt.San, row.Fen, line, ev, weak);
                defences.Add(new Defence(alt.San, row.Fen, MoverText(ev, moverWhite), line,
                    obvious is null ? SquareFacts(c, weak, row.Fen, line, alt.Uci) : [], obvious, alt.Natural));
            }
            // Nicht offensichtliche Verteidigungen zuerst (die Auswahl ordnet nur nach Brettgeometrie, hier zählt die Linie).
            defences = defences.OrderBy(x => x.ObviousLoss is null ? 0 : 1).ToList();
            if (defences.Count > 0)
                result.Add(new Decision(spot.Prefix, spot.FenBefore, spot.MainSan, spot.InCheck, spot.LegalMoves,
                    defences.Count == spot.LegalMoves - 1, defences));
        }
        return result;
    }

    /// <summary>Die Entscheidungen des Verteidigers in der besten Antwort-Linie auf den schwächeren Zug.</summary>
    private static List<Spot> Spots(MoveComparisonLine weak, MaiaNaturalMoves? natural)
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
                    var mainNet = StaticNet(fen, main);
                    // 1.003.0: dazu die natürlichen Züge laut Maia (Befund Vereinspartie 259) — vor den anderen, nach
                    // Wahrscheinlichkeit; offensichtliche Einsteller bleiben hinten.
                    var nat = natural?.At(fen) ?? [];
                    MaiaNaturalMoves.Natural? N(Move m) => nat.FirstOrDefault(n => n.Uci == GamePlies.ToUci(m));
                    var near = (inCheck ? others : others.Where(m => Takes(m, target) || N(m) is not null))
                        .OrderBy(m => !(m.San!.Contains('+') || m.San.Contains('#')) && StaticNet(fen, m) <= mainNet - ObviousLossPawns ? 1 : 0)
                        .ThenBy(m => N(m) is { } n ? -n.P : 0)
                        .ThenBy(m => Takes(m, target) ? 0 : PieceType(m) == 'k' ? 1 : 2)
                        .ThenBy(m => PieceType(m) switch { 'k' => 0, 'p' => 1, 'n' or 'b' => 3, 'r' => 5, _ => 9 })
                        .ThenBy(m => GamePlies.ToUci(m), StringComparer.Ordinal)
                        .Select(m => new Alternative(GamePlies.ToUci(m), m.San!, N(m))).ToList();
                    spots.Add(new Spot(i, fen, prefix.ToList(), main.San, inCheck, others.Count + 1, near, NextSans(board, main, pv, i)));
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

    /// <summary>Schlägt der Zug die Figur auf <paramref name="square"/>? Seit 1.001.1 auch en passant (Befund Vereinspartie 261,
    /// 20.f4 exf3: Zielfeld f3, geschlagen wird der Bauer auf f4).</summary>
    internal static bool Takes(Move m, string? square) => square is not null && m.CapturedPiece is not null && CapturedSquare(m) == square;

    /// <summary>Das Feld der geschlagenen Figur — beim En-passant-Schlag die Linie des Ziels auf der Reihe des Ausgangsfelds.</summary>
    private static string CapturedSquare(Move m)
    {
        var to = m.NewPosition.ToString();
        return m.Parameter is MoveEnPassant ? $"{to[0]}{m.OriginalPosition.ToString()[1]}" : to;
    }

    /// <summary>Ab so vielen Bauern Material schlechter als die Hauptlinie gilt eine andere Verteidigung als offensichtlicher
    /// Einsteller (Auswahl und Fakten, 1.001.1).</summary>
    internal const double ObviousLossPawns = 2.0;

    /// <summary>Die nächsten zwei Züge der Hauptlinie nach dem Zug des Verteidigers (SAN), zum Vergleich der Materialbilanz.</summary>
    private static List<string> NextSans(ChessBoard board, Move main, IReadOnlyList<string> pv, int i)
    {
        var result = new List<string>();
        try
        {
            var copy = ChessBoard.LoadFromFen(board.ToFen());
            copy.Move(FindMove(copy.Moves(), GamePlies.ToUci(main))!);
            for (var k = i + 1; k < pv.Count && k <= i + 2; k++)
            {
                var m = FindMove(copy.Moves(generateSan: true), pv[k]);
                if (m?.San is null) break;
                result.Add(m.San);
                copy.Move(m);
            }
        }
        catch
        {
            // Linie nicht nachzuspielen — dann ohne Fortsetzung.
        }
        return result;
    }

    /// <summary>
    /// Grobe Brettrechnung für die Reihenfolge der Auswahl (1.001.1): was der Zug schlägt, abzüglich des besten einfachen
    /// Schlags, den der Gegner danach hat (ungedeckte Figur voll, gedeckte nur, wenn sie mehr wert ist als der Schläger) —
    /// in Bauern aus Sicht des Ziehenden. Fesselungen und Zwischenzüge bleiben außen vor; die Fakten prüfen später an der
    /// Engine-Linie (<see cref="ObviousLossOf"/>).
    /// </summary>
    private static int StaticNet(string fen, Move m)
    {
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var move = FindMove(board.Moves(), GamePlies.ToUci(m));
            if (move is null) return 0;
            var gained = move.CapturedPiece is { } cp ? Value(cp.Type.AsChar) : 0;
            board.Move(move);
            if (BoardGeometry.FromFen(board.ToFen()) is not { } geo) return gained;
            var best = 0;
            foreach (var reply in board.Moves().Where(r => r.CapturedPiece is not null))
            {
                var victim = Value(reply.CapturedPiece!.Type.AsChar);
                var sq = CapturedSquare(reply);
                var defended = geo.Attackers(sq, char.IsUpper(geo.PieceAt(sq))).Count > 0;
                best = Math.Max(best, defended ? victim - Value(reply.Piece.Type.AsChar) : victim);
            }
            return gained - best;
        }
        catch { return 0; }
    }

    private static int Value(char type) => char.ToLowerInvariant(type) switch
    { 'p' => 1, 'n' or 'b' => 3, 'r' => 5, 'q' => 9, _ => 0 };

    /// <summary>Material aus Sicht von Weiß (Bauerneinheiten) einer FEN.</summary>
    private static int Material(string fen)
    {
        var sum = 0;
        foreach (var ch in fen.Split(' ')[0])
            if (char.IsLetter(ch)) sum += char.IsUpper(ch) ? Value(ch) : -Value(ch);
        return sum;
    }

    /// <summary>Die Materialbilanz (Sicht von Weiß) nach jedem der ersten <paramref name="plies"/> Züge ab <paramref name="fen"/>.</summary>
    private static List<int> MaterialAlong(string fen, IReadOnlyList<string> sans, int plies)
    {
        var result = new List<int>();
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            foreach (var san in sans.Take(plies))
            {
                var m = Array.Find(board.Moves(generateSan: true), x => x.San == san);
                if (m is null) break;
                board.Move(m);
                result.Add(Material(board.ToFen()));
            }
        }
        catch
        {
            // was bis hier gespielt ist, zählt
        }
        return result;
    }

    /// <summary>
    /// Stellt die andere Verteidigung offensichtlich Material ein (1.001.1, Befund Vereinspartie 261, 19...Le5: 20...Lxf4
    /// 21.Lxf4 — „nutzlos, absolut offensichtlich“)? Ja, wenn sie kein Schach gibt, der erste Zug der Engine-Linie schlägt,
    /// die Materialbilanz des Verteidigers nach einem UND nach zwei Halbzügen der Linie mindestens
    /// <see cref="ObviousLossPawns"/> Bauern unter der der Hauptlinie (Zug des Verteidigers + zwei Halbzüge) liegt und auch
    /// die Bewertung mindestens so viel schlechter ist (eine Gegendrohung, die das Material zurückholt, bleibt eine echte
    /// Wahl). Nicht betroffen sind Widerlegungen mit Schach oder Gabel wie 245/25...Lc8 (26...Txc8 27.Sd7+). → der
    /// schlagende Zug (SAN) oder <c>null</c>.
    /// </summary>
    private static string? ObviousLossOf(Spot spot, string san, string fenAfter, IReadOnlyList<string> line, Ev ev, MoveComparisonLine weak)
    {
        if (san.Contains('+') || san.Contains('#') || line.Count == 0 || !line[0].Contains('x')) return null;
        var defenderWhite = FenFields.WhiteToMove(spot.FenBefore);
        int Own(int whiteMaterial) => defenderWhite ? whiteMaterial : -whiteMaterial;
        if (MoveComparisonBoard.AfterMove(spot.FenBefore, spot.MainSan) is not { } mainFen) return null;
        var main = MaterialAlong(mainFen, spot.MainNext, 2).Select(Own).Prepend(Own(Material(mainFen))).Max();
        var alt = MaterialAlong(fenAfter, line, 2).Select(Own).ToList();
        if (alt.Count == 0 || alt.Max() > main - ObviousLossPawns) return null;
        // Die Bewertung muss es bestätigen (aus Sicht des Verteidigers; Matt zählt wie ±1000).
        if (EvalOf(weak.Fen, weak.ResultJson) is not { } mainEv) return null;
        if (OwnScore(mainEv, defenderWhite) - OwnScore(ev, defenderWhite) < ObviousLossPawns) return null;
        return line[0];
    }

    private static double OwnScore(Ev e, bool white)
    {
        var w = Math.Clamp(e.Score / 100.0, -1000, 1000);
        return white ? w : -w;
    }

    private static char PieceType(Move m) => char.ToLowerInvariant(m.Piece.Type.AsChar);

    /// <summary>
    /// Geprüfte Aussagen über das Feld, auf das der erste Zug der Widerlegung zieht — der „eigentliche Grund“: (1) es ist das
    /// Feld, das der Fehlzug geräumt hat; (2) nach dem Fehlzug deckten es nur Figuren, die inzwischen nicht mehr dort stehen
    /// (geschlagen oder weggelenkt), und jetzt deckt es der Verteidiger gar nicht mehr. Nur Brettgeometrie
    /// (<see cref="BoardGeometry"/>): „decken“ heißt bestreichen, Fesselungen bleiben außen vor.
    /// </summary>
    private static List<string> SquareFacts(MoveComparison c, MoveComparisonLine weak, string fenAfter, IReadOnlyList<string> line, string defenceUci)
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
                // Ist die deckende Figur selbst auf das Feld gezogen (20...Lxf4 21.Lxf4 in Partie 261), ist sie nicht „weggelenkt“ —
                // der Satz „f4 war nur vom Läufer gedeckt“ führte den Text in die Irre (1.001.1).
                var selfMoved = defenceUci.Length >= 4 && defenceUci[2..4] == square && covered.Any(p => p.Square == defenceUci[..2]);
                if (!selfMoved && covered.Count > 0 && covered.All(p => now.PieceAt(p.Square) != p.Piece) && now.Attackers(square, defenderWhite).Count == 0)
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
