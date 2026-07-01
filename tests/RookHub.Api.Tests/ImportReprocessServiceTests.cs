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

    // Wie SamplePgn, aber mit einem [%alt]-Marker → gilt als „modern" gefetchte Quelle
    // (SourceHasModernMarkers), also lokal vollständig aufbereitbar statt Re-Fetch.
    private const string ModernPgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

2. Nf3 {[%alt g1e2] Develops.} Nc6 3. Bb5 {The pin.} a6 *
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
        await SeedBookAsync("manual-loc.pgn", 0, SamplePgn, null);                    // veraltet, lokal aufbereitbar (nicht-Chessable)
        await SeedBookAsync("chessable-u7-loc.pgn", 0, SamplePgn, "chessable");       // Chessable MIT Quelle → trotzdem Re-Fetch (Alt-PGN ohne Marker)
        await SeedBookAsync("chessable-u7-ref.pgn", 0, null, "chessable");            // Chessable ohne Quelle → Re-Fetch
        await SeedBookAsync("manual.pgn", 0, null, null);                            // veraltet, nur Re-Import
        await SeedBookAsync("current.pgn", ImportPipeline.CurrentVersion, "x", null); // aktuell

        var svc = ReprocessTestHelper.Build(_db);
        var status = await svc.GetCourseStatusAsync(UserId, isAdmin: false);

        Assert.Equal(ImportPipeline.CurrentVersion, status.CurrentVersion);
        Assert.Equal(5, status.Total);
        Assert.Equal(4, status.Stale);
        Assert.Equal(1, status.ReprocessableLocally); // nur das nicht-Chessable-Buch mit Quelle
        Assert.Equal(2, status.Refetchable);          // beide Chessable-Kurse, auch der mit gecachter Quelle
        Assert.Equal(1, status.NeedsReimport);
    }

    [Fact]
    public async Task GetCourseStatus_ChessableWithModernSource_CountsAsLocalNotRefetch()
    {
        // Chessable-Buch, dessen gespeicherte Quelle bereits [%alt]/[%info] enthält → lokal aufbereitbar.
        await SeedBookAsync("chessable-u7-modern.pgn", 0, ModernPgn, "chessable");

        var status = await ReprocessTestHelper.Build(_db).GetCourseStatusAsync(UserId, isAdmin: false);

        Assert.Equal(1, status.Stale);
        Assert.Equal(1, status.ReprocessableLocally);
        Assert.Equal(0, status.Refetchable);          // kein Re-Fetch, obwohl Chessable
    }

    [Fact]
    public async Task ReprocessCourses_ChessableWithModernSource_ReprocessesLocally_NoRefetch()
    {
        var book = await SeedBookAsync("chessable-u7-modern.pgn", 0, ModernPgn, "chessable");
        var stub = new StubCourseReimporter();

        var result = await ReprocessTestHelper.Build(_db, stub).ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Empty(stub.Calls);                     // kein Chessable-Re-Fetch
        Assert.Equal(1, result.Reprocessed);          // lokal aus dem gespeicherten PGN
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Books.SingleAsync(b => b.Id == book.Id)).ImportVersion);
    }

    [Fact]
    public async Task ReprocessCourses_LocalSource_UpdatesPuzzlesInPlace_AndBumpsVersion()
    {
        var book = await SeedBookAsync("manual-loc.pgn", 0, SamplePgn, null);
        var puzzle = new BookPuzzle
        {
            LineId = "manual-loc.pgn:1", BookFileName = book.FileName, BookId = book.Id, Round = "1",
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
    public async Task ReprocessCourses_ManyChessable_FetchesCacheSetOnce_AndPassesKnownCachedPerBid()
    {
        // Drei Chessable-Kurse; zwei davon liegen im piratechess-Cache. Der Massen-Reprocess soll die
        // Cache-Menge EINMAL en bloc holen (nicht je Kurs) und pro Kurs das Ergebnis durchreichen.
        await SeedBookAsync("chessable-u7-111.pgn", 0, null, "chessable");
        await SeedBookAsync("chessable-u7-222.pgn", 0, null, "chessable");
        await SeedBookAsync("chessable-u7-333.pgn", 0, null, "chessable");
        var stub = new StubCourseReimporter { ReturnId = 1, CachedBids = new HashSet<string> { "111", "333" } };
        var svc = ReprocessTestHelper.Build(_db, stub);

        var result = await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(3, result.Enqueued);
        Assert.Equal(1, stub.GetCachedBidsCalls); // genau EIN Batch-Abruf für alle Kurse
        Assert.Equal(true, stub.Calls.Single(c => c.Bid == "111").KnownCached);
        Assert.Equal(false, stub.Calls.Single(c => c.Bid == "222").KnownCached);
        Assert.Equal(true, stub.Calls.Single(c => c.Bid == "333").KnownCached);
    }

    [Fact]
    public async Task ReprocessCourses_Admin_PassesTrustOwnership()
    {
        // Admin-Reprocess soll die Eigentumsprüfung überspringen (getHomeData listet nur einen Teil).
        await SeedBookAsync("chessable-u7-777.pgn", 0, null, "chessable");
        var stub = new StubCourseReimporter { ReturnId = 1 };
        var svc = ReprocessTestHelper.Build(_db, stub);

        await svc.ReprocessCoursesAsync(UserId, isAdmin: true);

        Assert.True(stub.Calls.Single().TrustOwnership);
    }

    [Fact]
    public async Task ReprocessCourses_NonAdmin_DoesNotTrustOwnership()
    {
        // Nicht-Admin: Eigentumsprüfung bleibt aktiv (Schutz vor Cached-Content-Diebstahl).
        await SeedBookAsync("chessable-u7-777.pgn", 0, null, "chessable");
        var stub = new StubCourseReimporter { ReturnId = 1 };
        var svc = ReprocessTestHelper.Build(_db, stub);

        await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.False(stub.Calls.Single().TrustOwnership);
    }

    [Fact]
    public async Task ReprocessCourses_NonCachedRecentlyFetched_SkippedByBackoff()
    {
        // Kurs ist nicht im Cache (truncated → piratechess cachet ihn nicht) UND wurde gerade eben
        // schon geholt → im Backoff-Fenster überspringen, damit er Chessable nicht erneut flutet.
        await SeedBookAsync("chessable-u7-999.pgn", 0, null, "chessable");
        _db.ChessableImports.Add(new ChessableImport
        {
            UserId = UserId, Bid = "999", CourseName = "X", Target = "book",
            Status = "completed", CompletedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        var stub = new StubCourseReimporter { ReturnId = 1 }; // CachedBids leer → 999 nicht gecacht
        var svc = ReprocessTestHelper.Build(_db, stub);

        var result = await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Empty(stub.Calls);           // NICHT erneut eingereiht
        Assert.Equal(0, result.Enqueued);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task ReprocessCourses_NonCachedFetchedLongAgo_RetriedAfterBackoff()
    {
        // Letzter (erfolgloser) Fetch liegt außerhalb des Backoff-Fensters → erneut versuchen.
        await SeedBookAsync("chessable-u7-888.pgn", 0, null, "chessable");
        _db.ChessableImports.Add(new ChessableImport
        {
            UserId = UserId, Bid = "888", CourseName = "X", Target = "book",
            Status = "completed", CompletedAt = DateTime.UtcNow.AddHours(-48), CreatedAt = DateTime.UtcNow.AddHours(-48),
        });
        await _db.SaveChangesAsync();
        var stub = new StubCourseReimporter { ReturnId = 1 };
        var svc = ReprocessTestHelper.Build(_db, stub);

        var result = await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(1, result.Enqueued);   // Backoff abgelaufen → wieder versucht
        Assert.Single(stub.Calls);
    }

    [Fact]
    public async Task ReprocessCourses_LocalOnly_SkipsBatchCacheFetch()
    {
        // „Aus Cache"-Knopf (localOnly) reiht keine Chessable-Re-Fetches ein → kein Batch-Cache-Abruf nötig.
        await SeedBookAsync("chessable-u7-111.pgn", 0, null, "chessable");
        var stub = new StubCourseReimporter { ReturnId = 1 };
        var svc = ReprocessTestHelper.Build(_db, stub);

        await svc.ReprocessCoursesAsync(UserId, isAdmin: false, localOnly: true);

        Assert.Equal(0, stub.GetCachedBidsCalls);
        Assert.Empty(stub.Calls);
    }

    [Fact]
    public async Task ReprocessCourses_ChessableWithSource_RefetchesInsteadOfLocalReprocess()
    {
        // Bestehender Chessable-Kurs MIT gecachtem Alt-PGN, dem markerbasierte Daten ([%info]) fehlen.
        // Lokales Reprocess würde die Version hochmarkieren, ohne IsInfoOnly zu setzen → muss Re-Fetch sein.
        var book = await SeedBookAsync("chessable-u7-abc123.pgn", 0, SamplePgn, "chessable");
        var stub = new StubCourseReimporter { ReturnId = 42 };
        var svc = ReprocessTestHelper.Build(_db, stub);

        var result = await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(1, result.Enqueued);      // Re-Fetch eingereiht …
        Assert.Equal(0, result.Reprocessed);   // … KEIN lokales Reprocess aus dem Alt-PGN
        var call = Assert.Single(stub.Calls);
        Assert.Equal("abc123", call.Bid);
        Assert.Equal("book", call.Target);
        // Version bleibt veraltet, bis der Re-Fetch-Job das frische PGN eingespielt hat.
        Assert.Equal(0, (await _db.Books.SingleAsync(b => b.Id == book.Id)).ImportVersion);
    }

    [Fact]
    public async Task ReprocessCourses_LocalOnly_ReprocessesCachedSource_SkipsChessableRefetch()
    {
        // Lokal aufbereitbares (nicht-Chessable) Buch mit Quelle + Chessable-Altbestand (bräuchte Re-Fetch).
        var local = await SeedBookAsync("manual-loc.pgn", 0, SamplePgn, null);
        _db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = "manual-loc.pgn:1", BookFileName = local.FileName, BookId = local.Id, Round = "1",
            Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
            Moves = "g1f3 b8c6 f1b5 a7a6", StartPly = -1,
        });
        await SeedBookAsync("chessable-u7-abc123.pgn", 0, null, "chessable");
        await _db.SaveChangesAsync();

        var stub = new StubCourseReimporter { ReturnId = 42 };
        var result = await ReprocessTestHelper.Build(_db, stub).ReprocessCoursesAsync(UserId, isAdmin: false, localOnly: true);

        Assert.Equal(1, result.Reprocessed);            // lokales Buch aufbereitet
        Assert.Equal(0, result.Enqueued);               // KEIN Chessable-Re-Fetch
        Assert.Empty(stub.Calls);
        // Das Chessable-Altbuch bleibt veraltet (per „Alle" später nachholbar).
        Assert.Equal(0, (await _db.Books.SingleAsync(b => b.FileName == "chessable-u7-abc123.pgn")).ImportVersion);
    }

    [Fact]
    public async Task ReprocessRepertoires_LocalOnly_MarksNonChessable_SkipsChessableRefetch()
    {
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        await SeedRepertoireAsync(user.Id, 0, "my-own.pgn");            // lokal: Versions-Mark
        var ch = await SeedRepertoireAsync(user.Id, 0, "chessable-128648.pgn"); // Chessable: Re-Fetch
        var stub = new StubCourseReimporter { ReturnId = 555 };

        var result = await ReprocessTestHelper.Build(_db, stub).ReprocessRepertoiresAsync(user.Id, localOnly: true);

        Assert.Equal(1, result.Reprocessed);            // nur das manuelle Repertoire
        Assert.Equal(0, result.Enqueued);               // KEIN Chessable-Re-Fetch
        Assert.Empty(stub.Calls);
        Assert.Equal(0, (await _db.Repertoires.SingleAsync(r => r.Id == ch.Id)).ImportVersion); // Chessable bleibt veraltet
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

    [Fact]
    public async Task ReprocessRepertoires_Admin_CoversAllUsers()
    {
        var a = new AppUser { Username = "a", PasswordHash = "h" };
        var b = new AppUser { Username = "b", PasswordHash = "h" };
        _db.AppUsers.AddRange(a, b);
        await _db.SaveChangesAsync();
        await SeedRepertoireAsync(a.Id, 0, "a-own.pgn");   // stale, non-chessable
        await SeedRepertoireAsync(b.Id, 0, "b-own.pgn");   // stale, non-chessable (anderer User)

        var svc = ReprocessTestHelper.Build(_db);
        // Nicht-Admin (User a): sieht/aktualisiert nur EIGENES.
        Assert.Equal(1, (await svc.GetRepertoireStatusAsync(a.Id, isAdmin: false)).Stale);
        // Admin: sieht beide User.
        Assert.Equal(2, (await svc.GetRepertoireStatusAsync(a.Id, isAdmin: true)).Stale);

        var result = await svc.ReprocessRepertoiresAsync(a.Id, isAdmin: true);
        Assert.Equal(2, result.Reprocessed);   // beide User-Repertoires hochgezogen
        Assert.True(await _db.Repertoires.AllAsync(r => r.ImportVersion == ImportPipeline.CurrentVersion));
    }

    private async Task<Repertoire> SeedRepertoireAsync(int userId, int version, string? fileName, string? courseId = null, string pgn = "x")
    {
        var rep = new Repertoire { UserId = userId, Name = "Rep", ImportVersion = version, ChessableCourseId = courseId };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        if (fileName != null)
        {
            _db.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = fileName, PgnContent = pgn, FileSize = 1 });
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
    public async Task ReprocessRepertoires_ChessableWithModernSource_MarksLocally_NoRefetch()
    {
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        // Chessable-Repertoire, dessen gespeichertes PGN bereits [%alt] enthält → kein Re-Fetch, nur Versions-Mark.
        var rep = await SeedRepertoireAsync(user.Id, 0, "chessable-128648.pgn", pgn: ModernPgn);
        var stub = new StubCourseReimporter();

        var result = await ReprocessTestHelper.Build(_db, stub).ReprocessRepertoiresAsync(user.Id);

        Assert.Empty(stub.Calls);                     // kein Re-Fetch
        Assert.Equal(1, result.Reprocessed);
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Repertoires.SingleAsync(r => r.Id == rep.Id)).ImportVersion);
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

    [Fact]
    public async Task ReprocessRepertoires_Admin_PassesTrustOwnership_AndBatchesCacheOnce()
    {
        // Repertoire-Reprocess nutzt jetzt denselben zentralen Pfad wie Kurse: Admin-Trust + 1 Batch-Cache-Abruf.
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        await SeedRepertoireAsync(user.Id, 0, fileName: null, courseId: "111");
        await SeedRepertoireAsync(user.Id, 0, fileName: null, courseId: "222");
        var stub = new StubCourseReimporter { ReturnId = 1 };

        await ReprocessTestHelper.Build(_db, stub).ReprocessRepertoiresAsync(user.Id, isAdmin: true);

        Assert.Equal(1, stub.GetCachedBidsCalls);              // genau EIN Batch-Abruf für beide
        Assert.All(stub.Calls, c => Assert.True(c.TrustOwnership));
    }
}
