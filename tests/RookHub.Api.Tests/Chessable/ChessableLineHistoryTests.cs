using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Chessable;

namespace RookHub.Api.Tests.Chessable;

/// <summary>Chessable-Stand je Linie (0.885.0): jüngster Durchlauf, Stufenregel, Rückfall getReview, Fehler je Zug, Alternativen,
/// Link, Beschriftung des PGN-Abschnitts ohne Änderung des Linien-Schlüssels.</summary>
public class ChessableLineHistoryTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private void Session(string oid, DateTime at, string json, int user = 1) =>
        _db.ChessableSessionMoves.Add(new ChessableSessionMove { UserId = user, Bid = "228856", Oid = oid, MovesJson = json, CreatedAt = at });

    private static readonly DateTime Nov5 = new(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Load_LatestSessionWins_LevelIsTheLowestOwnNonInformationalMove_MissesSumUp()
    {
        // ältere Session: Stufe 3; jüngere: eigene Züge 7/5/9 (der 2 ist informativ, der 1 ist ein Schwarzzug)
        Session("111", Nov5.AddDays(-3), """[{"mid":0,"practiceMode":"w","level":3,"wrong":["d4"]},{"mid":1,"practiceMode":"w","level":3,"wrong":[]}]""");
        Session("111", Nov5, """
            [{"mid":0,"practiceMode":"w","level":7,"wrong":["Nf3","c4"]},{"mid":1,"practiceMode":"w","level":5,"wrong":[]},
             {"mid":2,"practiceMode":"w","level":2,"informational":true},{"mid":3,"practiceMode":"b","level":1,"wrong":["e5"]},
             {"mid":4,"practiceMode":"w","level":9}]
            """);
        Session("111", Nov5.AddDays(1), """[{"mid":0,"level":1}]""", user: 2);   // anderer Nutzer zählt nicht
        await _db.SaveChangesAsync();

        var s = (await new ChessableLineHistory(_db).LoadAsync(1, [new("111", 'w', false)], default))["111"];
        Assert.Equal(Nov5, s.LastTrained);
        Assert.Equal(5, s.Level);
        Assert.Null(s.LevelAsOf);
        Assert.Equal(new ChessableLineHistory.Miss(3, "c4"), s.Misses[0]);   // 1 + 2 über beide Durchläufe, zuletzt c4
        Assert.False(s.Misses.ContainsKey(7));                                // Schwarzzug — nicht die eigene Farbe
        Assert.Single(s.Misses);
    }

    [Fact]
    public async Task Load_NoSession_FallsBackToReviewLevel_ByColor_WithDate()
    {
        var review = """
            {"lesson":{"moves":[
              {"mid":0,"move_white":"e4","move_black":"c6","level":10,"level_b":4,"informational":false},
              {"mid":1,"move_white":"d4","move_black":"d5","level":6,"level_b":8,"informational":false},
              {"mid":2,"move_white":"Nc3","move_black":"","level":2,"level_b":null,"informational":true}]}}
            """;
        _db.ChessableReviewLines.Add(new ChessableReviewLine { UserId = 1, Bid = "1", Oid = "222", Json = review, UpdatedAt = Nov5 });
        _db.ChessableReviewLines.Add(new ChessableReviewLine { UserId = 1, Bid = "1", Oid = "333", Json = review, UpdatedAt = Nov5 });
        await _db.SaveChangesAsync();

        var r = await new ChessableLineHistory(_db).LoadAsync(1, [new("222", 'w', false), new("333", 'b', false), new("444", 'w', false)], default);
        Assert.Equal((6, (DateTime?)Nov5, (DateTime?)null), (r["222"].Level, r["222"].LevelAsOf, r["222"].LastTrained));   // level = Weiß
        Assert.Equal(4, r["333"].Level);                                                                                  // level_b = Schwarz
        Assert.False(r.ContainsKey("444"));
        Assert.Equal("Chessable: noch nicht trainiert", ChessableLineHistory.Summary(null, null));
        Assert.Equal("Chessable: Stufe 6 (Stand vom 05.11.2026)", ChessableLineHistory.Summary(r["222"], null));
    }

    [Fact]
    public async Task Trained_OnlyWithASessionThatDidNotSkip()
    {
        Session("1", Nov5, """[{"mid":0,"level":1,"alternativeSkipped":true}]""");
        Session("2", Nov5, """[{"mid":0,"level":0,"alternativeSkipped":true}]""");
        Session("2", Nov5.AddDays(1), """[{"mid":0,"level":2}]""");
        await _db.SaveChangesAsync();
        var trained = await new ChessableLineHistory(_db).TrainedAsync(1, ["1", "2", "3"], default);
        Assert.Equal(["2"], trained.Order());
    }

    [Theory]
    [InlineData("12 | Steinitz ohne f2-f4 | Alternative 7...a5 #1", null, true)]
    [InlineData("3...g6 #11", "ALTERNATIVE lines", true)]
    [InlineData("Alternatives? no", null, false)]
    [InlineData("Main line, not an alternative", "Kapitel 3", false)]
    [InlineData(null, null, false)]
    public void IsAlternative_ByName(string? white, string? black, bool expected) =>
        Assert.Equal(expected, ChessableLineHistory.IsAlternative(white, black));

    [Fact]
    public void Urls()
    {
        Assert.Equal("https://www.chessable.com/variation/123456/", ChessableLineHistory.VariationUrl("123456"));
        Assert.Equal("https://www.chessable.com/course/228856/", ChessableLineHistory.CourseUrl("228856"));
        Assert.Equal("123", ChessableLineHistory.OidOf("[Event \"x\"]\n[ChessableOid \"123\"]\n\n1. e4 *"));
        Assert.Null(ChessableLineHistory.OidOf("[ChessableOid \"12a\"]"));
        Assert.Null(ChessableLineHistory.OidOf("[Event \"x\"]\n\n1. e4 *"));
    }

    [Theory]
    [InlineData(0, false, 1, "1.Nf3")]
    [InlineData(1, false, 1, "1…e5")]
    [InlineData(13, false, 1, "7…Sc6")]
    [InlineData(0, true, 12, "12…Sc6")]
    public void MoveLabel_NumberAndDots(int ply, bool black, int start, string expected) =>
        Assert.Equal(expected, ChessableLineHistory.MoveLabel(ply, black, start, expected[(expected.IndexOfAny(['.', '…']) + 1)..]));

    [Fact]
    public void Annotate_HeadersAndComments_MovesAndLineKeyUnchanged()
    {
        const string section = "[Event \"x\"]\n[White \"Spanisch\"]\n[Black \"Kapitel\"]\n[ChessableOid \"987\"]\n\n"
                               + "1. e4 e5 {Hauptlinie} 2. Nf3 (2. Bc4 Nf6) 2... Nc6 3. Bb5 $1 a6 *";
        var status = new ChessableLineHistory.Status("987", Nov5, 7, null,
            new Dictionary<int, ChessableLineHistory.Miss> { [2] = new(3, "Nc3"), [4] = new(1, null) });

        var annotated = ChessableLineHistory.Annotate(section, status, "987", startsWithBlack: false, startMoveNumber: 1);

        Assert.Contains("[ChessableLastTrained \"2026.11.05\"]\n[ChessableLevel \"7\"]\n[ChessableUrl \"https://www.chessable.com/variation/987/\"]\n", annotated);
        Assert.Contains("\n\n{Chessable: zuletzt trainiert 05.11.2026, Stufe 7 — https://www.chessable.com/variation/987/} 1. e4", annotated);
        Assert.Contains("{Hauptlinie} {Chessable: hier 3× falsch (zuletzt 2.Nc3)} 2. Nf3 (2. Bc4 Nf6)", annotated);
        Assert.Contains("{Chessable: hier 1× falsch} 3. Bb5 $1 a6 *", annotated);
        Assert.StartsWith("[Event \"x\"]\n[White \"Spanisch\"]", annotated);

        // Hauptvariante und Schlüssel wie vorher (Trainer + Prep)
        Assert.Equal(ChessableTrainedLineService.MainlineSans(section), ChessableTrainedLineService.MainlineSans(annotated));
        Assert.Equal(ChessableTrainedLineService.LineKeyFromSans(ChessableTrainedLineService.MainlineSans(section)),
            ChessableTrainedLineService.LineKeyFromSans(ChessableTrainedLineService.MainlineSans(annotated)));
        var before = PgnMoveTree.ParseSections(section).Single().Moves.Select(m => m.San);
        var after = PgnMoveTree.ParseSections(annotated).Single().Moves.Select(m => m.San);
        Assert.Equal(before, after);

        // ohne Stand: nur „noch nicht trainiert" + Link
        var unknown = ChessableLineHistory.Annotate(section, null, "987", false, 1);
        Assert.Contains("{Chessable: noch nicht trainiert — https://www.chessable.com/variation/987/} 1. e4", unknown);
        Assert.DoesNotContain("ChessableLevel", unknown);
        Assert.DoesNotContain("ChessableLastTrained", unknown);
    }

    [Fact]
    public async Task MissesByOid_SumsPerMidAndColor_OverRuns_OnlyOwnUser()
    {
        Session("5", Nov5.AddDays(-1), """[{"mid":1,"practiceMode":"w","wrong":["Bc4"]},{"mid":2,"practiceMode":"b","wrong":["e5"]}]""");
        Session("5", Nov5, """[{"mid":1,"practiceMode":"W","wrong":["Nc3","d4"]},{"mid":3,"wrong":[]}]""");
        Session("5", Nov5, """[{"mid":1,"practiceMode":"w","wrong":["h4"]}]""", user: 2);
        Session("6", Nov5, """[{"mid":0,"level":3}]""");
        await _db.SaveChangesAsync();
        var m = await new ChessableLineHistory(_db).MissesByOidAsync(1, ["5", "6"], default);
        Assert.Equal([new ChessableLineHistory.RawMiss(1, "w", 3, "d4"), new ChessableLineHistory.RawMiss(2, "b", 1, "e5")], m["5"]);
        Assert.False(m.ContainsKey("6"));                                      // ohne Fehler nichts
        Assert.Equal(["1", "22"], ChessableLineHistory.OidsIn("[ChessableOid \"1\"]\n[ChessableOid \"22\"]\n[ChessableOid \"x\"]\n[ChessableOid \"1\"]"));
    }

    [Fact]
    public void Ply_FromMidAndColor()
    {
        Assert.Equal(0, ChessableLineHistory.Ply(0, 'w', false));
        Assert.Equal(1, ChessableLineHistory.Ply(0, 'b', false));
        Assert.Equal(13, ChessableLineHistory.Ply(6, 'b', false));
        Assert.Equal(0, ChessableLineHistory.Ply(0, 'b', true));    // Linie beginnt mit Schwarz am Zug
        Assert.Equal(1, ChessableLineHistory.Ply(1, 'w', true));
    }
}
