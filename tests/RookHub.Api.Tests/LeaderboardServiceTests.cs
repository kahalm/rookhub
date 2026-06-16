using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.Tests;

public class LeaderboardServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly LeaderboardService _service;

    public LeaderboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new LeaderboardService(_db);
    }

    public void Dispose() => _db.Dispose();

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
                DiscordUsername = discordId != null ? username + "#disc" : null,
            },
        };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    private void AddPuzzleSolve(int userId, int puzzleId, bool solved, DateTime at)
        => _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = userId, PuzzleId = puzzleId, Solved = solved, AttemptedAt = at });

    private void AddEndlessRun(int userId, DateTime createdAt)
        => _db.EndlessSessions.Add(new EndlessSession { UserId = userId, CreatedAt = createdAt, TotalSolved = 5, MaxRating = 1500 });

    private void AddCourseLine(int userId, int bookPuzzleId, DateTime solvedAt)
        => _db.CoursePuzzleResults.Add(new CoursePuzzleResult { UserId = userId, BookId = 1, BookPuzzleId = bookPuzzleId, SolvedAt = solvedAt });

    [Fact]
    public async Task GetAsync_CountsUniquePuzzles_OrdersByCountDesc_WithDiscord()
    {
        var anna = await CreateUserAsync("anna", discordId: "111");
        var ben = await CreateUserAsync("ben");
        var now = DateTime.UtcNow;

        // anna: 3 einzigartige Puzzles (Puzzle 10 doppelt gelöst → zählt nur einmal).
        AddPuzzleSolve(anna.Id, 10, true, now);
        AddPuzzleSolve(anna.Id, 10, true, now);
        AddPuzzleSolve(anna.Id, 11, true, now);
        AddPuzzleSolve(anna.Id, 12, true, now);
        // ben: 1 gelöstes Puzzle + 1 nicht gelöstes (zählt nicht).
        AddPuzzleSolve(ben.Id, 10, true, now);
        AddPuzzleSolve(ben.Id, 13, false, now);
        await _db.SaveChangesAsync();

        var res = await _service.GetAsync("alltime");

        Assert.Equal("alltime", res.Period);
        Assert.Equal(2, res.Puzzles.Count);
        Assert.Equal("anna", res.Puzzles[0].Name);
        Assert.Equal(3, res.Puzzles[0].Count);          // einzigartig, nicht 4
        Assert.Equal("111", res.Puzzles[0].DiscordId);
        Assert.Equal("ben", res.Puzzles[1].Name);
        Assert.Equal(1, res.Puzzles[1].Count);
    }

    [Fact]
    public async Task GetAsync_IgnoresAnonymousAttempts()
    {
        var anna = await CreateUserAsync("anna");
        var now = DateTime.UtcNow;
        AddPuzzleSolve(anna.Id, 1, true, now);
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = null, AnonymousSessionId = "anon", PuzzleId = 2, Solved = true, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var res = await _service.GetAsync("alltime");
        Assert.Single(res.Puzzles);
        Assert.Equal("anna", res.Puzzles[0].Name);
        Assert.Equal(1, res.Puzzles[0].Count);
    }

    [Fact]
    public async Task GetAsync_DailyWindow_ExcludesOlderSolves_AlltimeIncludesThem()
    {
        var anna = await CreateUserAsync("anna");
        var now = DateTime.UtcNow;
        AddPuzzleSolve(anna.Id, 1, true, now);                  // heute
        AddPuzzleSolve(anna.Id, 2, true, now.AddDays(-2));      // vor 2 Tagen
        await _db.SaveChangesAsync();

        var daily = await _service.GetAsync("daily");
        Assert.Single(daily.Puzzles);
        Assert.Equal(1, daily.Puzzles[0].Count);                // nur das heutige

        var alltime = await _service.GetAsync("alltime");
        Assert.Equal(2, alltime.Puzzles[0].Count);              // beide
    }

    [Fact]
    public async Task GetAsync_CountsEndlessRunsAndCourseLines()
    {
        var anna = await CreateUserAsync("anna");
        var ben = await CreateUserAsync("ben");
        var now = DateTime.UtcNow;

        AddEndlessRun(anna.Id, now);
        AddEndlessRun(anna.Id, now);
        AddEndlessRun(ben.Id, now);

        AddCourseLine(anna.Id, 100, now);
        AddCourseLine(ben.Id, 100, now);
        AddCourseLine(ben.Id, 101, now);
        await _db.SaveChangesAsync();

        var res = await _service.GetAsync("alltime");

        Assert.Equal(2, res.EndlessRuns[0].Count);              // anna: 2 Läufe
        Assert.Equal("anna", res.EndlessRuns[0].Name);
        Assert.Equal("ben", res.CourseLines[0].Name);           // ben: 2 Linien führt
        Assert.Equal(2, res.CourseLines[0].Count);
    }

    [Fact]
    public void WindowStart_ComputesUtcBoundaries()
    {
        // Mittwoch, 2026-06-17 12:00 UTC
        var now = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 6, 17), LeaderboardService.WindowStart("daily", now));
        Assert.Equal(new DateTime(2026, 6, 15), LeaderboardService.WindowStart("weekly", now));   // Montag dieser Woche
        Assert.Equal(new DateTime(2026, 6, 1), LeaderboardService.WindowStart("monthly", now));
        Assert.Equal(DateTime.MinValue, LeaderboardService.WindowStart("alltime", now));
    }
}
