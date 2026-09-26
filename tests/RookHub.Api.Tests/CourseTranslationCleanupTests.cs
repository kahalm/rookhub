using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Jeder Pfad, der <c>BookPuzzles</c> loescht, raeumt deren Kurs-Uebersetzungen (Saetze + Texte) mit ab — und
/// ein geloeschtes Buch seine Uebersetzungsauftraege. InMemory kaskadiert nicht; ohne das blieben hier Waisen.
/// </summary>
public class CourseTranslationCleanupTests : IDisposable
{
    private readonly AppDbContext _db;

    public CourseTranslationCleanupTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(AppUser Owner, Book Book, BookPuzzle A, BookPuzzle B, BookPuzzle C)> SeedAsync()
    {
        var owner = new AppUser { Username = "owner", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(owner);
        await _db.SaveChangesAsync();
        var book = new Book
        {
            FileName = $"b-{Guid.NewGuid():N}.pgn", DisplayName = "Kurs", OwnerUserId = owner.Id, CommentLanguage = "en",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource(),
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        BookPuzzle Line(string round, string chapter) => new()
        {
            LineId = $"{book.FileName}:{round}", BookFileName = book.FileName, BookId = book.Id, Round = round,
            Fen = "8/8/8/4k3/8/8/4K3/8 w - - 0 1", Moves = "e2e3", Chapter = chapter, Comment = "Kommentar " + round,
        };
        var a = Line("001", "Eins");
        var b = Line("002", "Eins");
        var c = Line("003", "Zwei");
        _db.BookPuzzles.AddRange(a, b, c);
        await _db.SaveChangesAsync();
        foreach (var line in new[] { a, b, c })
            foreach (var lang in new[] { "de", "fr" })
            {
                var set = new CommentSet { BookPuzzleId = line.Id, Language = lang, Origin = CommentOrigin.Machine };
                set.Texts.Add(new CommentText { Ply = CourseTextSlots.Comment, Text = "x", SourceHash = CourseTextHash.Of(line.Comment!) });
                set.Texts.Add(new CommentText { Ply = CourseTextSlots.Chapter, Text = "y", SourceHash = CourseTextHash.Of(line.Chapter!) });
                _db.CommentSets.Add(set);
            }
        _db.CourseTranslationJobs.Add(new CourseTranslationJob { BookId = book.Id, Language = "de" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (owner, book, a, b, c);
    }

    private Task<List<int>> LinesWithSetsAsync()
        => _db.CommentSets.Where(s => s.BookPuzzleId != null).Select(s => s.BookPuzzleId!.Value).Distinct().ToListAsync();

    [Fact]
    public async Task DeleteLine_RaeumtNurDieSaetzeDieserLinieAb()
    {
        var (owner, book, a, b, c) = await SeedAsync();

        await new CourseAuthoringService(_db).DeleteLineAsync(owner.Id, book.Id, a.Id, isAdmin: false);

        Assert.Equal(new[] { b.Id, c.Id }.OrderBy(i => i), (await LinesWithSetsAsync()).OrderBy(i => i));
        Assert.Equal(8, await _db.CommentTexts.CountAsync());   // 2 Linien × 2 Sprachen × 2 Texte
        Assert.Equal(1, await _db.CourseTranslationJobs.CountAsync());
    }

    [Fact]
    public async Task DeleteChapter_RaeumtDieSaetzeAllerSeinerLinienAb()
    {
        var (owner, book, _, _, c) = await SeedAsync();

        await new CourseAuthoringService(_db).DeleteChapterAsync(owner.Id, book.Id, "Eins", isAdmin: false);

        Assert.Equal(new[] { c.Id }, await LinesWithSetsAsync());
        Assert.Equal(4, await _db.CommentTexts.CountAsync());
    }

    [Fact]
    public async Task DeleteBook_RaeumtSaetzeTexteUndAuftraegeAb()
    {
        var (_, book, _, _, _) = await SeedAsync();
        // Eine Partie-Uebersetzung daneben darf nicht mitgehen.
        var game = new LibraryGame { Pgn = "x" };
        _db.LibraryGames.Add(game);
        await _db.SaveChangesAsync();
        _db.CommentSets.Add(new CommentSet
        {
            LibraryGameId = game.Id, Language = "de", Texts = [new CommentText { Ply = 0, Text = "Partie" }],
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await new BookAdminService(_db).DeleteBookAsync(book.Id);

        Assert.Empty(await LinesWithSetsAsync());
        Assert.Equal(1, await _db.CommentSets.CountAsync());
        Assert.Equal("Partie", (await _db.CommentTexts.SingleAsync()).Text);
        Assert.Empty(await _db.CourseTranslationJobs.ToListAsync());
    }

    [Fact]
    public async Task DeletePersonalCourse_GehtUeberDeleteBook_RaeumtAb()
    {
        var (owner, book, _, _, _) = await SeedAsync();

        await TestServices.Course(_db).DeletePersonalCourseAsync(owner.Id, book.Id);

        Assert.Empty(await _db.CommentSets.ToListAsync());
        Assert.Empty(await _db.CommentTexts.ToListAsync());
        Assert.Empty(await _db.CourseTranslationJobs.ToListAsync());
    }
}
