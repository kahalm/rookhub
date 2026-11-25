using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>Der Strom kommt in beliebigen Stücken — ganze Partien sollen trotzdem genau einmal herauskommen.</summary>
public class BroadcastPgnSplitterTests
{
    private static readonly string Two =
        LivePgn.Game("g1", "A", "B", "1. d4 { [%clk 1:30:59] } 1... Nf6 { [%clk 1:30:55] }")
        + LivePgn.Game("g2", "C", "D", "1. e4 { [%clk 1:30:56] } 1... e5 { [%clk 1:30:40] } 2. Nf3", "1-0");

    [Fact]
    public void Feed_WholeText_TwoGames()
    {
        var s = new BroadcastPgnSplitter();
        var games = s.Feed(Two);
        Assert.Equal(2, games.Count);
        Assert.Empty(s.Flush());
        Assert.Equal("g2", LiveGameParser.Parse(games[1])!.Id);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void Feed_InChunks_SameGames(int size)
    {
        var s = new BroadcastPgnSplitter();
        var games = new List<string>();
        for (var i = 0; i < Two.Length; i += size) games.AddRange(s.Feed(Two.AsSpan(i, Math.Min(size, Two.Length - i))));
        games.AddRange(s.Flush());
        Assert.Equal(["g1", "g2"], games.Select(g => LiveGameParser.Parse(g)!.Id));
        Assert.Equal(1 * 3600 + 30 * 60 + 55, LiveGameParser.Parse(games[0])!.Black.ClockSeconds);
    }

    [Fact]
    public void Feed_GameWithoutTrailingNewline_ComesOnlyWithResultToken()
    {
        var s = new BroadcastPgnSplitter();
        var text = LivePgn.Game("g3", "A", "B", "1. e4").TrimEnd();          // „… 1. e4 *" ohne Zeilenende
        Assert.Empty(s.Feed(text));
        Assert.Single(s.Feed("\n"));
    }

    [Fact]
    public void Feed_CommentWithBracketLine_NotAHeader()
    {
        var s = new BroadcastPgnSplitter();
        var pgn = "[Event \"x\"]\n[White \"A\"]\n[Black \"B\"]\n[Result \"*\"]\n\n1. e4 { lang\n[%clk 0:10:00] } e5 *\n";
        var games = s.Feed(pgn);
        Assert.Single(games);
        Assert.Equal(2, LiveGameParser.Parse(games[0])!.Plies);
    }
}
