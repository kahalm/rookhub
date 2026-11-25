using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>Partien aus dem Übertragungs-PGN: Stellung, letzter Zug, Uhren (%clk), Bewertung (%eval), Spieler.</summary>
public class LiveGameParserTests
{
    private const string Moves = "1. d4 { [%clk 1:30:59] } 1... Nf6 { [%clk 1:30:55] } 2. c4 { [%clk 1:31:27] } 2... e6 { [%clk 1:31:18] }";

    [Fact]
    public void Parse_RealBroadcastGame_FenLastMoveClocksPlayers()
    {
        var g = LiveGameParser.Parse(LivePgn.Game("1pPEJGno", "Aditya Mittal", "Hagner, Bennet", Moves,
            whiteFide: "35042025", blackFide: "16224477", whiteTeam: "Pinggau-Friedberg", blackTeam: "Schachklub Schwaz"))!;
        Assert.Equal("1pPEJGno", g.Id);
        Assert.Equal("rnbqkb1r/pppp1ppp/4pn2/8/2PP4/8/PP2PPPP/RNBQKBNR w KQkq - 0 3", g.Fen);
        Assert.Equal("e7e6", g.LastMove);
        Assert.Equal(4, g.Plies);
        Assert.Equal(1 * 3600 + 31 * 60 + 27, g.White.ClockSeconds);
        Assert.Equal(1 * 3600 + 31 * 60 + 18, g.Black.ClockSeconds);
        Assert.Equal(("35042025", "Pinggau-Friedberg", 2616, "GM"), (g.White.FideId, g.White.Team, g.White.Elo, g.White.Title));
        Assert.Equal("16224477", g.Black.FideId);
        Assert.Equal("*", g.Result);
        Assert.False(g.Finished);
        Assert.True(g.WhiteToMove);
        Assert.Null(g.Eval);
    }

    [Fact]
    public void Parse_EvalAndMate_FromLastPly()
    {
        var g = LiveGameParser.Parse(LivePgn.Game("x", "A", "B", "1. e4 { [%eval 0.18] [%clk 0:10:00] } 1... e5 { [%eval -0.3,24] [%clk 0:09:58] }"))!;
        Assert.Equal(new LiveEval(-30, null, "broadcast", 24), g.Eval);
        var m = LiveGameParser.Parse(LivePgn.Game("y", "A", "B", "1. e4 { [%eval #-3] }"))!;
        Assert.Equal(-3, m.Eval!.Mate);
    }

    [Fact]
    public void Parse_FinishedGame_SkipsVariationsAndNags()
    {
        var g = LiveGameParser.Parse(LivePgn.Game("z", "A", "B", "1. e4 $1 (1. d4 { [%clk 9:59:59] } d5) 1... e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7#", "1-0"))!;
        Assert.True(g.Finished);
        Assert.Equal("h5f7", g.LastMove);
        Assert.Equal(7, g.Plies);
        Assert.Null(g.White.ClockSeconds);                       // die Uhr der Variante zählt nicht
    }

    [Fact]
    public void Parse_IllegalMove_KeepsPositionBefore()
    {
        var g = LiveGameParser.Parse(LivePgn.Game("w", "A", "B", "1. e4 e5 2. Ke3"))!;
        Assert.Equal(2, g.Plies);
        Assert.Equal("e7e5", g.LastMove);
    }

    [Fact]
    public void Parse_SameText_SameHash_ChangedText_OtherHash()
    {
        var a = LiveGameParser.Parse(LivePgn.Game("v", "A", "B", "1. e4"))!;
        var b = LiveGameParser.Parse(LivePgn.Game("v", "A", "B", "1. e4"))!;
        var c = LiveGameParser.Parse(LivePgn.Game("v", "A", "B", "1. e4 e5"))!;
        Assert.Equal(a.Hash, b.Hash);
        Assert.NotEqual(a.Hash, c.Hash);
    }

    [Fact]
    public void ClockOf_ParsesFractions() => Assert.Equal(59, LiveGameParser.ClockOf("[%clk 0:00:59.4]"));
}
