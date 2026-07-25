using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Zugriffsregel der OFFENEN Buch-Puzzle-Endpoints (<see cref="BookAccess"/>): anonym nur öffentliche
/// bzw. in einen offenen Pool gestellte Bücher; eingeloggt zusätzlich eigene/geteilte/gruppen-freigegebene.
/// Vorher waren ALLE Bücher anonym vollständig auslesbar (`/books` + `?bookId=` + `{id}/next`).
/// </summary>
public class BookAccessTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly BookPuzzleService _service;

    public BookAccessTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue());
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> CreateBookAsync(string fileName, int? ownerUserId = null,
        bool isPublic = false, bool forRandom = false, bool forDaily = false)
    {
        var book = new Book
        {
            FileName = fileName,
            DisplayName = fileName,
            OwnerUserId = ownerUserId,
            IsPublic = isPublic,
            ForRandom = forRandom,
            ForDaily = forDaily,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    private async Task<BookPuzzle> AddPuzzleAsync(Book book, string lineId, string round = "001")
    {
        var puzzle = new BookPuzzle
        {
            LineId = lineId,
            BookFileName = book.FileName,
            BookId = book.Id,
            Round = round,
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
            Moves = "e7e5 d2d4",
        };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        return puzzle;
    }

    private async Task<AppUser> CreateUserAsync(string username)
    {
        var user = new AppUser { Username = username, PasswordHash = "x" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    // ---- CanReadAsync ---------------------------------------------------------------------------

    [Fact]
    public async Task CanReadAsync_PrivateBook_Anonymous_ReturnsFalse()
    {
        var book = await CreateBookAsync("private.pgn", ownerUserId: 42);
        Assert.False(await BookAccess.CanReadAsync(_db, book.Id, userId: null, isAdmin: false));
    }

    [Fact]
    public async Task CanReadAsync_PublicBook_Anonymous_ReturnsTrue()
    {
        var book = await CreateBookAsync("public.pgn", isPublic: true);
        Assert.True(await BookAccess.CanReadAsync(_db, book.Id, userId: null, isAdmin: false));
    }

    [Fact]
    public async Task CanReadAsync_PoolBook_Anonymous_ReturnsTrue()
    {
        // ForRandom/ForDaily/ForBlind werden ohnehin anonym ausgewürfelt → bewusst offen.
        var random = await CreateBookAsync("pool.pgn", forRandom: true);
        var daily = await CreateBookAsync("daily.pgn", forDaily: true);
        Assert.True(await BookAccess.CanReadAsync(_db, random.Id, userId: null, isAdmin: false));
        Assert.True(await BookAccess.CanReadAsync(_db, daily.Id, userId: null, isAdmin: false));
    }

    [Fact]
    public async Task CanReadAsync_ForeignPrivateBook_LoggedIn_ReturnsFalse()
    {
        var owner = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var book = await CreateBookAsync("owned.pgn", ownerUserId: owner.Id);
        Assert.False(await BookAccess.CanReadAsync(_db, book.Id, other.Id, isAdmin: false));
        Assert.True(await BookAccess.CanReadAsync(_db, book.Id, owner.Id, isAdmin: false));
    }

    [Fact]
    public async Task CanReadAsync_SharedBook_RecipientCanRead()
    {
        var owner = await CreateUserAsync("owner");
        var recipient = await CreateUserAsync("recipient");
        var book = await CreateBookAsync("shared.pgn", ownerUserId: owner.Id);
        _db.CourseShares.Add(new CourseShare
        {
            BookId = book.Id, OwnerId = owner.Id, RecipientId = recipient.Id, SharedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        Assert.True(await BookAccess.CanReadAsync(_db, book.Id, recipient.Id, isAdmin: false));
    }

    [Fact]
    public async Task CanReadAsync_GroupAccess_MemberCanRead_NonMemberCannot()
    {
        var member = await CreateUserAsync("member");
        var stranger = await CreateUserAsync("stranger");
        var group = new Group { Name = "Trainingsgruppe" };
        _db.Groups.Add(group);
        await _db.SaveChangesAsync();
        _db.UserGroups.Add(new UserGroup { UserId = member.Id, GroupId = group.Id });
        var book = await CreateBookAsync("group.pgn");
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = book.Id, GroupId = group.Id });
        await _db.SaveChangesAsync();

        Assert.True(await BookAccess.CanReadAsync(_db, book.Id, member.Id, isAdmin: false));
        Assert.False(await BookAccess.CanReadAsync(_db, book.Id, stranger.Id, isAdmin: false));
    }

    [Fact]
    public async Task CanReadAsync_EveryoneGroupAccess_AnyLoggedInUserCanRead()
    {
        var user = await CreateUserAsync("someone");
        var everyone = new Group { Name = "Everyone", IsEveryone = true };
        _db.Groups.Add(everyone);
        await _db.SaveChangesAsync();
        var book = await CreateBookAsync("everyone.pgn");
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = book.Id, GroupId = everyone.Id });
        await _db.SaveChangesAsync();

        Assert.True(await BookAccess.CanReadAsync(_db, book.Id, user.Id, isAdmin: false));
        // Anonym bleibt es zu: „Everyone" meint alle REGISTRIERTEN Nutzer.
        Assert.False(await BookAccess.CanReadAsync(_db, book.Id, userId: null, isAdmin: false));
    }

    [Fact]
    public async Task CanReadAsync_Admin_SeesEverything()
    {
        var book = await CreateBookAsync("private.pgn", ownerUserId: 42);
        Assert.True(await BookAccess.CanReadAsync(_db, book.Id, userId: 7, isAdmin: true));
    }

    [Fact]
    public async Task CanReadAsync_UnknownBookId_ReturnsFalse()
    {
        Assert.False(await BookAccess.CanReadAsync(_db, 12345, userId: null, isAdmin: true));
    }

    [Fact]
    public async Task CanReadPuzzleAsync_LegacyPuzzleWithoutBookRow_IsUngated()
    {
        // Altbestand: BookPuzzle ohne BookId und ohne Book-Zeile → nicht gate-bar (dort hängt keine Freigabe).
        var puzzle = new BookPuzzle
        {
            LineId = "legacy.pgn:001", BookFileName = "legacy.pgn", Round = "001",
            Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1b1",
        };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();

        Assert.True(await BookAccess.CanReadPuzzleAsync(_db, puzzle, userId: null, isAdmin: false));
    }

    // ---- Endpoint-Verhalten --------------------------------------------------------------------

    [Fact]
    public async Task GetRandomAsync_WithBookIdOfPrivateBook_Anonymous_Throws()
    {
        var book = await CreateBookAsync("private.pgn", ownerUserId: 42);
        await AddPuzzleAsync(book, "private.pgn:001");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.GetRandomAsync("random", null, book.Id, userId: null, isAdmin: false));
    }

    [Fact]
    public async Task GetRandomAsync_WithBookIdOfPoolBook_Anonymous_Works()
    {
        // Der schach-bot (/kurs) zieht anonym per bookId aus dem Katalog — muss weiter funktionieren.
        var book = await CreateBookAsync("pool.pgn", forRandom: true);
        var puzzle = await AddPuzzleAsync(book, "pool.pgn:001");

        var dto = await _service.GetRandomAsync("random", null, book.Id, userId: null, isAdmin: false);
        Assert.Equal(puzzle.Id, dto.Id);
    }

    [Fact]
    public async Task GetRandomAsync_WithBookIdOfOwnBook_OwnerWorks()
    {
        var owner = await CreateUserAsync("owner");
        var book = await CreateBookAsync("owned.pgn", ownerUserId: owner.Id);
        var puzzle = await AddPuzzleAsync(book, "owned.pgn:001");

        var dto = await _service.GetRandomAsync("random", null, book.Id, owner.Id, isAdmin: false);
        Assert.Equal(puzzle.Id, dto.Id);
    }

    [Fact]
    public async Task GetRandomAsync_PoolDraw_WithoutBookId_StillAnonymous()
    {
        var book = await CreateBookAsync("pool.pgn", forRandom: true);
        var puzzle = await AddPuzzleAsync(book, "pool.pgn:001");

        var dto = await _service.GetRandomAsync("random", null, null, userId: null, isAdmin: false);
        Assert.Equal(puzzle.Id, dto.Id);
    }

    [Fact]
    public async Task GetNextInBookAsync_PrivateBook_Anonymous_Throws()
    {
        var book = await CreateBookAsync("private.pgn", ownerUserId: 42);
        var first = await AddPuzzleAsync(book, "private.pgn:001", "001");
        await AddPuzzleAsync(book, "private.pgn:002", "002");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.GetNextInBookAsync(first.Id, userId: null, isAdmin: false));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.GetRandomInBookAsync(first.Id, userId: null, isAdmin: false));
    }

    [Fact]
    public async Task GetNextInBookAsync_PublicBook_Anonymous_Works()
    {
        var book = await CreateBookAsync("public.pgn", isPublic: true);
        var first = await AddPuzzleAsync(book, "public.pgn:001", "001");
        var second = await AddPuzzleAsync(book, "public.pgn:002", "002");

        var dto = await _service.GetNextInBookAsync(first.Id, userId: null, isAdmin: false);
        Assert.Equal(second.Id, dto.Id);
    }

    [Fact]
    public async Task GetBooksAsync_Anonymous_ListsOnlyExposedBooks()
    {
        var pub = await CreateBookAsync("public.pgn", isPublic: true);
        var pool = await CreateBookAsync("pool.pgn", forRandom: true);
        var priv = await CreateBookAsync("private.pgn", ownerUserId: 42);
        await AddPuzzleAsync(pub, "public.pgn:001");
        await AddPuzzleAsync(pool, "pool.pgn:001");
        await AddPuzzleAsync(priv, "private.pgn:001");

        var books = await _service.GetBooksAsync(userId: null, isAdmin: false);
        var names = books.Select(b => b.BookFileName).ToList();

        Assert.Contains("public.pgn", names);
        Assert.Contains("pool.pgn", names);
        Assert.DoesNotContain("private.pgn", names);
    }

    [Fact]
    public async Task GetBooksAsync_Owner_SeesOwnBook()
    {
        var owner = await CreateUserAsync("owner");
        var priv = await CreateBookAsync("private.pgn", ownerUserId: owner.Id);
        await AddPuzzleAsync(priv, "private.pgn:001");

        var books = await _service.GetBooksAsync(owner.Id, isAdmin: false);
        Assert.Contains("private.pgn", books.Select(b => b.BookFileName));
    }

    [Fact]
    public async Task GetBooksAsync_Admin_SeesAll()
    {
        var priv = await CreateBookAsync("private.pgn", ownerUserId: 42);
        await AddPuzzleAsync(priv, "private.pgn:001");

        var books = await _service.GetBooksAsync(userId: 7, isAdmin: true);
        Assert.Contains("private.pgn", books.Select(b => b.BookFileName));
    }

    [Fact]
    public async Task GetBooksAsync_LegacyPuzzleWithoutBookRow_StaysListed()
    {
        _db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = "legacy.pgn:001", BookFileName = "legacy.pgn", Round = "001",
            Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1b1",
        });
        await _db.SaveChangesAsync();

        var books = await _service.GetBooksAsync(userId: null, isAdmin: false);
        Assert.Contains("legacy.pgn", books.Select(b => b.BookFileName));
    }
}
