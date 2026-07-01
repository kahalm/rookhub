using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Sichtbarkeit persönlicher Bücher (Book.OwnerUserId, z. B. eigener Chessable-Import):
/// nur der Besitzer (und Admins) sehen das Buch als Kurs.
/// </summary>
public class CourseServiceOwnerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _svc;

    public CourseServiceOwnerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db), new BookAdminService(_db), new RepertoireService(_db, new RepertoireAnalyzeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()))));
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> SeedPersonalBookAsync(int ownerUserId)
    {
        var book = new Book
        {
            FileName = $"chessable-u{ownerUserId}-x.pgn",
            DisplayName = "My Chessable Course",
            OwnerUserId = ownerUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task Owner_CanAccess_PersonalBook()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        Assert.True(await _svc.CanAccessAsync(userId: 1, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task NonOwner_CannotAccess_PersonalBook()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        Assert.False(await _svc.CanAccessAsync(userId: 2, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task Admin_CanAccess_AnyPersonalBook()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        Assert.True(await _svc.CanAccessAsync(userId: 99, book.Id, isAdmin: true));
    }

    [Fact]
    public async Task GetCourses_ListsOwnPersonalBook_NotForOthers()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);

        var ownerCourses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.Contains(ownerCourses, c => c.BookId == book.Id);

        var otherCourses = await _svc.GetCoursesAsync(userId: 2, isAdmin: false);
        Assert.DoesNotContain(otherCourses, c => c.BookId == book.Id);
    }

    [Fact]
    public async Task HasAnyAccess_TrueForOwner_FalseForOther()
    {
        await SeedPersonalBookAsync(ownerUserId: 1);
        Assert.True(await _svc.HasAnyAccessAsync(userId: 1, isAdmin: false));
        Assert.False(await _svc.HasAnyAccessAsync(userId: 2, isAdmin: false));
    }

    /// <summary>Buch über eine Gruppe freigegeben (kein OwnerUserId) — öffentlicher Kurs.</summary>
    private async Task<Book> SeedGroupBookAsync(int groupId, int memberUserId)
    {
        var book = new Book
        {
            FileName = $"group-{groupId}.pgn",
            DisplayName = "Group Course",
            OwnerUserId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Books.Add(book);
        _db.UserGroups.Add(new UserGroup { UserId = memberUserId, GroupId = groupId });
        await _db.SaveChangesAsync();
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = book.Id, GroupId = groupId });
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task GetCourses_MarksPersonalBook_AsOwned()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        var courses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.True(courses.Single(c => c.BookId == book.Id).IsOwned);
    }

    [Fact]
    public async Task GetCourses_MarksGroupBook_AsNotOwned()
    {
        var book = await SeedGroupBookAsync(groupId: 5, memberUserId: 1);
        var courses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.False(courses.Single(c => c.BookId == book.Id).IsOwned);
    }

    [Fact]
    public async Task GetCourses_OtherUsersPersonalBook_NotOwned_ForAdmin()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        var courses = await _svc.GetCoursesAsync(userId: 99, isAdmin: true);
        Assert.False(courses.Single(c => c.BookId == book.Id).IsOwned);
    }

    [Fact]
    public async Task GetCourses_SetsLastActivityAt_FromCourseProgress()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        var used = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.CourseProgresses.Add(new CourseProgress
        {
            UserId = 1, BookId = book.Id, LastMode = "sequential",
            CreatedAt = used, UpdatedAt = used
        });
        await _db.SaveChangesAsync();

        var courses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.Equal(used, courses.Single(c => c.BookId == book.Id).LastActivityAt);
    }

    [Fact]
    public async Task GetCourses_LastActivityAt_NullWhenNotStarted()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        var courses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.Null(courses.Single(c => c.BookId == book.Id).LastActivityAt);
    }

    [Fact]
    public async Task GetBookPgn_WithSourcePgn_ReturnsRawVerbatim_WithVariationsAndComments()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        // Roh-PGN mit Variante (Klammer) + Kommentar — muss unverändert durchgereicht werden.
        var raw = "[Event \"X\"]\n\n1. e4 e5 (1... c5 {Sizilianisch}) 2. Nf3 {Hauptlinie} *\n";
        book.SourcePgn = raw;
        await _db.SaveChangesAsync();

        var (pgn, fileName) = await _svc.GetBookPgnAsync(userId: 1, book.Id, isAdmin: false);

        Assert.Equal(raw, pgn);                       // verbatim, nicht rekonstruiert
        Assert.Contains("(1... c5 {Sizilianisch})", pgn);
        Assert.EndsWith(".pgn", fileName);
    }

    [Fact]
    public async Task GetBookPgn_WithoutSourcePgn_ReconstructsFromPuzzles_WithMoveComments()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1); // kein SourcePgn (Altbestand)
        _db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = "l1",
            BookId = book.Id,
            BookFileName = book.FileName,
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            Moves = "e2e4 e7e5",
            MoveComments = "{\"0\":\"Bester Zug\"}"
        });
        await _db.SaveChangesAsync();

        var (pgn, _) = await _svc.GetBookPgnAsync(userId: 1, book.Id, isAdmin: false);

        Assert.Contains("1. e4 {Bester Zug} e5", pgn); // rekonstruiert inkl. Zug-Kommentar
    }

    // --- Persönlicher Kurs-Upload (PGN → eigenes Buch) ---

    // Puzzle-PGN im Chessable-Stil (FEN + Round + [%tqu]-Trainingsmarker) — nur solche PGNs
    // erzeugt die Import-Pipeline zu Kurs-Puzzles.
    private const string SamplePgn =
        "[Event \"T\"]\n[Round \"1\"]\n[FEN \"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1\"]\n\n" +
        "1. e4 e5 2. Nf3 Nc6 {[%tqu \"En\",\"find\",\"\",\"\",\"f1b5\",\"\",10]} 3. Bb5 a6 *\n";

    [Fact]
    public async Task UploadPersonalCourse_CreatesOwnedStudyBook_WithPuzzles()
    {
        var dto = await _svc.UploadPersonalCourseAsync(userId: 7, "ruy-lopez.pgn", SamplePgn, null);

        Assert.True(dto.IsOwned);
        Assert.True(dto.PuzzleCount > 0);
        Assert.Equal(0, dto.SolvedCount);

        var book = await _db.Books.SingleAsync(b => b.Id == dto.BookId);
        Assert.Equal(7, book.OwnerUserId);
        Assert.Equal(BookKind.Study, book.Kind);
        Assert.StartsWith("user-u7-", book.FileName);        // interner, pro-User-eindeutiger Name
        Assert.Equal("ruy-lopez", book.DisplayName);          // aus Dateiname abgeleitet (ohne .pgn)
        // Der Besitzer sieht den Kurs sofort; ein anderer User nicht.
        Assert.True(await _svc.HasAnyAccessAsync(userId: 7, isAdmin: false));
        Assert.False(await _svc.HasAnyAccessAsync(userId: 8, isAdmin: false));
    }

    [Fact]
    public async Task UploadPersonalCourse_UsesProvidedDisplayName()
    {
        var dto = await _svc.UploadPersonalCourseAsync(userId: 1, "ignored.pgn", SamplePgn, "  Mein Eröffnungsrepertoire  ");
        Assert.Equal("Mein Eröffnungsrepertoire", dto.DisplayName);
    }

    [Fact]
    public async Task UploadPersonalCourse_InvalidPgn_ThrowsAndCreatesNoBook()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.UploadPersonalCourseAsync(userId: 1, "notpgn.pgn", "this is not a pgn at all", null));
        Assert.Empty(_db.Books);
    }

    [Fact]
    public async Task DeletePersonalCourse_Owner_RemovesBook()
    {
        var dto = await _svc.UploadPersonalCourseAsync(userId: 1, "line.pgn", SamplePgn, null);
        await _svc.DeletePersonalCourseAsync(userId: 1, dto.BookId);
        Assert.False(await _db.Books.AnyAsync(b => b.Id == dto.BookId));
    }

    [Fact]
    public async Task DeletePersonalCourse_NonOwner_Throws()
    {
        var dto = await _svc.UploadPersonalCourseAsync(userId: 1, "line.pgn", SamplePgn, null);
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.DeletePersonalCourseAsync(userId: 2, dto.BookId));
        Assert.True(await _db.Books.AnyAsync(b => b.Id == dto.BookId)); // unangetastet
    }

    [Fact]
    public async Task DeletePersonalCourse_GroupBook_Throws_NotOwned()
    {
        var book = await SeedGroupBookAsync(groupId: 3, memberUserId: 1);
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.DeletePersonalCourseAsync(userId: 1, book.Id));
    }

    // --- Dashboard-Pinning ---

    [Fact]
    public async Task GetCourses_IsPinned_FalseByDefault()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        var courses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.False(courses.Single(c => c.BookId == book.Id).IsPinned);
    }

    [Fact]
    public async Task PinCourse_MarksIsPinned_InCourseList()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        await _svc.PinCourseAsync(userId: 1, book.Id, isAdmin: false);

        var courses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.True(courses.Single(c => c.BookId == book.Id).IsPinned);
        Assert.Single(_db.CoursePins);
    }

    [Fact]
    public async Task PinCourse_IsIdempotent()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        await _svc.PinCourseAsync(userId: 1, book.Id, isAdmin: false);
        await _svc.PinCourseAsync(userId: 1, book.Id, isAdmin: false);
        Assert.Single(_db.CoursePins);
    }

    [Fact]
    public async Task UnpinCourse_RemovesPin()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        await _svc.PinCourseAsync(userId: 1, book.Id, isAdmin: false);
        await _svc.UnpinCourseAsync(userId: 1, book.Id);

        Assert.Empty(_db.CoursePins);
        var courses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.False(courses.Single(c => c.BookId == book.Id).IsPinned);
    }

    [Fact]
    public async Task UnpinCourse_WhenNotPinned_IsNoOp()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        await _svc.UnpinCourseAsync(userId: 1, book.Id); // kein Pin vorhanden
        Assert.Empty(_db.CoursePins);
    }

    [Fact]
    public async Task PinCourse_InaccessibleBook_Throws_AndPinsNothing()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1); // gehört User 1
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.PinCourseAsync(userId: 2, book.Id, isAdmin: false));
        Assert.Empty(_db.CoursePins);
    }

    [Fact]
    public async Task Pin_IsPerUser_NotVisibleToOtherUser()
    {
        var book = await SeedGroupBookAsync(groupId: 4, memberUserId: 1);
        _db.UserGroups.Add(new UserGroup { UserId = 2, GroupId = 4 });
        await _db.SaveChangesAsync();

        await _svc.PinCourseAsync(userId: 1, book.Id, isAdmin: false);

        Assert.True((await _svc.GetCoursesAsync(1, false)).Single(c => c.BookId == book.Id).IsPinned);
        Assert.False((await _svc.GetCoursesAsync(2, false)).Single(c => c.BookId == book.Id).IsPinned);
    }

    [Fact]
    public async Task DeletePersonalCourse_AlsoRemovesPins()
    {
        var dto = await _svc.UploadPersonalCourseAsync(userId: 1, "line.pgn", SamplePgn, null);
        await _svc.PinCourseAsync(userId: 1, dto.BookId, isAdmin: false);
        Assert.Single(_db.CoursePins);

        await _svc.DeletePersonalCourseAsync(userId: 1, dto.BookId);
        Assert.Empty(_db.CoursePins);
    }
}
