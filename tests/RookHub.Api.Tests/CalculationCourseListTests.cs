using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Kursübersicht + Admin-Verwaltung für Kalkulationsbücher: dort gibt es nichts zu „lösen", also
/// zählt die Übersicht ALLE Stellungen (auch die zug-losen, die im normalen Kurs als Info-Linien
/// aus dem Fortschritt fallen) und als „erledigt" die Stellungen mit eigenem Analysebaum.
/// </summary>
public class CalculationCourseListTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _courses;
    private readonly CalculationService _calc;
    private readonly BookAdminService _bookAdmin;

    public CalculationCourseListTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _bookAdmin = new BookAdminService(_db);
        _courses = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db), _bookAdmin,
            new RepertoireService(_db, new RepertoireAnalyzeService(_db, new MemoryCache(new MemoryCacheOptions()))));
        _calc = new CalculationService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> CreateUserAsync(string name = "u1")
    {
        var user = new AppUser { Username = name, PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Book> SeedBookAsync(int ownerUserId, bool isCalculation)
    {
        var book = new Book
        {
            FileName = $"b-{Guid.NewGuid():N}.pgn", DisplayName = "Buch", OwnerUserId = ownerUserId,
            IsCalculation = isCalculation, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    private async Task<BookPuzzle> SeedLineAsync(Book book, string round, bool infoOnly)
    {
        var p = new BookPuzzle
        {
            LineId = $"{book.FileName}:{round}", BookFileName = book.FileName, BookId = book.Id, Round = round,
            Fen = "8/8/8/4k3/8/8/4K3/8 w - - 0 1", Moves = infoOnly ? "" : "e2e3", StartPly = -1,
            IsInfoOnly = infoOnly,
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    [Fact]
    public async Task CalculationBook_CountsAllPositions_AndTreesAsDone()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id, isCalculation: true);
        var a = await SeedLineAsync(book, "1", infoOnly: true);    // reine Stellung
        await SeedLineAsync(book, "2", infoOnly: true);
        await SeedLineAsync(book, "3", infoOnly: false);           // Linie mit Zügen
        await _calc.SaveTreeAsync(user.Id, a.Id, new SaveCalcTreeDto { TreeJson = "{\"v\":1}" }, isAdmin: false);

        var item = (await _courses.GetCoursesAsync(user.Id, isAdmin: false)).Single();

        Assert.True(item.IsCalculation);
        Assert.Equal(3, item.PuzzleCount);      // ALLE Stellungen, auch die zug-losen
        Assert.Equal(1, item.SolvedCount);      // eine bearbeitet
        Assert.Equal(33, item.ProgressPercent);
    }

    [Fact]
    public async Task NormalBook_KeepsClassicCounting()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id, isCalculation: false);
        await SeedLineAsync(book, "1", infoOnly: true);            // Info-Linie zählt nicht
        var quiz = await SeedLineAsync(book, "2", infoOnly: false);
        _db.CoursePuzzleResults.Add(new CoursePuzzleResult
        {
            UserId = user.Id, BookId = book.Id, BookPuzzleId = quiz.Id, SolvedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var item = (await _courses.GetCoursesAsync(user.Id, isAdmin: false)).Single();

        Assert.False(item.IsCalculation);
        Assert.Equal(1, item.PuzzleCount);
        Assert.Equal(1, item.SolvedCount);
    }

    [Fact]
    public async Task CalculationBook_TreesOfOtherUsers_DoNotCount()
    {
        var mine = await CreateUserAsync("mine");
        var other = await CreateUserAsync("other");
        var book = await SeedBookAsync(mine.Id, isCalculation: true);
        var pos = await SeedLineAsync(book, "1", infoOnly: true);
        await _calc.SaveTreeAsync(other.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "{\"v\":1}" }, isAdmin: true);

        var item = (await _courses.GetCoursesAsync(mine.Id, isAdmin: false)).Single();
        Assert.Equal(0, item.SolvedCount);
    }

    [Fact]
    public async Task AdminUpdate_TogglesCalculationFlag()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id, isCalculation: false);

        var on = await _bookAdmin.UpdateBookAsync(book.Id, new UpdateBookDto { IsCalculation = true });
        Assert.True(on.IsCalculation);
        Assert.True((await _db.Books.FindAsync(book.Id))!.IsCalculation);

        // null = unverändert lassen (wie die anderen Flags).
        var untouched = await _bookAdmin.UpdateBookAsync(book.Id, new UpdateBookDto { DisplayName = "Neu" });
        Assert.True(untouched.IsCalculation);

        var off = await _bookAdmin.UpdateBookAsync(book.Id, new UpdateBookDto { IsCalculation = false });
        Assert.False(off.IsCalculation);
    }

    [Fact]
    public async Task AdminBookList_ExposesCalculationFlag()
    {
        var user = await CreateUserAsync();
        await SeedBookAsync(user.Id, isCalculation: true);
        var list = await _bookAdmin.GetBooksAsync();
        Assert.True(list.Single().IsCalculation);
    }

    [Fact]
    public async Task DeleteBook_WithSavedCalculationTrees_Works()
    {
        // CalculationTree hängt per Restrict-FK am BookPuzzle → ohne explizites Aufräumen
        // scheitert das Buch-Löschen am DB-Constraint.
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id, isCalculation: true);
        var pos = await SeedLineAsync(book, "1", infoOnly: true);
        await _calc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "{\"v\":1}" }, isAdmin: false);

        await _bookAdmin.DeleteBookAsync(book.Id);

        Assert.Empty(_db.Books);
        Assert.Empty(_db.BookPuzzles);
        Assert.Empty(_db.CalculationTrees);
    }
}
