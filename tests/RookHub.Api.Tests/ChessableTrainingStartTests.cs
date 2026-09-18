using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Trainingsstart Chessable-stämmiger Linien beim Umwandeln Repertoire → Kurs. Ein Repertoire-PGN
/// trägt keinen <c>[%tqu]</c>-Marker; gehört der erste Zug dem GEGNER („widerlege 10…Sd4"), stand im
/// Kurs ohne Nachtrag die falsche Seite am Zug (gemeldet 2026-09-18).
/// </summary>
public class ChessableTrainingStartTests
{
    // Stellung nach 10.Qh5 (Schwarz am Zug); im Kurs soll 10…Nd4 vorgespielt und 11.Bg5 gefragt werden.
    private const string Fen = "r1bqk2r/1ppp1ppp/p1n3n1/3Np2Q/2B1P3/3P4/PPP2PP1/R1B1K2R b KQkq - 0 10";

    private static string RepertoireBlock(string oid = "73000253") => $"""
        [Event "Chess Olympiad 2026"]
        [Round "003.003"]
        [White "Liang, Awonder (USA) vs. Muazaz, Dhahir Habeeb (Iraq)"]
        [Black "Round 1"]
        [FEN "{Fen}"]
        [Result "*"]
        [ChessableOid "{oid}"]

        10... Nd4 11. Bg5 f6 *
        """;

    /// <summary>Dieselbe Linie, wie der Linien-Cache sie im Modus „FirstKeyMove" liefert: Marker hinter
    /// dem vorgespielten Zug, also vor dem ersten Zug des Lösers.</summary>
    private static string CachedBlockWithMarker() => $$"""
        [Event "Chessable"]
        [FEN "{{Fen}}"]
        [ChessableOid "73000253"]

        10... Nd4 {[%tqu "En","find the move","","","c1g5","",10]} 11. Bg5 f6 *
        """;

    [Fact]
    public void OidsWithoutStart_OnlyLinesMissingBothMarkerAndColor()
    {
        var pgn = RepertoireBlock("1")                                    // fehlt beides → gesucht
            + "\n\n" + RepertoireBlock("2").Replace("[Result \"*\"]", "[Result \"*\"]\n[ChessableColor \"white\"]")
            + "\n\n" + CachedBlockWithMarker()                            // hat Marker
            + "\n\n" + RepertoireBlock("3").Replace("[ChessableOid \"3\"]\n", "");  // ohne oid

        Assert.Equal(new[] { "1" }, ChessableTrainingStart.OidsWithoutStart(pgn));
    }

    [Fact]
    public void OidsWithoutStart_PlainUserPgn_IsEmpty()
    {
        // Ohne oids gibt es nichts nachzufragen — ein gewöhnliches Nutzer-PGN löst keinen Abruf aus.
        var pgn = "[Event \"Meine Partie\"]\n[Round \"1\"]\n\n1. e4 e5 2. Nf3 *";
        Assert.Empty(ChessableTrainingStart.OidsWithoutStart(pgn));
    }

    [Fact]
    public void SolverColorOf_MarkerAfterOpponentMove_IsWhite()
    {
        // FEN = Schwarz am Zug, Marker hinter 10…Nd4 (Index 0) ⇒ der Löser zieht bei Index 1 = Weiß.
        Assert.Equal("white", ChessableTrainingStart.SolverColorOf(CachedBlockWithMarker()));
    }

    [Fact]
    public void SolverColorOf_MarkerAtRoot_IsSideToMove()
    {
        var block = $"[Event \"Chessable\"]\n[FEN \"{Fen}\"]\n\n{{[%tqu \"En\",\"f\",\"\",\"\",\"c6d4\",\"\",10]}} 10... Nd4 11. Bg5 *";
        Assert.Equal("black", ChessableTrainingStart.SolverColorOf(block));
    }

    [Fact]
    public void SolverColorOf_WithoutMarker_IsNull()
        => Assert.Null(ChessableTrainingStart.SolverColorOf(RepertoireBlock()));

    [Fact]
    public void InsertColors_AddsHeaderAfterLastHeader_AndLeavesMovetextUntouched()
    {
        var pgn = RepertoireBlock();
        var result = ChessableTrainingStart.InsertColors(pgn, new Dictionary<string, string> { ["73000253"] = "white" });

        Assert.Contains("[ChessableColor \"white\"]", result);
        Assert.Contains("10... Nd4 11. Bg5 f6 *", result);
        // Der Header steht im Kopf, nicht im Zugtext.
        Assert.True(result.IndexOf("[ChessableColor", StringComparison.Ordinal)
                    < result.IndexOf("10... Nd4", StringComparison.Ordinal));
    }

    [Fact]
    public void InsertColors_UnknownOidOrColor_LeavesPgnUnchanged()
    {
        var pgn = RepertoireBlock();
        Assert.Equal(pgn, ChessableTrainingStart.InsertColors(pgn, new Dictionary<string, string> { ["999"] = "white" }));
        Assert.Equal(pgn, ChessableTrainingStart.InsertColors(pgn, new Dictionary<string, string> { ["73000253"] = "grau" }));
    }

    // ---- Verdrahtung: Umwandeln holt die Farbe aus dem Linien-Cache -------------------------------

    /// <summary>Antwortet auf den Parse-Endpoint mit der gecachten Linie samt Marker und merkt sich den Body.</summary>
    private sealed class ParseHandler : HttpMessageHandler
    {
        public string? Body;
        private readonly string _pgn;
        public ParseHandler(string pgn) => _pgn = pgn;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var json = System.Text.Json.JsonSerializer.Serialize(new { pgn = _pgn });
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CourseService CourseServiceWith(AppDbContext db, HttpMessageHandler handler)
    {
        var notifications = new NotificationService(db);
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") });
        return new CourseService(db, NullLogger<CourseService>.Instance, new PgnImportService(db),
            new BookAdminService(db), TestServices.Repertoire(db),
            new FriendService(db, notifications), notifications, proxy);
    }

    [Fact]
    public async Task UploadPersonalCourse_ChessableLineWithoutMarker_TakesStartFromCache()
    {
        using var db = NewDb();
        var handler = new ParseHandler(CachedBlockWithMarker());
        var course = await CourseServiceWith(db, handler)
            .UploadPersonalCourseAsync(1, "kurs.pgn", RepertoireBlock(), "Kurs");

        var puzzle = await db.BookPuzzles.SingleAsync(bp => bp.BookId == course.BookId);
        // 10…Nd4 wird vorgespielt, gelöst wird ab 11.Bg5.
        Assert.Equal(0, puzzle.StartPly);
        Assert.StartsWith("c6d4 c1g5", puzzle.Moves);
        Assert.Equal("73000253", puzzle.ChessableOid);
        // Gefragt wurde im Marker-Modus — nur der liefert den Trainingsstart.
        Assert.Contains("FirstKeyMove", handler.Body ?? "");
    }

    [Fact]
    public async Task UploadPersonalCourse_ProxyDown_KeepsPreviousBehaviour()
    {
        using var db = NewDb();
        var course = await CourseServiceWith(db, new ThrowingHandler())
            .UploadPersonalCourseAsync(1, "kurs.pgn", RepertoireBlock(), "Kurs");

        var puzzle = await db.BookPuzzles.SingleAsync(bp => bp.BookId == course.BookId);
        Assert.Equal(-1, puzzle.StartPly);      // wie bisher: ab der FEN lösen
        Assert.Equal("73000253", puzzle.ChessableOid);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("Connection refused");
    }
}
