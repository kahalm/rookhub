using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AnonymousPuzzleTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PuzzleService _service;

    public AnonymousPuzzleTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new PuzzleService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<PuzzleService>.Instance, new PuzzleTaggingService(_db, NullLogger<PuzzleTaggingService>.Instance));
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

    private async Task<Puzzle> CreatePuzzleAsync(int rating = 1500, string lichessId = "")
    {
        var puzzle = new Puzzle
        {
            LichessId = string.IsNullOrEmpty(lichessId) ? Guid.NewGuid().ToString()[..8] : lichessId,
            Fen = "r1bqkbnr/pppppppp/2n5/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 2",
            Moves = "e2e4 d7d5 e4d5",
            Rating = rating
        };
        _db.Puzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        return puzzle;
    }

    [Fact]
    public async Task RecordAnonymousAttempt_CreatesRecordWithSessionId()
    {
        var puzzle = await CreatePuzzleAsync();
        var sessionId = "abc-123-def";

        var result = await _service.RecordAnonymousAttemptAsync(sessionId, puzzle.Id,
            new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 30 });

        Assert.True(result.Solved);
        Assert.Equal(30, result.TimeSpentSeconds);

        var attempt = await _db.PuzzleAttempts.SingleAsync();
        Assert.Null(attempt.UserId);
        Assert.Equal(sessionId, attempt.AnonymousSessionId);
    }

    [Fact]
    public async Task RecordAnonymousAttempt_TrimsToCapPerSession()
    {
        var puzzle = await CreatePuzzleAsync(lichessId: "trim1");
        var sessionId = "trim-session";
        var baseTime = DateTime.UtcNow.AddDays(-1);

        // 205 bestehende anonyme Attempts dieser Session direkt einfuegen.
        for (var i = 0; i < 205; i++)
            _db.PuzzleAttempts.Add(new PuzzleAttempt
            {
                AnonymousSessionId = sessionId, PuzzleId = puzzle.Id,
                Solved = true, TimeSpentSeconds = 1, AttemptedAt = baseTime.AddSeconds(i)
            });
        await _db.SaveChangesAsync();

        // Ein weiterer Attempt ueber den Service -> Trim greift.
        await _service.RecordAnonymousAttemptAsync(sessionId, puzzle.Id,
            new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 1 });

        var count = await _db.PuzzleAttempts.CountAsync(a => a.AnonymousSessionId == sessionId);
        Assert.Equal(200, count); // auf Cap getrimmt, neueste behalten
    }

    [Fact]
    public async Task RecordAnonymousAttempt_ThrowsWhenPuzzleNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.RecordAnonymousAttemptAsync("session-1", 99999,
                new RecordPuzzleAttemptDto { Solved = true }));
    }

    [Fact]
    public async Task GetAnonymousStats_ReturnsCorrectStats()
    {
        var p1 = await CreatePuzzleAsync(lichessId: "anon-s1");
        var p2 = await CreatePuzzleAsync(lichessId: "anon-s2");
        var p3 = await CreatePuzzleAsync(lichessId: "anon-s3");
        var sessionId = "session-stats";

        _db.PuzzleAttempts.AddRange(
            new PuzzleAttempt { AnonymousSessionId = sessionId, PuzzleId = p1.Id, Solved = true, TimeSpentSeconds = 10, AttemptedAt = DateTime.UtcNow.AddMinutes(-3) },
            new PuzzleAttempt { AnonymousSessionId = sessionId, PuzzleId = p2.Id, Solved = false, TimeSpentSeconds = 20, AttemptedAt = DateTime.UtcNow.AddMinutes(-2) },
            new PuzzleAttempt { AnonymousSessionId = sessionId, PuzzleId = p3.Id, Solved = true, TimeSpentSeconds = 15, AttemptedAt = DateTime.UtcNow.AddMinutes(-1) }
        );
        await _db.SaveChangesAsync();

        var stats = await _service.GetAnonymousStatsAsync(sessionId);

        Assert.Equal(3, stats.TotalAttempts);
        Assert.Equal(2, stats.Solved);
        Assert.Equal(66.7, stats.Accuracy);
        Assert.Equal(1, stats.CurrentStreak);
    }

    [Fact]
    public async Task GetAnonymousStats_ReturnsZeros_WhenNoAttempts()
    {
        var stats = await _service.GetAnonymousStatsAsync("nonexistent-session");

        Assert.Equal(0, stats.TotalAttempts);
        Assert.Equal(0, stats.Solved);
    }

    [Fact]
    public async Task GetAnonymousStats_DoesNotIncludeOtherSessions()
    {
        var puzzle = await CreatePuzzleAsync(lichessId: "isolation1");

        _db.PuzzleAttempts.AddRange(
            new PuzzleAttempt { AnonymousSessionId = "session-a", PuzzleId = puzzle.Id, Solved = true, TimeSpentSeconds = 10 },
            new PuzzleAttempt { AnonymousSessionId = "session-b", PuzzleId = puzzle.Id, Solved = false, TimeSpentSeconds = 20 }
        );
        await _db.SaveChangesAsync();

        var statsA = await _service.GetAnonymousStatsAsync("session-a");
        var statsB = await _service.GetAnonymousStatsAsync("session-b");

        Assert.Equal(1, statsA.TotalAttempts);
        Assert.True(statsA.Accuracy > 99);
        Assert.Equal(1, statsB.TotalAttempts);
        Assert.Equal(0.0, statsB.Accuracy);
    }

    [Fact]
    public async Task ClaimSession_TransfersAttemptsToUser()
    {
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync(lichessId: "claim1");
        var sessionId = "claim-session";

        _db.PuzzleAttempts.AddRange(
            new PuzzleAttempt { AnonymousSessionId = sessionId, PuzzleId = puzzle.Id, Solved = true, TimeSpentSeconds = 10 },
            new PuzzleAttempt { AnonymousSessionId = sessionId, PuzzleId = puzzle.Id, Solved = false, TimeSpentSeconds = 20 }
        );
        await _db.SaveChangesAsync();

        var claimed = await _service.ClaimSessionAsync(userId, sessionId);

        Assert.Equal(2, claimed);

        var attempts = await _db.PuzzleAttempts.ToListAsync();
        Assert.All(attempts, a =>
        {
            Assert.Equal(userId, a.UserId);
            Assert.Null(a.AnonymousSessionId);
        });
    }

    [Fact]
    public async Task ClaimSession_ReturnsZero_WhenNoMatchingAttempts()
    {
        var userId = await CreateUserAsync();

        var claimed = await _service.ClaimSessionAsync(userId, "nonexistent-session");

        Assert.Equal(0, claimed);
    }

    [Fact]
    public async Task ClaimSession_DoesNotAffectAlreadyClaimedAttempts()
    {
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");
        var puzzle = await CreatePuzzleAsync(lichessId: "already-claimed");
        var sessionId = "shared-session";

        // One already claimed by user1, one still anonymous
        _db.PuzzleAttempts.AddRange(
            new PuzzleAttempt { UserId = user1, AnonymousSessionId = null, PuzzleId = puzzle.Id, Solved = true, TimeSpentSeconds = 10 },
            new PuzzleAttempt { AnonymousSessionId = sessionId, PuzzleId = puzzle.Id, Solved = false, TimeSpentSeconds = 20 }
        );
        await _db.SaveChangesAsync();

        var claimed = await _service.ClaimSessionAsync(user2, sessionId);

        Assert.Equal(1, claimed);

        var attempts = await _db.PuzzleAttempts.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(user1, attempts[0].UserId); // unchanged
        Assert.Equal(user2, attempts[1].UserId); // claimed by user2
    }

    [Fact]
    public async Task ClaimSession_ThenGetStats_ShowsCombinedData()
    {
        var userId = await CreateUserAsync();
        var p1 = await CreatePuzzleAsync(lichessId: "combined1");
        var p2 = await CreatePuzzleAsync(lichessId: "combined2");
        var sessionId = "combine-session";

        // One existing user attempt
        _db.PuzzleAttempts.Add(new PuzzleAttempt
        {
            UserId = userId, PuzzleId = p1.Id, Solved = true, TimeSpentSeconds = 10
        });
        // One anonymous attempt
        _db.PuzzleAttempts.Add(new PuzzleAttempt
        {
            AnonymousSessionId = sessionId, PuzzleId = p2.Id, Solved = true, TimeSpentSeconds = 20
        });
        await _db.SaveChangesAsync();

        await _service.ClaimSessionAsync(userId, sessionId);

        var stats = await _service.GetStatsAsync(userId);

        Assert.Equal(2, stats.TotalAttempts);
        Assert.Equal(2, stats.Solved);
    }

    [Fact]
    public async Task RecordAnonymousAttempt_WithMoveLog_StoresJson()
    {
        var puzzle = await CreatePuzzleAsync(lichessId: "anon_ml1");
        var sessionId = "movelog-session";
        var moveLog = "[{\"i\":2,\"uci\":\"Bc4\",\"exp\":\"d4d5\",\"ms\":5600,\"ok\":false}]";

        var result = await _service.RecordAnonymousAttemptAsync(sessionId, puzzle.Id,
            new RecordPuzzleAttemptDto { Solved = false, TimeSpentSeconds = 20, MoveLog = moveLog });

        Assert.Equal(moveLog, result.MoveLog);
        var attempt = await _db.PuzzleAttempts.SingleAsync();
        Assert.Equal(moveLog, attempt.MoveLog);
        Assert.Equal(sessionId, attempt.AnonymousSessionId);
    }
}
