using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

    // „Modern" gefetchte Quelle: enthält [ChessableOid] (piratechess ≥ v1.0.39) → lokal vollständig
    // aufbereitbar statt Re-Fetch. Maßgeblich ist die oid; [%alt] allein macht eine Quelle NICHT modern.
    private const string ModernPgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]
[ChessableOid ""10""]

2. Nf3 {[%alt g1e2] Develops.} Nc6 3. Bb5 {The pin.} a6 *
";

    // Chessable-Quelle aus der Zeit VOR der oid-Ära: hat [%alt], aber KEINE [ChessableOid] → NICHT modern,
    // muss re-gefetcht werden (Regression: wurde früher fälschlich als „modern → lokal" eingestuft, sodass
    // der Kurs beim „Aktualisieren" nur versions-markiert wurde und nie oids/Fortschritt bekam).
    private const string AltNoOidPgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

2. Nf3 {[%alt g1e2] Develops.} Nc6 3. Bb5 {The pin.} a6 *
";

    private const string ModernFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2";

    /// <summary>Eine Chessable-Linie, wie sie im gespeicherten Kurs steht (piratechess-Format mit oid).</summary>
    private static string ChessableLine(string round, string oid, string moves) =>
        $"\n[Event \"Kapitel 1\"]\n[Round \"{round}\"]\n[White \"Linie {oid}\"]\n[Black \"Kapitel 1\"]\n"
        + $"[FEN \"{ModernFen}\"]\n[Result \"*\"]\n[ChessableOid \"{oid}\"]\n\n{moves}\n\n";

    /// <summary>Dieselbe Linie, wie der Linien-Cache sie liefert: Fake-Kapitel „x", Zählung ab 001.001.</summary>
    private static string CacheLine(string oid, string moves) =>
        $"[Event \"x\"]\n[Round \"001.001\"]\n[White \"x\"]\n[Black \"x\"]\n[FEN \"{ModernFen}\"]\n"
        + $"[Result \"*\"]\n[ChessableOid \"{oid}\"]\n\n{moves}";

    private async Task<Book> SeedBookAsync(string fileName, int version, string? sourcePgn, string? tags, int? owner = UserId)
    {
        var book = new Book
        {
            FileName = fileName, DisplayName = fileName, OwnerUserId = owner,
            ImportVersion = version, Source = new BookSource { SourcePgn = sourcePgn }, Tags = tags,
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
    public async Task GetCourseStatus_ChessableWithModernSource_CountsAsFromCache_NotRefetch()
    {
        // Chessable-Buch, dessen gespeicherte Quelle schon [ChessableOid] trägt → wird aus dem geteilten
        // Linien-Cache neu erzeugt (StaleAction.Cache), nicht mehr nur lokal umgeparst — und auch mit dem
        // eigenen Chessable-Weg (Vorgabe: an) NICHT re-gefetcht.
        await SeedBookAsync("chessable-u7-modern.pgn", 0, ModernPgn, "chessable");

        var status = await ReprocessTestHelper.Build(_db).GetCourseStatusAsync(UserId, isAdmin: false);

        Assert.Equal(1, status.Stale);
        Assert.Equal(1, status.FromCache);
        // Für das Banner ist es dasselbe wie „lokal": ein Klick, kein Download.
        Assert.Equal(1, status.ReprocessableLocally);
        Assert.Equal(0, status.Refetchable);          // kein Re-Fetch, obwohl Chessable
        Assert.Equal(0, status.NeedsReimport);
    }

    [Fact]
    public async Task GetCourseStatus_OnlyChessableCoursesWithOidsCountAsFromCache()
    {
        await SeedBookAsync("chessable-u7-modern.pgn", 0, ModernPgn, "chessable");   // Cache
        await SeedBookAsync("upload-with-oids.pgn", 0, ModernPgn, null);            // oids, aber kein Chessable-Kurs → lokal
        await SeedBookAsync("manual-loc.pgn", 0, SamplePgn, null);                  // lokal

        var status = await ReprocessTestHelper.Build(_db).GetCourseStatusAsync(UserId, isAdmin: false);

        Assert.Equal(1, status.FromCache);
        Assert.Equal(3, status.ReprocessableLocally);  // Local + Cache
        Assert.Equal(3, status.Stale);
    }

    [Fact]
    public async Task GetCourseStatus_ChessableAltButNoOid_CountsAsRefetchNotLocal()
    {
        // Regression: [%alt] vorhanden, aber KEINE [ChessableOid] → NICHT modern → muss re-gefetcht werden
        // (früher fälschlich als lokal aufbereitbar eingestuft, wodurch die oids nie reinkamen).
        await SeedBookAsync("chessable-u7-altnooid.pgn", 0, AltNoOidPgn, "chessable");

        var status = await ReprocessTestHelper.Build(_db).GetCourseStatusAsync(UserId, isAdmin: false);

        Assert.Equal(1, status.Stale);
        Assert.Equal(0, status.ReprocessableLocally);
        Assert.Equal(1, status.Refetchable);
    }

    [Fact]
    public async Task ReprocessCourses_ChessableWithModernSource_RebuildsFromCache_InPlace_NoRefetch()
    {
        var book = await SeedBookAsync("chessable-u7-modern.pgn", 0, ModernPgn, "chessable");
        var puzzle = new BookPuzzle
        {
            LineId = "chessable-u7-modern.pgn:1", BookFileName = book.FileName, BookId = book.Id, Round = "1",
            Fen = ModernFen, Moves = "g1f3 b8c6 f1b5 a7a6", StartPly = -1, MoveComments = null, ChessableOid = "10",
        };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        var puzzleId = puzzle.Id;
        var stub = new StubCourseReimporter();
        var lines = new StubCachedLineSource();
        lines.Lines["10"] = CacheLine("10", "2. Nf3 {[%alt g1e2] Develops from the cache.} Nc6 3. Bb5 {The pin.} a6 *");

        var result = await ReprocessTestHelper.Build(_db, stub, lines).ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Empty(stub.Calls);                     // kein Chessable-Re-Fetch
        Assert.Equal(1, result.Reprocessed);
        Assert.Equal(1, result.RebuiltFromCache);
        Assert.Equal(1, result.CacheLinesReplaced);
        Assert.Equal(1, result.UpdatedLines);
        // Die Quelle trägt den neuen Zugtext — und ihre EIGENEN Header (Event/Round), nicht die der Cache-Antwort.
        var source = (await _db.BookSources.AsNoTracking().SingleAsync(s => s.Id == book.Id)).SourcePgn!;
        Assert.Contains("Develops from the cache.", source);
        Assert.Contains("[Event \"X\"]", source);
        Assert.Contains("[Round \"1\"]", source);
        Assert.DoesNotContain("[Event \"x\"]", source);
        // In-place: dieselbe Puzzle-Id (Fortschritt hängt daran), Zug-Kommentare aus dem neuen Text.
        var refreshed = await _db.BookPuzzles.AsNoTracking().SingleAsync(p => p.BookId == book.Id);
        Assert.Equal(puzzleId, refreshed.Id);
        Assert.Contains("Develops from the cache.", refreshed.MoveComments);
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Books.AsNoTracking().SingleAsync(b => b.Id == book.Id)).ImportVersion);
    }

    [Fact]
    public async Task ReprocessCourses_CacheKnowsNoLine_BookStaysStale_Skipped_SourceUnchanged()
    {
        // Leerer Cache (oder ein Server ohne ihn): würde das Buch trotzdem hochgesetzt, wäre es für den
        // Cache-Weg verbrannt, ohne dass sich etwas geändert hat.
        var book = await SeedBookAsync("chessable-u7-modern.pgn", 0, ModernPgn, "chessable");
        var lines = new StubCachedLineSource();

        var result = await ReprocessTestHelper.Build(_db, cachedLines: lines).ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Reprocessed);
        Assert.Equal(0, result.RebuiltFromCache);
        Assert.Equal(0, result.Failed);
        Assert.Empty(lines.PgnCalls);                  // ohne gecachte Linie auch keine teure PGN-Abfrage
        Assert.Equal(ModernPgn, (await _db.BookSources.AsNoTracking().SingleAsync(s => s.Id == book.Id)).SourcePgn);
        Assert.Equal(0, (await _db.Books.AsNoTracking().SingleAsync(b => b.Id == book.Id)).ImportVersion);
    }

    [Fact]
    public async Task ReprocessCourses_NoLineTakenFromCache_BecauseOfModeMismatch_StaysStale()
    {
        // Die Linie liegt im Cache, passt aber nicht (Marker nur auf einer Seite) → nichts übernommen →
        // wie „nicht gecacht": das Buch bleibt veraltet, statt ohne Änderung als erneuert zu gelten.
        var book = await SeedBookAsync("chessable-u7-modern.pgn", 0, ModernPgn, "chessable");
        var lines = new StubCachedLineSource();
        lines.Lines["10"] = CacheLine("10", "2. Nf3 {[%tqu \"En\",\"find it\"] Develops.} Nc6 3. Bb5 a6 *");

        var result = await ReprocessTestHelper.Build(_db, cachedLines: lines).ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.RebuiltFromCache);
        Assert.Equal(0, (await _db.Books.AsNoTracking().SingleAsync(b => b.Id == book.Id)).ImportVersion);
    }

    [Fact]
    public async Task ReprocessCourses_SomeLinesNotCached_RebuildsTheRest_AndBumpsVersion()
    {
        // Eine einzelne fehlende Linie darf den Kurs nicht für immer im Banner halten; sie behält ihren Text.
        var pgn = ChessableLine("001.001", "21", "2. Nf3 {Old one.} Nc6 3. Bb5 a6 *")
                + ChessableLine("001.002", "22", "2. Nf3 {Old two.} Nc6 3. Bb5 a6 *");
        var book = await SeedBookAsync("chessable-u7-5.pgn", 0, pgn, "chessable");
        var lines = new StubCachedLineSource();
        lines.Lines["21"] = CacheLine("21", "2. Nf3 {New one.} Nc6 3. Bb5 a6 *");

        var result = await ReprocessTestHelper.Build(_db, cachedLines: lines).ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(1, result.RebuiltFromCache);
        Assert.Equal(1, result.CacheLinesReplaced);
        var source = (await _db.BookSources.AsNoTracking().SingleAsync(s => s.Id == book.Id)).SourcePgn!;
        Assert.Contains("New one.", source);
        Assert.Contains("Old two.", source);
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Books.AsNoTracking().SingleAsync(b => b.Id == book.Id)).ImportVersion);
        Assert.Equal(2, await _db.BookPuzzles.CountAsync(p => p.BookId == book.Id));
    }

    [Fact]
    public async Task ReprocessCourses_CacheThrowsInSecondPortion_SourceUnchanged_Failed_StaysStale()
    {
        // Mehr Linien als eine Portion → zwei Abfragen. Wirft die zweite (piratechess weg), wird NICHTS
        // geschrieben: kein halb erneuerter Kurs mit hochgesetzter Version, der nächste Klick versucht es neu.
        var count = ImportReprocessService.CacheRebuildBatchSize + 1;
        var lines = new StubCachedLineSource { ThrowOnPgnCall = 2 };
        var pgn = new System.Text.StringBuilder();
        for (var i = 1; i <= count; i++)
        {
            var oid = (1000 + i).ToString();
            pgn.Append(ChessableLine($"001.{i:000}", oid, "2. Nf3 {Old.} Nc6 3. Bb5 a6 *"));
            lines.Lines[oid] = CacheLine(oid, "2. Nf3 {New.} Nc6 3. Bb5 a6 *");
        }
        var book = await SeedBookAsync("chessable-u7-6.pgn", 0, pgn.ToString(), "chessable");

        var result = await ReprocessTestHelper.Build(_db, cachedLines: lines).ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(2, lines.PgnCalls.Count);
        Assert.Equal(ImportReprocessService.CacheRebuildBatchSize, lines.PgnCalls[0].Oids.Count);
        Assert.Single(lines.PgnCalls[1].Oids);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Reprocessed);
        Assert.Equal(pgn.ToString(), (await _db.BookSources.AsNoTracking().SingleAsync(s => s.Id == book.Id)).SourcePgn);
        Assert.Equal(0, (await _db.Books.AsNoTracking().SingleAsync(b => b.Id == book.Id)).ImportVersion);
    }

    [Fact]
    public async Task ReprocessCourses_BookWrittenWhileAskingTheCache_WritesNothing_StaysStale()
    {
        // Ein großer Kurs braucht Dutzende Cache-Abfragen. Hängt in der Zeit ein Browser-Import Linien an
        // dasselbe Buch, fehlen sie im umgeschriebenen Text — der Lauf überschriebe sie. Also nichts schreiben;
        // beim nächsten „Aktualisieren" kommt das Buch mit dem neuen Stand dran.
        var book = await SeedBookAsync("chessable-u7-modern.pgn", 0, ModernPgn, "chessable");
        var lines = new StubCachedLineSource();
        lines.Lines["10"] = CacheLine("10", "2. Nf3 {[%alt g1e2] Develops from the cache.} Nc6 3. Bb5 {The pin.} a6 *");
        lines.OnPgnCall = () =>
        {
            var b = _db.Books.Single(x => x.Id == book.Id);
            b.UpdatedAt = b.UpdatedAt.AddSeconds(1);   // so schreibt ImportIntoBookAsync jeden Import mit
            _db.SaveChanges();
        };

        var result = await ReprocessTestHelper.Build(_db, cachedLines: lines).ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.RebuiltFromCache);
        Assert.Equal(0, result.Failed);
        Assert.Equal(ModernPgn, (await _db.BookSources.AsNoTracking().SingleAsync(s => s.Id == book.Id)).SourcePgn);
        Assert.Equal(0, (await _db.Books.AsNoTracking().SingleAsync(b => b.Id == book.Id)).ImportVersion);
    }

    [Fact]
    public async Task ReprocessCourses_CacheMode_FollowsTheTrainingMarkersOfTheStoredCourse()
    {
        // Buch-Kurs (mit [%tqu]) fragt den Modus mit Marker, ein aus einem Repertoire umgewandelter Kurs
        // (ohne Marker) den ohne — sonst bekäme er Marker und damit einen anderen Trainingsstart.
        await SeedBookAsync("chessable-u7-71.pgn", 0,
            ChessableLine("001.001", "71", "2. Nf3 {[%tqu \"En\",\"find it\"] Develops.} Nc6 3. Bb5 a6 *"), "chessable");
        await SeedBookAsync("chessable-u7-72.pgn", 0,
            ChessableLine("001.001", "72", "2. Nf3 {Develops.} Nc6 3. Bb5 a6 *"), "chessable");
        var lines = new StubCachedLineSource();
        lines.Lines["71"] = CacheLine("71", "2. Nf3 {[%tqu \"En\",\"find it\"] Develops.} Nc6 3. Bb5 a6 *");
        lines.Lines["72"] = CacheLine("72", "2. Nf3 {Develops.} Nc6 3. Bb5 a6 *");

        await ReprocessTestHelper.Build(_db, cachedLines: lines).ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal("FirstKeyMove", lines.PgnCalls.Single(c => c.Oids.Contains("71")).Mode);
        Assert.Equal("None", lines.PgnCalls.Single(c => c.Oids.Contains("72")).Mode);
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
    public async Task ReprocessCourses_FailingBook_DoesNotAbortBatch_RefetchesStillEnqueued()
    {
        // FALLE: ohne per-Buch-Isolation reißt EIN scheiterndes Buch den ganzen Batch mit — die
        // restlichen Bücher bleiben veraltet UND die eingesammelten Re-Fetch-Kandidaten werden nie
        // eingereiht (das Einreihen läuft erst nach der Schleife).
        await SeedBookAsync("manual-a.pgn", 0, SamplePgn, null);
        await SeedBookAsync("manual-b.pgn", 0, SamplePgn, null);
        await SeedBookAsync("chessable-u7-abc123.pgn", 0, null, "chessable");

        // Kaputter DbContext ⇒ jeder lokale Re-Parse wirft (steht für korruptes SourcePgn / DB-Fehler).
        var brokenDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        brokenDb.Dispose();
        var stub = new StubCourseReimporter { ReturnId = 42 };
        var svc = new ImportReprocessService(_db, new PgnImportService(brokenDb), stub,
            NullLogger<ImportReprocessService>.Instance);

        var result = await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(0, result.Reprocessed);
        Assert.Equal(2, result.Failed);                        // beide lokalen Bücher fehlgeschlagen, kein Abbruch
        Assert.Equal(0, result.Skipped);                       // „kaputt" ist NICHT „nichts zu tun"
        Assert.Equal(1, result.Enqueued);                      // Re-Fetch trotzdem eingereiht
        Assert.Equal("abc123", Assert.Single(stub.Calls).Bid);
    }

    [Fact]
    public async Task ReprocessCourses_FailingBook_CountsAsFailed_NotSkipped_AndStaysStale()
    {
        // Regression (Codereview 2026-08-07): ein Fehlschlag wurde wie „keine Quelle" als Skipped
        // gezählt. Das Buch behält seine ImportVersion → es bleibt im „Aktualisieren (N)"-Banner
        // stehen, ohne dass irgendwo steht, dass etwas kaputt ist. Getrennte Zähler:
        // ohne Quelle = Skipped, mit Wurf = Failed.
        await SeedBookAsync("manual-nosource.pgn", 0, null, null);          // keine Quelle → Skipped
        var broken = await SeedBookAsync("manual-broken.pgn", 0, SamplePgn, null);   // wirft → Failed

        var brokenDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        brokenDb.Dispose();
        var svc = new ImportReprocessService(_db, new PgnImportService(brokenDb), new StubCourseReimporter(),
            NullLogger<ImportReprocessService>.Instance);

        var result = await svc.ReprocessCoursesAsync(UserId, isAdmin: false);

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Reprocessed);
        // Das kaputte Buch bleibt veraltet — genau deshalb braucht der Aufrufer den Fehler-Zähler.
        Assert.Equal(0, (await _db.Books.SingleAsync(b => b.Id == broken.Id)).ImportVersion);
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
            Status = ChessableImportStatus.Completed, CompletedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
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
            Status = ChessableImportStatus.Completed, CompletedAt = DateTime.UtcNow.AddHours(-48), CreatedAt = DateTime.UtcNow.AddHours(-48),
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
    public async Task ReprocessCourses_LocalOnly_ReprocessesStoredSourceAndRebuildsFromCache_SkipsChessableRefetch()
    {
        // „Aus Cache" heißt: ohne Chessable-Netzabruf. Der Linien-Cache ist genau das — er gehört dazu; nur der
        // Re-Fetch bleibt ausgelassen. Drei Bücher: lokal aufbereitbar, Chessable mit oids, Chessable-Altbestand.
        var local = await SeedBookAsync("manual-loc.pgn", 0, SamplePgn, null);
        _db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = "manual-loc.pgn:1", BookFileName = local.FileName, BookId = local.Id, Round = "1",
            Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
            Moves = "g1f3 b8c6 f1b5 a7a6", StartPly = -1,
        });
        var modern = await SeedBookAsync("chessable-u7-modern.pgn", 0, ModernPgn, "chessable");
        await SeedBookAsync("chessable-u7-abc123.pgn", 0, null, "chessable");
        await _db.SaveChangesAsync();
        var lines = new StubCachedLineSource();
        lines.Lines["10"] = CacheLine("10", "2. Nf3 {[%alt g1e2] Develops from the cache.} Nc6 3. Bb5 {The pin.} a6 *");

        var stub = new StubCourseReimporter { ReturnId = 42 };
        var result = await ReprocessTestHelper.Build(_db, stub, lines).ReprocessCoursesAsync(UserId, isAdmin: false, localOnly: true);

        Assert.Equal(2, result.Reprocessed);            // lokales Buch + Chessable aus dem Cache
        Assert.Equal(1, result.RebuiltFromCache);
        Assert.Equal(0, result.Enqueued);               // KEIN Chessable-Re-Fetch
        Assert.Empty(stub.Calls);
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Books.AsNoTracking().SingleAsync(b => b.Id == modern.Id)).ImportVersion);
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
    public async Task ReprocessRepertoires_ChessableAltButNoOid_EnqueuesRefetch_NotVersionMark()
    {
        // Regression: Chessable-Repertoire mit [%alt], aber OHNE [ChessableOid] darf NICHT als „modern"
        // durchgehen und versions-markiert werden — es muss re-gefetcht werden, damit die oids reinkommen.
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        _db.ChessableCredentials.Add(new ChessableCredential { UserId = user.Id, EncryptedBearer = "x" });
        await _db.SaveChangesAsync();
        var rep = await SeedRepertoireAsync(user.Id, 0, "chessable-128648.pgn", pgn: AltNoOidPgn);
        var stub = new StubCourseReimporter { ReturnId = 777 };

        var result = await ReprocessTestHelper.Build(_db, stub).ReprocessRepertoiresAsync(user.Id);

        Assert.Equal(1, result.Enqueued);
        Assert.Equal(0, result.Reprocessed);          // NICHT versions-markiert
        var call = Assert.Single(stub.Calls);
        Assert.Equal("128648", call.Bid);
        Assert.Equal(rep.Id, call.TargetRepertoireId);
        // Version bleibt veraltet, bis der Re-Fetch das oid-tragende PGN eingespielt hat.
        Assert.Equal(0, (await _db.Repertoires.SingleAsync(r => r.Id == rep.Id)).ImportVersion);
    }

    [Fact]
    public async Task ReprocessRepertoires_Chessable_EnqueuesInPlaceRefetch_KeepsRepertoireId()
    {
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        _db.ChessableCredentials.Add(new ChessableCredential { UserId = user.Id, EncryptedBearer = "x" });
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
    public async Task ReprocessRepertoires_Chessable_NoBearerNotCached_MarksLocally_ClearsBanner()
    {
        // Kein Bearer des Owners UND nicht gecacht → nie automatisch holbar. Statt es (wie früher) bei
        // jedem „Aktualisieren" still zu überspringen und ewig im Banner hängen zu lassen, wird es als
        // aktuell markiert (Banner klärt; echter Re-Import erst nach Bearer-Hinterlegung).
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var rep = await SeedRepertoireAsync(user.Id, 0, "chessable-128648.pgn");
        var stub = new StubCourseReimporter { ReturnId = null }; // egal — es wird gar nicht erst eingereiht

        var result = await ReprocessTestHelper.Build(_db, stub).ReprocessRepertoiresAsync(user.Id);

        Assert.Equal(0, result.Enqueued);
        Assert.Empty(stub.Calls);                    // kein Enqueue-Versuch
        Assert.Equal(1, result.Reprocessed);         // stattdessen Versions-Mark
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Repertoires.SingleAsync(r => r.Id == rep.Id)).ImportVersion);
    }

    [Fact]
    public async Task ReprocessRepertoires_Admin_NoBearerButCached_RefetchesFromCache()
    {
        // Genau der gemeldete Fall: ein Repertoire eines Users OHNE Bearer, dessen Kurs aber (von jemand
        // anderem) gecacht ist → Admin-Reprocess holt es OHNE Bearer aus dem Cache in-place.
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var rep = await SeedRepertoireAsync(user.Id, 0, fileName: null, courseId: "320357");
        var stub = new StubCourseReimporter { ReturnId = 777, CachedBids = new HashSet<string> { "320357" } };

        var result = await ReprocessTestHelper.Build(_db, stub).ReprocessRepertoiresAsync(user.Id, isAdmin: true);

        Assert.Equal(1, result.Enqueued);
        var call = Assert.Single(stub.Calls);
        Assert.Equal("320357", call.Bid);
        Assert.Equal(rep.Id, call.TargetRepertoireId);
        Assert.True(call.TrustOwnership);
        Assert.Equal(1, stub.GetCachedBidsCalls);            // genau EIN Batch-Abruf
        // Version bleibt veraltet, bis der Hintergrund-Job das frische PGN eingespielt hat.
        Assert.Equal(0, (await _db.Repertoires.SingleAsync(r => r.Id == rep.Id)).ImportVersion);
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
        // gecacht → im Admin-Reprocess auch ohne Bearer holbar (sonst würden beide nur versions-markiert)
        var stub = new StubCourseReimporter { ReturnId = 1, CachedBids = new HashSet<string> { "111", "222" } };

        await ReprocessTestHelper.Build(_db, stub).ReprocessRepertoiresAsync(user.Id, isAdmin: true);

        Assert.Equal(1, stub.GetCachedBidsCalls);              // genau EIN Batch-Abruf für beide
        Assert.Equal(2, stub.Calls.Count);
        Assert.All(stub.Calls, c => Assert.True(c.TrustOwnership));
    }
}
