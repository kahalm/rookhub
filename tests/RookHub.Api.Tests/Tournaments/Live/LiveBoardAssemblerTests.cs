using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>Lichess-Partien → chess-results-Bretter: FIDE-ID zuerst, Name als Rückfall, Reihenfolge nach chess-results.</summary>
public class LiveBoardAssemblerTests
{
    private static LiveGame G(string id, string white, string black, string? wf, string? bf, string result = "*",
        string? wt = null, string? bt = null, string round = "1.1") =>
        LiveGameParser.Parse(LivePgn.Game(id, white, black, "1. e4", result, wf, bf, wt, bt, round))!;

    [Fact]
    public void Team_GamesGoToMatch_OrderedByRosterBoard_ColorPerBoard()
    {
        var players = new List<CrPlayer>
        {
            new("Pranav, V", "25060783", 2679, "Pinggau-Friedberg", 1),
            new("Aditya, Mittal", "35042025", 2616, "Pinggau-Friedberg", 2),
            new("Vaibhav, Suri", "5045185", 2569, "Pinggau-Friedberg", 3),
            new("Hagner, Bennet", "16224477", 2462, "SK Schwaz", 1),
            new("Schmider, Niklas", "12993778", 2398, "SK Schwaz", 2),
            new("Muster, Max", "990001", 2000, "SK Absam", 1),
        };
        var round = new CrRound(true, players, [],
            [new CrMatch(1, "Pinggau-Friedberg", "SK Schwaz", null, null), new CrMatch(2, "SK Absam", "SK Wörgl", null, null)]);
        var games = new[]
        {
            // Lichess-Reihenfolge absichtlich umgedreht: Brett 2 (Vaibhav, Schwarz) kommt zuerst.
            G("b2", "Schmider, Niklas", "Vaibhav, Suri", "12993778", "5045185", "1-0", round: "1.1"),
            G("b1", "Aditya Mittal", "Hagner, Bennet", "35042025", "16224477", "1/2-1/2", round: "1.2"),
            G("x", "Fremd, Eins", "Fremd, Zwei", "990098", "990099"),
        };
        var layout = LiveBoardAssembler.Assemble(round, games);
        Assert.True(layout.Team);
        var m = layout.Matches.Single(x => x.MatchNumber == 1);
        Assert.Equal(["b1", "b2"], m.Boards.Select(b => b.GameId));
        Assert.Equal([1, 2], m.Boards.Select(b => b.Board));
        Assert.Equal([true, false], m.Boards.Select(b => b.HomeIsWhite));
        Assert.Equal((0.5m, 1.5m), (m.LiveHome, m.LiveAway));
        Assert.Empty(layout.Matches.Single(x => x.MatchNumber == 2).Boards);
        Assert.Equal(["x"], layout.Unassigned);
    }

    [Fact]
    public void Team_WithoutFideIds_FallsBackToNamesAndTeamHeaders()
    {
        var players = new List<CrPlayer> { new("Hagner, Bennet", null, 2462, "SK Schwaz", 1) };
        var round = new CrRound(true, players, [], [new CrMatch(1, "Pinggau-Friedberg", "SK Schwaz", null, null)]);
        var layout = LiveBoardAssembler.Assemble(round,
            [G("g", "Aditya Mittal", "Bennet Hagner", null, null, wt: "Pinggau-Friedberg", bt: "Schachklub Schwaz")]);
        Assert.Equal("g", layout.Matches[0].Boards.Single().GameId);
        Assert.True(layout.Matches[0].Boards[0].HomeIsWhite);    // Mittal (Heim, über WhiteTeam) hat Weiß
    }

    [Fact]
    public void Single_BoardsFromPairings_FideFirst_NameFallback_SwappedColors()
    {
        var players = new List<CrPlayer>
        {
            new("Oberschmid, Patrik", "1693034", 1923, null, null),
            new("Muster, Max", "990002", 1800, null, null),
            new("Österreicher, Jörg", null, 1700, null, null),
            new("Beispiel, Eva", "990003", 1650, null, null),
        };
        var round = new CrRound(false, players,
            [new CrPairing(2, "Österreicher, Jörg", "Beispiel, Eva", null), new CrPairing(1, "Oberschmid, Patrik", "Muster, Max", null)], []);
        var layout = LiveBoardAssembler.Assemble(round,
        [
            G("p1", "Patrik Oberschmid (AUT)", "Max Muster", "1693034", "990002"),    // Name anders geschrieben, FIDE-ID trägt
            G("p2", "Eva Beispiel", "Jorg Osterreicher", "990003", null),             // Farben vertauscht, Name ohne Umlaute
        ]);
        Assert.False(layout.Team);
        Assert.Equal([1, 2], layout.Boards.Select(b => b.Board));
        Assert.Equal(["p1", "p2"], layout.Boards.Select(b => b.GameId));
        Assert.Empty(layout.Unassigned);
    }

    [Fact]
    public void NameKey_IgnoresOrderCommasAccentsTitles() =>
        Assert.Equal(LiveBoardAssembler.NameKey("Mag. Österreicher, Jörg"), LiveBoardAssembler.NameKey("Jorg Osterreicher"));
}
