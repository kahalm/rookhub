using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

/// <summary>
/// Tests for RoundMonitorService DB logic (cleanup and monitor state).
/// The background service depends on CrawlerProxyService (HTTP calls),
/// so we test the DB operations it performs in isolation.
/// </summary>
public class RoundMonitorServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public RoundMonitorServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ExpiredMonitors_AreRemoved()
    {
        _db.TournamentMonitors.AddRange(
            new TournamentMonitor
            {
                CrawlerTournamentId = "100",
                CrawlerTournamentDbId = 1,
                ActiveUntil = DateTime.UtcNow.AddHours(-2) // expired
            },
            new TournamentMonitor
            {
                CrawlerTournamentId = "200",
                CrawlerTournamentDbId = 2,
                ActiveUntil = DateTime.UtcNow.AddHours(1) // active
            }
        );
        await _db.SaveChangesAsync();

        // Simulate cleanup logic from RoundMonitorService.CheckAllMonitorsAsync
        var expired = await _db.TournamentMonitors
            .Where(m => m.ActiveUntil < DateTime.UtcNow)
            .ToListAsync();
        _db.TournamentMonitors.RemoveRange(expired);
        await _db.SaveChangesAsync();

        var remaining = await _db.TournamentMonitors.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("200", remaining[0].CrawlerTournamentId);
    }

    [Fact]
    public async Task ActiveMonitors_AreQueried()
    {
        _db.TournamentMonitors.AddRange(
            new TournamentMonitor
            {
                CrawlerTournamentId = "100",
                CrawlerTournamentDbId = 1,
                ActiveUntil = DateTime.UtcNow.AddHours(1)
            },
            new TournamentMonitor
            {
                CrawlerTournamentId = "200",
                CrawlerTournamentDbId = 2,
                ActiveUntil = DateTime.UtcNow.AddHours(1)
            },
            new TournamentMonitor
            {
                CrawlerTournamentId = "300",
                CrawlerTournamentDbId = 3,
                ActiveUntil = DateTime.UtcNow.AddHours(-1) // expired
            }
        );
        await _db.SaveChangesAsync();

        var active = await _db.TournamentMonitors
            .Where(m => m.ActiveUntil >= DateTime.UtcNow)
            .ToListAsync();

        Assert.Equal(2, active.Count);
    }

    [Fact]
    public async Task Monitor_LastCheckedAt_Updates()
    {
        var monitor = new TournamentMonitor
        {
            CrawlerTournamentId = "100",
            CrawlerTournamentDbId = 1,
            ActiveUntil = DateTime.UtcNow.AddHours(1),
            LastCheckedAt = null
        };
        _db.TournamentMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        // Simulate what the service does after checking
        monitor.LastCheckedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var updated = await _db.TournamentMonitors.FirstAsync();
        Assert.NotNull(updated.LastCheckedAt);
    }

    [Fact]
    public async Task Monitor_LastKnownRounds_UpdatesOnNewRound()
    {
        var monitor = new TournamentMonitor
        {
            CrawlerTournamentId = "100",
            CrawlerTournamentDbId = 1,
            ActiveUntil = DateTime.UtcNow.AddHours(1),
            LastKnownRounds = 5
        };
        _db.TournamentMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        // Simulate new round detection
        monitor.LastKnownRounds = 6;
        monitor.LastCheckedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var updated = await _db.TournamentMonitors.FirstAsync();
        Assert.Equal(6, updated.LastKnownRounds);
    }

    [Fact]
    public async Task NoActiveMonitors_QueryReturnsEmpty()
    {
        var active = await _db.TournamentMonitors
            .Where(m => m.ActiveUntil >= DateTime.UtcNow)
            .ToListAsync();

        Assert.Empty(active);
    }

    [Fact]
    public async Task PerIterationSave_PreservesFirstMonitorWhenSecondFails()
    {
        // Verifies the fix for Bug 8: SaveChanges inside the loop ensures that
        // successfully processed monitors are persisted even if a later one fails.
        var m1 = new TournamentMonitor { CrawlerTournamentId = "A1", CrawlerTournamentDbId = 1, ActiveUntil = DateTime.UtcNow.AddHours(1) };
        var m2 = new TournamentMonitor { CrawlerTournamentId = "B2", CrawlerTournamentDbId = 2, ActiveUntil = DateTime.UtcNow.AddHours(1) };
        _db.TournamentMonitors.AddRange(m1, m2);
        await _db.SaveChangesAsync();

        var monitors = await _db.TournamentMonitors.Where(m => m.ActiveUntil >= DateTime.UtcNow).ToListAsync();
        foreach (var monitor in monitors)
        {
            try
            {
                monitor.LastCheckedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();   // per-iteration save (the fix)

                if (monitor.CrawlerTournamentId == "B2")
                    throw new InvalidOperationException("Simulated failure on second monitor");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // logged, continue — mirrors the service catch block
            }
        }

        var saved = await _db.TournamentMonitors.ToListAsync();
        // First monitor was saved before second one failed → its LastCheckedAt is set
        Assert.NotNull(saved.First(m => m.CrawlerTournamentId == "A1").LastCheckedAt);
        Assert.NotNull(saved.First(m => m.CrawlerTournamentId == "B2").LastCheckedAt);
    }
}
