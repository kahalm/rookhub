using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Tests für den getReview→PGN-Konverter. Der Kerntest erzeugt aus der echten getReview-Fixture das PGN
/// und lässt es durch den ECHTEN <see cref="PgnImportService.ImportFileAsync"/> laufen — geprüft wird am
/// resultierenden <see cref="BookPuzzle"/>, dass Hauptlinie, Alternativen, Kommentare, Board-Annotationen
/// und Trainingsstart identisch zum getGame-Weg entstehen.
/// </summary>
public class ChessableReviewParserTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessableReviewParserTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private static string LoadFixture()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "getReview-sample.json"));

    [Fact]
    public void TryConvert_Fixture_ExtractsIdsAndChapter()
    {
        var line = ChessableReviewParser.TryConvert(LoadFixture());

        Assert.NotNull(line);
        Assert.Equal("36730415", line!.Oid);
        Assert.Equal("228856", line.Bid);
        Assert.Contains("Fantasy Variation", line.ChapterTitle);
    }

    [Fact]
    public async Task TryConvert_Fixture_ThroughRealImport_ProducesFaithfulBookPuzzle()
    {
        var line = ChessableReviewParser.TryConvert(LoadFixture());
        Assert.NotNull(line);

        var import = new PgnImportService(_db);
        await import.ImportFileAsync("gustafsson_review.pgn", line!.Pgn, CancellationToken.None);

        var bp = await _db.BookPuzzles.SingleAsync();

        // --- echte Puzzle-Linie, kein Info-Zweig ---
        Assert.False(bp.IsInfoOnly);
        Assert.Equal("36730415", bp.ChessableOid);

        // --- 13-Zug-Hauptlinie (13 Weiß + 12 Schwarz = 25 Halbzüge), plausible UCI ---
        var uci = bp.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(25, uci.Length);
        Assert.All(uci, u => Assert.InRange(u.Length, 4, 5));
        Assert.Equal("e2e4", uci[0]);
        Assert.Equal("c7c6", uci[1]);
        Assert.Equal("d2d4", uci[2]);
        Assert.Equal("g1f3", uci[14]);   // erster Schlüsselzug (Nf3)

        // --- Trainingsstart aus isKey: erster Schlüsselzug ist mid7 (Nf3) = Halbzug 14 → StartPly 13 ---
        Assert.Equal(13, bp.StartPly);

        // --- AltMoves an den 8 Halbzügen mit alt_white (mid 0,1,2,4,7,8,10,12) ---
        var alts = JsonSerializer.Deserialize<Dictionary<int, List<string>>>(bp.AltMoves!)!;
        Assert.Equal(new[] { 0, 2, 4, 8, 14, 16, 20, 24 }, alts.Keys.OrderBy(k => k).ToArray());
        // e4-Alternativen aus der Grundstellung: d4/Nf3/c4.
        Assert.Contains("d2d4", alts[0]);
        Assert.Contains("g1f3", alts[0]);
        Assert.Contains("c2c4", alts[0]);
        // f3-Alternativen (Halbzug 4): e5/Nc3/exd5/Nd2.
        Assert.Contains("e4e5", alts[4]);
        Assert.Contains("e4d5", alts[4]);

        // --- MoveComments an den Zügen mit Kommentar (Schlüssel = 0-basierter Halbzug NACH dem Zug) ---
        var comments = JsonSerializer.Deserialize<Dictionary<int, string>>(bp.MoveComments!)!;
        Assert.Contains("fianchetto", comments[5]);                 // g6
        Assert.Contains("natural development", comments[6]);        // Nc3
        Assert.Contains("Conclusion", comments[24]);               // Kb1 (Schluss)
        // @@SAN@@- und HTML-Marker sind entfernt, keine geschweiften Klammern durchgesickert.
        Assert.DoesNotContain("@@", comments[5]);
        Assert.DoesNotContain("<strong>", comments[24]);
        Assert.DoesNotContain("{", comments[24]);

        // --- MoveShapes an Zug 3 (f3 = Halbzug 4): Pfeil f3→e4 grün + Kreis e4 ---
        var shapes = JsonSerializer.Deserialize<Dictionary<int, List<PgnParser.MoveShape>>>(bp.MoveShapes!)!;
        Assert.True(shapes.ContainsKey(4));
        Assert.Contains(shapes[4], s => s.O == "f3" && s.D == "e4" && s.B == "green");   // Pfeil
        Assert.Contains(shapes[4], s => s.O == "e4" && s.D == null && s.B == "green");    // Kreis
    }

    [Fact]
    public void TryConvert_EmptyOrBrokenJson_ReturnsNullNoThrow()
    {
        Assert.Null(ChessableReviewParser.TryConvert(null));
        Assert.Null(ChessableReviewParser.TryConvert(""));
        Assert.Null(ChessableReviewParser.TryConvert("   "));
        Assert.Null(ChessableReviewParser.TryConvert("{ this is not valid json"));
        Assert.Null(ChessableReviewParser.TryConvert("[]"));                       // kein Objekt
        Assert.Null(ChessableReviewParser.TryConvert("{\"lesson\":{}}"));          // keine moves
        Assert.Null(ChessableReviewParser.TryConvert("{\"lesson\":{\"moves\":[]}}")); // leere moves
    }

    [Fact]
    public async Task TryConvert_BlackEmptyAtLineEnd_LastRowHasNoTrailingBlackMove()
    {
        // Linie endet auf einem Weiß-Zug: move_black == "" beim letzten Vollzug (wie im echten getReview).
        const string json = """
        {"lesson":{"studyCol":"white","nColor":1,"chapter":{"title":"Test","bid":100},
          "moves":[
            {"isKey":false,"informational":false,"move_white":"e4","move_black":"e5",
             "move_fen":"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
             "comment_white":"","comment_black":"","comment_before_white":"","comment_before_black":"",
             "drawing_white":[],"drawing_black":[],"alt_white":null,"alt_black":null,
             "bid":100,"oid":555,"chapterTitle":"Test"},
            {"isKey":true,"informational":false,"move_white":"Nf3","move_black":"",
             "move_fen":"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
             "comment_white":"","comment_black":"","comment_before_white":"","comment_before_black":"",
             "drawing_white":[],"drawing_black":[],"alt_white":null,"alt_black":null,
             "bid":100,"oid":555,"chapterTitle":"Test"}
          ]}}
        """;

        var line = ChessableReviewParser.TryConvert(json);
        Assert.NotNull(line);
        // Kein baumelnder Zug/keine leere SAN im Movetext (nach dem Header-Block); endet auf Nf3.
        var moveText = line!.Pgn[(line.Pgn.IndexOf("\n\n", StringComparison.Ordinal) + 2)..].Replace("\n", " ").TrimEnd();
        Assert.DoesNotContain("  ", moveText);
        Assert.EndsWith("Nf3", moveText);

        var import = new PgnImportService(_db);
        await import.ImportFileAsync("mini.pgn", line.Pgn, CancellationToken.None);
        var bp = await _db.BookPuzzles.SingleAsync();

        var uci = bp.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { "e2e4", "e7e5", "g1f3" }, uci);   // 3 Halbzüge, kein 4.
        Assert.Equal(1, bp.StartPly);                          // erster Schlüsselzug = Nf3 (Halbzug 2)
    }

    [Fact]
    public async Task TryConvert_InformationalLine_ImportsAsInfoOnly()
    {
        const string json = """
        {"lesson":{"studyCol":"white","chapter":{"title":"Intro","bid":100},
          "moves":[
            {"isKey":false,"informational":true,"move_white":"e4","move_black":"e5",
             "move_fen":"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
             "comment_white":"{\"data\":[{\"key\":\"C\",\"val\":\"Just look at this position.\"}]}",
             "comment_black":"","comment_before_white":"","comment_before_black":"",
             "drawing_white":[],"drawing_black":[],"alt_white":null,"alt_black":null,
             "bid":100,"oid":777,"chapterTitle":"Intro"}
          ]}}
        """;

        var line = ChessableReviewParser.TryConvert(json);
        Assert.NotNull(line);
        Assert.Contains("[%info]", line!.Pgn);

        var import = new PgnImportService(_db);
        await import.ImportFileAsync("intro.pgn", line.Pgn, CancellationToken.None);
        var bp = await _db.BookPuzzles.SingleAsync();
        Assert.True(bp.IsInfoOnly);
    }
}
