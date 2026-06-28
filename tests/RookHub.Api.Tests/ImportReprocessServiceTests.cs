using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class ImportReprocessServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private const int UserId = 7;

    public ImportReprocessServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private const string SamplePgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

2. Nf3 {Develops.} Nc6 3. Bb5 {The pin.} a6 *
";

    private async Task<Book> SeedBookAsync(string fileName, int version, string? sourcePgn, string? tags, int? owner = UserId)
    {
        var book = new Book
        {
            FileName = fileName, DisplayName = fileName, OwnerUserId = owner,
            ImportVersion = version, SourcePgn = sourcePgn, Tags = tags,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task GetCourseStatus_CategorisesStaleBooks()
    {
        await SeedBookAsync("chessable-u7-loc.pgn", 0, SamplePgn, "chessable");      // veraltet, lokal aufbereitbar
        await SeedBookAsync("chessable-u7-ref.pgn", 0, null, "chessable");           // veraltet, Re-Fetch
        await SeedBookAsync("manual.pgn", 0, null, null);                            // veraltet, nur Re-Import
        await SeedBookAsync("current.pgn", ImportPipeline.CurrentVersion, "x", null); // aktuell

        var svc = ReprocessTestHelper.Build(_db);
        var status = await svc.GetCourseStatusAsync(UserId, isAdmin: false);

        Assert.Equal(ImportPipeline.CurrentVersion, status.CurrentVersion);
        Assert.Equal(4, status.Total);
        Assert.Equal(3, status.Stale);
        Assert.Equal(1, status.ReprocessableLocally);
        Assert.Equal(1, status.Refetchable);
        Assert.Equal(1, status.NeedsReimport);
    }

    [Fact]
    public async Task ReprocessCourses_LocalSource_UpdatesPuzzlesInPlace_AndBumpsVersion()
    {
        var book = await SeedBookAsync("chessable-u7-loc.pgn", 0, SamplePgn, "chessable");
        var puzzle = new BookPuzzle
        {
            LineId = "chessable-u7-loc.pgn:1", BookFileName = book.FileName, BookId = book.Id, Round = "1",
            Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
            Moves = "g1f3 b8c6 f1b5 a7a6", StartPly = -1, MoveComments = null,
        };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        var id = puzzle.Id;

        var stub = new StubCourseReimporter();
        var svc = ReprocessTestHelper.Build(_db, stub);
        var result = await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(1, result.Reprocessed);
        Assert.Equal(1, result.UpdatedLines);
        Assert.Empty(stub.Calls);                                  // lokal → kein Re-Fetch
        var refreshed = await _db.BookPuzzles.SingleAsync(p => p.Id == id);
        Assert.NotNull(refreshed.MoveComments);                    // Kommentare nachgezogen, Id erhalten
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Books.SingleAsync(b => b.Id == book.Id)).ImportVersion);
    }

    [Fact]
    public async Task ReprocessCourses_ChessableWithoutSource_EnqueuesRefetchWithParsedBid()
    {
        await SeedBookAsync("chessable-u7-abc123.pgn", 0, null, "chessable");
        var stub = new StubCourseReimporter { ReturnId = 42 };
        var svc = ReprocessTestHelper.Build(_db, stub);

        var result = await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(1, result.Enqueued);
        Assert.Equal(0, result.Reprocessed);
        var call = Assert.Single(stub.Calls);
        Assert.Equal("abc123", call.Bid);
        Assert.Equal("book", call.Target);
        Assert.Equal(UserId, call.OwnerUserId);
    }

    [Fact]
    public async Task ReprocessCourses_ChessableNoBearer_CountsSkipped()
    {
        await SeedBookAsync("chessable-u7-x.pgn", 0, null, "chessable");
        var stub = new StubCourseReimporter { ReturnId = null };   // kein Bearer
        var svc = ReprocessTestHelper.Build(_db, stub);

        var result = await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(0, result.Enqueued);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task ReprocessCourses_NonAdmin_OnlyTouchesOwnBooks()
    {
        await SeedBookAsync("chessable-u9-foreign.pgn", 0, SamplePgn, "chessable", owner: 99);
        var svc = ReprocessTestHelper.Build(_db);

        var result = await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(0, result.Reprocessed);   // fremdes Buch nicht angefasst
        Assert.Equal(0, result.Skipped);
        var foreign = await _db.Books.SingleAsync(b => b.FileName == "chessable-u9-foreign.pgn");
        Assert.Equal(0, foreign.ImportVersion);
    }

    [Fact]
    public async Task ReprocessRepertoires_BumpsVersion_NoOpDerivedData()
    {
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        _db.Repertoires.Add(new Repertoire { UserId = user.Id, Name = "Old", ImportVersion = 0 });
        _db.Repertoires.Add(new Repertoire { UserId = user.Id, Name = "Current", ImportVersion = ImportPipeline.CurrentVersion });
        await _db.SaveChangesAsync();

        var svc = ReprocessTestHelper.Build(_db);
        var status = await svc.GetRepertoireStatusAsync(user.Id);
        Assert.Equal(2, status.Total);
        Assert.Equal(1, status.Stale);

        var result = await svc.ReprocessRepertoiresAsync(user.Id);
        Assert.Equal(1, result.Reprocessed);
        Assert.True(await _db.Repertoires.AllAsync(r => r.ImportVersion == ImportPipeline.CurrentVersion));
    }

    private async Task<Repertoire> SeedRepertoireAsync(int userId, int version, string? fileName, string? courseId = null)
    {
        var rep = new Repertoire { UserId = userId, Name = "Rep", ImportVersion = version, ChessableCourseId = courseId };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        if (fileName != null)
        {
            _db.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = fileName, PgnContent = "x", FileSize = 1 });
            await _db.SaveChangesAsync();
        }
        return rep;
    }

    [Fact]
    public async Task GetRepertoireStatus_StaleChessableRepertoire_IsRefetchable()
    {
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        await SeedRepertoireAsync(user.Id, 0, "chessable-128648.pgn"); // Chessable (per Dateiname), ohne CourseId
        await SeedRepertoireAsync(user.Id, 0, "my-own.pgn");           // manuell → nur lokaler Versions-Mark

        var status = await ReprocessTestHelper.Build(_db).GetRepertoireStatusAsync(user.Id);

        Assert.Equal(2, status.Stale);
        Assert.Equal(1, status.Refetchable);          // nur das Chessable-Repertoire
        Assert.Equal(1, status.ReprocessableLocally); // das manuelle
    }

    [Fact]
    public async Task ReprocessRepertoires_Chessable_EnqueuesInPlaceRefetch_KeepsRepertoireId()
    {
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var rep = await SeedRepertoireAsync(user.Id, 0, fileName: null, courseId: "128648"); // CourseId-Weg
        var stub = new StubCourseReimporter { ReturnId = 555 };

        var result = await ReprocessTestHelper.Build(_db, stub).ReprocessRepertoiresAsync(user.Id);

        Assert.Equal(1, result.Enqueued);
        Assert.Equal(0, result.Reprocessed);
        var call = Assert.Single(stub.Calls);
        Assert.Equal("repertoire", call.Target);
        Assert.Equal("128648", call.Bid);
        Assert.Equal(rep.Id, call.TargetRepertoireId);  // in-place ins bestehende Repertoire
        // Version bleibt veraltet, bis der Hintergrund-Job das frische PGN eingespielt hat.
        var reloaded = await _db.Repertoires.SingleAsync(r => r.Id == rep.Id);
        Assert.Equal(0, reloaded.ImportVersion);
    }

    [Fact]
    public async Task ReprocessRepertoires_Chessable_NoBearer_CountsAsSkipped()
    {
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        await SeedRepertoireAsync(user.Id, 0, "chessable-128648.pgn");
        var stub = new StubCourseReimporter { ReturnId = null }; // kein Bearer → Enqueue scheitert

        var result = await ReprocessTestHelper.Build(_db, stub).ReprocessRepertoiresAsync(user.Id);

        Assert.Equal(0, result.Enqueued);
        Assert.Equal(1, result.Skipped);
    }
}
