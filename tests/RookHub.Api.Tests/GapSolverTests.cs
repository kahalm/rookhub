using System.Diagnostics;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Suche nach den Zügen ZWISCHEN zwei erinnerten Stellungen („Lücke schließen").
/// Geprüft werden die drei Dinge, an denen sie steht und fällt: sie findet die kürzeste Erklärung,
/// sie nennt die Wege ALLE (mehrere sind der Normalfall), und sie unterscheidet „gibt es nicht"
/// von „habe ich nicht gefunden".
/// </summary>
public class GapSolverTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
    private const string AfterE4E5 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2";
    private const string AfterE4E5Nf3 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2";
    private const string AfterFourPlies = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";

    [Fact]
    public void Solve_FindsTheOneMissingHalfMove()
    {
        var result = GapSolver.Solve(Start, AfterE4);

        Assert.Equal("e4", Assert.Single(result.Solutions).San);
        Assert.Equal(1, result.Solutions[0].Plies);
        Assert.Null(result.Reason);
        Assert.False(result.BudgetExhausted);
    }

    [Fact]
    public void Solve_FindsATwoPlyGap()
    {
        var result = GapSolver.Solve(AfterE4, AfterE4E5Nf3);

        Assert.Equal("e5 Nf3", Assert.Single(result.Solutions).San);
        Assert.Equal(2, result.Solutions[0].Plies);
    }

    [Fact]
    public void Solve_MatchesTargets_WhoseLastMoveWasADoublePawnStep()
    {
        // Der Stellungs-Editor schreibt kein en-passant-Feld, die Zug-Erzeugung nach „e5" schon.
        // Verlangte der Vergleich Gleichheit, fände diese Suche NICHTS.
        Assert.DoesNotContain(" e6 ", AfterE4E5);

        var result = GapSolver.Solve(Start, AfterE4E5);

        Assert.Equal("e4 e5", Assert.Single(result.Solutions).San);
    }

    [Fact]
    public void Solve_ReportsEveryWay_WhenTheMovesCanBeTransposed()
    {
        var result = GapSolver.Solve(Start, AfterFourPlies, maxPlies: 4);

        Assert.True(result.Solutions.Count >= 2, $"nur {result.Solutions.Count} Weg(e)");
        Assert.All(result.Solutions, s => Assert.Equal(4, s.Plies));
        Assert.Contains(result.Solutions, s => s.San == "e4 e5 Nf3 Nc6");
        Assert.Contains(result.Solutions, s => s.San == "Nf3 Nc6 e4 e5");
    }

    [Fact]
    public void Solve_ShortestFirst_DoesNotAlsoListLongerDetours()
    {
        // Von der Grundstellung nach 1.e4 geht es auch in drei Halbzügen NICHT (Schwarz käme nicht
        // zurück) — aber die Regel gilt allgemein: ist eine Tiefe fündig, hört die Suche auf.
        var result = GapSolver.Solve(Start, AfterE4, maxPlies: GapSolver.MaxSearchPlies);

        Assert.All(result.Solutions, s => Assert.Equal(1, s.Plies));
    }

    [Fact]
    public void Solve_TooFar_WhenTheGapIsDeeperThanAllowed()
    {
        var result = GapSolver.Solve(AfterE4, AfterE4E5Nf3, maxPlies: 1);

        Assert.Empty(result.Solutions);
        Assert.Equal("too-far", result.Reason);
    }

    [Fact]
    public void Solve_Unreachable_WhenPiecesWouldHaveToAppear()
    {
        // Zusätzliche weiße Dame, aber kein weißer Bauer weg → keine Umwandlung, keine Partie.
        const string extraQueen = "rnbqkbnr/pppppppp/8/8/8/4Q3/PPPPPPPP/RNBQKBNR b KQkq - 0 1";

        var result = GapSolver.Solve(Start, extraQueen);

        Assert.Empty(result.Solutions);
        Assert.Equal("unreachable", result.Reason);
    }

    [Fact]
    public void Solve_SamePosition_IsNotAGap()
    {
        var result = GapSolver.Solve(Start, "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 7 9");

        Assert.Equal("same-position", result.Reason);
    }

    [Theory]
    [InlineData("kein FEN", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "invalid-from")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "", "invalid-to")]
    public void Solve_SaysWhichSideIsUnreadable(string from, string to, string reason)
        => Assert.Equal(reason, GapSolver.Solve(from, to).Reason);

    [Fact]
    public void Solve_BudgetExhausted_IsNotTheSameAsNoSolution()
    {
        var result = GapSolver.Solve(Start, AfterFourPlies, maxPlies: 4, nodeBudget: 40);

        Assert.Empty(result.Solutions);
        Assert.True(result.BudgetExhausted);
        Assert.Equal("budget", result.Reason);
    }

    [Fact]
    public void Solve_StaysWithinItsNodeBudget()
    {
        var result = GapSolver.Solve(Start, AfterFourPlies, maxPlies: 4, nodeBudget: 500);

        Assert.True(result.Nodes <= 500 + 1, $"{result.Nodes} Knoten");
    }

    [Fact]
    public void Solve_DeepestSearch_StaysUnderAFewSeconds()
    {
        // Der teuerste erlaubte Fall: sechs Halbzüge ab der Grundstellung, und die Tiefen 2 und 4
        // müssen erst vollständig scheitern. Das Budget ist die Zusage an den Aufrufer, dass eine
        // Anfrage keinen Request-Thread minutenlang bindet.
        const string afterSixPlies = "r1bqkbnr/1ppp1ppp/p1n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 4";
        var sw = Stopwatch.StartNew();
        var result = GapSolver.Solve(Start, afterSixPlies, maxPlies: GapSolver.MaxSearchPlies);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"{sw.Elapsed.TotalSeconds:0.0} s bei {result.Nodes} Knoten");
        Assert.NotEmpty(result.Solutions);
        Assert.All(result.Solutions, x => Assert.Equal(6, x.Plies));
    }

    [Fact]
    public void Solve_EveryReportedWay_ReallyLeadsToTheTargetPosition()
    {
        // Die Liste ist ein Angebot an den Menschen — steht dort ein Weg, der woanders endet, ist die
        // ganze Auskunft wertlos. Deshalb wird jeder gemeldete Weg nachgespielt.
        var result = GapSolver.Solve(Start, AfterFourPlies, maxPlies: 4);

        Assert.NotEmpty(result.Solutions);
        foreach (var solution in result.Solutions)
        {
            var board = Chess.ChessBoard.LoadFromFen(Start);
            foreach (var san in solution.San.Split(' '))
                Assert.True(board.Move(san), $"„{san}“ aus „{solution.San}“ ist nicht spielbar");
            Assert.True(GapSolver.Matches(board.ToFen(), AfterFourPlies), $"„{solution.San}“ endet woanders");
        }
    }

    [Fact]
    public void MinPlies_IsALowerBound_AndKnowsTheParity()
    {
        Assert.Equal(0, GapSolver.MinPlies(Start, Start));
        Assert.Equal(1, GapSolver.MinPlies(Start, AfterE4));
        Assert.Equal(2, GapSolver.MinPlies(AfterE4, AfterE4E5Nf3));
        // Dieselbe Seite am Zug → gerade Zahl, auch wenn das Brett weniger verlangt.
        Assert.Equal(0, GapSolver.MinPlies(Start, Start) % 2);
        Assert.Equal(int.MaxValue,
            GapSolver.MinPlies(Start, "rnbqkbnr/pppppppp/8/8/8/4Q3/PPPPPPPP/RNBQKBNR b KQkq - 0 1"));
    }

    [Fact]
    public void Matches_IgnoresTheHalfMoveClocks_AndAnUnstatedEnPassantSquare()
    {
        Assert.True(GapSolver.Matches(
            "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2", AfterE4E5));
        Assert.False(GapSolver.Matches(AfterE4, AfterE4E5));
        // Nennen BEIDE ein Feld, muss es dasselbe sein.
        Assert.False(GapSolver.Matches(
            "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2",
            "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2"));
    }
}
