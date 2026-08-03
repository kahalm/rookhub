using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Persistente Flashcard-Markierungen: Kurs-Linien (BookPuzzleId) und Repertoire-Linien (LineKey)
/// je User — idempotentes Setzen/Entfernen, Zugriffsprüfung (Kurs 404-Semantik, Repertoire
/// Besitzer/Freigabe), Linien-Zugehörigkeit, Aufräumen beim Linien-Löschen.
/// </summary>
public class FlashcardMarkServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FlashcardMarkService _svc;

    public FlashcardMarkServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new FlashcardMarkService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> CreateUserAsync(string name = "u1")
    {
        var user = new AppUser { Username = name, PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<(Book Book, BookPuzzle Line)> SeedBookAsync(int ownerId)
    {
        var book = new Book { FileName = $"b-{Guid.NewGuid():N}.pgn", DisplayName = "Kurs",
            OwnerUserId = ownerId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var bp = new BookPuzzle { LineId = $"{book.FileName}:1", BookFileName = book.FileName,
            BookId = book.Id, Round = "1", Fen = "8/8/8/4k3/8/8/4K3/8 w - - 0 1", Moves = "e2e3", StartPly = -1 };
        _db.BookPuzzles.Add(bp);
        await _db.SaveChangesAsync();
        return (book, bp);
    }

    [Fact]
    public async Task CourseMark_ToggleIsIdempotent_AndListed()
    {
        var u = await CreateUserAsync();
        var (book, line) = await SeedBookAsync(u.Id);

        Assert.True(await _svc.SetCourseMarkAsync(u.Id, book.Id, line.Id, true, isAdmin: false));
        Assert.True(await _svc.SetCourseMarkAsync(u.Id, book.Id, line.Id, true, isAdmin: false));  // idempotent
        Assert.Equal(new[] { line.Id }, await _svc.GetCourseMarksAsync(u.Id, book.Id, false));

        Assert.False(await _svc.SetCourseMarkAsync(u.Id, book.Id, line.Id, false, isAdmin: false));
        Assert.Empty((await _svc.GetCourseMarksAsync(u.Id, book.Id, false))!);
    }

    [Fact]
    public async Task CourseMark_NoAccessOrForeignLine_ReturnsNull()
    {
        var u = await CreateUserAsync();
        var stranger = await CreateUserAsync("fremd");
        var (book, line) = await SeedBookAsync(stranger.Id);

        Assert.Null(await _svc.GetCourseMarksAsync(u.Id, book.Id, false));               // kein Zugriff
        Assert.Null(await _svc.SetCourseMarkAsync(u.Id, book.Id, line.Id, true, false));

        // Zugreifbarer Kurs, aber Linie aus ANDEREM Buch → null (kein Cross-Book-Markieren).
        var (own, _) = await SeedBookAsync(u.Id);
        Assert.Null(await _svc.SetCourseMarkAsync(u.Id, own.Id, line.Id, true, false));
    }

    [Fact]
    public async Task CourseMark_RemovedWhenLineIsDeleted()
    {
        var u = await CreateUserAsync();
        var (book, line) = await SeedBookAsync(u.Id);
        await _svc.SetCourseMarkAsync(u.Id, book.Id, line.Id, true, isAdmin: false);

        // Linien-Löschpfad der Detailseite muss die Markierung mit abräumen (Restrict-FK).
        var authoring = new CourseAuthoringService(_db);
        await authoring.DeleteLineAsync(u.Id, book.Id, line.Id, isAdmin: false);

        Assert.Empty(_db.CourseFlashcardMarks);
    }

    [Fact]
    public async Task RepertoireMark_OwnerAndRecipient_MayMark_StrangerMayNot()
    {
        var owner = await CreateUserAsync("owner");
        var guest = await CreateUserAsync("guest");
        var stranger = await CreateUserAsync("stranger");
        var rep = new Repertoire { UserId = owner.Id, Name = "R", Kind = RepertoireKind.Opening,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        _db.RepertoireShares.Add(new RepertoireShare
        {
            RepertoireId = rep.Id, OwnerId = owner.Id, RecipientId = guest.Id, SharedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        Assert.True(await _svc.SetRepertoireMarkAsync(owner.Id, rep.Id, "labc", true));
        Assert.True(await _svc.SetRepertoireMarkAsync(guest.Id, rep.Id, "labc", true));   // eigener Satz
        Assert.Null(await _svc.SetRepertoireMarkAsync(stranger.Id, rep.Id, "labc", true));

        Assert.Equal(new[] { "labc" }, await _svc.GetRepertoireMarksAsync(owner.Id, rep.Id));
        Assert.Equal(new[] { "labc" }, await _svc.GetRepertoireMarksAsync(guest.Id, rep.Id));
        Assert.Null(await _svc.GetRepertoireMarksAsync(stranger.Id, rep.Id));

        Assert.False(await _svc.SetRepertoireMarkAsync(guest.Id, rep.Id, "labc", false));
        Assert.Empty((await _svc.GetRepertoireMarksAsync(guest.Id, rep.Id))!);
        Assert.Single((await _svc.GetRepertoireMarksAsync(owner.Id, rep.Id))!);           // unabhängig
    }

    [Fact]
    public async Task RepertoireMark_RejectsEmptyOrOversizedKey()
    {
        var u = await CreateUserAsync();
        var rep = new Repertoire { UserId = u.Id, Name = "R", Kind = RepertoireKind.Opening,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();

        Assert.Null(await _svc.SetRepertoireMarkAsync(u.Id, rep.Id, "", true));
        Assert.Null(await _svc.SetRepertoireMarkAsync(u.Id, rep.Id, new string('x', 121), true));
    }
}
