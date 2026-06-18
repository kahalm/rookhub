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

    private void AddDaily(DateOnly date, int bookPuzzleId)
        => _db.DailyPuzzles.Add(new DailyPuzzle { Date = date, BookPuzzleId = bookPuzzleId });

    private void AddBookSolve(int userId, int bookPuzzleId, bool solved, DateTime at)
        => _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt { UserId = userId, BookPuzzleId = bookPuzzleId, Solved = solved, AttemptedAt = at });

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

        var res = await _service.GetAsync("alltime", viewerId: 0);

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

        var res = await _service.GetAsync("alltime", viewerId: 0);
        Assert.Single(res.Puzzles);
        Assert.Equal("anna", res.Puzzles[0].Name);
        Assert.Equal(1, res.Puzzles[0].Count);
    }

    [Fact]
    public async Task GetAsync_WeeklyWindow_ExcludesOlderSolves_AlltimeIncludesThem()
    {
        var anna = await CreateUserAsync("anna");
        var now = DateTime.UtcNow;
        AddPuzzleSolve(anna.Id, 1, true, now);                  // diese Woche
        AddPuzzleSolve(anna.Id, 2, true, now.AddDays(-10));     // vor 10 Tagen → garantiert vor Wochenstart (Montag)
        await _db.SaveChangesAsync();

        var weekly = await _service.GetAsync("weekly", viewerId: 0);
        Assert.Single(weekly.Puzzles);
        Assert.Equal(1, weekly.Puzzles[0].Count);               // nur das aus dieser Woche

        var alltime = await _service.GetAsync("alltime", viewerId: 0);
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

        var res = await _service.GetAsync("alltime", viewerId: 0);

        Assert.Equal(2, res.EndlessRuns[0].Count);              // anna: 2 Läufe
        Assert.Equal("anna", res.EndlessRuns[0].Name);
        Assert.Equal("ben", res.CourseLines[0].Name);           // ben: 2 Linien führt
        Assert.Equal(2, res.CourseLines[0].Count);
    }

    [Fact]
    public async Task GetAsync_CountsUniqueSolvedDailyPuzzles_OnlyDailyOnes()
    {
        var anna = await CreateUserAsync("anna");
        var ben = await CreateUserAsync("ben");
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        // Buch-Puzzles 100 + 101 sind Tagespuzzles; 999 ist KEIN Tagespuzzle.
        AddDaily(today, 100);
        AddDaily(today.AddDays(-1), 101);

        // anna: 100 zweimal gelöst (zählt einmal) + 101 gelöst → 2 einzigartige Dailies.
        AddBookSolve(anna.Id, 100, true, now);
        AddBookSolve(anna.Id, 100, true, now);
        AddBookSolve(anna.Id, 101, true, now);
        // ben: 100 gelöst (1 Daily) + 999 gelöst (kein Daily → zählt nicht) + 101 nur versucht (nicht gelöst).
        AddBookSolve(ben.Id, 100, true, now);
        AddBookSolve(ben.Id, 999, true, now);
        AddBookSolve(ben.Id, 101, false, now);
        await _db.SaveChangesAsync();

        var res = await _service.GetAsync("alltime", viewerId: 0);

        Assert.Equal(2, res.DailyPuzzles.Count);
        Assert.Equal("anna", res.DailyPuzzles[0].Name);
        Assert.Equal(2, res.DailyPuzzles[0].Count);
        Assert.Equal("ben", res.DailyPuzzles[1].Name);
        Assert.Equal(1, res.DailyPuzzles[1].Count);   // nur das Daily, nicht 999
    }

    [Fact]
    public void WindowStart_ComputesUtcBoundaries()
    {
        // Mittwoch, 2026-06-17 12:00 UTC
        var now = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 6, 15), LeaderboardService.WindowStart("weekly", now));   // Montag dieser Woche
        Assert.Equal(new DateTime(2026, 6, 1), LeaderboardService.WindowStart("monthly", now));
        Assert.Equal(DateTime.MinValue, LeaderboardService.WindowStart("alltime", now));
    }

    [Fact]
    public async Task GetAsync_SetsTrueRankAndMarksViewer()
    {
        var anna = await CreateUserAsync("anna");
        var ben = await CreateUserAsync("ben");
        var now = DateTime.UtcNow;
        AddPuzzleSolve(anna.Id, 1, true, now);
        AddPuzzleSolve(anna.Id, 2, true, now);   // anna 2 → Rang 1
        AddPuzzleSolve(ben.Id, 1, true, now);    // ben 1 → Rang 2
        await _db.SaveChangesAsync();

        var res = await _service.GetAsync("alltime", viewerId: ben.Id);

        Assert.Equal(1, res.Puzzles[0].Rank);
        Assert.False(res.Puzzles[0].IsMe);       // anna
        Assert.Equal(2, res.Puzzles[1].Rank);
        Assert.True(res.Puzzles[1].IsMe);        // ben = Viewer
    }

    [Fact]
    public async Task GetAsync_ReturnsTop5PlusViewerWindow_WithGap()
    {
        var now = DateTime.UtcNow;
        var users = new List<AppUser>();
        for (var i = 1; i <= 11; i++)
        {
            var u = await CreateUserAsync($"u{i:D2}");
            users.Add(u);
            var count = 12 - i;                  // u01=11 … u11=1 → echte Ränge 1..11
            for (var j = 0; j < count; j++)
                AddPuzzleSolve(u.Id, i * 100 + j, true, now);
        }
        await _db.SaveChangesAsync();

        var viewer = users[8];                   // u09 → Rang 9
        var res = await _service.GetAsync("alltime", viewerId: viewer.Id, top: 5, around: 2);

        var ranks = res.Puzzles.Select(e => e.Rank).ToList();
        // Top 5 (Ränge 1–5) + Fenster ±2 um Rang 9 (Ränge 7–11); Rang 6 fällt raus → Lücke.
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 7, 8, 9, 10, 11 }, ranks);
        Assert.DoesNotContain(6, ranks);
        Assert.True(res.Puzzles.Single(e => e.Rank == 9).IsMe);
        Assert.Equal("u09", res.Puzzles.Single(e => e.IsMe).Name);
    }
}
