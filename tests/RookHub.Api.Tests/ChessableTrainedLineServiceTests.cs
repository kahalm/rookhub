using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// „Auf Chessable trainiert → in RookHub gelernt": Kurs-Linie als gelöst markieren (idempotent,
/// ohne Fake-Versuch) + Repertoire-SR nachziehen (neu → gelernt, fällig → +1 Stufe, nicht
/// fällig/pausiert → unangetastet). Dazu der SERVERSEITIGE SPIEGEL des Frontend-Linien-Schlüssels
/// (repertoire-line-key.util.ts) — die Vektoren stammen aus der Original-JS-Implementierung.
/// </summary>
public class ChessableTrainedLineServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ChessableTrainedLineService _svc;

    public ChessableTrainedLineServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new ChessableTrainedLineService(_db, new RepertoireTrainingService(_db));
    }

    public void Dispose() => _db.Dispose();

    // ===== Hash-Spiegel (Vektoren aus repertoire-line-key.util.ts via Node erzeugt) ==========

    [Theory]
    [InlineData("l2eac1aan9n2", new[] { "e4", "e5", "Nf3", "Nc6", "Bb5" })]
    [InlineData("lgaojnnug81", new[] { "d4", "d5", "c4", "e6", "Nc3", "Nf6", "Bg5", "Be7" })]
    [InlineData("l1pzvjpxtqs9", new[] { "e4", "c6", "d4", "d5", "exd5", "cxd5" })]
    [InlineData("l1sv2o9a142o", new[] { "e4", "e6", "d4", "d5", "Nd2", "c5", "exd5", "Qxd5+" })]
    [InlineData("l65y1fu91w7", new[] { "O-O", "O-O-O" })]
    [InlineData("lbve4kkccho", new[] { "e4" })]
    // Chessable-Schreibweisen: Umwandlung OHNE "=" und Rochade mit Nullen müssen denselben
    // Schlüssel ergeben wie die chess.js-Form, aus der das Frontend seine Keys bildet.
    [InlineData("l3oj25ls9tj", new[] { "e4", "d5", "exd5", "c6", "dxc6", "b5", "c7", "a5", "c8=Q" })]
    [InlineData("l3oj25ls9tj", new[] { "e4", "d5", "exd5", "c6", "dxc6", "b5", "c7", "a5", "c8Q" })]
    [InlineData("l27uzjsul7qp", new[] { "a2", "b1=Q+" })]
    [InlineData("l27uzjsul7qp", new[] { "a2", "b1Q+" })]
    [InlineData("lf2l4fs48fe", new[] { "O-O" })]
    [InlineData("lf2l4fs48fe", new[] { "0-0" })]
    public void LineKey_MirrorsFrontendHash(string expected, string[] sans)
    {
        Assert.Equal(expected, ChessableTrainedLineService.LineKeyFromSans(sans));
    }

    [Theory]
    [InlineData("c8Q", "c8=Q")]
    [InlineData("c8q", "c8=Q")]
    [InlineData("c8=q", "c8=Q")]
    [InlineData("bxc8N+", "bxc8=N")]
    [InlineData("0-0", "O-O")]
    [InlineData("0-0-0", "O-O-O")]
    [InlineData("Nf3!", "Nf3")]
    [InlineData("Qxd5+", "Qxd5")]
    [InlineData("e4", "e4")]
    public void CanonicalSan_MatchesFrontendNormSan(string raw, string expected)
    {
        Assert.Equal(expected, ChessableTrainedLineService.CanonicalSan(raw));
    }

    // ===== PGN-Extraktion =====================================================================

    private const string RepPgn = """
[Event "Kurs"]
[White "Linie A"]
[ChessableOid "111"]

1. e4 e5 2. Nf3 (2. Nc3 Nf6) 2... Nc6 {Kommentar} 3. Bb5! *

[Event "Kurs"]
[White "Linie B"]
[ChessableOid "222"]

1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 1-0
""";

    [Fact]
    public void MainlineSansForOid_FindsGame_SkipsVariationsCommentsAnnotations()
    {
        var sans = ChessableTrainedLineService.MainlineSansForOid(RepPgn, "111");
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5" }, sans);
        // Der Schlüssel der Mainline entspricht dem Frontend-Vektor derselben Zugfolge.
        Assert.Equal("l2eac1aan9n2", ChessableTrainedLineService.LineKeyFromSans(sans!));

        Assert.Equal(8, ChessableTrainedLineService.MainlineSansForOid(RepPgn, "222")!.Count);
        Assert.Null(ChessableTrainedLineService.MainlineSansForOid(RepPgn, "999"));
    }

    // ===== Kurs + SR ==========================================================================

    private async Task<AppUser> CreateUserAsync(string name = "u1")
    {
        var user = new AppUser { Username = name, PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<(Book Book, BookPuzzle Puzzle)> SeedBookAsync(int userId, string bid, string oid)
    {
        var file = $"chessable-u{userId}-{bid}.pgn";
        var book = new Book { FileName = file, DisplayName = "Kurs", OwnerUserId = userId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var bp = new BookPuzzle
        {
            LineId = $"{file}:1", BookFileName = file, BookId = book.Id, Round = "1",
            Fen = "8/8/8/4k3/8/8/4K3/8 w - - 0 1", Moves = "e2e3", StartPly = -1,
            ChessableOid = oid,
        };
        _db.BookPuzzles.Add(bp);
        await _db.SaveChangesAsync();
        return (book, bp);
    }

    private async Task<Repertoire> SeedRepertoireAsync(int userId, string bid, string pgn)
    {
        var rep = new Repertoire
        {
            UserId = userId, Name = "Rep", Kind = RepertoireKind.Opening,
            ChessableCourseId = bid, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        _db.RepertoireFiles.Add(new RepertoireFile
        {
            RepertoireId = rep.Id, FileName = $"chessable-{bid}.pgn",
            PgnContent = pgn, FileSize = pgn.Length,
        });
        await _db.SaveChangesAsync();
        return rep;
    }

    [Fact]
    public async Task MarkTrained_MarksCourseLineOnce_WithoutAttemptRow()
    {
        var user = await CreateUserAsync();
        var (book, bp) = await SeedBookAsync(user.Id, "500", "111");

        var first = await _svc.MarkTrainedAsync(user.Id, "500", "111");
        Assert.True(first.CourseLineFound);
        Assert.True(first.CourseLineMarked);

        var second = await _svc.MarkTrainedAsync(user.Id, "500", "111");
        Assert.True(second.CourseLineFound);
        Assert.False(second.CourseLineMarked);   // idempotent

        var result = Assert.Single(_db.CoursePuzzleResults.Where(r => r.UserId == user.Id));
        Assert.Equal(bp.Id, result.BookPuzzleId);
        Assert.Equal(book.Id, result.BookId);
        Assert.Empty(_db.CourseAttempts);        // kein 0-Sekunden-Fake-Versuch in der Statistik
    }

    [Fact]
    public async Task MarkTrained_LearnsNewRepertoireLine()
    {
        var user = await CreateUserAsync();
        var rep = await SeedRepertoireAsync(user.Id, "500", RepPgn);

        var res = await _svc.MarkTrainedAsync(user.Id, "500", "111");

        Assert.Equal(1, res.RepertoireLinesAdvanced);
        var card = Assert.Single(_db.RepertoireCardStates.Where(c => c.RepertoireId == rep.Id));
        Assert.Equal("l2eac1aan9n2", card.CardKey);
        Assert.Equal(1, card.Level);             // „einmal gelernt"
        Assert.True(card.InPool);
        Assert.True(card.DueAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task MarkTrained_AdvancesDueLine_ButLeavesNotDueAndPausedAlone()
    {
        var user = await CreateUserAsync();
        var rep = await SeedRepertoireAsync(user.Id, "500", RepPgn);
        var keyA = "l2eac1aan9n2";               // Linie A (oid 111)
        var keyB = ChessableTrainedLineService.LineKeyFromSans(
            ChessableTrainedLineService.MainlineSansForOid(RepPgn, "222")!);

        _db.RepertoireCardStates.AddRange(
            new RepertoireCardState { UserId = user.Id, RepertoireId = rep.Id, CardKey = keyA,
                Level = 3, InPool = true, DueAt = DateTime.UtcNow.AddHours(-1) },      // fällig
            new RepertoireCardState { UserId = user.Id, RepertoireId = rep.Id, CardKey = keyB,
                Level = 3, InPool = true, DueAt = DateTime.UtcNow.AddDays(3) });       // nicht fällig
        await _db.SaveChangesAsync();

        var resA = await _svc.MarkTrainedAsync(user.Id, "500", "111");
        Assert.Equal(1, resA.RepertoireLinesAdvanced);
        Assert.Equal(4, _db.RepertoireCardStates.Single(c => c.CardKey == keyA).Level);  // +1 Stufe

        var resB = await _svc.MarkTrainedAsync(user.Id, "500", "222");
        Assert.Equal(0, resB.RepertoireLinesAdvanced);
        Assert.Equal(1, resB.RepertoireLinesSkipped);
        Assert.Equal(3, _db.RepertoireCardStates.Single(c => c.CardKey == keyB).Level);  // unangetastet

        // Pausierte Linie bleibt pausiert und rückt nicht vor.
        var cardA = _db.RepertoireCardStates.Single(c => c.CardKey == keyA);
        cardA.Paused = true; cardA.DueAt = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();
        var resPaused = await _svc.MarkTrainedAsync(user.Id, "500", "111");
        Assert.Equal(0, resPaused.RepertoireLinesAdvanced);
        Assert.Equal(4, _db.RepertoireCardStates.Single(c => c.CardKey == keyA).Level);
    }

    [Fact]
    public async Task MarkTrained_UnknownBidOrOid_IsHarmless()
    {
        var user = await CreateUserAsync();
        var res = await _svc.MarkTrainedAsync(user.Id, "12345", "999");
        Assert.False(res.CourseLineFound);
        Assert.Equal(0, res.RepertoireLinesAdvanced);
    }
}
