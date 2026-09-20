using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Steht der RookHub-EIGENE Chessable-Weg auf <c>Chessable:Enabled=false</c> (PROD seit 2026-09-09),
/// läuft keine Import-Lane. Ein veraltetes Buch darf dann nicht als „aktualisierbar" angeboten werden:
/// gemeldet am 2026-09-20 stand „1 Kurs kann aktualisiert werden" dauerhaft im Banner, und jeder Klick
/// meldete im Log nur „0 eingereiht, 6 übersprungen".
/// </summary>
public class ReprocessWithoutChessableTests : IDisposable
{
    private readonly AppDbContext _db;

    public ReprocessWithoutChessableTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private sealed class StubReimporter : ICourseReimporter
    {
        public List<string> Enqueued { get; } = new();
        public Task<HashSet<string>> GetCachedBidsAsync(CancellationToken ct = default)
            => Task.FromResult(new HashSet<string>());
        public Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName,
            int? targetRepertoireId = null, bool? knownCached = null, bool trustOwnership = false, CancellationToken ct = default)
        {
            Enqueued.Add(bid);
            return Task.FromResult<int?>(Enqueued.Count);
        }
    }

    private ImportReprocessService Service(StubReimporter stub, bool chessableEnabled)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Chessable:Enabled"] = chessableEnabled ? "true" : "false" })
            .Build();
        return new ImportReprocessService(_db, new PgnImportService(_db), stub,
            NullLogger<ImportReprocessService>.Instance, config);
    }

    /// <summary>Chessable-Buch, veraltet, Quelle OHNE [ChessableOid] — der klassische Re-Fetch-Fall.</summary>
    private async Task<Book> AddStaleChessableBookAsync(string? sourcePgn)
    {
        var book = new Book
        {
            FileName = "chessable-u5-91808.pgn",
            DisplayName = "Lifetime Repertoires",
            Tags = "chessable",
            OwnerUserId = 5,
            ImportVersion = ImportPipeline.CurrentVersion - 1,
            SourcePgn = sourcePgn,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task Status_WithChessableOff_OffersTheLocalPath_InsteadOfARefetchThatCannotRun()
    {
        await AddStaleChessableBookAsync("[Event \"x\"]\n\n1. e4 *\n");

        var on = await Service(new StubReimporter(), chessableEnabled: true).GetCourseStatusAsync(5, isAdmin: true);
        Assert.Equal(1, on.Refetchable);
        Assert.Equal(0, on.ReprocessableLocally);

        var off = await Service(new StubReimporter(), chessableEnabled: false).GetCourseStatusAsync(5, isAdmin: true);
        Assert.Equal(0, off.Refetchable);
        Assert.Equal(1, off.ReprocessableLocally);   // die gespeicherte Quelle trägt die Zug-Kommentare
        Assert.Equal(0, off.NeedsReimport);
    }

    [Fact]
    public async Task Status_WithChessableOff_AndNoSource_IsNotActionableAtAll()
    {
        await AddStaleChessableBookAsync(null);

        var off = await Service(new StubReimporter(), chessableEnabled: false).GetCourseStatusAsync(5, isAdmin: true);
        Assert.Equal(0, off.Refetchable);
        Assert.Equal(0, off.ReprocessableLocally);
        // Nur der (wegklickbare) Hinweis bleibt — das Banner verspricht keine Aktualisierung mehr.
        Assert.Equal(1, off.NeedsReimport);
    }

    [Fact]
    public async Task Reprocess_WithChessableOff_EnqueuesNothing()
    {
        await AddStaleChessableBookAsync(null);
        var stub = new StubReimporter();

        var res = await Service(stub, chessableEnabled: false).ReprocessCoursesAsync(5, isAdmin: true);

        Assert.Empty(stub.Enqueued);   // ein Auftrag würde ohne Lane ewig auf „läuft" stehen
        Assert.Equal(0, res.Enqueued);
        Assert.Equal(1, res.Skipped);
    }

    [Fact]
    public async Task Reprocess_WithChessableOn_StillEnqueuesTheRefetch()
    {
        await AddStaleChessableBookAsync(null);
        var stub = new StubReimporter();

        var res = await Service(stub, chessableEnabled: true).ReprocessCoursesAsync(5, isAdmin: true);

        Assert.Equal(new[] { "91808" }, stub.Enqueued);
        Assert.Equal(1, res.Enqueued);
    }

    [Fact]
    public async Task Status_AndRun_AgreeOnEveryBook_SoTheBannerCanEmpty()
    {
        // Genau der gemeldete Zustand: die Anzeige zählt es als aktualisierbar, der Lauf überspringt es.
        await AddStaleChessableBookAsync("[Event \"x\"]\n\n1. e4 *\n");
        var svc = Service(new StubReimporter(), chessableEnabled: false);

        var before = await svc.GetCourseStatusAsync(5, isAdmin: true);
        Assert.Equal(1, before.ReprocessableLocally + before.Refetchable);

        var res = await svc.ReprocessCoursesAsync(5, isAdmin: true);
        Assert.Equal(1, res.Reprocessed);

        var after = await svc.GetCourseStatusAsync(5, isAdmin: true);
        Assert.Equal(0, after.ReprocessableLocally + after.Refetchable);   // Banner ist danach leer
        Assert.Equal(0, after.Stale);
    }

    /// <summary>Kurs anlegen, den die Liste zeigt (Besitzer = User 5).</summary>
    private async Task<Book> AddOwnedBookAsync(string fileName, string? tags, string? sourcePgn, int importVersion)
    {
        var book = new Book
        {
            FileName = fileName, DisplayName = fileName, Tags = tags, OwnerUserId = 5,
            ImportVersion = importVersion, SourcePgn = sourcePgn,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task CourseList_MarksExactlyTheCoursesTheUpdateButtonCannotFix()
    {
        // 1) Chessable-Kurs ohne moderne Marker, eigener Chessable-Weg AUS, KEINE gespeicherte Quelle
        //    → Showstopper, bekommt das (!).
        var stuck = await AddOwnedBookAsync("chessable-u5-91808.pgn", "chessable", null, ImportPipeline.CurrentVersion - 1);
        // 2) Derselbe Fall MIT gespeicherter Quelle → der Knopf bereitet ihn lokal auf, kein (!).
        var fixable = await AddOwnedBookAsync("chessable-u5-55720.pgn", "chessable", "[Event \"x\"]\n\n1. e4 *\n", ImportPipeline.CurrentVersion - 1);
        // 3) Aktueller Kurs → nie ein (!).
        var current = await AddOwnedBookAsync("chessable-u5-1.pgn", "chessable", null, ImportPipeline.CurrentVersion);
        // 4) Von Hand hochgeladener, veralteter Kurs ohne Quelle → auch ohne Chessable ein Showstopper.
        var manual = await AddOwnedBookAsync("eigene-partien.pgn", null, null, ImportPipeline.CurrentVersion - 1);

        var courses = await TestServices.Course(_db, configuration: TestServices.ChessableSwitch(false))
            .GetCoursesAsync(5, isAdmin: false);

        Assert.True(courses.Single(c => c.BookId == stuck.Id).NeedsReimport);
        Assert.False(courses.Single(c => c.BookId == fixable.Id).NeedsReimport);
        Assert.False(courses.Single(c => c.BookId == current.Id).NeedsReimport);
        Assert.True(courses.Single(c => c.BookId == manual.Id).NeedsReimport);
    }

    [Fact]
    public async Task CourseList_WithChessableOn_MarksNothingThatCanStillBeFetched()
    {
        var refetchable = await AddOwnedBookAsync("chessable-u5-91808.pgn", "chessable", null, ImportPipeline.CurrentVersion - 1);

        var courses = await TestServices.Course(_db, configuration: TestServices.ChessableSwitch(true))
            .GetCoursesAsync(5, isAdmin: false);

        Assert.False(courses.Single(c => c.BookId == refetchable.Id).NeedsReimport);
    }

    [Fact]
    public async Task RepertoireList_MarksTheChessableRepertoireThatCannotBeFetchedAnyMore()
    {
        _db.AppUsers.Add(new AppUser { Id = 5, Username = "u", PasswordHash = "x" });
        var stuck = new Repertoire
        {
            UserId = 5, Name = "Caro-Kann", ChessableCourseId = "163418",
            ImportVersion = ImportPipeline.CurrentVersion - 1,
            Files = { new RepertoireFile { FileName = "chessable-163418.pgn", PgnContent = "[Event \"x\"]\n\n1. e4 *\n" } },
        };
        var modern = new Repertoire
        {
            UserId = 5, Name = "Sizilianisch", ChessableCourseId = "1",
            ImportVersion = ImportPipeline.CurrentVersion - 1,
            Files = { new RepertoireFile { FileName = "chessable-1.pgn", PgnContent = "[ChessableOid \"9\"]\n\n1. e4 *\n" } },
        };
        var handmade = new Repertoire
        {
            UserId = 5, Name = "Eigenes", ImportVersion = ImportPipeline.CurrentVersion - 1,
            Files = { new RepertoireFile { FileName = "eigenes.pgn", PgnContent = "1. e4 *" } },
        };
        _db.Repertoires.AddRange(stuck, modern, handmade);
        await _db.SaveChangesAsync();

        var list = await TestServices.Repertoire(_db, configuration: TestServices.ChessableSwitch(false)).GetAllAsync(5);

        Assert.True(list.Single(r => r.Id == stuck.Id).NeedsReimport);
        Assert.False(list.Single(r => r.Id == modern.Id).NeedsReimport);     // Marker schon drin
        Assert.False(list.Single(r => r.Id == handmade.Id).NeedsReimport);   // nichts von Chessable

        // Mit eigenem Chessable-Weg ist nichts davon ein Showstopper — der Knopf holt es.
        var withChessable = await TestServices.Repertoire(_db, configuration: TestServices.ChessableSwitch(true)).GetAllAsync(5);
        Assert.All(withChessable, r => Assert.False(r.NeedsReimport));
    }

    [Fact]
    public async Task RepertoireReprocess_WithChessableOff_LeavesTheStuckOneStale_SoTheMarkerStays()
    {
        _db.AppUsers.Add(new AppUser { Id = 5, Username = "u", PasswordHash = "x" });
        var stuck = new Repertoire
        {
            UserId = 5, Name = "Caro-Kann", ChessableCourseId = "163418",
            ImportVersion = ImportPipeline.CurrentVersion - 1,
            Files = { new RepertoireFile { FileName = "chessable-163418.pgn", PgnContent = "1. e4 *" } },
        };
        var handmade = new Repertoire
        {
            UserId = 5, Name = "Eigenes", ImportVersion = ImportPipeline.CurrentVersion - 1,
            Files = { new RepertoireFile { FileName = "eigenes.pgn", PgnContent = "1. e4 *" } },
        };
        _db.Repertoires.AddRange(stuck, handmade);
        await _db.SaveChangesAsync();

        var res = await Service(new StubReimporter(), chessableEnabled: false).ReprocessRepertoiresAsync(5);

        // Das handgemachte bekommt den Versions-Mark, das Chessable-eigene bleibt ehrlich veraltet.
        Assert.Equal(1, res.Reprocessed);
        Assert.Equal(1, res.Skipped);
        Assert.Equal(ImportPipeline.CurrentVersion - 1, (await _db.Repertoires.SingleAsync(r => r.Id == stuck.Id)).ImportVersion);
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Repertoires.SingleAsync(r => r.Id == handmade.Id)).ImportVersion);
    }
}
