using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
        _controller = new CourseController(_db);
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
            _db,
            new PuzzleService(_db, new MemoryCache(new MemoryCacheOptions()), NullLogger<PuzzleService>.Instance),
            new PgnImportService(_db));
        SetUser(admin, UserId);
        Assert.IsType<NoContentResult>(await admin.DeleteBook(book.Id));

        Assert.Equal(0, await _db.CoursePuzzleResults.CountAsync());
        Assert.Equal(0, await _db.CourseProgresses.CountAsync());
        Assert.Equal(0, await _db.BookPuzzles.CountAsync());
    }

    // ===== Phase 2: Gruppen-Berechtigungen =====

    private AdminController CreateAdminController()
    {
        var admin = new AdminController(
            _db,
            new PuzzleService(_db, new MemoryCache(new MemoryCacheOptions()), NullLogger<PuzzleService>.Instance),
            new PgnImportService(_db));
        SetUser(admin, UserId);
        return admin;
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
        var groupController = new GroupController(_db);
        SetUser(groupController, UserId);
        var groupId = await CreateGroupAsync("G");
        var (book, _) = await SeedBookAsync("B", 1);
        await GrantAccessAsync(book.Id, groupId);

        Assert.IsType<NoContentResult>(await groupController.Delete(groupId));
        Assert.Equal(0, await _db.BookGroupAccesses.CountAsync());
    }
}
