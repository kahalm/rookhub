using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>„Kurs → Repertoire umwandeln" und umgekehrt: der Kurs-PGN wird zum Repertoire, der
/// Repertoire-PGN zum persönlichen Kurs (nur bei Puzzle-PGN im Chessable-Stil).</summary>
public class CourseConversionTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _courses;
    private readonly RepertoireService _repertoires;

    // Chessable-Stil-Puzzle-PGN (FEN + [%tqu]-Trainingsmarker) → erzeugt beim Import ein Kurs-Puzzle.
    private const string PuzzlePgn = @"
[Event ""Test Book""]
[Round ""1.1""]
[White ""Italian Idea""]
[Result ""*""]
[SetUp ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

{ [%tqu ""En"",""Finde den Zug""] Pointe. } 2.Nf3 Nc6 3. Bb5 $1 a6 *
";

    // Reines Eröffnungs-PGN ohne Puzzle-Marker → kein quiz-barer Kurs-Inhalt.
    private const string PlainPgn = "[Event \"Opening\"]\n[Result \"*\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 *";

    public CourseConversionTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _repertoires = new RepertoireService(_db, new RepertoireAnalyzeService(_db, new MemoryCache(new MemoryCacheOptions())));
        _courses = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db), new BookAdminService(_db), _repertoires);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ConvertCourseToRepertoire_CreatesRepertoireWithBookPgn()
    {
        var book = new Book
        {
            FileName = "chessable-u1-x.pgn", DisplayName = "My Course", OwnerUserId = 1,
            SourcePgn = PlainPgn, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        var rep = await _courses.ConvertToRepertoireAsync(userId: 1, bookId: book.Id, isAdmin: false);

        var saved = await _db.Repertoires.Include(r => r.Files).FirstAsync(r => r.Id == rep.Id);
        Assert.Equal(1, saved.UserId);
        Assert.Equal("My Course", saved.Name);
        Assert.False(saved.UseForExtension);              // wie importierte Kurse: Extension-Nutzung aus
        var file = Assert.Single(saved.Files);
        Assert.Contains("1. e4 e5", file.PgnContent);      // Kurs-PGN im Repertoire gelandet
        // Verschieben: der EIGENE Original-Kurs ist nach der Umwandlung entfernt.
        Assert.False(await _db.Books.AnyAsync(b => b.Id == book.Id));
    }

    [Fact]
    public async Task ConvertCourseToRepertoire_SharedCourse_KeepsOriginal()
    {
        // Geteiltes Gruppen-Buch (kein OwnerUserId), für User 1 über eine Gruppe freigegeben.
        var book = new Book { FileName = "group.pgn", DisplayName = "Group Course", OwnerUserId = null, SourcePgn = PlainPgn, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        _db.UserGroups.Add(new UserGroup { UserId = 1, GroupId = 7 });
        await _db.SaveChangesAsync();
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = book.Id, GroupId = 7 });
        await _db.SaveChangesAsync();

        var rep = await _courses.ConvertToRepertoireAsync(userId: 1, bookId: book.Id, isAdmin: false);

        Assert.True(await _db.Repertoires.AnyAsync(r => r.Id == rep.Id));
        // Geteilter Kurs gehört dem User nicht → bleibt bestehen (nicht gelöscht).
        Assert.True(await _db.Books.AnyAsync(b => b.Id == book.Id));
    }

    [Fact]
    public async Task ConvertCourseToRepertoire_NoAccess_Throws()
    {
        var book = new Book { FileName = "chessable-u1-y.pgn", DisplayName = "Foreign", OwnerUserId = 1, SourcePgn = PlainPgn };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _courses.ConvertToRepertoireAsync(userId: 2, bookId: book.Id, isAdmin: false));
    }

    [Fact]
    public async Task ConvertRepertoireToCourse_WithPuzzlePgn_CreatesCourse()
    {
        // Repertoire mit Chessable-Stil-Puzzle-PGN anlegen (wie es aus „Kurs → Repertoire" entstünde).
        var rep = await _repertoires.CreateFromPgnAsync(userId: 1, name: "Puzzles", fileName: "p.pgn", pgn: PuzzlePgn);

        // Reverse (wie im Controller): kombiniertes PGN → persönlicher Kurs.
        var pgn = await _repertoires.GetCombinedPgnAsync(rep.Id, userId: 1);
        var course = await _courses.UploadPersonalCourseAsync(userId: 1, "Puzzles.pgn", pgn, "Puzzles");

        Assert.True(course.PuzzleCount >= 1);
        Assert.True(course.IsOwned);
        Assert.Equal("Puzzles", course.DisplayName);
    }

    [Fact]
    public async Task ConvertRepertoireToCourse_PlainPgn_ThrowsNoPlayableLines()
    {
        var rep = await _repertoires.CreateFromPgnAsync(userId: 1, name: "Opening", fileName: "o.pgn", pgn: PlainPgn);
        var pgn = await _repertoires.GetCombinedPgnAsync(rep.Id, userId: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courses.UploadPersonalCourseAsync(userId: 1, "Opening.pgn", pgn, "Opening"));
    }
}
