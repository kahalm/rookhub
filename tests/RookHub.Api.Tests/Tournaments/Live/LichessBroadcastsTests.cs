using RookHub.Api.Services;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>Gemeinsamer Lese-Baustein der Lichess-Übertragungen (LeagueHub + Live-Bretter).</summary>
public class LichessBroadcastsTests
{
    [Fact]
    public void ParseTop_ReadsChessResultsTnrFromStandingsAndWebsite()
    {
        var top = LichessBroadcasts.ParseTop("""
            {"active":[{"tour":{"id":"IjDgZVUR","name":"1. Bundesliga Austria 2026/2027",
               "info":{"standings":"https://s3.chess-results.com/tnr1404438.aspx?lan=0&art=0&turdet=YES","website":"https://www.chess.at/"}}}],
             "upcoming":[],
             "past":{"currentPageResults":[{"tour":{"id":"r58BT7YR","name":"Dürnstein","info":{"standings":"https://chess-results.com/tnr1423444.aspx?lan=0"}}}]}}
            """);
        Assert.Equal(["IjDgZVUR", "r58BT7YR"], top.Select(t => t.Id));
        Assert.Equal(["1404438"], top[0].ChessResultsIds);
        Assert.Equal(["1423444"], top[1].ChessResultsIds);
    }

    [Fact]
    public void ParseTourDetail_Rounds()
    {
        var d = LichessBroadcasts.ParseTourDetail("""
            {"tour":{"id":"IjDgZVUR","name":"BL"},"rounds":[
              {"id":"L3MX2oE3","name":"Round 1","ongoing":true,"startsAt":1795611600000},
              {"id":"orkSD4Rv","name":"Round 2","startsAt":1795698000000}]}
            """)!;
        Assert.Equal(2, d.Rounds.Count);
        Assert.True(d.Rounds[0].Ongoing);
        Assert.False(d.Rounds[1].Finished);
        Assert.False(d.AllFinished);
    }

    [Theory]
    [InlineData("Round 3", 3)]
    [InlineData("Runde 3", 3)]
    [InlineData("3. Runde", 3)]
    [InlineData("R3", 3)]
    [InlineData("Finale", null)]
    public void RoundNumberOf(string name, int? expected) => Assert.Equal(expected, LichessBroadcasts.RoundNumberOf(name));

    [Fact]
    public void RoundFor_ByNumber_ElseByOrder()
    {
        var named = new List<LichessBroadcasts.Round> { new("a", "Round 2", null, false, false), new("b", "Round 1", null, false, false) };
        Assert.Equal("b", LichessBroadcasts.RoundFor(named, 1)!.Id);
        var unnamed = new List<LichessBroadcasts.Round> { new("a", "Vorrunde", null, false, false), new("b", "Finale", null, false, false) };
        Assert.Equal("b", LichessBroadcasts.RoundFor(unnamed, 2)!.Id);
        Assert.Null(LichessBroadcasts.RoundFor(unnamed, 3));
    }

    [Fact]
    public void IdsOf_GameLinkCountsAsRound() =>
        Assert.Equal(((string?)null, "L3MX2oE3"),
            LichessBroadcasts.IdsOf("https://lichess.org/broadcast/1-bundesliga-austria-20262027/round-1/L3MX2oE3/1pPEJGno"));
}
