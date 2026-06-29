using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

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
        _controller = new BookPuzzleController(new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue()), HintTestHelper.Build(_db), new NoOpTaskQueue(), _db);
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

    private async Task<AppUser> CreateUserAsync(string username, string? discordId = null)
    {
        var u = new AppUser
        {
            Username = username,
            Email = $"{username}@test.com",
            PasswordHash = "hash",
            Profile = new UserProfile
            {
                DisplayName = username,
                DiscordId = discordId,
                DiscordUsername = discordId != null ? username + "#disc" : null
            }
        };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task RecordAttempt_AndResults_AggregatesSolversMitDiscord()
    {
        var p = await CreateBookPuzzleAsync(lineId: "daily.pgn:1", bookFileName: "daily.pgn");
        var anna = await CreateUserAsync("anna", discordId: "111");
        var ben = await CreateUserAsync("ben");    // gelöst, nicht verknüpft
        var carl = await CreateUserAsync("carl");  // nur versucht, nicht gelöst

        SetUser(anna.Id);
        Assert.IsType<OkResult>(await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 23 }));
        SetUser(ben.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 40 });
        SetUser(carl.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = false, TimeSeconds = 10 });
        // anna nochmal (Fehlversuch) → darf Solver-Status/Count nicht doppeln
        SetUser(anna.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = false, TimeSeconds = 5 });

        var res = Assert.IsType<BookPuzzleResultsDto>(((OkObjectResult)(await _controller.GetResults(p.Id, null)).Result!).Value);
        Assert.Equal(2, res.SolvedCount);                 // anna, ben
        Assert.Equal(3, res.AttemptCount);                // anna, ben, carl (je User 1×)
        Assert.Equal("111", res.Solvers.Single(s => s.Name == "anna").DiscordId);
        Assert.Null(res.Solvers.Single(s => s.Name == "ben").DiscordId);
        Assert.DoesNotContain(res.Solvers, s => s.Name == "carl");
    }

    [Fact]
    public async Task RecordAttempt_NotFound_WhenPuzzleMissing()
    {
        SetUser(1);
        Assert.IsType<NotFoundObjectResult>(await _controller.RecordAttempt(99999, new RecordBookAttemptDto { Solved = true, TimeSeconds = 1 }));
    }

    [Fact]
    public async Task RecordAttempt_LogsStartAndSolveTime()
    {
        var logger = new TestLogger<BookPuzzleService>();
        var controller = new BookPuzzleController(new BookPuzzleService(_db, logger, new NoOpTaskQueue()), HintTestHelper.Build(_db), new NoOpTaskQueue(), _db) { ControllerContext = _controller.ControllerContext };
        var p = await CreateBookPuzzleAsync(lineId: "log.pgn:1", bookFileName: "log.pgn");

        Assert.IsType<OkResult>(await controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 15 }));

        var log = Assert.Single(logger.Messages, m => m.Contains("BookPuzzleAttempt"));
        Assert.Contains($"book-puzzle {p.Id}", log);
        Assert.Contains("solved", log);
        Assert.Contains("StartedAt=", log);
        Assert.Contains("SolvedAt=", log);
        Assert.Contains("in 15s", log);
    }

    [Fact]
    public async Task RecordAnonymousAttempt_CountsInResults_DedupedPerSession()
    {
        var p = await CreateBookPuzzleAsync(lineId: "anon.pgn:1", bookFileName: "anon.pgn");
        await _controller.RecordAnonymousAttempt(p.Id, new RecordAnonymousBookAttemptDto { Solved = true, TimeSeconds = 10, SessionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });
        await _controller.RecordAnonymousAttempt(p.Id, new RecordAnonymousBookAttemptDto { Solved = true, TimeSeconds = 5, SessionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });  // gleiche Session → dedupe
        await _controller.RecordAnonymousAttempt(p.Id, new RecordAnonymousBookAttemptDto { Solved = true, TimeSeconds = 7, SessionId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" });

        var res = Assert.IsType<BookPuzzleResultsDto>(((OkObjectResult)(await _controller.GetResults(p.Id, null)).Result!).Value);
        Assert.Equal(2, res.AnonymousSolvedCount);   // aaaa + bbbb (aaaa nur 1×)
        Assert.Equal(0, res.SolvedCount);            // keine eingeloggten
        Assert.Empty(res.Solvers);
        Assert.Equal(1, await _db.BookPuzzleAttempts.CountAsync(a => a.AnonymousSessionId == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
    }

    [Fact]
    public async Task RecordAnonymousAttempt_InvalidSession_BadRequest()
    {
        var p = await CreateBookPuzzleAsync(lineId: "anon2.pgn:1", bookFileName: "anon2.pgn");
        Assert.IsType<BadRequestObjectResult>(
            await _controller.RecordAnonymousAttempt(p.Id, new RecordAnonymousBookAttemptDto { Solved = true, SessionId = "" }));
    }

    [Fact]
    public async Task RecordAnonymousAttempt_ShortGuessableSession_BadRequest()
    {
        // IDOR-Härtung: ein zu kurzer (erratbarer) Session-Wert wird abgelehnt — anonyme Stats
        // sind nur über die Session-Id partitioniert, daher muss sie hoch-entropisch (≥32 Zeichen,
        // UUID-Form) sein. Echte Clients nutzen crypto.randomUUID() → unbetroffen.
        var p = await CreateBookPuzzleAsync(lineId: "anon3.pgn:1", bookFileName: "anon3.pgn");
        Assert.IsType<BadRequestObjectResult>(
            await _controller.RecordAnonymousAttempt(p.Id, new RecordAnonymousBookAttemptDto { Solved = true, SessionId = "1" }));
        Assert.IsType<BadRequestObjectResult>(
            await _controller.RecordAnonymousAttempt(p.Id, new RecordAnonymousBookAttemptDto { Solved = true, SessionId = "deadbeef" }));
    }

    [Fact]
    public async Task Results_CombineNamedAndAnonymous()
    {
        var p = await CreateBookPuzzleAsync(lineId: "mix.pgn:1", bookFileName: "mix.pgn");
        var anna = await CreateUserAsync("anna2");
        SetUser(anna.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 12 });
        await _controller.RecordAnonymousAttempt(p.Id, new RecordAnonymousBookAttemptDto { Solved = true, TimeSeconds = 8, SessionId = "ffffffffffffffffffffffffffffffff" });

        var res = Assert.IsType<BookPuzzleResultsDto>(((OkObjectResult)(await _controller.GetResults(p.Id, null)).Result!).Value);
        Assert.Equal(1, res.SolvedCount);            // anna2 (eingeloggt)
        Assert.Equal(1, res.AnonymousSolvedCount);   // Session ffff
        Assert.Equal(2, res.AttemptCount);           // 1 User + 1 anonyme Session
    }

    [Fact]
    public async Task GetResults_FirstAttemptDecidesSolverState()
    {
        // Daily-Fairness: nach einem fehlgeschlagenen ersten Versuch zählt ein späterer Solve
        // nicht mehr als Lösung. Umgekehrt: solve-zuerst → spätere Fehlversuche ändern den Status nicht.
        var p = await CreateBookPuzzleAsync(lineId: "fair.pgn:1", bookFileName: "fair.pgn");
        var dora = await CreateUserAsync("dora");   // fail → solve → DARF NICHT als Löser zählen
        var eve = await CreateUserAsync("eve");     // solve → fail → bleibt Löser
        var finn = await CreateUserAsync("finn");   // nur solve → Löser

        SetUser(dora.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = false, TimeSeconds = 8 });
        await Task.Delay(10);                       // monotone Reihenfolge sicherstellen
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 14 });

        SetUser(eve.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 30 });
        await Task.Delay(10);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = false, TimeSeconds = 2 });

        SetUser(finn.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 19 });

        var res = Assert.IsType<BookPuzzleResultsDto>(((OkObjectResult)(await _controller.GetResults(p.Id, null)).Result!).Value);
        Assert.Equal(2, res.SolvedCount);                              // eve + finn
        Assert.DoesNotContain(res.Solvers, s => s.Name == "dora");
        Assert.Contains(res.Solvers, s => s.Name == "eve");
        Assert.Contains(res.Solvers, s => s.Name == "finn");
    }

    [Fact]
    public async Task GetResults_IncludesTimeSecondsOfFirstAttempt()
    {
        var p = await CreateBookPuzzleAsync(lineId: "time.pgn:1", bookFileName: "time.pgn");
        var anna = await CreateUserAsync("anna_time", discordId: "999");
        SetUser(anna.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 42 });
        // zweiter Versuch — TimeSeconds des ERSTEN muss gelten
        await Task.Delay(10);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 99 });

        var res = Assert.IsType<BookPuzzleResultsDto>(((OkObjectResult)(await _controller.GetResults(p.Id, null)).Result!).Value);
        Assert.Equal(42, res.Solvers.Single(s => s.Name == "anna_time").TimeSeconds);
    }

    [Fact]
    public async Task GetResults_IncludesHintsUsedOfFirstAttempt()
    {
        var p = await CreateBookPuzzleAsync(lineId: "hints.pgn:1", bookFileName: "hints.pgn");
        var hinted = await CreateUserAsync("hinted", discordId: "111");
        SetUser(hinted.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 10, HintsUsed = 2 });
        // späterer Versuch ohne Tipps darf den Erstversuch-Wert NICHT überschreiben
        await Task.Delay(10);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 5, HintsUsed = 0 });

        var clean = await CreateUserAsync("clean", discordId: "222");
        SetUser(clean.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 8, HintsUsed = 0 });

        var res = Assert.IsType<BookPuzzleResultsDto>(((OkObjectResult)(await _controller.GetResults(p.Id, null)).Result!).Value);
        Assert.Equal(2, res.Solvers.Single(s => s.Name == "hinted").HintsUsed);
        Assert.Equal(0, res.Solvers.Single(s => s.Name == "clean").HintsUsed);
    }

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
    public async Task GetBooks_IncludesStableBookId()
    {
        var (book, _) = await CreateBookWithPuzzleAsync("withid.pgn", "withid.pgn:1", forRandom: true);

        var result = await _controller.GetBooks() as OkObjectResult;
        var books = Assert.IsType<List<BookInfoDto>>(result!.Value);
        var dto = Assert.Single(books, b => b.BookFileName == "withid.pgn");
        Assert.Equal(book.Id, dto.BookId);
    }

    [Fact]
    public async Task GetRandom_WithBookId_ReturnsFromThatBook_OverridingPool()
    {
        await CreateBookWithPuzzleAsync("rand.pgn", "rand.pgn:1", forRandom: true);
        // Zielbuch ist in KEINEM Pool (kein forRandom/forDaily/forBlind):
        var (target, tp) = await CreateBookWithPuzzleAsync("chosen.pgn", "chosen.pgn:1");

        // Trotz pool=random liefert bookId das Puzzle aus dem gewählten Buch.
        for (int i = 0; i < 5; i++)
        {
            var result = await _controller.GetRandom("random", null, target.Id) as OkObjectResult;
            var dto = Assert.IsType<BookPuzzleDto>(result!.Value);
            Assert.Equal(tp.Id, dto.Id);
        }
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

    [Fact]
    public async Task FlagHints_SetsAndClearsFlag()
    {
        var puzzle = new BookPuzzle
        {
            LineId = "flag.pgn:001", BookFileName = "flag.pgn", Round = "001",
            Fen = "8/8/8/8/8/8/8/4K3 w - - 0 1", Moves = "e1e2",
            HintsJson = "{\"en\":[\"a\",\"b\",\"c\"]}", HintsVersion = 1
        };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        SetUser(1, "User");   // jeder eingeloggte User darf markieren, nicht nur Admin

        var set = await _controller.FlagHints(puzzle.Id, new FlagHintsDto { Flagged = true }) as OkObjectResult;
        Assert.NotNull(set);
        Assert.True((await _db.BookPuzzles.FindAsync(puzzle.Id))!.HintsFlagged);
        // DTO trägt das Flag mit
        var dto = Assert.IsType<BookPuzzleDto>(((await _controller.GetById(puzzle.Id)) as OkObjectResult)!.Value);
        Assert.True(dto.HintsFlagged);

        var clear = await _controller.FlagHints(puzzle.Id, new FlagHintsDto { Flagged = false }) as OkObjectResult;
        Assert.NotNull(clear);
        Assert.False((await _db.BookPuzzles.FindAsync(puzzle.Id))!.HintsFlagged);
    }

    [Fact]
    public async Task FlagHints_UnknownPuzzle_NotFound()
    {
        SetUser(1, "Admin");
        var result = await _controller.FlagHints(999999, new FlagHintsDto { Flagged = true });
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task ClaimSession_TransfersAnonymousAttemptsToUser()
    {
        var p = await CreateBookPuzzleAsync(lineId: "claim.pgn:1", bookFileName: "claim.pgn");
        var user = await CreateUserAsync("claimer");

        // Anonymer Attempt mit einer Session-ID
        await _controller.RecordAnonymousAttempt(p.Id, new RecordAnonymousBookAttemptDto { Solved = true, TimeSeconds = 10, SessionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });

        // Vorher: 0 eingeloggte Löser, 1 anonym
        var before = Assert.IsType<BookPuzzleResultsDto>(((OkObjectResult)(await _controller.GetResults(p.Id, null)).Result!).Value);
        Assert.Equal(0, before.SolvedCount);
        Assert.Equal(1, before.AnonymousSolvedCount);

        // Claim
        SetUser(user.Id);
        var claimResult = await _controller.ClaimSession(new ClaimBookSessionDto { SessionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });
        var ok = Assert.IsType<OkObjectResult>(claimResult);
        var transferred = (int)ok.Value!.GetType().GetProperty("transferred")!.GetValue(ok.Value)!;
        Assert.Equal(1, transferred);

        // Nachher: 1 eingeloggt, 0 anonym
        var after = Assert.IsType<BookPuzzleResultsDto>(((OkObjectResult)(await _controller.GetResults(p.Id, null)).Result!).Value);
        Assert.Equal(1, after.SolvedCount);
        Assert.Equal(0, after.AnonymousSolvedCount);
        Assert.Contains(after.Solvers, s => s.Name == "claimer");
    }

    [Fact]
    public async Task ClaimSession_SkipsIfUserAlreadyHasAttempt()
    {
        var p = await CreateBookPuzzleAsync(lineId: "claim2.pgn:1", bookFileName: "claim2.pgn");
        var user = await CreateUserAsync("claimer2");

        // User hat bereits selbst gelöst
        SetUser(user.Id);
        await _controller.RecordAttempt(p.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 5 });

        // Anonymer Attempt derselben Person (andere Session)
        await _controller.RecordAnonymousAttempt(p.Id, new RecordAnonymousBookAttemptDto { Solved = true, TimeSeconds = 8, SessionId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" });

        // Claim → soll 0 übertragen, da Puzzle schon beim User
        var claimResult = await _controller.ClaimSession(new ClaimBookSessionDto { SessionId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" });
        var ok = Assert.IsType<OkObjectResult>(claimResult);
        var transferred = (int)ok.Value!.GetType().GetProperty("transferred")!.GetValue(ok.Value)!;
        Assert.Equal(0, transferred);

        // Anonym-Eintrag wird gelöscht (gleicher User, doppelter Eintrag sinnlos)
        var after = Assert.IsType<BookPuzzleResultsDto>(((OkObjectResult)(await _controller.GetResults(p.Id, null)).Result!).Value);
        Assert.Equal(1, after.SolvedCount);         // nur claimer2
        Assert.Equal(0, after.AnonymousSolvedCount); // bbbb wurde gelöscht
    }

    // --- Tagespuzzle-Leaderboards (Monats-Ladder + Hall of Fame) ---------------------------

    /// <summary>Macht <paramref name="puzzle"/> zum Tagespuzzle des UTC-Datums.</summary>
    private async Task AssignDailyAsync(DateOnly date, BookPuzzle puzzle)
    {
        _db.DailyPuzzles.Add(new DailyPuzzle { Date = date, BookPuzzleId = puzzle.Id, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    /// <summary>Erfasst einen Versuch mit explizitem Zeitpunkt (für Erstversuch-/Ranking-Kontrolle).</summary>
    private async Task AddAttemptAsync(BookPuzzle puzzle, AppUser user, bool solved, int timeSeconds, DateTime attemptedAt)
    {
        _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt
        {
            BookPuzzleId = puzzle.Id,
            UserId = user.Id,
            Solved = solved,
            TimeSeconds = timeSeconds,
            AttemptedAt = attemptedAt
        });
        await _db.SaveChangesAsync();
    }

    private static DailyLadderDto LadderOf(IActionResult result)
        => Assert.IsType<DailyLadderDto>(Assert.IsType<OkObjectResult>(result).Value);

    private static DailyHallOfFameDto HofOf(IActionResult result)
        => Assert.IsType<DailyHallOfFameDto>(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task DailyLeaderboard_RanksByPoints_WithSpeedBonus()
    {
        var p1 = await CreateBookPuzzleAsync(lineId: "lb.pgn:1", bookFileName: "lb.pgn", round: "1");
        var p2 = await CreateBookPuzzleAsync(lineId: "lb.pgn:2", bookFileName: "lb.pgn", round: "2");
        await AssignDailyAsync(new DateOnly(2026, 6, 1), p1);
        await AssignDailyAsync(new DateOnly(2026, 6, 2), p2);

        var anna = await CreateUserAsync("anna", discordId: "111");
        var ben = await CreateUserAsync("ben");

        // Tag 1: anna 20s (🥇), ben 40s (🥈)
        await AddAttemptAsync(p1, anna, true, 20, new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc));
        await AddAttemptAsync(p1, ben, true, 40, new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc));
        // Tag 2: ben 15s (🥇), anna gar nicht gelöst
        await AddAttemptAsync(p2, ben, true, 15, new DateTime(2026, 6, 2, 8, 0, 0, DateTimeKind.Utc));
        await AddAttemptAsync(p2, anna, false, 5, new DateTime(2026, 6, 2, 8, 30, 0, DateTimeKind.Utc));

        var lb = LadderOf(await _controller.GetDailyLeaderboard("2026-06"));
        Assert.Equal("2026-06", lb.Period);
        Assert.Equal(2, lb.Entries.Count);

        // anna: Tag1 10+5(🥇) = 15. ben: Tag1 10+3(🥈)=13, Tag2 10+5(🥇)=15 → 28.
        var benE = lb.Entries.Single(e => e.Name == "ben");
        var annaE = lb.Entries.Single(e => e.Name == "anna");
        Assert.Equal(28, benE.Points);
        Assert.Equal(2, benE.Solved);
        Assert.Equal(1, benE.Golds);
        Assert.Equal(15, annaE.Points);
        Assert.Equal(1, annaE.Solved);
        Assert.Equal(1, annaE.Golds);
        // Sortierung: ben (28) vor anna (15)
        Assert.Equal("ben", lb.Entries[0].Name);
        Assert.Equal("111", annaE.DiscordId);
    }

    [Fact]
    public async Task DailyLeaderboard_OnlyFirstAttemptCounts()
    {
        var p = await CreateBookPuzzleAsync(lineId: "lbf.pgn:1", bookFileName: "lbf.pgn");
        await AssignDailyAsync(new DateOnly(2026, 6, 5), p);
        var carl = await CreateUserAsync("carl");

        // Erster Versuch fehlgeschlagen → zählt NICHT als Löser, auch wenn später gelöst
        await AddAttemptAsync(p, carl, false, 10, new DateTime(2026, 6, 5, 8, 0, 0, DateTimeKind.Utc));
        await AddAttemptAsync(p, carl, true, 12, new DateTime(2026, 6, 5, 8, 5, 0, DateTimeKind.Utc));

        var lb = LadderOf(await _controller.GetDailyLeaderboard("2026-06"));
        Assert.Empty(lb.Entries);
    }

    [Fact]
    public async Task DailyLeaderboard_ExcludesOtherMonths()
    {
        var pMay = await CreateBookPuzzleAsync(lineId: "lbm.pgn:1", bookFileName: "lbm.pgn", round: "1");
        var pJun = await CreateBookPuzzleAsync(lineId: "lbm.pgn:2", bookFileName: "lbm.pgn", round: "2");
        await AssignDailyAsync(new DateOnly(2026, 5, 31), pMay);
        await AssignDailyAsync(new DateOnly(2026, 6, 1), pJun);
        var dora = await CreateUserAsync("dora");
        await AddAttemptAsync(pMay, dora, true, 10, new DateTime(2026, 5, 31, 8, 0, 0, DateTimeKind.Utc));
        await AddAttemptAsync(pJun, dora, true, 10, new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc));

        var june = LadderOf(await _controller.GetDailyLeaderboard("2026-06"));
        Assert.Equal(1, june.Entries.Single().Solved);   // nur das Juni-Daily
    }

    [Fact]
    public async Task DailyLeaderboard_InvalidMonth_BadRequest()
        => Assert.IsType<BadRequestObjectResult>(await _controller.GetDailyLeaderboard("2026/06"));

    [Fact]
    public async Task DailyHallOfFame_AggregatesAllTime()
    {
        var p1 = await CreateBookPuzzleAsync(lineId: "hof.pgn:1", bookFileName: "hof.pgn", round: "1");
        var p2 = await CreateBookPuzzleAsync(lineId: "hof.pgn:2", bookFileName: "hof.pgn", round: "2");
        await AssignDailyAsync(new DateOnly(2026, 4, 10), p1);   // anderer Monat als p2
        await AssignDailyAsync(new DateOnly(2026, 6, 2), p2);

        var anna = await CreateUserAsync("anna", discordId: "111");
        var ben = await CreateUserAsync("ben");

        // p1: anna 30s (🥇), ben 50s. p2: anna 9s (🥇, zugleich schnellste je), ben gar nicht.
        await AddAttemptAsync(p1, anna, true, 30, new DateTime(2026, 4, 10, 8, 0, 0, DateTimeKind.Utc));
        await AddAttemptAsync(p1, ben, true, 50, new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc));
        await AddAttemptAsync(p2, anna, true, 9, new DateTime(2026, 6, 2, 8, 0, 0, DateTimeKind.Utc));

        var hof = HofOf(await _controller.GetDailyHallOfFame(5));

        Assert.Equal("anna", hof.MostSolved[0].Name);
        Assert.Equal(2, hof.MostSolved[0].Value);
        Assert.Equal(1, hof.MostSolved.Single(e => e.Name == "ben").Value);

        Assert.Equal("anna", hof.MostGolds[0].Name);
        Assert.Equal(2, hof.MostGolds[0].Value);            // beide Tage 🥇
        Assert.DoesNotContain(hof.MostGolds, e => e.Name == "ben");  // 0 Golds → ausgeblendet

        Assert.NotNull(hof.Fastest);
        Assert.Equal("anna", hof.Fastest!.Name);
        Assert.Equal(9, hof.Fastest.TimeSeconds);
        Assert.Equal("2026-06-02", hof.Fastest.Date);
        Assert.Equal("111", hof.Fastest.DiscordId);
    }

    [Fact]
    public async Task DailyHallOfFame_Empty_WhenNoDailies()
    {
        var hof = HofOf(await _controller.GetDailyHallOfFame());
        Assert.Empty(hof.MostSolved);
        Assert.Empty(hof.MostGolds);
        Assert.Null(hof.Fastest);
    }

    // ---- Track solves (geteilte Einzel-Puzzles) ----

    private void SetAnonymous() => _controller.ControllerContext = new ControllerContext
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
    };

    private static SharedPuzzleCountsDto TrackResult(ActionResult<SharedPuzzleCountsDto> r)
        => Assert.IsType<SharedPuzzleCountsDto>(Assert.IsType<OkObjectResult>(r.Result).Value);

    [Fact]
    public async Task Track_CountsSolvedAndFailed_PerVisitor()
    {
        var p = await CreateBookPuzzleAsync(lineId: "track.pgn:1", bookFileName: "track.pgn");

        SetUser(11);
        TrackResult(await _controller.Track(p.Id, new RecordSharedAttemptDto { Solved = true }));
        SetUser(22);
        var afterTwo = TrackResult(await _controller.Track(p.Id, new RecordSharedAttemptDto { Solved = false }));

        Assert.Equal(1, afterTwo.Solved);
        Assert.Equal(1, afterTwo.Failed);
    }

    [Fact]
    public async Task Track_OnlyFirstAttemptCountsPerVisitor()
    {
        var p = await CreateBookPuzzleAsync(lineId: "track.pgn:2", bookFileName: "track.pgn");

        SetUser(11);
        TrackResult(await _controller.Track(p.Id, new RecordSharedAttemptDto { Solved = true }));   // Erstversuch: gelöst
        var after = TrackResult(await _controller.Track(p.Id, new RecordSharedAttemptDto { Solved = false })); // Reset später → ignoriert

        Assert.Equal(1, after.Solved);
        Assert.Equal(0, after.Failed);   // Erstversuch bleibt „gelöst"
    }

    [Fact]
    public async Task Track_Anonymous_UsesSessionId_AndDistinctSessionsCountSeparately()
    {
        var p = await CreateBookPuzzleAsync(lineId: "track.pgn:3", bookFileName: "track.pgn");
        SetAnonymous();

        TrackResult(await _controller.Track(p.Id, new RecordSharedAttemptDto { Solved = true, SessionId = "11111111-1111-1111-1111-111111111111" }));
        var after = TrackResult(await _controller.Track(p.Id, new RecordSharedAttemptDto { Solved = false, SessionId = "22222222-2222-2222-2222-222222222222" }));

        Assert.Equal(1, after.Solved);
        Assert.Equal(1, after.Failed);
    }

    [Fact]
    public async Task Track_Anonymous_InvalidSession_ReturnsBadRequest()
    {
        var p = await CreateBookPuzzleAsync(lineId: "track.pgn:4", bookFileName: "track.pgn");
        SetAnonymous();

        var result = await _controller.Track(p.Id, new RecordSharedAttemptDto { Solved = true, SessionId = "short" });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TrackCounts_ReturnsCurrentTotals()
    {
        var p = await CreateBookPuzzleAsync(lineId: "track.pgn:5", bookFileName: "track.pgn");
        SetUser(11);
        await _controller.Track(p.Id, new RecordSharedAttemptDto { Solved = false });

        SetAnonymous();
        var counts = Assert.IsType<SharedPuzzleCountsDto>(Assert.IsType<OkObjectResult>((await _controller.TrackCounts(p.Id)).Result).Value);
        Assert.Equal(0, counts.Solved);
        Assert.Equal(1, counts.Failed);
    }
}
