using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// DIE Regel, wer ein Buchdiagramm löst (0.973.1, Meldung 2026-11-14: „da ist Schwarz lost – was soll die Übung bezwecken?“ —
/// „Alekhine: Move by Move“, S. 154: Schwarz am Zug, die Buchlinie ist die Partiefortsetzung 30…Ta8 31.Tb6 Txa7 32.Tb8#, und
/// der Löser sollte die schwarzen Züge spielen). Stockfish bestätigt nur den ERSTEN Zug der Buchlinie als besten — auch der
/// beste Zug einer verlorenen Stellung ist „bestätigt“. Deshalb gilt: <b>der Löser spielt nie die Seite, die in der Linie
/// verliert.</b> Alle Verbraucher einer Buchdiagramm-Aufgabe (<see cref="BookExercises"/> → Empfohlene Übungen → Kurs
/// „Persönliche Taktiken“; Bestandsreparatur <c>PersonalTacticsLineRepair</c>) fragen hier.
///
/// <list type="bullet">
/// <item><b>Linie endet matt</b>: es gewinnt, wer den letzten Zug macht. Ungerade Zahl von Halbzügen → die Seite am Zug löst
/// (<see cref="OpponentFirst"/> falsch). Gerade Zahl → der erste Zug gehört dem Verlierer: klassische Form wie eine
/// Lichess-Aufgabe (Stellung VOR dem Gegnerzug, <c>Moves</c> = Gegnerzug + Lösung; <see cref="OpponentFirst"/> wahr).</item>
/// <item><b>Linie endet nicht matt</b>: sicher ist die gewinnende Seite nur, wenn die Engine die Seite am Zug klar vorn sieht
/// (≥ <see cref="GameExercises.MinTacticCp"/>, dieselbe Schwelle wie die Motiv-Signatur) — dann löst sie. Sonst (keine
/// Bewertung, ausgeglichen, Seite am Zug steht schlechter) ist die Seite NICHT bestimmbar: keine Aufgabe. Bewusst kein
/// Umdrehen bei klar schlechterer Bewertung: die Antwort des Gewinners stammt dann ungeprüft aus dem Buch und endet nicht in
/// einem Matt, das sie selbst belegt.</item>
/// <item><b>Plausibilität</b>: leere oder nicht regelgerecht nachspielbare Linie → keine Aufgabe; nach Abzug des Gegnerzugs
/// muss mindestens ein Löserzug bleiben.</item>
/// </list>
/// </summary>
/// <param name="Moves">Die Linie ab der Diagrammstellung (UCI), unverändert.</param>
/// <param name="OpponentFirst">Der erste Zug ist der des Gegners (klassische Form, <c>StartPly = 0</c>); sonst ist der Löser
/// in der Diagrammstellung am Zug (<c>StartPly = -1</c>).</param>
public sealed record BookSolverLine(IReadOnlyList<string> Moves, bool OpponentFirst)
{
    /// <summary><c>BookPuzzle.StartPly</c> der Aufgabe.</summary>
    public int StartPly => OpponentFirst ? 0 : -1;

    /// <param name="fen">Diagrammstellung.</param>
    /// <param name="solutionUci">Buchlinie ab der Diagrammstellung, durch Leerzeichen getrennt.</param>
    /// <param name="engineEval">Bewertung der Diagrammstellung für die Seite am Zug (<c>EbookDiagram.EngineEval</c>); nur für
    /// Linien ohne Matt nötig.</param>
    /// <returns><c>null</c> = keine lösbare Aufgabe (Linie unspielbar oder gewinnende Seite nicht bestimmbar).</returns>
    public static BookSolverLine? Of(string? fen, string? solutionUci, int? engineEval)
    {
        var moves = (solutionUci ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (moves.Length == 0 || PlayLegal(fen, moves) is not { } end) return null;
        if (MatePattern.IsMate(end)) return new BookSolverLine(moves, moves.Length % 2 == 0);
        return engineEval is >= GameExercises.MinTacticCp ? new BookSolverLine(moves, false) : null;
    }

    /// <summary>
    /// Dieselbe Regel für den Modus „Lösen“ des E-Book-Lesers (0.975.0) — der Leser rechnet nichts davon selbst, er bekommt
    /// das Ergebnis je Diagramm (<c>EbookDiagramDto.Solvable</c>/<c>OpponentFirst</c>). Unterschied zu <see cref="Of"/>: eine
    /// Kurs-AUFGABE braucht eine sicher gewinnende Seite (≥ <see cref="GameExercises.MinTacticCp"/>), der Leser zeigt aber
    /// jedes Diagramm des Buchs, und sein Löser kennt „jeder Zug, der das Ergebnis hält, zählt“ (Remis bleibt Remis). Dort
    /// fällt „Lösen“ deshalb nur weg, wenn feststeht, dass der Löser die Verliererseite spielen würde:
    /// <list type="bullet">
    /// <item>keine gespeicherte Linie → lösbar wie bisher (gegen die im Browser gerechnete Stockfish-Linie);</item>
    /// <item>Linie nicht regelgerecht nachspielbar → nicht lösbar;</item>
    /// <item>Linie endet matt → lösbar, bei gerader Halbzugzahl mit dem Gegnerzug zuerst (wie <see cref="Of"/>);</item>
    /// <item>kein Matt: die Seite am Zug steht klar auf Verlust (≤ −<see cref="GameExercises.MinTacticCp"/>) → nicht lösbar
    /// (kein Umdrehen, Begründung oben); sonst — klar vorn, ausgeglichen, noch nicht bewertet — löst die Seite am Zug.</item>
    /// </list>
    /// </summary>
    public static ReaderSolve ForReader(string? fen, string? solutionUci, int? engineEval)
    {
        var moves = (solutionUci ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (moves.Length == 0) return new ReaderSolve(true, false);
        if (PlayLegal(fen, moves) is not { } end) return new ReaderSolve(false, false);
        if (MatePattern.IsMate(end)) return new ReaderSolve(true, moves.Length % 2 == 0);
        return new ReaderSolve(engineEval is not <= -GameExercises.MinTacticCp, false);
    }

    /// <summary>Ergebnis von <see cref="ForReader"/>.</summary>
    /// <param name="Solvable">„Lösen“ wird angeboten.</param>
    /// <param name="OpponentFirst">Der erste Zug der gespeicherten Linie gehört dem Gegner: der Leser spielt ihn vor, der Löser
    /// hat die andere Farbe als die Seite am Zug.</param>
    public readonly record struct ReaderSolve(bool Solvable, bool OpponentFirst);

    /// <summary>Linie regelgerecht nachspielen; <c>null</c>, sobald ein Zug nicht legal ist. Die Rochade kennt
    /// <see cref="TacticsBoard.LegalMoves"/> nicht (keine Rochaderechte im Brett) — sie wird nur ausgeführt, nicht geprüft.</summary>
    internal static TacticsBoard? PlayLegal(string? fen, IReadOnlyList<string> moves)
    {
        var board = TacticsBoard.TryParse(fen);
        foreach (var uci in moves)
        {
            if (board is null) return null;
            if (uci.Length < 4 || TacticsBoard.SquareIndex(uci[..2]) is not int from || TacticsBoard.SquareIndex(uci[2..4]) is not int to) return null;
            if (board.At(from) is not { } piece || TacticsBoard.IsWhite(piece) != board.WhiteToMove) return null;
            var castle = char.ToLowerInvariant(piece) == 'k'
                && (Math.Abs(to % 8 - from % 8) > 1 || (board.At(to) is { } target && TacticsBoard.IsWhite(target) == TacticsBoard.IsWhite(piece)));
            if (!castle)
            {
                char? promo = uci.Length >= 5 ? char.ToLowerInvariant(uci[4]) : null;
                if (!board.IsLegal(from, to, promo)) return null;
            }
            board = board.Apply(uci);
        }
        return board;
    }
}
