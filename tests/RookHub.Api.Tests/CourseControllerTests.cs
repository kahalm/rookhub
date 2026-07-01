using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class CourseControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseController _controller;
    private const int UserId = 1;

    public CourseControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _controller = new CourseController(new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db), new BookAdminService(_db), new RepertoireService(_db, new RepertoireAnalyzeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())))), ReprocessTestHelper.Build(_db), new RecordingReprocessLauncher());
        SetUser(_controller, UserId);
    }

    public void Dispose() => _db.Dispose();

    private static void SetUser(ControllerBase controller, int userId, bool isAdmin = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private async Task<int> CreateGroupAsync(string name)
    {
        var g = new Group { Name = name, CreatedAt = DateTime.UtcNow };
        _db.Groups.Add(g);
        await _db.SaveChangesAsync();
        return g.Id;
    }

    private async Task AddToGroupAsync(int userId, int groupId)
    {
        _db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = groupId });
        await _db.SaveChangesAsync();
    }

    private async Task GrantAccessAsync(int bookId, int groupId)
    {
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = bookId, GroupId = groupId });
        await _db.SaveChangesAsync();
    }

    private async Task<int> CreateUserAsync()
    {
        var u = new AppUser { Id = UserId, Username = "admin", Email = "a@t.com", PasswordHash = "h", IsAdmin = true };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    /// <summary>Buch mit n Puzzles; gibt die aufsteigend nach Id sortierten Puzzle-Ids zurück.</summary>
    private async Task<(Book book, List<int> puzzleIds)> SeedBookAsync(string name, int puzzleCount)
    {
        var book = new Book { FileName = $"{name}.pgn", DisplayName = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        var ids = new List<int>();
        for (var i = 0; i < puzzleCount; i++)
        {
            var p = new BookPuzzle
            {
                LineId = $"{name}-{i}",
                BookFileName = book.FileName,
                BookId = book.Id,
                Round = "1",
                Fen = "8/8/8/8/8/8/8/K6k w - - 0 1",
                Moves = "a1a2",
            };
            _db.BookPuzzles.Add(p);
            await _db.SaveChangesAsync();
            ids.Add(p.Id);
        }
        return (book, ids);
    }

    private static T Unwrap<T>(IActionResult result) where T : class
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value!);
    }

    [Fact]
    public async Task GetCourses_ReturnsBooksWithProgress()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Endgames", 4);
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[1], Solved = true });

        var list = Unwrap<List<CourseListItemDto>>(await _controller.GetCourses());

        var dto = Assert.Single(list);
        Assert.Equal(book.Id, dto.BookId);
        Assert.Equal(4, dto.PuzzleCount);
        Assert.Equal(2, dto.SolvedCount);
        Assert.Equal(50, dto.ProgressPercent);
    }

    [Fact]
    public async Task RecordResult_PersistsTimeSeconds_ForTrainingGoalTracking()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Timed", 2);

        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true, TimeSeconds = 42 });

        var row = await _db.CoursePuzzleResults.SingleAsync(r => r.BookPuzzleId == ids[0]);
        Assert.Equal(42, row.TimeSeconds);
    }

    [Fact]
    public async Task RecordResult_LogsCourseAttempt_ForSolvedAndFailed()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Attempts", 2);

        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = false, TimeSeconds = 11 });
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true, TimeSeconds = 22 });

        // Beide Versuche landen im append-only Zeit-Log …
        var attempts = await _db.CourseAttempts.Where(a => a.BookPuzzleId == ids[0]).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.False(attempts[0].Solved);
        Assert.Equal(11, attempts[0].TimeSeconds);
        Assert.True(attempts[1].Solved);
        Assert.Equal(22, attempts[1].TimeSeconds);
        Assert.All(attempts, a => Assert.Equal(book.Id, a.BookId));

        // … aber nur die erste Lösung erzeugt eine (idempotente) CoursePuzzleResult-Zeile.
        Assert.Equal(1, await _db.CoursePuzzleResults.CountAsync(r => r.BookPuzzleId == ids[0]));
    }

    [Fact]
    public async Task RecordResult_RepeatedSolve_AddsAttempt_ButKeepsSingleResult()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Repeat", 2);

        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true, TimeSeconds = 30 });
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true, TimeSeconds = 40 });

        Assert.Equal(2, await _db.CourseAttempts.CountAsync(a => a.BookPuzzleId == ids[0]));
        Assert.Equal(1, await _db.CoursePuzzleResults.CountAsync(r => r.BookPuzzleId == ids[0]));
    }

    [Fact]
    public async Task GetNext_Sequential_ReturnsFirstUnsolvedInOrder()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Seq", 3);

        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.False(next.Completed);
        Assert.Equal(ids[0], next.Puzzle!.Id);

        // Erstes lösen -> nächstes ist das zweite.
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });
        var next2 = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.Equal(ids[1], next2.Puzzle!.Id);
    }

    [Fact]
    public async Task GetNext_Sequential_WithAfter_SkipsToNext()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Seq", 3);

        // Überspringen: after=erstes -> liefert zweites (obwohl nichts gelöst).
        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential", after: ids[0]));
        Assert.Equal(ids[1], next.Puzzle!.Id);

        // after=letztes -> kein größeres mehr -> Wrap auf erstes ungelöstes.
        var wrap = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential", after: ids[2]));
        Assert.Equal(ids[0], wrap.Puzzle!.Id);
    }

    [Fact]
    public async Task GetNext_Random_ReturnsUnsolved()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Rand", 3);
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[1], Solved = true });

        // Nur ids[2] ist ungelöst -> muss genau dieses sein.
        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "random"));
        Assert.False(next.Completed);
        Assert.Equal(ids[2], next.Puzzle!.Id);
    }

    [Fact]
    public async Task GetNext_Random_ExcludesFailedUntilReset()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("RandFail", 3);
        // ids[0] gelöst, ids[1] FALSCH gelöst → beide bis zum Reset aus dem Random-Pool.
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[1], Solved = false });

        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "random"));
        Assert.False(next.Completed);
        Assert.Equal(ids[2], next.Puzzle!.Id);   // nur das unangetastete Puzzle bleibt im Pool

        // Auch ids[2] scheitern → alle einmal versucht → Pool leer → Completed (trotz nur 1 gelöst).
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[2], Solved = false });
        var done = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "random"));
        Assert.True(done.Completed);
        Assert.Null(done.Puzzle);
        Assert.Equal(1, done.SolvedCount);

        // Reset bringt die falsch gelösten Puzzles wieder in den Pool.
        await _controller.Reset(book.Id);
        var afterReset = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "random"));
        Assert.False(afterReset.Completed);
        Assert.NotNull(afterReset.Puzzle);
    }

    [Fact]
    public async Task GetNext_AllSolved_ReturnsCompleted()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Done", 2);
        foreach (var id in ids)
            await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = id, Solved = true });

        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.True(next.Completed);
        Assert.Null(next.Puzzle);
        Assert.Equal(2, next.SolvedCount);
        Assert.Equal(2, next.Total);
    }

    /// <summary>Buch mit gemischten Quiz-/Info-Linien (info[i]=true ⇒ IsInfoOnly). Ids aufsteigend.</summary>
    private async Task<(Book book, List<int> puzzleIds)> SeedMixedBookAsync(string name, params bool[] info)
    {
        var book = new Book { FileName = $"{name}.pgn", DisplayName = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var ids = new List<int>();
        for (var i = 0; i < info.Length; i++)
        {
            var p = new BookPuzzle
            {
                LineId = $"{name}-{i}", BookFileName = book.FileName, BookId = book.Id,
                Round = "1", Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1a2", IsInfoOnly = info[i],
            };
            _db.BookPuzzles.Add(p);
            await _db.SaveChangesAsync();
            ids.Add(p.Id);
        }
        return (book, ids);
    }

    [Fact]
    public async Task GetNext_Random_ExcludesInfoLines()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedMixedBookAsync("MixR", false, true, false);  // quiz, info, quiz
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });

        // Nur ids[2] (quiz) bleibt ziehbar; die Info-Linie ids[1] darf NIE kommen.
        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "random"));
        Assert.Equal(ids[2], next.Puzzle!.Id);
        Assert.Equal(2, next.Total);          // Info-Linie zählt nicht zum Total
    }

    [Fact]
    public async Task GetNext_Sequential_IncludesInfoLine_ForClickThrough()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedMixedBookAsync("MixS", false, true, false);  // quiz, info, quiz

        // Nach ids[0] kommt sequenziell die Info-Linie ids[1] (zum Durchklicken).
        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential", after: ids[0]));
        Assert.Equal(ids[1], next.Puzzle!.Id);
        Assert.True(next.Puzzle.IsInfoOnly);
        Assert.Equal(2, next.Total);
    }

    [Fact]
    public async Task GetNext_InfoLinesDoNotBlockCompletion()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedMixedBookAsync("MixC", false, true);  // 1 quiz + 1 info
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });

        // Einzige Quiz-Linie gelöst → Kurs durch, obwohl die Info-Linie nie „gelöst" wird.
        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.True(next.Completed);
        Assert.Null(next.Puzzle);
        Assert.Equal(1, next.Total);
        Assert.Equal(1, next.SolvedCount);
    }

    [Fact]
    public async Task GetCourses_ExcludesInfoLinesFromPuzzleCount()
    {
        await CreateUserAsync();
        await SeedMixedBookAsync("MixL", false, true, false, true);  // 2 quiz + 2 info

        var list = Unwrap<List<CourseListItemDto>>(await _controller.GetCourses());
        Assert.Equal(2, Assert.Single(list).PuzzleCount);
    }

    [Fact]
    public async Task MarkInfoSeen_ThenSequentialResume_SkipsSeenInfoLine()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedMixedBookAsync("MixSeen", true, false);  // info, quiz

        // Frischer Wiedereinstieg (kein after) zeigt zuerst die Info-Linie.
        var first = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.Equal(ids[0], first.Puzzle!.Id);
        Assert.True(first.Puzzle.IsInfoOnly);

        // Info-Linie durchgeklickt → merken.
        Assert.IsType<NoContentResult>(await _controller.MarkInfoSeen(book.Id, new MarkInfoSeenDto { BookPuzzleId = ids[0] }));

        // Nächster Wiedereinstieg (wieder kein after) überspringt die gesehene Info-Linie.
        var resumed = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.Equal(ids[1], resumed.Puzzle!.Id);
        Assert.False(resumed.Puzzle.IsInfoOnly);
    }

    [Fact]
    public async Task MarkInfoSeen_NonInfoPuzzle_Returns404()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedMixedBookAsync("MixQuiz", false, true);  // quiz, info

        // Quiz-Linien werden über CoursePuzzleResult gemerkt, nicht als Info-View.
        Assert.IsType<NotFoundObjectResult>(await _controller.MarkInfoSeen(book.Id, new MarkInfoSeenDto { BookPuzzleId = ids[0] }));
        Assert.Empty(_db.CourseInfoViews);
    }

    [Fact]
    public async Task MarkInfoSeen_Idempotent_OneRow()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedMixedBookAsync("MixIdem", true);  // info only

        Assert.IsType<NoContentResult>(await _controller.MarkInfoSeen(book.Id, new MarkInfoSeenDto { BookPuzzleId = ids[0] }));
        Assert.IsType<NoContentResult>(await _controller.MarkInfoSeen(book.Id, new MarkInfoSeenDto { BookPuzzleId = ids[0] }));
        Assert.Single(_db.CourseInfoViews);
    }

    [Fact]
    public async Task Reset_ClearsSeenInfoLines()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedMixedBookAsync("MixReset", true, false);  // info, quiz
        await _controller.MarkInfoSeen(book.Id, new MarkInfoSeenDto { BookPuzzleId = ids[0] });

        await _controller.Reset(book.Id);

        Assert.Empty(_db.CourseInfoViews);
        // Nach dem Reset wird die Info-Linie wieder von vorn gezeigt.
        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.Equal(ids[0], next.Puzzle!.Id);
        Assert.True(next.Puzzle.IsInfoOnly);
    }

    [Fact]
    public async Task GetNext_BookNotFound_Returns404()
    {
        await CreateUserAsync();
        Assert.IsType<NotFoundObjectResult>(await _controller.GetNext(999, "sequential"));
    }

    [Fact]
    public async Task GetNext_SetsLastMode()
    {
        await CreateUserAsync();
        var (book, _) = await SeedBookAsync("Mode", 2);
        await _controller.GetNext(book.Id, "random");

        var list = Unwrap<List<CourseListItemDto>>(await _controller.GetCourses());
        Assert.Equal("random", Assert.Single(list).LastMode);
    }

    [Fact]
    public async Task RecordResult_IsIdempotent()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Idem", 3);

        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });
        var dto = Unwrap<CourseProgressDto>(
            await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true }));

        Assert.Equal(1, dto.SolvedCount);
        Assert.Equal(1, await _db.CoursePuzzleResults.CountAsync());
    }

    [Fact]
    public async Task RecordResult_NotSolved_DoesNotCount()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Fail", 2);
        var dto = Unwrap<CourseProgressDto>(
            await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = false }));

        Assert.Equal(0, dto.SolvedCount);
        Assert.False(dto.Completed);
    }

    [Fact]
    public async Task RecordResult_PuzzleNotInBook_Returns404()
    {
        await CreateUserAsync();
        var (bookA, _) = await SeedBookAsync("A", 1);
        var (_, idsB) = await SeedBookAsync("B", 1);

        // Puzzle aus Buch B gegen Buch A aufzeichnen -> 404.
        var res = await _controller.RecordResult(bookA.Id, new RecordCourseResultDto { BookPuzzleId = idsB[0], Solved = true });
        Assert.IsType<NotFoundObjectResult>(res);
    }

    [Fact]
    public async Task RecordResult_LogsStartAndSolveTime()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Log", 1);
        var logger = new TestLogger<CourseService>();
        var controller = new CourseController(new CourseService(_db, logger, new PgnImportService(_db), new BookAdminService(_db), new RepertoireService(_db, new RepertoireAnalyzeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())))), ReprocessTestHelper.Build(_db), new RecordingReprocessLauncher()) { ControllerContext = _controller.ControllerContext };

        await controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true, TimeSeconds = 20 });

        var log = Assert.Single(logger.Messages, m => m.Contains("CoursePuzzleAttempt"));
        Assert.Contains($"course-puzzle {ids[0]}", log);
        Assert.Contains($"in book {book.Id}", log);
        Assert.Contains("solved", log);
        Assert.Contains("StartedAt=", log);
        Assert.Contains("SolvedAt=", log);
        Assert.Contains("in 20s", log);
    }

    [Fact]
    public async Task Reset_ClearsProgress()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("Reset", 2);
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });

        var dto = Unwrap<CourseProgressDto>(await _controller.Reset(book.Id));
        Assert.Equal(0, dto.SolvedCount);
        Assert.Equal(0, await _db.CoursePuzzleResults.CountAsync());
    }

    [Fact]
    public async Task Progress_IsPerUser()
    {
        await CreateUserAsync();
        var other = new AppUser { Id = 2, Username = "bob", Email = "b@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(other);
        await _db.SaveChangesAsync();
        var (book, ids) = await SeedBookAsync("Shared", 2);

        // User 1 löst ein Puzzle.
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });

        // User 2 sieht 0 Fortschritt.
        SetUser(_controller, 2);
        var list = Unwrap<List<CourseListItemDto>>(await _controller.GetCourses());
        Assert.Equal(0, Assert.Single(list).SolvedCount);
    }

    [Fact]
    public async Task DeleteBook_RemovesCourseData()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookAsync("ToDelete", 2);
        await _controller.GetNext(book.Id, "sequential");                 // legt CourseProgress an
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });

        Assert.Equal(1, await _db.CoursePuzzleResults.CountAsync());
        Assert.Equal(1, await _db.CourseProgresses.CountAsync());

        var admin = new AdminController(
            new AdminService(_db),
            new BookAdminService(_db),
            new PuzzleService(_db, new MemoryCache(new MemoryCacheOptions()), NullLogger<PuzzleService>.Instance),
            new PgnImportService(_db),
            new AuthService(_db, new ConfigurationBuilder().Build(), NullLogger<AuthService>.Instance),
            new ConfigurationBuilder().Build(),
            new FakeWebHostEnvironment(),
            new NoOpTaskQueue());
        SetUser(admin, UserId);
        Assert.IsType<NoContentResult>(await admin.DeleteBook(book.Id));

        Assert.Equal(0, await _db.CoursePuzzleResults.CountAsync());
        Assert.Equal(0, await _db.CourseProgresses.CountAsync());
        Assert.Equal(0, await _db.BookPuzzles.CountAsync());
    }

    [Fact]
    public async Task DeleteBook_AlsoRemovesBookPuzzleAttempts()
    {
        // Regression: BookPuzzleAttempt hat eine Restrict-FK auf BookPuzzle → ohne explizites
        // Entfernen scheitert das Löschen eines Buchs mit aufgezeichneten Solves (real FK-Fehler).
        var (book, ids) = await SeedBookAsync("WithSolves", 1);
        _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt
        {
            BookPuzzleId = ids[0], UserId = UserId, Solved = true, TimeSeconds = 5, AttemptedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var admin = CreateAdminController();
        Assert.IsType<NoContentResult>(await admin.DeleteBook(book.Id));
        Assert.Equal(0, await _db.BookPuzzleAttempts.CountAsync());
        Assert.Equal(0, await _db.BookPuzzles.CountAsync());
    }

    // ===== Phase 2: Gruppen-Berechtigungen =====

    private AdminController CreateAdminController()
    {
        var admin = new AdminController(
            new AdminService(_db),
            new BookAdminService(_db),
            new PuzzleService(_db, new MemoryCache(new MemoryCacheOptions()), NullLogger<PuzzleService>.Instance),
            new PgnImportService(_db),
            new AuthService(_db, new ConfigurationBuilder().Build(), NullLogger<AuthService>.Instance),
            new ConfigurationBuilder().Build(),
            new FakeWebHostEnvironment(),
            new NoOpTaskQueue());
        SetUser(admin, UserId);
        return admin;
    }

    [Fact]
    public async Task GetAllPuzzles_ReturnsAllPuzzles_ForAccessibleBook()
    {
        var (book, ids) = await SeedBookAsync("OfflineBook", 3);
        var list = Unwrap<List<BookPuzzleDto>>((await _controller.GetAllPuzzles(book.Id)).Result!);
        Assert.Equal(3, list.Count);
        Assert.Equal(ids.OrderBy(x => x).ToList(), list.Select(p => p.Id).OrderBy(x => x).ToList());
    }

    [Fact]
    public async Task GetAllPuzzles_NonAdmin_NoAccess_Returns404()
    {
        var (book, _) = await SeedBookAsync("Secret", 2);
        SetUser(_controller, 2, isAdmin: false);
        Assert.IsType<NotFoundObjectResult>((await _controller.GetAllPuzzles(book.Id)).Result);
    }

    [Fact]
    public async Task GetCourses_NonAdmin_OnlySeesAccessibleBooks()
    {
        var (bookA, _) = await SeedBookAsync("Visible", 2);
        await SeedBookAsync("Hidden", 2);
        var groupId = await CreateGroupAsync("Trainees");
        await AddToGroupAsync(2, groupId);
        await GrantAccessAsync(bookA.Id, groupId);

        SetUser(_controller, 2, isAdmin: false);
        var list = Unwrap<List<CourseListItemDto>>(await _controller.GetCourses());

        Assert.Equal(bookA.Id, Assert.Single(list).BookId);
    }

    [Fact]
    public async Task GetNext_NonAdmin_NoAccess_Returns404_WithAccess_Works()
    {
        var (book, _) = await SeedBookAsync("Course", 2);

        SetUser(_controller, 2, isAdmin: false);
        Assert.IsType<NotFoundObjectResult>(await _controller.GetNext(book.Id, "sequential"));

        // Zugriff gewähren -> funktioniert.
        var groupId = await CreateGroupAsync("G");
        await AddToGroupAsync(2, groupId);
        await GrantAccessAsync(book.Id, groupId);
        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.False(next.Completed);
        Assert.NotNull(next.Puzzle);
    }

    [Fact]
    public async Task HasAnyAccess_DependsOnRoleAndGrants()
    {
        var (book, _) = await SeedBookAsync("Course", 1);

        // Admin: true sobald irgendein Buch existiert.
        var adminAccess = Assert.IsType<OkObjectResult>(await _controller.HasAnyAccess()).Value!;
        Assert.True((bool)adminAccess.GetType().GetProperty("hasAccess")!.GetValue(adminAccess)!);

        // Nicht-Admin ohne Freigabe: false.
        SetUser(_controller, 2, isAdmin: false);
        var noAccess = Assert.IsType<OkObjectResult>(await _controller.HasAnyAccess()).Value!;
        Assert.False((bool)noAccess.GetType().GetProperty("hasAccess")!.GetValue(noAccess)!);

        // Nach Freigabe: true.
        var groupId = await CreateGroupAsync("G");
        await AddToGroupAsync(2, groupId);
        await GrantAccessAsync(book.Id, groupId);
        var withAccess = Assert.IsType<OkObjectResult>(await _controller.HasAnyAccess()).Value!;
        Assert.True((bool)withAccess.GetType().GetProperty("hasAccess")!.GetValue(withAccess)!);
    }

    [Fact]
    public async Task Admin_SetBookGroups_ReplacesAndIgnoresInvalid()
    {
        var admin = CreateAdminController();
        var g1 = await CreateGroupAsync("G1");
        var g2 = await CreateGroupAsync("G2");
        var (book, _) = await SeedBookAsync("B", 1);

        await admin.SetBookGroups(book.Id, new SetBookGroupsDto { GroupIds = new() { g1, g2, 999 } });
        var ids = Unwrap<List<int>>(await admin.GetBookGroups(book.Id));
        Assert.Equal(new[] { g1, g2 }.OrderBy(x => x), ids.OrderBy(x => x));   // 999 ignoriert

        // GetBooks liefert die Freigaben mit.
        var books = Unwrap<List<DTOs.BookDto>>(await admin.GetBooks());
        Assert.Equal(2, books.Single(b => b.Id == book.Id).AccessGroupIds.Count);

        // Ersetzen auf nur g1.
        await admin.SetBookGroups(book.Id, new SetBookGroupsDto { GroupIds = new() { g1 } });
        var ids2 = Unwrap<List<int>>(await admin.GetBookGroups(book.Id));
        Assert.Equal(g1, Assert.Single(ids2));
    }

    [Fact]
    public async Task DeleteBook_RemovesBookGroupAccess()
    {
        var admin = CreateAdminController();
        var groupId = await CreateGroupAsync("G");
        var (book, _) = await SeedBookAsync("B", 1);
        await GrantAccessAsync(book.Id, groupId);

        Assert.IsType<NoContentResult>(await admin.DeleteBook(book.Id));
        Assert.Equal(0, await _db.BookGroupAccesses.CountAsync());
    }

    [Fact]
    public async Task DeleteGroup_RemovesBookGroupAccess()
    {
        var groupController = new GroupController(_db, new TrainingGoalService(_db));
        SetUser(groupController, UserId);
        var groupId = await CreateGroupAsync("G");
        var (book, _) = await SeedBookAsync("B", 1);
        await GrantAccessAsync(book.Id, groupId);

        Assert.IsType<NoContentResult>(await groupController.Delete(groupId));
        Assert.Equal(0, await _db.BookGroupAccesses.CountAsync());
    }

    // ===== Kapitelübersicht =====

    /// <summary>Buch, dessen Puzzles (in Reihenfolge) die gegebenen Kapitelnamen tragen. Gibt Buch + Ids zurück.</summary>
    private async Task<(Book book, List<int> puzzleIds)> SeedBookWithChaptersAsync(string name, params string?[] chapters)
    {
        var book = new Book { FileName = $"{name}.pgn", DisplayName = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        var ids = new List<int>();
        for (var i = 0; i < chapters.Length; i++)
        {
            var p = new BookPuzzle
            {
                LineId = $"{name}-{i}", BookFileName = book.FileName, BookId = book.Id,
                Round = "1", Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1a2",
                Chapter = chapters[i],
            };
            _db.BookPuzzles.Add(p);
            await _db.SaveChangesAsync();
            ids.Add(p.Id);
        }
        return (book, ids);
    }

    [Fact]
    public async Task GetChapters_ReturnsChaptersInReadingOrderWithCounts()
    {
        await CreateUserAsync();
        // Reihenfolge nach Id: A, A, B, C, C, C
        var (book, _) = await SeedBookWithChaptersAsync("Multi", "A", "A", "B", "C", "C", "C");

        var chapters = Unwrap<List<CourseChapterDto>>((await _controller.GetChapters(book.Id)).Result!);

        Assert.Equal(3, chapters.Count);
        Assert.Equal(new[] { "A", "B", "C" }, chapters.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, chapters.Select(c => c.Index).ToArray());
        Assert.Equal(new[] { 2, 1, 3 }, chapters.Select(c => c.PuzzleCount).ToArray());
        Assert.All(chapters, c => Assert.Equal(0, c.SolvedCount));
    }

    [Fact]
    public async Task GetChapters_ReflectsPerChapterProgress()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookWithChaptersAsync("Prog", "A", "A", "B", "B");
        // Eines aus Kapitel A lösen.
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });

        var chapters = Unwrap<List<CourseChapterDto>>((await _controller.GetChapters(book.Id)).Result!);

        var a = chapters.Single(c => c.Name == "A");
        var b = chapters.Single(c => c.Name == "B");
        Assert.Equal(1, a.SolvedCount);
        Assert.Equal(50, a.ProgressPercent);
        Assert.Equal(0, b.SolvedCount);
        Assert.Equal(0, b.ProgressPercent);
    }

    [Fact]
    public async Task GetChapters_GroupsBlankChapterAsNoChapter()
    {
        await CreateUserAsync();
        // null und "" sollen in dieselbe „ohne Kapitel"-Gruppe (Name=null) fallen.
        var (book, _) = await SeedBookWithChaptersAsync("Blank", null, "", "X");

        var chapters = Unwrap<List<CourseChapterDto>>((await _controller.GetChapters(book.Id)).Result!);

        Assert.Equal(2, chapters.Count);
        var none = chapters.Single(c => c.Name == null);
        Assert.Equal(2, none.PuzzleCount);
        Assert.Equal("X", chapters.Single(c => c.Index == 1).Name);
    }

    [Fact]
    public async Task GetNext_WithChapterIndex_StaysWithinChapter()
    {
        await CreateUserAsync();
        // Index 0 = A (ids 0,1), Index 1 = B (ids 2,3).
        var (book, ids) = await SeedBookWithChaptersAsync("Scoped", "A", "A", "B", "B");

        var first = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential", chapterIndex: 1));
        Assert.Equal(ids[2], first.Puzzle!.Id);
        Assert.Equal(2, first.Total); // nur Kapitel B zählt

        // Erstes in B lösen -> nächstes ist das zweite in B, NICHT etwas aus A.
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[2], Solved = true, ChapterIndex = 1 });
        var second = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential", chapterIndex: 1));
        Assert.Equal(ids[3], second.Puzzle!.Id);

        // Beide in B gelöst -> Kapitel abgeschlossen, obwohl A komplett offen ist.
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[3], Solved = true, ChapterIndex = 1 });
        var done = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential", chapterIndex: 1));
        Assert.True(done.Completed);
        Assert.Equal(2, done.SolvedCount);
        Assert.Equal(2, done.Total);
    }

    [Fact]
    public async Task GetNext_WithChapterIndex_ScopesProgressNotWholeBook()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookWithChaptersAsync("ScopeCount", "A", "A", "B", "B");
        // Ein Puzzle in A lösen (buchweit 1/4, Kapitel B aber 0/2).
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true });

        var inB = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential", chapterIndex: 1));
        Assert.Equal(0, inB.SolvedCount);
        Assert.Equal(2, inB.Total);

        // Buchweit (ohne chapterIndex) bleibt 1/4.
        var whole = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.Equal(1, whole.SolvedCount);
        Assert.Equal(4, whole.Total);
    }

    [Fact]
    public async Task RecordResult_WithChapterIndex_ReturnsChapterScopedProgress()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookWithChaptersAsync("RecScope", "A", "A", "B", "B");

        var progress = Unwrap<CourseProgressDto>(
            await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[2], Solved = true, ChapterIndex = 1 }));

        // Kapitel B: 1 von 2 gelöst.
        Assert.Equal(1, progress.SolvedCount);
        Assert.Equal(2, progress.Total);
        Assert.Equal(50, progress.ProgressPercent);
        Assert.False(progress.Completed);
    }

    [Fact]
    public async Task GetNext_InvalidChapterIndex_FallsBackToWholeBook()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookWithChaptersAsync("Fallback", "A", "B");

        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential", chapterIndex: 99));
        Assert.Equal(2, next.Total); // ganzes Buch
        Assert.Equal(ids[0], next.Puzzle!.Id);
    }

    [Fact]
    public async Task CourseStats_AccumulateTimeAndFirstTryAccuracy_PerBookAndChapter()
    {
        await CreateUserAsync();
        // Kapitel A = ids 0,1 ; Kapitel B = ids 2,3.
        var (book, ids) = await SeedBookWithChaptersAsync("Stats", "A", "A", "B", "B");

        // A/ids0: erst falsch (30s), dann richtig (20s) → Erst-Versuch falsch.
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = false, TimeSeconds = 30, ChapterIndex = 0 });
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true, TimeSeconds = 20, ChapterIndex = 0 });
        // A/ids1: gleich richtig (10s) → Erst-Versuch korrekt.
        var after = Unwrap<CourseProgressDto>(
            await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[1], Solved = true, TimeSeconds = 10, ChapterIndex = 0 }));

        // Kapitel A: 2 bearbeitet, 1 Erst-Versuch korrekt = 50 %; Zeit 30+20+10 = 60; beide gelöst.
        Assert.Equal("A", after.ChapterName);
        Assert.NotNull(after.Chapter);
        Assert.Equal(60, after.Chapter!.TotalSeconds);
        Assert.Equal(2, after.Chapter.AttemptedCount);
        Assert.Equal(1, after.Chapter.FirstTryCorrect);
        Assert.Equal(50, after.Chapter.AccuracyPercent);
        Assert.Equal(2, after.Chapter.SolvedCount);
        Assert.Equal(2, after.Chapter.Total);

        // Buch gesamt (bisher nur A bearbeitet): gleiche Zeit/Trefferquote, aber Total = 4.
        Assert.NotNull(after.Book);
        Assert.Equal(60, after.Book!.TotalSeconds);
        Assert.Equal(2, after.Book.AttemptedCount);
        Assert.Equal(1, after.Book.FirstTryCorrect);
        Assert.Equal(50, after.Book.AccuracyPercent);
        Assert.Equal(2, after.Book.SolvedCount);
        Assert.Equal(4, after.Book.Total);
    }

    [Fact]
    public async Task SingleChapterBook_HasNoSeparateChapterBlock()
    {
        await CreateUserAsync();
        var (book, _) = await SeedBookAsync("Flat", 3);   // alle ohne Kapitel → 1 Gruppe
        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.NotNull(next.Book);
        Assert.Null(next.Chapter);   // kein separater Kapitel-Block bei nur einem Kapitel
    }

    [Fact]
    public async Task Reset_MakesEveryAttemptCountAsFirstAgain()
    {
        await CreateUserAsync();
        var (book, ids) = await SeedBookWithChaptersAsync("Rst", "A", "A");

        // ids0: erst falsch, dann richtig (Erst-Versuch falsch); ids1: gleich richtig.
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = false, TimeSeconds = 5 });
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true, TimeSeconds = 5 });
        await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[1], Solved = true, TimeSeconds = 5 });
        // Vor-Reset-Versuche eindeutig in die Vergangenheit datieren (deterministischer Zeitfilter).
        foreach (var a in _db.CourseAttempts) a.AttemptedAt = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();

        await _controller.Reset(book.Id);

        // Nach Reset: nichts gelöst, keine Zeit, keine gezählten Versuche.
        var next = Unwrap<CourseNextPuzzleDto>(await _controller.GetNext(book.Id, "sequential"));
        Assert.Equal(0, next.Book!.SolvedCount);
        Assert.Equal(0, next.Book.TotalSeconds);
        Assert.Equal(0, next.Book.AttemptedCount);
        Assert.Equal(0, next.Book.AccuracyPercent);

        // ids0 jetzt gleich richtig lösen → zählt wieder als Erst-Versuch korrekt (100 %).
        var afterReset = Unwrap<CourseProgressDto>(
            await _controller.RecordResult(book.Id, new RecordCourseResultDto { BookPuzzleId = ids[0], Solved = true, TimeSeconds = 7 }));
        Assert.Equal(7, afterReset.Book!.TotalSeconds);
        Assert.Equal(1, afterReset.Book.AttemptedCount);
        Assert.Equal(1, afterReset.Book.FirstTryCorrect);
        Assert.Equal(100, afterReset.Book.AccuracyPercent);
    }
}
