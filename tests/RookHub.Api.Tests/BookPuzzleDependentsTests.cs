using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// <see cref="BookPuzzleDependents"/>: EINE Liste dessen, was an Linien hängt, für „Linie/Kapitel löschen" und
/// „Buch löschen" (Codereview A9-007). Vorher führte jeder Pfad seine eigene Handliste, und kein Test band sie an
/// das FK-Modell — InMemory prüft keine Fremdschlüssel, eine vergessene Restrict-Tabelle fiele erst auf MariaDB
/// als 500 auf.
/// </summary>
public class BookPuzzleDependentsTests : IDisposable
{
    private readonly AppDbContext _db;

    public BookPuzzleDependentsTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void RestrictDependents_MatchTheForeignKeyModel()
    {
        // Jede Tabelle, deren Fremdschlüssel auf BookPuzzle das Löschen der Linie in der Datenbank NICHT selbst
        // erledigt (alles außer Cascade/SetNull), muss der gemeinsame Abräumer kennen — sonst scheitert
        // „Linie löschen" bzw. „Kurs löschen" auf MariaDB mit einem FK-Fehler.
        var fromModel = _db.Model.FindEntityType(typeof(BookPuzzle))!
            .GetReferencingForeignKeys()
            .Where(fk => fk.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.SetNull))
            .Select(fk => fk.DeclaringEntityType.ClrType)
            .Distinct()
            .OrderBy(t => t.Name)
            .ToList();

        Assert.NotEmpty(fromModel);
        Assert.Equal(fromModel, BookPuzzleDependents.RestrictDependents.OrderBy(t => t.Name).ToList());
    }

    private async Task<(Book Book, BookPuzzle Doomed, BookPuzzle Keep, int UserId)> SeedAsync()
    {
        var user = new AppUser { Username = "u", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(user);
        var book = new Book
        {
            FileName = "b.pgn", DisplayName = "B", OwnerUserId = null,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource(),
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        BookPuzzle Line(string round) => new()
        {
            LineId = $"b.pgn:{round}", BookFileName = book.FileName, BookId = book.Id, Round = round,
            Fen = "8/8/8/4k3/8/8/4K3/8 w - - 0 1", Moves = "e2e3",
        };
        var doomed = Line("1");
        var keep = Line("2");
        _db.BookPuzzles.AddRange(doomed, keep);
        await _db.SaveChangesAsync();

        var day = new DateOnly(2026, 1, 1);
        foreach (var line in new[] { doomed, keep })
        {
            _db.CoursePuzzleResults.Add(new CoursePuzzleResult { UserId = user.Id, BookId = book.Id, BookPuzzleId = line.Id });
            _db.CourseAttempts.Add(new CourseAttempt { UserId = user.Id, BookId = book.Id, BookPuzzleId = line.Id });
            _db.CourseInfoViews.Add(new CourseInfoView { UserId = user.Id, BookId = book.Id, BookPuzzleId = line.Id });
            _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt { UserId = user.Id, BookPuzzleId = line.Id, AttemptedAt = DateTime.UtcNow });
            _db.DailyPuzzles.Add(new DailyPuzzle { Date = day, BookPuzzleId = line.Id, CreatedAt = DateTime.UtcNow });
            day = day.AddDays(1);
            _db.CalculationTrees.Add(new CalculationTree { UserId = user.Id, BookId = book.Id, BookPuzzleId = line.Id });
            _db.CourseFlashcardMarks.Add(new CourseFlashcardMark { UserId = user.Id, BookId = book.Id, BookPuzzleId = line.Id });
            _db.SharedPuzzleAttempts.Add(new SharedPuzzleAttempt { BookPuzzleId = line.Id, IdentityKey = "anon" });
            _db.FavoritePuzzles.Add(new FavoritePuzzle { UserId = user.Id, PuzzleId = line.Id, Source = PuzzleSource.Book });
        }
        // Ein Standard-Puzzle mit derselben Zahl als Id ist etwas anderes und bleibt (bei einem anderen Konto,
        // damit der Kachel-/Listen-Vergleich unten nur die Buch-Favoriten sieht).
        var other = new AppUser { Username = "o", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(other);
        await _db.SaveChangesAsync();
        _db.FavoritePuzzles.Add(new FavoritePuzzle { UserId = other.Id, PuzzleId = doomed.Id, Source = PuzzleSource.Standard });
        await _db.SaveChangesAsync();
        return (book, doomed, keep, user.Id);
    }

    /// <summary>Wie viele Zeilen hängen in den Restrict-Tabellen, den „Track solves" und den Buch-Favoriten
    /// noch an dieser Linie?</summary>
    private async Task<int> DependentsOfAsync(int lineId) =>
        await _db.CoursePuzzleResults.CountAsync(x => x.BookPuzzleId == lineId)
        + await _db.CourseAttempts.CountAsync(x => x.BookPuzzleId == lineId)
        + await _db.CourseInfoViews.CountAsync(x => x.BookPuzzleId == lineId)
        + await _db.BookPuzzleAttempts.CountAsync(x => x.BookPuzzleId == lineId)
        + await _db.DailyPuzzles.CountAsync(x => x.BookPuzzleId == lineId)
        + await _db.CalculationTrees.CountAsync(x => x.BookPuzzleId == lineId)
        + await _db.CourseFlashcardMarks.CountAsync(x => x.BookPuzzleId == lineId)
        + await _db.SharedPuzzleAttempts.CountAsync(x => x.BookPuzzleId == lineId)
        + await _db.FavoritePuzzles.CountAsync(x => x.PuzzleId == lineId && x.Source == PuzzleSource.Book);

    [Fact]
    public async Task DeleteLine_ThenDeleteBook_LeaveNothingOnTheLines_AndFavoriteCountMatchesTheList()
    {
        var (book, doomed, keep, userId) = await SeedAsync();
        const int perLine = 9;
        Assert.Equal(perLine, await DependentsOfAsync(doomed.Id));

        await new CourseAuthoringService(_db).DeleteLineAsync(0, book.Id, doomed.Id, isAdmin: true);

        Assert.Equal(0, await DependentsOfAsync(doomed.Id));
        Assert.Equal(perLine, await DependentsOfAsync(keep.Id));
        Assert.True(await _db.FavoritePuzzles.AnyAsync(f => f.PuzzleId == doomed.Id && f.Source == PuzzleSource.Standard));
        // Die Favoriten-Kachel zählte den Favoriten der gelöschten Linie weiter, die Liste ließ ihn aus.
        var favorites = new FavoriteService(_db);
        Assert.Equal((await favorites.ListAsync(userId)).Count, await favorites.CountAsync(userId));

        await new BookAdminService(_db).DeleteBookAsync(book.Id);

        Assert.Equal(0, await DependentsOfAsync(keep.Id));
        Assert.False(await _db.BookPuzzles.AnyAsync());
    }
}
