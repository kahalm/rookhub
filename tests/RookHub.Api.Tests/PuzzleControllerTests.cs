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

public class PuzzleControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PuzzleService _service;
    private readonly PuzzleController _controller;

    public PuzzleControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new PuzzleService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<PuzzleService>.Instance);
        _controller = new PuzzleController(_service);
        SetUser(1);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private void SetAnonymousUser()
    {
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };
    }

    private async Task<int> CreateUserAsync(string username = "testuser")
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@test.com",
            PasswordHash = "hash",
            Profile = new UserProfile()
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Puzzle> CreatePuzzleAsync(int rating = 1500, string themes = "middlegame fork")
    {
        var puzzle = new Puzzle
        {
            LichessId = Guid.NewGuid().ToString()[..8],
            Fen = "r1bqkbnr/pppppppp/2n5/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 2",
            Moves = "e2e4 d7d5 e4d5",
            Rating = rating,
            Themes = themes
        };
        _db.Puzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        return puzzle;
    }

    // ---- GetRatingRange ----

    [Fact]
    public async Task GetRatingRange_ReturnsMinMax()
    {
        await CreatePuzzleAsync(rating: 800);
        await CreatePuzzleAsync(rating: 2200);

        var result = await _controller.GetRatingRange() as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        Assert.Equal(800, (int)data.GetType().GetProperty("min")!.GetValue(data)!);
        Assert.Equal(2200, (int)data.GetType().GetProperty("max")!.GetValue(data)!);
    }

    [Fact]
    public async Task GetRatingRange_ReturnsNotFound_WhenNoPuzzles()
    {
        var result = await _controller.GetRatingRange();

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- GetRandom ----

    [Fact]
    public async Task GetRandom_ReturnsOk_WithPuzzle()
    {
        var userId = await CreateUserAsync();
        SetUser(userId);
        await CreatePuzzleAsync();

        var result = await _controller.GetRandom(null, null, null, false) as OkObjectResult;

        Assert.NotNull(result);
        var puzzle = result.Value as PuzzleDto;
        Assert.NotNull(puzzle);
    }

    [Fact]
    public async Task GetRandom_ReturnsNotFound_WhenNoPuzzles()
    {
        var result = await _controller.GetRandom(null, null, null, false);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetRandom_AnonymousUser_Works()
    {
        SetAnonymousUser();
        await CreatePuzzleAsync();

        var result = await _controller.GetRandom(null, null, null, false) as OkObjectResult;

        Assert.NotNull(result);
    }

    // ---- GetById ----

    [Fact]
    public async Task GetById_ReturnsOk_WhenExists()
    {
        var puzzle = await CreatePuzzleAsync();

        var result = await _controller.GetById(puzzle.Id) as OkObjectResult;

        Assert.NotNull(result);
        var dto = result.Value as PuzzleDto;
        Assert.NotNull(dto);
        Assert.Equal(puzzle.Id, dto.Id);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMissing()
    {
        var result = await _controller.GetById(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- RecordAttempt ----

    [Fact]
    public async Task RecordAttempt_ReturnsOk()
    {
        var userId = await CreateUserAsync();
        SetUser(userId);
        var puzzle = await CreatePuzzleAsync();

        var result = await _controller.RecordAttempt(puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true,
            TimeSpentSeconds = 30
        }) as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task RecordAttempt_PersistsHintsUsed()
    {
        var userId = await CreateUserAsync();
        SetUser(userId);
        var puzzle = await CreatePuzzleAsync();

        await _controller.RecordAttempt(puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true, TimeSpentSeconds = 12, HintsUsed = 2
        });

        var attempt = await _db.PuzzleAttempts.SingleAsync(a => a.PuzzleId == puzzle.Id);
        Assert.Equal(2, attempt.HintsUsed);
    }

    // ---- FlagHints (jeder eingeloggte User) ----

    [Fact]
    public async Task FlagHints_SetsAndClearsFlag_AndDtoReflects()
    {
        var puzzle = await CreatePuzzleAsync();

        var set = await _controller.FlagHints(puzzle.Id, new FlagHintsDto { Flagged = true }) as OkObjectResult;
        Assert.NotNull(set);
        Assert.True((await _db.Puzzles.FindAsync(puzzle.Id))!.HintsFlagged);
        var dto = ((await _controller.GetById(puzzle.Id)) as OkObjectResult)!.Value as PuzzleDto;
        Assert.True(dto!.HintsFlagged);

        await _controller.FlagHints(puzzle.Id, new FlagHintsDto { Flagged = false });
        Assert.False((await _db.Puzzles.FindAsync(puzzle.Id))!.HintsFlagged);
    }

    [Fact]
    public async Task FlagHints_UnknownPuzzle_NotFound()
    {
        var result = await _controller.FlagHints(99999, new FlagHintsDto { Flagged = true });
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task RecordAttempt_ReturnsNotFound_WhenPuzzleMissing()
    {
        var userId = await CreateUserAsync();
        SetUser(userId);

        var result = await _controller.RecordAttempt(99999, new RecordPuzzleAttemptDto
        {
            Solved = true,
            TimeSpentSeconds = 10
        });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- GetStats ----

    [Fact]
    public async Task GetStats_ReturnsOk()
    {
        var userId = await CreateUserAsync();
        SetUser(userId);

        var result = await _controller.GetStats(null);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var stats = okResult.Value as PuzzleStatsDto;
        Assert.NotNull(stats);
        Assert.Equal(0, stats.TotalAttempts);
    }

    // ---- GetHistory ----

    [Fact]
    public async Task GetHistory_ReturnsOk_WithPagination()
    {
        var userId = await CreateUserAsync();
        SetUser(userId);
        var puzzle = await CreatePuzzleAsync();

        _db.PuzzleAttempts.Add(new PuzzleAttempt
        {
            UserId = userId, PuzzleId = puzzle.Id, Solved = true, TimeSpentSeconds = 10
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetHistory(1, 10);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var history = okResult.Value as List<PuzzleAttemptDto>;
        Assert.NotNull(history);
        Assert.Single(history);
    }
}
