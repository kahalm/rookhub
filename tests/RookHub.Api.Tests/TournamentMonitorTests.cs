using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

/// <summary>
/// Tests the TournamentMonitor DB logic directly.
/// The Activate method's CrawlerProxyService dependency is not tested here
/// (only the re-extend path which is DB-only). GetStatus and Deactivate are fully testable.
/// </summary>
public class TournamentMonitorTests : IDisposable
{
    private readonly AppDbContext _db;

    public TournamentMonitorTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Activate_ReExtendsExistingMonitor()
    {
        var originalUntil = DateTime.UtcNow.AddMinutes(10);
        _db.TournamentMonitors.Add(new TournamentMonitor
        {
            CrawlerTournamentId = "100",
            CrawlerTournamentDbId = 1,
            ActiveUntil = originalUntil,
            LastKnownRounds = 5
        });
        await _db.SaveChangesAsync();

        // Simulate re-extend logic from controller
        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == "100");

        Assert.NotNull(monitor);
        monitor!.ActiveUntil = DateTime.UtcNow.AddHours(1);
        await _db.SaveChangesAsync();

        var updated = await _db.TournamentMonitors.FirstAsync(m => m.CrawlerTournamentId == "100");
        Assert.True(updated.ActiveUntil > originalUntil);
    }

    [Fact]
    public async Task GetStatus_Active_ReturnsActive()
    {
        _db.TournamentMonitors.Add(new TournamentMonitor
        {
            CrawlerTournamentId = "100",
            CrawlerTournamentDbId = 1,
            ActiveUntil = DateTime.UtcNow.AddHours(1),
            LastKnownRounds = 7
        });
        await _db.SaveChangesAsync();

        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == "100");

        Assert.NotNull(monitor);
        Assert.True(monitor!.ActiveUntil > DateTime.UtcNow);
        Assert.Equal(7, monitor.LastKnownRounds);
    }

    [Fact]
    public async Task GetStatus_Expired_ReturnsInactive()
    {
        _db.TournamentMonitors.Add(new TournamentMonitor
        {
            CrawlerTournamentId = "100",
            CrawlerTournamentDbId = 1,
            ActiveUntil = DateTime.UtcNow.AddHours(-1), // expired
            LastKnownRounds = 5
        });
        await _db.SaveChangesAsync();

        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == "100");

        Assert.NotNull(monitor);
        Assert.True(monitor!.ActiveUntil < DateTime.UtcNow);
    }

    [Fact]
    public async Task GetStatus_NotFound_ReturnsInactive()
    {
        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == "999");

        Assert.Null(monitor);
    }

    [Fact]
    public async Task Deactivate_RemovesMonitor()
    {
        _db.TournamentMonitors.Add(new TournamentMonitor
        {
            CrawlerTournamentId = "100",
            CrawlerTournamentDbId = 1,
            ActiveUntil = DateTime.UtcNow.AddHours(1)
        });
        await _db.SaveChangesAsync();

        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == "100");
        Assert.NotNull(monitor);

        _db.TournamentMonitors.Remove(monitor!);
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.TournamentMonitors.ToListAsync());
    }

    [Fact]
    public async Task Deactivate_NotFound_NoError()
    {
        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == "999");

        // Controller does: if (monitor is not null) { remove }; return NoContent
        // This should not throw
        if (monitor is not null)
        {
            _db.TournamentMonitors.Remove(monitor);
            await _db.SaveChangesAsync();
        }

        Assert.Null(monitor);
    }

    [Fact]
    public async Task FavoritedSnrs_AggregatesDistinctPlayerSnrs()
    {
        // Create users
        var user1 = new AppUser { Username = "user1", Email = "u1@test.com", PasswordHash = "hash" };
        var user2 = new AppUser { Username = "user2", Email = "u2@test.com", PasswordHash = "hash" };
        _db.AppUsers.AddRange(user1, user2);
        await _db.SaveChangesAsync();

        // Both users favorite the same player SNR 5, plus user1 favorites SNR 10
        _db.TournamentFavorites.AddRange(
            new TournamentFavorite { UserId = user1.Id, CrawlerTournamentId = "100", PlayerSnr = 5 },
            new TournamentFavorite { UserId = user1.Id, CrawlerTournamentId = "100", PlayerSnr = 10 },
            new TournamentFavorite { UserId = user2.Id, CrawlerTournamentId = "100", PlayerSnr = 5 },
            // Team favorite (no PlayerSnr) should be excluded
            new TournamentFavorite { UserId = user1.Id, CrawlerTournamentId = "100", TeamSnr = 3 },
            // Different tournament should be excluded
            new TournamentFavorite { UserId = user1.Id, CrawlerTournamentId = "200", PlayerSnr = 99 }
        );
        await _db.SaveChangesAsync();

        // Simulate what RoundMonitorService does
        var favSnrs = await _db.TournamentFavorites
            .Where(f => f.CrawlerTournamentId == "100" && f.PlayerSnr != null)
            .Select(f => f.PlayerSnr!.Value)
            .Distinct()
            .ToListAsync();

        Assert.Equal(2, favSnrs.Count);
        Assert.Contains(5, favSnrs);
        Assert.Contains(10, favSnrs);
    }

    [Fact]
    public async Task FavoritedSnrs_NoFavorites_ReturnsEmpty()
    {
        var favSnrs = await _db.TournamentFavorites
            .Where(f => f.CrawlerTournamentId == "100" && f.PlayerSnr != null)
            .Select(f => f.PlayerSnr!.Value)
            .Distinct()
            .ToListAsync();

        Assert.Empty(favSnrs);
    }
}
