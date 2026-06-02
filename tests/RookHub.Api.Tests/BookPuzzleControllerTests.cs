using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

public class BookPuzzleControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly BookPuzzleController _controller;

    public BookPuzzleControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _controller = new BookPuzzleController(_db);
        SetUser(99);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId, string role = "Admin")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role)
        };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private async Task<BookPuzzle> CreateBookPuzzleAsync(
        string lineId = "testbook.pgn:001",
        string bookFileName = "testbook.pgn",
        string round = "001",
        string fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
        string moves = "e7e5 d2d4")
    {
        var puzzle = new BookPuzzle
        {
            LineId = lineId,
            BookFileName = bookFileName,
            Round = round,
            Fen = fen,
            Moves = moves,
            Title = "Test Title",
            Chapter = "Test Chapter",
            Comment = "Test comment",
            Difficulty = "Anfaenger",
            BookRating = 3,
            Tags = "Taktik Mattsetzen"
        };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        return puzzle;
    }

    [Fact]
    public async Task GetNextInBook_ReturnsNextInSameBook_WrapsAtEnd()
    {
        var p1 = await CreateBookPuzzleAsync(lineId: "b.pgn:1", bookFileName: "b.pgn", round: "1");
        var p2 = await CreateBookPuzzleAsync(lineId: "b.pgn:2", bookFileName: "b.pgn", round: "2");
        var p3 = await CreateBookPuzzleAsync(lineId: "b.pgn:3", bookFileName: "b.pgn", round: "3");
        await CreateBookPuzzleAsync(lineId: "other.pgn:1", bookFileName: "other.pgn", round: "1"); // anderes Buch

        var next = Assert.IsType<BookPuzzleDto>(((OkObjectResult)await _controller.GetNextInBook(p1.Id)).Value);
        Assert.Equal(p2.Id, next.Id);

        var wrap = Assert.IsType<BookPuzzleDto>(((OkObjectResult)await _controller.GetNextInBook(p3.Id)).Value);
        Assert.Equal(p1.Id, wrap.Id);   // am Ende → erstes Puzzle des Buchs
    }

    [Fact]
    public async Task GetRandomInBook_ReturnsOtherPuzzleFromSameBook()
    {
        var p1 = await CreateBookPuzzleAsync(lineId: "b.pgn:1", bookFileName: "b.pgn");
        var p2 = await CreateBookPuzzleAsync(lineId: "b.pgn:2", bookFileName: "b.pgn");
        await CreateBookPuzzleAsync(lineId: "other.pgn:1", bookFileName: "other.pgn"); // darf nicht kommen

        var r = Assert.IsType<BookPuzzleDto>(((OkObjectResult)await _controller.GetRandomInBook(p1.Id)).Value);
        Assert.Equal(p2.Id, r.Id);   // einziges anderes im selben Buch
    }

    [Fact]
    public async Task GetRandomInBook_SinglePuzzle_ReturnsItself()
    {
        var p1 = await CreateBookPuzzleAsync(lineId: "solo.pgn:1", bookFileName: "solo.pgn");
        var r = Assert.IsType<BookPuzzleDto>(((OkObjectResult)await _controller.GetRandomInBook(p1.Id)).Value);
        Assert.Equal(p1.Id, r.Id);
    }

    [Fact]
    public async Task GetNextInBook_NotFound_WhenMissing()
        => Assert.IsType<NotFoundObjectResult>(await _controller.GetNextInBook(99999));

    [Fact]
    public async Task GetById_ReturnsPuzzle()
    {
        var puzzle = await CreateBookPuzzleAsync();

        var result = await _controller.GetById(puzzle.Id) as OkObjectResult;

        Assert.NotNull(result);
        var dto = Assert.IsType<BookPuzzleDto>(result.Value);
        Assert.Equal(puzzle.LineId, dto.LineId);
        Assert.Equal(puzzle.Fen, dto.Fen);
        Assert.Equal(puzzle.Moves, dto.Moves);
        Assert.Equal(puzzle.Title, dto.Title);
        Assert.Equal(puzzle.Chapter, dto.Chapter);
        Assert.Equal(puzzle.Difficulty, dto.Difficulty);
        Assert.Equal(puzzle.BookRating, dto.BookRating);
        Assert.Equal(puzzle.Tags, dto.Tags);
    }

    [Fact]
    public async Task GetById_NotFound()
    {
        var result = await _controller.GetById(9999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetByLineId_ReturnsPuzzleId()
    {
        var puzzle = await CreateBookPuzzleAsync();

        var result = await _controller.GetByLineId(puzzle.LineId) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var id = (int)data.GetType().GetProperty("id")!.GetValue(data)!;
        Assert.Equal(puzzle.Id, id);
    }

    [Fact]
    public async Task GetByLineId_NotFound()
    {
        var result = await _controller.GetByLineId("nonexistent:001");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetByLineId_EmptyLineId_ReturnsBadRequest()
    {
        var result = await _controller.GetByLineId("");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetBooks_ReturnsGroupedBooks()
    {
        await CreateBookPuzzleAsync("book1.pgn:001", "book1.pgn", "001");
        await CreateBookPuzzleAsync("book1.pgn:002", "book1.pgn", "002");
        await CreateBookPuzzleAsync("book2.pgn:001", "book2.pgn", "001");

        var result = await _controller.GetBooks() as OkObjectResult;

        Assert.NotNull(result);
        var books = Assert.IsType<List<BookInfoDto>>(result.Value);
        Assert.Equal(2, books.Count);

        var book1 = books.First(b => b.BookFileName == "book1.pgn");
        Assert.Equal(2, book1.PuzzleCount);

        var book2 = books.First(b => b.BookFileName == "book2.pgn");
        Assert.Equal(1, book2.PuzzleCount);
    }

    [Fact]
    public async Task GetBooks_EmptyDb_ReturnsEmptyList()
    {
        var result = await _controller.GetBooks() as OkObjectResult;

        Assert.NotNull(result);
        var books = Assert.IsType<List<BookInfoDto>>(result.Value);
        Assert.Empty(books);
    }

    [Fact]
    public async Task Import_CreatesNewPuzzles()
    {
        var importData = new List<BookPuzzleImportDto>
        {
            new()
            {
                LineId = "import1.pgn:001",
                BookFileName = "import1.pgn",
                Round = "001",
                Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
                Moves = "e7e5 d2d4",
                Title = "Puzzle 1",
                Difficulty = "Anfaenger",
                BookRating = 3,
                Tags = "Taktik"
            },
            new()
            {
                LineId = "import1.pgn:002",
                BookFileName = "import1.pgn",
                Round = "002",
                Fen = "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR b KQkq - 0 1",
                Moves = "d7d5 c2c4",
                Title = "Puzzle 2"
            }
        };

        var result = await _controller.Import(importData) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var imported = (int)data.GetType().GetProperty("imported")!.GetValue(data)!;
        var skipped = (int)data.GetType().GetProperty("skipped")!.GetValue(data)!;
        Assert.Equal(2, imported);
        Assert.Equal(0, skipped);

        Assert.Equal(2, await _db.BookPuzzles.CountAsync());
    }

    [Fact]
    public async Task Import_SkipsDuplicates()
    {
        await CreateBookPuzzleAsync("existing.pgn:001", "existing.pgn", "001");

        var importData = new List<BookPuzzleImportDto>
        {
            new()
            {
                LineId = "existing.pgn:001",
                BookFileName = "existing.pgn",
                Round = "001",
                Fen = "some fen",
                Moves = "e2e4"
            },
            new()
            {
                LineId = "existing.pgn:002",
                BookFileName = "existing.pgn",
                Round = "002",
                Fen = "other fen",
                Moves = "d2d4"
            }
        };

        var result = await _controller.Import(importData) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var imported = (int)data.GetType().GetProperty("imported")!.GetValue(data)!;
        var skipped = (int)data.GetType().GetProperty("skipped")!.GetValue(data)!;
        Assert.Equal(1, imported);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public async Task Import_EmptyList_ReturnsBadRequest()
    {
        var result = await _controller.Import(new List<BookPuzzleImportDto>());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Import_Null_ReturnsBadRequest()
    {
        var result = await _controller.Import(null!);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Import_CreatesBookAndSetsBookId()
    {
        var importData = new List<BookPuzzleImportDto>
        {
            new() { LineId = "legacy.pgn:1", BookFileName = "legacy.pgn", Round = "1",
                    Fen = "8/8/8/8/8/8/8/4K3 w - - 0 1", Moves = "e1e2" },
        };

        var result = await _controller.Import(importData) as OkObjectResult;
        Assert.NotNull(result);

        // Book wurde angelegt (DisplayName ohne .pgn) und das Puzzle ist verknüpft.
        var book = await _db.Books.SingleAsync(b => b.FileName == "legacy.pgn");
        Assert.Equal("legacy", book.DisplayName);
        var puzzle = await _db.BookPuzzles.SingleAsync(bp => bp.LineId == "legacy.pgn:1");
        Assert.Equal(book.Id, puzzle.BookId);
    }

    [Fact]
    public async Task Import_ReusesExistingBook()
    {
        await _controller.Import(new List<BookPuzzleImportDto>
        {
            new() { LineId = "b.pgn:1", BookFileName = "b.pgn", Round = "1", Fen = "f", Moves = "e2e4" }
        });
        await _controller.Import(new List<BookPuzzleImportDto>
        {
            new() { LineId = "b.pgn:2", BookFileName = "b.pgn", Round = "2", Fen = "f", Moves = "e2e4" }
        });

        Assert.Equal(1, await _db.Books.CountAsync(b => b.FileName == "b.pgn"));
        Assert.Equal(2, await _db.BookPuzzles.CountAsync(bp => bp.BookFileName == "b.pgn"));
        Assert.True(await _db.BookPuzzles.AllAsync(bp => bp.BookId != null));
    }

    // ---- GetRandom (pool=daily|random|blind) -----------------------------

    private async Task<(Book book, BookPuzzle puzzle)> CreateBookWithPuzzleAsync(
        string fileName, string lineId,
        bool forDaily = false, bool forRandom = false, bool forBlind = false,
        string? difficulty = null, int? rating = null, string? tags = null)
    {
        var book = new Book
        {
            FileName = fileName,
            DisplayName = fileName,
            ForDaily = forDaily,
            ForRandom = forRandom,
            ForBlind = forBlind,
            Difficulty = difficulty,
            Rating = rating,
            Tags = tags,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        var puzzle = new BookPuzzle
        {
            LineId = lineId,
            BookFileName = fileName,
            BookId = book.Id,
            Round = "1",
            Fen = "8/8/8/8/8/8/8/4K3 w - - 0 1",
            Moves = "e1e2",
        };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        return (book, puzzle);
    }

    [Fact]
    public async Task GetRandom_Random_OnlyFromForRandomBooks()
    {
        await CreateBookWithPuzzleAsync("rand.pgn", "rand.pgn:1", forRandom: true);
        await CreateBookWithPuzzleAsync("daily.pgn", "daily.pgn:1", forDaily: true); // not forRandom

        for (int i = 0; i < 10; i++)
        {
            var result = await _controller.GetRandom("random", null) as OkObjectResult;
            Assert.NotNull(result);
            var dto = Assert.IsType<BookPuzzleDto>(result!.Value);
            Assert.Equal("rand.pgn:1", dto.LineId);
        }
    }

    [Fact]
    public async Task GetRandom_Blind_OnlyFromForBlindBooks()
    {
        await CreateBookWithPuzzleAsync("blind.pgn", "blind.pgn:1", forBlind: true);
        await CreateBookWithPuzzleAsync("rand.pgn", "rand.pgn:1", forRandom: true);

        var result = await _controller.GetRandom("blind", null) as OkObjectResult;
        Assert.NotNull(result);
        var dto = Assert.IsType<BookPuzzleDto>(result!.Value);
        Assert.Equal("blind.pgn:1", dto.LineId);
    }

    [Fact]
    public async Task GetRandom_Daily_IsDeterministicForSameDay()
    {
        await CreateBookWithPuzzleAsync("d1.pgn", "d1.pgn:1", forDaily: true);
        await CreateBookWithPuzzleAsync("d2.pgn", "d2.pgn:1", forDaily: true);
        await CreateBookWithPuzzleAsync("d3.pgn", "d3.pgn:1", forDaily: true);

        var first = await _controller.GetRandom("daily", null) as OkObjectResult;
        var firstDto = Assert.IsType<BookPuzzleDto>(first!.Value);
        for (int i = 0; i < 5; i++)
        {
            var again = await _controller.GetRandom("daily", null) as OkObjectResult;
            var dto = Assert.IsType<BookPuzzleDto>(again!.Value);
            Assert.Equal(firstDto.LineId, dto.LineId);
        }
    }

    [Fact]
    public async Task GetRandom_EnrichesMetadataFromBook()
    {
        await CreateBookWithPuzzleAsync("m.pgn", "m.pgn:1", forRandom: true,
            difficulty: "Meister", rating: 7, tags: "Taktik");

        var result = await _controller.GetRandom("random", null) as OkObjectResult;
        var dto = Assert.IsType<BookPuzzleDto>(result!.Value);
        Assert.Equal("Meister", dto.Difficulty);
        Assert.Equal(7, dto.BookRating);
        Assert.Equal("Taktik", dto.Tags);
    }

    [Fact]
    public async Task GetRandom_ExcludeFiltersIds()
    {
        var (_, p1) = await CreateBookWithPuzzleAsync("e.pgn", "e.pgn:1", forRandom: true);
        await CreateBookWithPuzzleAsync("e2.pgn", "e2.pgn:1", forRandom: true);

        for (int i = 0; i < 10; i++)
        {
            var result = await _controller.GetRandom("random", p1.Id.ToString()) as OkObjectResult;
            var dto = Assert.IsType<BookPuzzleDto>(result!.Value);
            Assert.NotEqual(p1.Id, dto.Id);
        }
    }

    [Fact]
    public async Task GetRandom_EmptyPool_ReturnsNotFound()
    {
        await CreateBookWithPuzzleAsync("rand.pgn", "rand.pgn:1", forRandom: true);

        var result = await _controller.GetRandom("daily", null);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetRandom_InvalidPool_ReturnsBadRequest()
    {
        var result = await _controller.GetRandom("bogus", null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetById_ReturnsAllFields()
    {
        var puzzle = new BookPuzzle
        {
            LineId = "allfields.pgn:001",
            BookFileName = "allfields.pgn",
            Round = "001",
            Fen = "8/8/8/8/8/8/8/4K3 w - - 0 1",
            Moves = "e1e2",
            Title = "My Title",
            Chapter = "Chapter 1",
            Comment = "A useful comment",
            Difficulty = "Meister",
            BookRating = 8,
            Tags = "Endspiel Strategie"
        };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();

        var result = await _controller.GetById(puzzle.Id) as OkObjectResult;

        Assert.NotNull(result);
        var dto = Assert.IsType<BookPuzzleDto>(result.Value);
        Assert.Equal("allfields.pgn:001", dto.LineId);
        Assert.Equal("allfields.pgn", dto.BookFileName);
        Assert.Equal("001", dto.Round);
        Assert.Equal("My Title", dto.Title);
        Assert.Equal("Chapter 1", dto.Chapter);
        Assert.Equal("A useful comment", dto.Comment);
        Assert.Equal("Meister", dto.Difficulty);
        Assert.Equal(8, dto.BookRating);
        Assert.Equal("Endspiel Strategie", dto.Tags);
    }
}
