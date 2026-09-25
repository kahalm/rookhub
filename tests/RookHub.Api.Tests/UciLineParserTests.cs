using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>Einzelheiten des uci.rs-Ports, die die Vektor-Tests (<see cref="EmitBuilderVectorTests"/>) nur
/// indirekt zeigen.</summary>
public class UciLineParserTests
{
    [Fact]
    public void Info_ReadsAllFields()
    {
        var p = UciLineParser.Parse(
            "info depth 20 seldepth 30 multipv 2 score cp -35 lowerbound nodes 123456 nps 999 hashfull 12 tbhits 0 time 789 pv e2e4 e7e5 g1f3");
        Assert.Equal(UciLineKind.Info, p.Kind);
        var i = p.Info!;
        Assert.Equal(20u, i.Depth);
        Assert.Equal(30u, i.SelDepth);
        Assert.Equal(2, i.MultiPv);
        Assert.Equal(new UciScore(false, -35, true, false), i.Score);
        Assert.Equal(123456ul, i.Nodes);
        Assert.Equal(789ul, i.TimeMs);
        Assert.Equal(["e2e4", "e7e5", "g1f3"], i.Pv);
    }

    [Fact]
    public void Score_MateNegative_AndBothBounds()
    {
        var i = UciLineParser.Parse("info depth 3 score mate -3 upperbound lowerbound pv h7h8").Info!;
        Assert.Equal(new UciScore(true, -3, true, true), i.Score);
    }

    [Theory]
    [InlineData("bestmove (none)", null, null)]
    [InlineData("bestmove", null, null)]
    [InlineData("bestmove e1g1 ponder e8g8", "e1g1", "e8g8")]
    [InlineData("bestmove e7e8Q ponder (none)", "e7e8q", null)]
    [InlineData("bestmove e2e4 ponder", "e2e4", null)]
    public void BestMove(string line, string? move, string? ponder)
    {
        var p = UciLineParser.Parse(line);
        Assert.Equal(UciLineKind.BestMove, p.Kind);
        Assert.Equal(move, p.BestMove!.Move);
        Assert.Equal(ponder, p.BestMove.Ponder);
    }

    [Theory]
    [InlineData("bestmove e2e4 garbage")]
    [InlineData("bestmove e9e4")]
    [InlineData("info depth 5 score cp 1 wdl 500 400 100 pv e2e4")]   // unbekanntes Schlüsselwort
    [InlineData("info depth -1")]
    [InlineData("info multipv 6")]
    [InlineData("info score")]
    [InlineData("info score cp")]
    [InlineData("info score mate 99999999999")]                         // mate ist i32
    [InlineData("info depth 1\rpv e2e4")]
    public void Errors(string line)
    {
        Assert.Equal(UciLineKind.Error, UciLineParser.Parse(line).Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("readyok")]
    [InlineData("id name Stockfish 19")]
    [InlineData("Stockfish 19 by the Stockfish developers")]
    public void OtherLines_AreIgnored(string line)
    {
        Assert.Equal(UciLineKind.Ignored, UciLineParser.Parse(line).Kind);
    }

    [Fact]
    public void String_SwallowsTheRestOfTheLine()
    {
        var i = UciLineParser.Parse("info depth 1 string NNUE evaluation  using nn-x.nnue   ").Info!;
        Assert.Equal("NNUE evaluation  using nn-x.nnue", i.String);
        Assert.Null(i.Pv);
    }

    [Theory]
    [InlineData("e2e4", true, "e2e4")]
    [InlineData("e7e8q", true, "e7e8q")]
    [InlineData("e7e8Q", true, "e7e8q")]
    [InlineData("n@f3", true, "N@f3")]
    [InlineData("0000", true, "0000")]
    [InlineData("E2E4", false, null)]
    [InlineData("e2e4qq", false, null)]
    [InlineData("e7e8x", false, null)]
    [InlineData("e2", false, null)]
    public void TryParseMove_LikeShakmaty(string input, bool ok, string? expected)
    {
        Assert.Equal(ok, UciLineParser.TryParseMove(input, out var move));
        if (ok) Assert.Equal(expected, move);
    }
}
