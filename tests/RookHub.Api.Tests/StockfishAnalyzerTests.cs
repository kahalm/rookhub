using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Reine Parse-Logik des UCI-Outputs (ohne echten Engine-Prozess).</summary>
public class StockfishAnalyzerTests
{
    [Fact]
    public void ParseUciOutput_Mate_ReturnsHashAndMateIn()
    {
        var lines = new[]
        {
            "info depth 1 score cp 30 pv e2e4",
            "info depth 12 score mate 3 pv d1h5 g8f6 h5f7",
            "bestmove d1h5 ponder g8f6",
        };

        var hint = StockfishAnalyzer.ParseUciOutput(lines);

        Assert.NotNull(hint);
        Assert.Equal("#3", hint!.EvalText);
        Assert.Equal(3, hint.MateIn);
        Assert.Equal("d1h5", hint.BestMoveUci);
    }

    [Fact]
    public void ParseUciOutput_Centipawns_FormatsPawnsAndKeepsLastScore()
    {
        var lines = new[]
        {
            "info depth 10 score cp 80 pv g1f3",
            "info depth 20 score cp 152 pv e2e4 e7e5",
            "bestmove e2e4",
        };

        var hint = StockfishAnalyzer.ParseUciOutput(lines);

        Assert.NotNull(hint);
        Assert.Equal("+1.5", hint!.EvalText);   // 152 cp → +1.5, letzter Score gewinnt
        Assert.Null(hint.MateIn);
        Assert.Equal("e2e4", hint.BestMoveUci);
    }

    [Fact]
    public void ParseUciOutput_NegativeCp_HasMinusSign()
    {
        var lines = new[] { "info depth 18 score cp -240 pv c7c5", "bestmove c7c5" };

        var hint = StockfishAnalyzer.ParseUciOutput(lines);

        Assert.Equal("-2.4", hint!.EvalText);
    }

    [Fact]
    public void ParseUciOutput_BestmoveNone_NoCrash()
    {
        var lines = new[] { "info depth 1 score mate 0", "bestmove (none)" };

        var hint = StockfishAnalyzer.ParseUciOutput(lines);

        Assert.NotNull(hint);
        Assert.Null(hint!.BestMoveUci);
        Assert.Equal(0, hint.MateIn);
    }

    [Fact]
    public void ParseUciOutput_Empty_ReturnsNull()
    {
        Assert.Null(StockfishAnalyzer.ParseUciOutput(System.Array.Empty<string>()));
    }
}
