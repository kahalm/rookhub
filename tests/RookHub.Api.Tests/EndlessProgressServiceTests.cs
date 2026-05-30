using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class EndlessProgressServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EndlessProgressService _service;

    public EndlessProgressServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new EndlessProgressService(_db);
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

    private SaveEndlessProgressDto MakeProgressDto(int highscore = 0, string? activeGame = null) => new()
    {
        StartElo = 700,
        Step = 40,
        Themes = "fork pin",
        Fasttrack = true,
        FasttrackThreshold1 = 1100,
        FasttrackThreshold2 = 1500,
        StockfishDepth = 16,
        Highscore = highscore,
        ActiveGameState = activeGame
    };

    private RecordEndlessSessionDto MakeSessionDto(long timestamp = 1000, int maxRating = 1200) => new()
    {
        Timestamp = timestamp,
        TotalSolved = 10,
        MaxRating = maxRating,
        DurationSeconds = 300,
        ConfigJson = "{\"startElo\":700}",
        MistakeAtRatings = "780,920"
    };

    // --- Progress Tests ---

    [Fact]
    public async Task GetProgress_NoData_ReturnsNull()
    {
        var userId = await CreateUserAsync();
        var result = await _service.GetSyncDataAsync(userId);
        Assert.Null(result.Progress);
        Assert.Empty(result.Sessions);
    }

    [Fact]
    public async Task SaveProgress_CreatesNew()
    {
        var userId = await CreateUserAsync();
        var dto = MakeProgressDto(highscore: 1500);

        var result = await _service.SaveProgressAsync(userId, dto);

        Assert.Equal(700, result.StartElo);
        Assert.Equal(40, result.Step);
        Assert.Equal("fork pin", result.Themes);
        Assert.True(result.Fasttrack);
        Assert.Equal(1500, result.Highscore);
        Assert.Single(await _db.EndlessProgresses.ToListAsync());
    }

    [Fact]
    public async Task SaveProgress_UpdatesExisting()
    {
        var userId = await CreateUserAsync();
        await _service.SaveProgressAsync(userId, MakeProgressDto(highscore: 1000));

        var updated = await _service.SaveProgressAsync(userId, MakeProgressDto(highscore: 2000));

        Assert.Equal(2000, updated.Highscore);
        Assert.Single(await _db.EndlessProgresses.ToListAsync());
    }

    [Fact]
    public async Task SaveProgress_StoresActiveGameState()
    {
        var userId = await CreateUserAsync();
        var gameState = "{\"lives\":2,\"solved\":5,\"level\":5}";

        var result = await _service.SaveProgressAsync(userId, MakeProgressDto(activeGame: gameState));

        Assert.Equal(gameState, result.ActiveGameState);
    }

    [Fact]
    public async Task SaveProgress_ClearsActiveGame_WhenNull()
    {
        var userId = await CreateUserAsync();
        await _service.SaveProgressAsync(userId, MakeProgressDto(activeGame: "{\"lives\":3}"));

        var result = await _service.SaveProgressAsync(userId, MakeProgressDto(activeGame: null));

        Assert.Null(result.ActiveGameState);
    }

    // --- Session Tests ---

    [Fact]
    public async Task RecordSession_CreatesRecord()
    {
        var userId = await CreateUserAsync();
        var dto = MakeSessionDto();

        var result = await _service.RecordSessionAsync(userId, dto);

        Assert.Equal(1000, result.Timestamp);
        Assert.Equal(10, result.TotalSolved);
        Assert.Equal(1200, result.MaxRating);
        Assert.Single(await _db.EndlessSessions.ToListAsync());
    }

    [Fact]
    public async Task RecordSession_DoesNotTrimForAuthenticatedUsers()
    {
        var userId = await CreateUserAsync();
        for (int i = 0; i < 52; i++)
        {
            await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: i));
        }

        var count = await _db.EndlessSessions.CountAsync(s => s.UserId == userId);
        Assert.Equal(52, count);
    }

    [Fact]
    public async Task GetSessions_OrderedByTimestamp()
    {
        var userId = await CreateUserAsync();
        await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: 100));
        await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: 300));
        await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: 200));

        var result = await _service.GetSyncDataAsync(userId);

        Assert.Equal(3, result.Sessions.Count);
        Assert.Equal(300, result.Sessions[0].Timestamp);
        Assert.Equal(200, result.Sessions[1].Timestamp);
        Assert.Equal(100, result.Sessions[2].Timestamp);
    }

    // --- Anonymous Tests ---

    [Fact]
    public async Task GetAnonymousProgress_ReturnsNull_WhenUnknown()
    {
        var result = await _service.GetAnonymousSyncDataAsync("unknown-session");
        Assert.Null(result.Progress);
        Assert.Empty(result.Sessions);
    }

    [Fact]
    public async Task SaveAnonymousProgress_CreatesRecord()
    {
        var result = await _service.SaveAnonymousProgressAsync("sess-123", MakeProgressDto(highscore: 900));

        Assert.Equal(900, result.Highscore);
        var progress = await _db.EndlessProgresses.FirstAsync();
        Assert.Equal("sess-123", progress.AnonymousSessionId);
        Assert.Null(progress.UserId);
    }

    [Fact]
    public async Task RecordAnonymousSession_StoresSessionId()
    {
        var result = await _service.RecordAnonymousSessionAsync("sess-456", MakeSessionDto());

        var session = await _db.EndlessSessions.FirstAsync();
        Assert.Equal("sess-456", session.AnonymousSessionId);
        Assert.Null(session.UserId);
    }

    // --- Claim Tests ---

    [Fact]
    public async Task ClaimSession_TransfersProgress()
    {
        var userId = await CreateUserAsync();
        await _service.SaveAnonymousProgressAsync("anon-1", MakeProgressDto(highscore: 1200));

        var transferred = await _service.ClaimSessionAsync(userId, "anon-1");

        var userProgress = await _db.EndlessProgresses.FirstOrDefaultAsync(p => p.UserId == userId);
        Assert.NotNull(userProgress);
        Assert.Equal(1200, userProgress.Highscore);
        Assert.Equal(700, userProgress.StartElo);

        var anonProgress = await _db.EndlessProgresses.FirstOrDefaultAsync(p => p.AnonymousSessionId == "anon-1");
        Assert.Null(anonProgress);
    }

    [Fact]
    public async Task ClaimSession_MergesHighscore_TakesMax()
    {
        var userId = await CreateUserAsync();
        await _service.SaveProgressAsync(userId, MakeProgressDto(highscore: 1500));
        await _service.SaveAnonymousProgressAsync("anon-2", MakeProgressDto(highscore: 1200));

        await _service.ClaimSessionAsync(userId, "anon-2");

        var userProgress = await _db.EndlessProgresses.FirstAsync(p => p.UserId == userId);
        Assert.Equal(1500, userProgress.Highscore);
    }

    [Fact]
    public async Task ClaimSession_TransfersSessions()
    {
        var userId = await CreateUserAsync();
        await _service.RecordAnonymousSessionAsync("anon-3", MakeSessionDto(timestamp: 100));
        await _service.RecordAnonymousSessionAsync("anon-3", MakeSessionDto(timestamp: 200));

        var transferred = await _service.ClaimSessionAsync(userId, "anon-3");

        Assert.Equal(2, transferred);
        var userSessions = await _db.EndlessSessions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Equal(2, userSessions.Count);
        Assert.All(userSessions, s => Assert.Null(s.AnonymousSessionId));
    }

    [Fact]
    public async Task ClaimSession_ReturnsZero_WhenNoData()
    {
        var userId = await CreateUserAsync();
        var transferred = await _service.ClaimSessionAsync(userId, "nonexistent");
        Assert.Equal(0, transferred);
    }

    [Fact]
    public async Task ClaimSession_KeepsUserConfig_WhenExists()
    {
        var userId = await CreateUserAsync();
        var userDto = MakeProgressDto(highscore: 800);
        userDto.StartElo = 500;
        await _service.SaveProgressAsync(userId, userDto);

        var anonDto = MakeProgressDto(highscore: 600);
        anonDto.StartElo = 900;
        await _service.SaveAnonymousProgressAsync("anon-4", anonDto);

        await _service.ClaimSessionAsync(userId, "anon-4");

        var userProgress = await _db.EndlessProgresses.FirstAsync(p => p.UserId == userId);
        Assert.Equal(500, userProgress.StartElo); // User config preserved
        Assert.Equal(800, userProgress.Highscore); // max(800, 600) = 800
    }

    [Fact]
    public async Task ClaimSession_TransfersActiveGame_WhenUserHasNone()
    {
        var userId = await CreateUserAsync();
        await _service.SaveProgressAsync(userId, MakeProgressDto(activeGame: null));
        await _service.SaveAnonymousProgressAsync("anon-5", MakeProgressDto(activeGame: "{\"lives\":2}"));

        await _service.ClaimSessionAsync(userId, "anon-5");

        var userProgress = await _db.EndlessProgresses.FirstAsync(p => p.UserId == userId);
        Assert.Equal("{\"lives\":2}", userProgress.ActiveGameState);
    }

    // --- Bulk Import Tests ---

    [Fact]
    public async Task BulkImport_CreatesMultiple()
    {
        var userId = await CreateUserAsync();
        var sessions = new List<RecordEndlessSessionDto>
        {
            MakeSessionDto(timestamp: 100),
            MakeSessionDto(timestamp: 200),
            MakeSessionDto(timestamp: 300)
        };

        var count = await _service.BulkImportSessionsAsync(userId, sessions);

        Assert.Equal(3, count);
        Assert.Equal(3, await _db.EndlessSessions.CountAsync());
    }

    [Fact]
    public async Task BulkImport_DoesNotTrimForAuthenticatedUsers()
    {
        var userId = await CreateUserAsync();
        var sessions = Enumerable.Range(0, 55)
            .Select(i => MakeSessionDto(timestamp: i))
            .ToList();

        await _service.BulkImportSessionsAsync(userId, sessions);

        Assert.Equal(55, await _db.EndlessSessions.CountAsync(s => s.UserId == userId));
    }

    [Fact]
    public async Task RecordAnonymousSession_StillTrimsToMax50()
    {
        for (int i = 0; i < 52; i++)
        {
            await _service.RecordAnonymousSessionAsync("anon-trim", MakeSessionDto(timestamp: i));
        }

        var count = await _db.EndlessSessions.CountAsync(s => s.AnonymousSessionId == "anon-trim");
        Assert.Equal(50, count);
    }

    // --- History Tests ---

    [Fact]
    public async Task GetSessionHistory_ReturnsEmpty_WhenNoSessions()
    {
        var userId = await CreateUserAsync();
        var result = await _service.GetSessionHistoryAsync(userId, 1, 20);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task GetSessionHistory_ReturnsPaginatedResults()
    {
        var userId = await CreateUserAsync();
        for (int i = 0; i < 25; i++)
        {
            await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: i, maxRating: 1000 + i));
        }

        // Page 1 with pageSize 10
        var page1 = await _service.GetSessionHistoryAsync(userId, 1, 10);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(1, page1.Page);
        Assert.Equal(10, page1.PageSize);
        // Ordered desc by timestamp — first item should be timestamp 24
        Assert.Equal(24, page1.Items[0].Timestamp);

        // Page 3 with pageSize 10 — only 5 items left
        var page3 = await _service.GetSessionHistoryAsync(userId, 3, 10);
        Assert.Equal(5, page3.Items.Count);
        Assert.Equal(25, page3.TotalCount);
    }

    [Fact]
    public async Task GetSessionHistory_ClampsPageSize()
    {
        var userId = await CreateUserAsync();
        await _service.RecordSessionAsync(userId, MakeSessionDto());

        var result = await _service.GetSessionHistoryAsync(userId, 1, 200);

        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public async Task GetSessionHistory_DoesNotReturnOtherUsersData()
    {
        var userId1 = await CreateUserAsync("user1");
        var userId2 = await CreateUserAsync("user2");
        await _service.RecordSessionAsync(userId1, MakeSessionDto(timestamp: 100));
        await _service.RecordSessionAsync(userId2, MakeSessionDto(timestamp: 200));

        var result = await _service.GetSessionHistoryAsync(userId1, 1, 20);

        Assert.Single(result.Items);
        Assert.Equal(100, result.Items[0].Timestamp);
    }

    // --- Archive Tests ---

    [Fact]
    public async Task ArchiveSessions_SetsFlag()
    {
        var userId = await CreateUserAsync();
        var s1 = await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: 100));
        var s2 = await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: 200));

        var updated = await _service.ArchiveSessionsAsync(userId, new List<int> { s1.Id, s2.Id }, true);

        Assert.Equal(2, updated);
        var sessions = await _db.EndlessSessions.Where(s => s.UserId == userId).ToListAsync();
        Assert.All(sessions, s => Assert.True(s.IsArchived));
    }

    [Fact]
    public async Task ArchiveSessions_DoesNotAffectOtherUsers()
    {
        var userId1 = await CreateUserAsync("user1");
        var userId2 = await CreateUserAsync("user2");
        var s1 = await _service.RecordSessionAsync(userId1, MakeSessionDto(timestamp: 100));
        var s2 = await _service.RecordSessionAsync(userId2, MakeSessionDto(timestamp: 200));

        var updated = await _service.ArchiveSessionsAsync(userId1, new List<int> { s1.Id, s2.Id }, true);

        Assert.Equal(1, updated);
        var otherSession = await _db.EndlessSessions.FirstAsync(s => s.UserId == userId2);
        Assert.False(otherSession.IsArchived);
    }

    [Fact]
    public async Task GetSyncData_ExcludesArchivedSessions()
    {
        var userId = await CreateUserAsync();
        var s1 = await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: 100));
        await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: 200));
        await _service.ArchiveSessionsAsync(userId, new List<int> { s1.Id }, true);

        var result = await _service.GetSyncDataAsync(userId);

        Assert.Single(result.Sessions);
        Assert.Equal(200, result.Sessions[0].Timestamp);
    }

    [Fact]
    public async Task GetSessionHistory_FiltersByArchived()
    {
        var userId = await CreateUserAsync();
        var s1 = await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: 100));
        await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: 200));
        await _service.ArchiveSessionsAsync(userId, new List<int> { s1.Id }, true);

        // All sessions (no filter)
        var all = await _service.GetSessionHistoryAsync(userId, 1, 20);
        Assert.Equal(2, all.TotalCount);

        // Only active
        var active = await _service.GetSessionHistoryAsync(userId, 1, 20, archived: false);
        Assert.Single(active.Items);
        Assert.Equal(200, active.Items[0].Timestamp);

        // Only archived
        var archived = await _service.GetSessionHistoryAsync(userId, 1, 20, archived: true);
        Assert.Single(archived.Items);
        Assert.Equal(100, archived.Items[0].Timestamp);
        Assert.True(archived.Items[0].IsArchived);
    }

    [Fact]
    public async Task UnarchiveSessions_ClearsFlag()
    {
        var userId = await CreateUserAsync();
        var s1 = await _service.RecordSessionAsync(userId, MakeSessionDto(timestamp: 100));
        await _service.ArchiveSessionsAsync(userId, new List<int> { s1.Id }, true);

        // Verify archived
        var session = await _db.EndlessSessions.FirstAsync(s => s.Id == s1.Id);
        Assert.True(session.IsArchived);

        // Unarchive
        await _service.ArchiveSessionsAsync(userId, new List<int> { s1.Id }, false);

        session = await _db.EndlessSessions.FirstAsync(s => s.Id == s1.Id);
        Assert.False(session.IsArchived);

        // Should appear in sync data again
        var syncData = await _service.GetSyncDataAsync(userId);
        Assert.Single(syncData.Sessions);
    }
}
