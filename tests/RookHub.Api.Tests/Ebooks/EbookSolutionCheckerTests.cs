using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookSolutionCheckerTests
{
    /// <summary>Engine-Attrappe: Bewertung je (FEN-Brett + gespielte Züge) aus einer Tabelle.</summary>
    private sealed class FakeEngine(Dictionary<string, UciResult?> table) : IPositionEvaluator
    {
        public List<string> Calls { get; } = [];
        public Task<UciResult?> EvaluateAsync(string fen, IReadOnlyList<string>? moves, int moveTimeMs, CancellationToken ct)
        {
            var key = moves is { Count: > 0 } ? string.Join(' ', moves) : "root";
            Calls.Add(key);
            return Task.FromResult(table.GetValueOrDefault(key));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private const string Fen = "4k3/8/8/8/8/8/4P3/R3K3 w - - 0 1";

    [Theory]
    [InlineData(300, 250, true)]     // 50 cp schlechter: gleichwertig
    [InlineData(300, 100, false)]    // 200 cp schlechter: fraglich
    [InlineData(9997, 900, true)]    // anderer Zug gewinnt schneller, Buchzug gewinnt trotzdem klar
    [InlineData(9997, 200, false)]
    public void BookMoveOk_ToleranceAndStillWinning(int best, int book, bool ok) => Assert.Equal(ok, EbookSolutionChecker.BookMoveOk(best, book));

    [Theory]
    [InlineData("2kr3r/1pp4p/pQ2pP2/4p3/3bP3/2KP4/PPP3PP/2BR4 b - - 0 17")]   // Weiß (nicht am Zug) im Schach — Prod-Buch 1
    [InlineData("4k3/8/8/8/8/8/8/4K2p w - - 0 1")]                          // Bauer auf der Grundreihe
    public async Task EngineUnsafePosition_IsSkipped_WithoutCallingTheEngine(string fen)
    {
        Assert.False(EbookSolutionChecker.IsLegal(fen));
        var d = new EbookDiagram { Fen = fen };
        var e = new FakeEngine(new() { ["root"] = new UciResult(0, false, ["a1a2"]) });
        await EbookSolutionChecker.CheckAsync(e, d, 100, default);
        Assert.Empty(e.Calls);
        Assert.Equal(EbookSolutionCheck.Unchecked, d.SolutionCheck);
    }

    [Fact]
    public void LegalPosition_IsAccepted() => Assert.True(EbookSolutionChecker.IsLegal(Fen));

    [Fact]
    public async Task BookSolution_BestMove_IsConfirmed()
    {
        var d = new EbookDiagram { Fen = Fen, SolutionUci = "a1a8 e8d7" };
        var e = new FakeEngine(new() { ["root"] = new UciResult(9999, true, ["a1a8"]) });
        await EbookSolutionChecker.CheckAsync(e, d, 100, default);
        Assert.Equal(EbookSolutionCheck.Confirmed, d.SolutionCheck);
        Assert.Equal("book", d.SolutionSource);
        Assert.Equal(9999, d.EngineEval);
    }

    [Fact]
    public async Task BookSolution_SideLineFirst_IsReplacedByTheOtherCandidateFromTheSameParagraph()
    {
        // Gelesen wurde „1.e3“ (Nebenvariante im Text), der Absatz enthält auch „1.Ra8+“ — die Engine nimmt den besseren.
        var d = new EbookDiagram
        {
            Fen = Fen, SolutionUci = "e2e3", SolutionSan = "e3", SolutionText = "1.e3 is too slow. 1.Ra8+ Kd7 wins.", SolutionPage = 5,
        };
        var e = new FakeEngine(new()
        {
            ["root"] = new UciResult(600, false, ["a1a8"]),
            ["e2e3"] = new UciResult(-20, false, []),   // aus Sicht Schwarz: Weiß hat nur noch +20
            ["a1a8"] = new UciResult(-590, false, []),
        });
        await EbookSolutionChecker.CheckAsync(e, d, 100, default);
        Assert.Equal(EbookSolutionCheck.Replaced, d.SolutionCheck);
        Assert.StartsWith("a1a8", d.SolutionUci);
        Assert.StartsWith("Ra8+", d.SolutionSan);
    }

    [Fact]
    public async Task BookSolution_NoBetterCandidate_IsDoubtful_ButKept()
    {
        var d = new EbookDiagram { Fen = Fen, SolutionUci = "e2e3", SolutionText = "1.e3 wins.", SolutionPage = 5 };
        var e = new FakeEngine(new() { ["root"] = new UciResult(600, false, ["a1a8"]), ["e2e3"] = new UciResult(-20, false, []) });
        await EbookSolutionChecker.CheckAsync(e, d, 100, default);
        Assert.Equal(EbookSolutionCheck.Doubtful, d.SolutionCheck);
        Assert.Equal("e2e3", d.SolutionUci);
    }

    [Fact]
    public async Task NoBookSolution_DecisivePosition_GetsTheEngineLine_UpToMate()
    {
        var d = new EbookDiagram { Fen = Fen };
        var e = new FakeEngine(new() { ["root"] = new UciResult(9998, true, ["a1a8", "e8d7", "a8a7"]) });
        await EbookSolutionChecker.CheckAsync(e, d, 100, default);
        Assert.Equal(EbookSolutionCheck.Engine, d.SolutionCheck);
        Assert.Equal("engine", d.SolutionSource);
        Assert.Equal("a1a8 e8d7 a8a7", d.SolutionUci);   // Matt in 2 → 3 Halbzüge
        Assert.Equal("Ra8+ Kd7 Ra7+", d.SolutionSan);
    }

    [Fact]
    public async Task NoBookSolution_BalancedPosition_IsNoPuzzle_AndIllegalFenIsSkipped()
    {
        var d = new EbookDiagram { Fen = Fen };
        await EbookSolutionChecker.CheckAsync(new FakeEngine(new() { ["root"] = new UciResult(30, false, ["e2e4"]) }), d, 100, default);
        Assert.Equal(EbookSolutionCheck.NoPuzzle, d.SolutionCheck);
        Assert.Null(d.SolutionUci);

        var study = new EbookDiagram { Fen = "8/8/8/8/8/8/8/8 w - - 0 1" };
        var e = new FakeEngine([]);
        await EbookSolutionChecker.CheckAsync(e, study, 100, default);
        Assert.Empty(e.Calls);
        Assert.Equal(EbookSolutionCheck.Unchecked, study.SolutionCheck);
    }

    [Fact]
    public void Parse_TakesTheLastScoreLine_MateAsSignedScore()
    {
        var r = UciEngineSession.Parse([
            "info depth 10 score cp 34 pv e2e4 e7e5",
            "info depth 18 score mate 3 pv h5f7 e8e7 f7e7",
            "bestmove h5f7",
        ]);
        Assert.True(r!.IsMate);
        Assert.Equal(9997, r.Score);
        Assert.Equal(["h5f7", "e8e7", "f7e7"], r.Pv);
        Assert.Equal(-9998, UciEngineSession.Parse(["info score mate -2 pv a1a2", "bestmove a1a2"])!.Score);
        Assert.Null(UciEngineSession.Parse(["bestmove (none)"]));
    }
}
