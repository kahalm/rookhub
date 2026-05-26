using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class PuzzleServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PuzzleService _service;

    public PuzzleServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new PuzzleService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string username = "testuser")
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Profile = new UserProfile()
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Puzzle> CreatePuzzleAsync(int rating = 1500, string themes = "middlegame fork", string lichessId = "")
    {
        var puzzle = new Puzzle
        {
            LichessId = string.IsNullOrEmpty(lichessId) ? Guid.NewGuid().ToString()[..8] : lichessId,
            Fen = "r1bqkbnr/pppppppp/2n5/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 2",
            Moves = "e2e4 d7d5 e4d5",
            Rating = rating,
            Themes = themes
        };
        _db.Puzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        return puzzle;
    }

    [Fact]
    public async Task GetRandom_ReturnsPuzzleInRatingRange()
    {
        var userId = await CreateUserAsync();
        await CreatePuzzleAsync(rating: 1000);
        await CreatePuzzleAsync(rating: 1500);
        await CreatePuzzleAsync(rating: 2000);

        var result = await _service.GetRandomAsync(userId, minRating: 1400, maxRating: 1600, null, false);

        Assert.NotNull(result);
        Assert.Equal(1500, result!.Rating);
    }

    [Fact]
    public async Task GetRandom_ReturnsNull_WhenNoPuzzles()
    {
        var userId = await CreateUserAsync();

        var result = await _service.GetRandomAsync(userId, null, null, null, false);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRandom_ExcludesSolvedPuzzles()
    {
        var userId = await CreateUserAsync();
        var solved = await CreatePuzzleAsync(rating: 1500, lichessId: "solved1");
        var unsolved = await CreatePuzzleAsync(rating: 1500, lichessId: "unsolved1");

        _db.PuzzleAttempts.Add(new PuzzleAttempt
        {
            UserId = userId,
            PuzzleId = solved.Id,
            Solved = true,
            TimeSpentSeconds = 30
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetRandomAsync(userId, null, null, null, excludeSolved: true);

        Assert.NotNull(result);
        Assert.Equal(unsolved.Id, result!.Id);
    }

    [Fact]
    public async Task GetRandom_AnonymousUser_IgnoresExcludeSolved()
    {
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "anon1");

        _db.PuzzleAttempts.Add(new PuzzleAttempt
        {
            UserId = userId,
            PuzzleId = puzzle.Id,
            Solved = true,
            TimeSpentSeconds = 30
        });
        await _db.SaveChangesAsync();

        // Anonymous user (null) with excludeSolved=true should still get puzzles
        var result = await _service.GetRandomAsync(null, null, null, null, excludeSolved: true);

        Assert.NotNull(result);
        Assert.Equal(puzzle.Id, result!.Id);
    }

    [Fact]
    public async Task GetRandom_FiltersByTheme()
    {
        var userId = await CreateUserAsync();
        await CreatePuzzleAsync(themes: "endgame mateIn2");
        await CreatePuzzleAsync(themes: "middlegame fork");

        var result = await _service.GetRandomAsync(userId, null, null, themes: "fork", false);

        Assert.NotNull(result);
        Assert.Contains("fork", result!.Themes);
    }

    [Fact]
    public async Task GetById_ReturnsPuzzle()
    {
        var puzzle = await CreatePuzzleAsync();

        var result = await _service.GetByIdAsync(puzzle.Id);

        Assert.NotNull(result);
        Assert.Equal(puzzle.LichessId, result!.LichessId);
    }

    [Fact]
    public async Task GetById_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetByIdAsync(99999);

        Assert.Null(result);
    }

    [Fact]
    public async Task RecordAttempt_CreatesRecord()
    {
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync();

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true,
            TimeSpentSeconds = 45
        });

        Assert.True(result.Solved);
        Assert.Equal(45, result.TimeSpentSeconds);
        Assert.Single(await _db.PuzzleAttempts.ToListAsync());
    }

    [Fact]
    public async Task RecordAttempt_ThrowsWhenPuzzleNotFound()
    {
        var userId = await CreateUserAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.RecordAttemptAsync(userId, 99999, new RecordPuzzleAttemptDto { Solved = true }));
    }

    [Fact]
    public async Task GetStats_CalculatesAccuracy()
    {
        var userId = await CreateUserAsync();
        var p1 = await CreatePuzzleAsync(lichessId: "stats1");
        var p2 = await CreatePuzzleAsync(lichessId: "stats2");
        var p3 = await CreatePuzzleAsync(lichessId: "stats3");

        _db.PuzzleAttempts.AddRange(
            new PuzzleAttempt { UserId = userId, PuzzleId = p1.Id, Solved = true, TimeSpentSeconds = 10, AttemptedAt = DateTime.UtcNow.AddMinutes(-3) },
            new PuzzleAttempt { UserId = userId, PuzzleId = p2.Id, Solved = false, TimeSpentSeconds = 20, AttemptedAt = DateTime.UtcNow.AddMinutes(-2) },
            new PuzzleAttempt { UserId = userId, PuzzleId = p3.Id, Solved = true, TimeSpentSeconds = 15, AttemptedAt = DateTime.UtcNow.AddMinutes(-1) }
        );
        await _db.SaveChangesAsync();

        var stats = await _service.GetStatsAsync(userId);

        Assert.Equal(3, stats.TotalAttempts);
        Assert.Equal(2, stats.Solved);
        Assert.Equal(66.7, stats.Accuracy);
        Assert.Equal(1, stats.CurrentStreak);
    }

    [Fact]
    public async Task GetStats_ReturnsZeros_WhenNoAttempts()
    {
        var userId = await CreateUserAsync();

        var stats = await _service.GetStatsAsync(userId);

        Assert.Equal(0, stats.TotalAttempts);
        Assert.Equal(0, stats.Solved);
        Assert.Equal(0, stats.Accuracy);
    }

    [Fact]
    public async Task GetHistory_ReturnsPaginated()
    {
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync();

        for (int i = 0; i < 5; i++)
        {
            _db.PuzzleAttempts.Add(new PuzzleAttempt
            {
                UserId = userId,
                PuzzleId = puzzle.Id,
                Solved = i % 2 == 0,
                TimeSpentSeconds = 10 + i,
                AttemptedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await _db.SaveChangesAsync();

        var page1 = await _service.GetHistoryAsync(userId, page: 1, pageSize: 2);
        var page2 = await _service.GetHistoryAsync(userId, page: 2, pageSize: 2);

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
    }

    [Fact]
    public async Task Import_InsertsFromCsv()
    {
        var csv = "abc123,rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1,e2e4 d7d5,1500,75,90,1000,middlegame fork,https://lichess.org/abc,Italian_Game\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var count = await _service.ImportFromCsvAsync(stream, null, null, null);

        Assert.Equal(1, count);
        var puzzle = await _db.Puzzles.FirstAsync();
        Assert.Equal("abc123", puzzle.LichessId);
        Assert.Equal(1500, puzzle.Rating);
        Assert.Equal("middlegame fork", puzzle.Themes);
    }

    [Fact]
    public async Task Import_SkipsDuplicates()
    {
        await CreatePuzzleAsync(lichessId: "dup1");

        var csv = "dup1,fen,moves,1500,75,90,1000,themes,,\nnew1,fen2,moves2,1600,80,85,500,endgame,,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var count = await _service.ImportFromCsvAsync(stream, null, null, null);

        Assert.Equal(1, count);
        Assert.Equal(2, await _db.Puzzles.CountAsync());
    }

    [Fact]
    public async Task Import_RespectsRatingFilter()
    {
        var csv = "a,fen,moves,800,75,90,1000,,,\nb,fen,moves,1500,75,90,1000,,,\nc,fen,moves,2500,75,90,1000,,,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var count = await _service.ImportFromCsvAsync(stream, minRating: 1000, maxRating: 2000, null);

        Assert.Equal(1, count);
        var puzzle = await _db.Puzzles.FirstAsync();
        Assert.Equal(1500, puzzle.Rating);
    }
}
