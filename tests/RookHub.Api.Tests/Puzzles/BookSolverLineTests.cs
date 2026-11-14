using RookHub.Api.Services.Puzzles;
using Xunit;

namespace RookHub.Api.Tests.Puzzles;

/// <summary>Wer löst ein Buchdiagramm (<see cref="BookSolverLine"/>, 0.973.1): nie die Seite, die in der Linie mattgesetzt wird.</summary>
public class BookSolverLineTests
{
    // Prod 2026-11-14, BookPuzzles 160276 („Alekhine: Move by Move“, S. 154): Schwarz am Zug, 30…Ta8 31.Tb6 Txa7 32.Tb8#.
    internal const string AlekhineFen = "3r1k2/P4pbp/2R3p1/8/5P2/6P1/Pn3P1P/4R1K1 b - - 0 30";
    internal const string AlekhineMoves = "d8a8 c6b6 a8a7 b6b8";
    // Prod, dieselbe Partie (…:02, „1500 Forced Mates“): Weiß am Zug setzt in vier Zügen matt.
    internal const string ForcedMateFen = "3R4/p1r1rppk/Q6p/1pP5/1P2n1qP/8/1B6/1K1R4 w - - 0 1";
    internal const string ForcedMateMoves = "d8h8 h7h8 a6h6 h8g8 d1d8 e7e8 d8e8";
    internal const string ForkFen = "r3k3/8/8/3N4/8/8/8/4K3 w - - 0 1";
    internal const string ForkMoves = "d5c7 e8d7 c7a8";

    [Fact]
    public void Of_OpponentOfTheSideToMoveMates_ClassicFormWithTheOpponentMoveFirst()
    {
        var line = BookSolverLine.Of(AlekhineFen, AlekhineMoves, engineEval: -9998);
        Assert.NotNull(line);
        Assert.True(line.OpponentFirst);                       // Löser = Weiß: 31.Tb6 Txa7 32.Tb8#
        Assert.Equal(0, line.StartPly);
        Assert.Equal(["d8a8", "c6b6", "a8a7", "b6b8"], line.Moves);
        // Die Bewertung spielt bei einem Matt keine Rolle.
        Assert.True(BookSolverLine.Of(AlekhineFen, AlekhineMoves, null)!.OpponentFirst);
    }

    [Fact]
    public void Of_SideToMoveMates_SolverMovesFirst()
    {
        var line = BookSolverLine.Of(ForcedMateFen, ForcedMateMoves, null);
        Assert.NotNull(line);
        Assert.False(line.OpponentFirst);
        Assert.Equal(-1, line.StartPly);
        Assert.Equal(7, line.Moves.Count);
        Assert.False(BookSolverLine.Of("6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1", "h6g7", null)!.OpponentFirst);
    }

    [Theory]
    [InlineData(600, true)]      // Seite am Zug steht klar auf Gewinn → sie löst
    [InlineData(200, true)]
    [InlineData(199, false)]     // nicht klar
    [InlineData(0, false)]
    [InlineData(-600, false)]    // Seite am Zug verliert, die Linie endet aber nicht matt → nicht sicher bestimmbar
    [InlineData(null, false)]    // keine Bewertung
    public void Of_NoMate_OnlyWhenTheSideToMoveClearlyWins(int? eval, bool solvable)
    {
        var line = BookSolverLine.Of(ForkFen, ForkMoves, eval);
        Assert.Equal(solvable, line is not null);
        if (line is not null) Assert.False(line.OpponentFirst);
    }

    [Theory]
    [InlineData(ForkFen, "")]                                  // keine Linie
    [InlineData(ForkFen, "a1a8")]                              // leeres Ausgangsfeld
    [InlineData(ForkFen, "d5d7")]                              // kein Springerzug
    [InlineData(ForkFen, "a8a7")]                              // falsche Seite am Zug
    [InlineData(ForkFen, "d5c7 a8a7")]                         // König bleibt im Schach stehen
    [InlineData("kaputt", "h6g7")]
    [InlineData("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g7 g8h8")]   // Zug nach dem Matt
    public void Of_UnplayableLine_NoExercise(string fen, string moves) =>
        Assert.Null(BookSolverLine.Of(fen, moves, 9999));

    [Fact]
    public void Of_CastlingAndPromotion_ArePlayed()
    {
        // Umwandlung braucht die Figur; die Rochade wird ausgeführt (das Brett prüft sie nicht): 1.0-0-0 Kh8 2.Td8#.
        var promo = BookSolverLine.Of("7k/5P2/6K1/8/8/8/8/8 w - - 0 1", "f7f8q", null);
        Assert.NotNull(promo);
        Assert.False(promo.OpponentFirst);
        Assert.Null(BookSolverLine.Of("7k/5P2/6K1/8/8/8/8/8 w - - 0 1", "f7f8", 9999));   // Umwandlung ohne Figur
        var castle = BookSolverLine.Of("6k1/5ppp/8/8/8/8/8/R3K3 w Q - 0 1", "e1c1 g8h8 d1d8", null);
        Assert.NotNull(castle);
        Assert.False(castle.OpponentFirst);
    }

    // ── Leser, Modus „Lösen“ (0.975.0): dieselbe Regel, je Diagramm in der Antwort ──────────────────────────────

    [Fact]
    public void ForReader_SideToMoveMates_SolvableSolverFirst() =>
        Assert.Equal(new BookSolverLine.ReaderSolve(true, false), BookSolverLine.ForReader(ForcedMateFen, ForcedMateMoves, null));

    [Fact]
    public void ForReader_OpponentMates_SolvableWithTheOpponentMoveFirst()
    {
        // Genau der gemeldete Fall: Schwarz am Zug, Weiß setzt matt — der Leser spielt 30…Ta8 vor, der Löser hat Weiß.
        Assert.Equal(new BookSolverLine.ReaderSolve(true, true), BookSolverLine.ForReader(AlekhineFen, AlekhineMoves, -9998));
        Assert.Equal(new BookSolverLine.ReaderSolve(true, true), BookSolverLine.ForReader(AlekhineFen, AlekhineMoves, null));
    }

    [Theory]
    [InlineData(600, true)]      // klar vorn
    [InlineData(0, true)]        // ausgeglichen: der Leser kennt „Remis bleibt Remis“
    [InlineData(-199, true)]
    [InlineData(null, true)]     // noch nicht bewertet
    [InlineData(-200, false)]    // Seite am Zug verliert, kein Matt in der Linie → Löserseite nicht bestimmbar
    [InlineData(-9000, false)]
    public void ForReader_NoMate_NotOfferedOnlyWhenTheSideToMoveClearlyLoses(int? eval, bool solvable) =>
        Assert.Equal(new BookSolverLine.ReaderSolve(solvable, false), BookSolverLine.ForReader(ForkFen, ForkMoves, eval));

    [Theory]
    [InlineData(ForkFen, "d5d7")]                              // kein Springerzug
    [InlineData(ForkFen, "a8a7")]                              // falsche Seite am Zug
    [InlineData("kaputt", "h6g7")]
    [InlineData("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g7 g8h8")]   // Zug nach dem Matt
    public void ForReader_UnplayableLine_NotOffered(string fen, string moves) =>
        Assert.Equal(new BookSolverLine.ReaderSolve(false, false), BookSolverLine.ForReader(fen, moves, 9999));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ForReader_NoStoredLine_StaysSolvable(string? moves) =>
        // Ohne gespeicherte Linie löst man wie bisher gegen die im Browser gerechnete Stockfish-Linie.
        Assert.Equal(new BookSolverLine.ReaderSolve(true, false), BookSolverLine.ForReader(ForkFen, moves, -900));
}
