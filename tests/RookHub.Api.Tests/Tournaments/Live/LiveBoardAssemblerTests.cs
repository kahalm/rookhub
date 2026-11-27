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
            // Ohne Round-Tag (Rückfall Meldeliste); Brett 2 (Vaibhav, Schwarz) kommt in der Liste zuerst.
            G("b2", "Schmider, Niklas", "Vaibhav, Suri", "12993778", "5045185", "1-0", round: ""),
            G("b1", "Aditya Mittal", "Hagner, Bennet", "35042025", "16224477", "1/2-1/2", round: ""),
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

    /// <summary>Elo-Schnitt je Mannschaft (1.025.0): Elo aus dem Strom, fehlt sie, aus der Startliste; ohne Elo zählt nicht.</summary>
    [Fact]
    public void Team_EloAverage_StreamThenRoster_SkipsMissing()
    {
        var players = new List<CrPlayer>
        {
            new("Aditya, Mittal", "35042025", 2616, "Pinggau-Friedberg", 1),
            new("Vaibhav, Suri", "5045185", 2569, "Pinggau-Friedberg", 2),
            new("Hagner, Bennet", "16224477", 2462, "SK Schwaz", 1),
            new("Schmider, Niklas", "12993778", null, "SK Schwaz", 2),
        };
        var round = new CrRound(true, players, [], [new CrMatch(1, "Pinggau-Friedberg", "SK Schwaz", null, null)]);
        var b1 = G("b1", "Aditya Mittal", "Hagner, Bennet", "35042025", "16224477", round: "1.1");      // Strom: 2616 / 2462
        var b2 = G("b2", "Schmider, Niklas", "Vaibhav, Suri", "12993778", "5045185", round: "1.2");
        // Brett 2 ohne Elo im Strom: Vaibhav aus der Startliste (2569), Schmider hat auch dort keine → zählt nicht.
        b2 = b2 with { White = b2.White with { Elo = null }, Black = b2.Black with { Elo = null } };
        var m = LiveBoardAssembler.Assemble(round, [b1, b2]).Matches.Single();
        Assert.Equal((2593, 2), (m.HomeElo, m.HomeEloBoards));                // (2616 + 2569) / 2 = 2592,5 → 2593
        Assert.Equal((2462, 1), (m.AwayElo, m.AwayEloBoards));
        var empty = LiveBoardAssembler.Assemble(round, []).Matches.Single();
        Assert.Equal(((int?)null, 0), (empty.HomeElo, empty.HomeEloBoards));
    }

    /// <summary>
    /// Echtes Muster der 1. BL 2026/27, Runde 1 (Lichess-Runde L3MX2oE3): „1.1"–„1.6" = Begegnung 1, „1.7"–„1.12" =
    /// Begegnung 2, je Block in Brettreihenfolge mit abwechselnden Farben. Die Meldelisten-Positionen stehen absichtlich
    /// quer (Ersatzspieler, Brett 1 der Meldeliste spielt nicht) — die Tags gewinnen.
    /// </summary>
    [Fact]
    public void Team_LichessRoundTagsBlockwise_OrderBoards_RosterOnlyFallback()
    {
        var players = new List<CrPlayer>();
        var games = new List<LiveGame>();
        for (var match = 0; match < 2; match++)
        {
            var home = match == 0 ? "Heim A" : "Heim B";
            var away = match == 0 ? "Gast A" : "Gast B";
            for (var b = 1; b <= 6; b++)
            {
                var hf = $"99{match}1{b:00}";
                var af = $"99{match}2{b:00}";
                // Meldeliste quer zur Brettfolge: 7 − b (Brett 1 hat die höchste Meldenummer).
                players.Add(new CrPlayer($"H{match}{b}, X", hf, 2400, home, 7 - b));
                players.Add(new CrPlayer($"A{match}{b}, Y", af, 2400, away, 7 - b));
                var homeWhite = b % 2 == 1;
                var tag = $"1.{match * 6 + b}";
                games.Add(homeWhite
                    ? G($"m{match}b{b}", $"H{match}{b}, X", $"A{match}{b}, Y", hf, af, round: tag)
                    : G($"m{match}b{b}", $"A{match}{b}, Y", $"H{match}{b}, X", af, hf, round: tag));
            }
        }
        games.Reverse();                                               // Eingangsreihenfolge egal
        var round = new CrRound(true, players, [],
            [new CrMatch(1, "Heim A", "Gast A", null, null), new CrMatch(2, "Heim B", "Gast B", null, null)]);
        var layout = LiveBoardAssembler.Assemble(round, games);
        for (var match = 0; match < 2; match++)
        {
            var m = layout.Matches[match];
            Assert.Equal(Enumerable.Range(1, 6).Select(b => $"m{match}b{b}"), m.Boards.Select(x => x.GameId));
            Assert.Equal([1, 2, 3, 4, 5, 6], m.Boards.Select(x => x.Board));
            Assert.Equal([true, false, true, false, true, false], m.Boards.Select(x => x.HomeIsWhite));
        }
        Assert.Empty(layout.Unassigned);
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

    /// <summary>Prod 27.11.2026 (1. BL Runde 3, Schach ohne Grenzen – Schwaz): drei Generationen derselben Runde im Bestand —
    /// vorbelegt mit falschen Paarungen ohne Zug (a), neu angelegt (b), nochmals neu angelegt (c, aktuell). Je Brettplatz bleibt
    /// genau die jüngste.</summary>
    [Fact]
    public void Team_GenerationswechselDerIds_JeBrettplatzNurDieJuengste()
    {
        var round = new CrRound(true, [
            new("Baldauf, Marco", "990201", 2450, "Schach ohne Grenzen", 1), new("Hagner, Bennet", "990202", 2462, "Schachklub Schwaz", 1),
            new("Schmider, Niklas", "990203", 2398, "Schachklub Schwaz", 2), new("Mons, Leon", "990204", 2400, "Schach ohne Grenzen", 2),
            new("Aditya Mittal", "990205", 2616, "Pinggau-Friedberg", 1),
        ], [], [new CrMatch(1, "Schach ohne Grenzen", "Schachklub Schwaz", null, null)]);
        LiveGame Gm(string id, string tag, string w, string b, string wf, string bf, long seen, bool moves = true) =>
            LiveGameParser.Parse(LivePgn.Game(id, w, b, moves ? "1. e4 { [%clk 1:30:00] }" : "", "*", wf, bf, round: tag))! with { Seen = seen };
        var games = new[]
        {
            Gm("5mv2r87i", "3.1", "Aditya Mittal", "Hagner, Bennet", "990205", "990202", 1, moves: false),   // a: vorbelegt
            Gm("WmuQuGE5", "3.1", "Baldauf, Marco", "Hagner, Bennet", "990201", "990202", 10),                // b
            Gm("wMAduO8I", "3.2", "Schmider, Niklas", "Mons, Leon", "990203", "990204", 11),                  // b
            Gm("fGU4RMj9", "3.1", "Baldauf, Marco", "Hagner, Bennet", "990201", "990202", 20),                // c (aktuell)
            Gm("W0ALPVpb", "3.2", "Schmider, Niklas", "Mons, Leon", "990203", "990204", 21),                  // c (aktuell)
        };
        var layout = LiveBoardAssembler.Assemble(round, games);
        Assert.Equal(["fGU4RMj9", "W0ALPVpb"], layout.Matches.Single().Boards.Select(b => b.GameId));
        Assert.Empty(layout.Unassigned);
    }

    /// <summary>Zwei verschiedene Partien MIT Zügen auf demselben Tag sind kein Generationswechsel — beide bleiben; ohne
    /// „r.n“-Tag (Einzelturnier mit „3“ für alle) greift das Netz gar nicht.</summary>
    [Fact]
    public void GleicherTag_VerschiedeneSpielerMitZuegen_OderTagOhneBrett_BleibenAlle()
    {
        var a = G("a1", "Eins, A", "Zwei, B", "990301", "990302", round: "3.1") with { Seen = 1 };
        var b = G("b1", "Drei, C", "Vier, D", "990303", "990304", round: "3.1") with { Seen = 2 };
        Assert.Equal(2, LiveBoardAssembler.Current([a, b]).Count);
        var s1 = G("s1", "Eins, A", "Zwei, B", "990301", "990302", round: "3") with { Seen = 1 };
        var s2 = G("s2", "Eins, A", "Zwei, B", "990301", "990302", round: "3") with { Seen = 2 };
        Assert.Equal(2, LiveBoardAssembler.Current([s1, s2]).Count);
    }
}
