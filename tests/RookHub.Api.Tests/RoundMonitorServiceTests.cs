using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

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
    public async Task FailedMonitorSave_DetachUnblocksSubsequentSaves()
    {
        // Regression zur CheckAllMonitorsAsync-Schleife: EIN DbContext für ALLE Monitore. Schlug
        // SaveChanges für Monitor A fehl (z. B. parallel abbestellt/gelöscht →
        // DbUpdateConcurrencyException), blieb As dirty Entity im Tracker liegen und ließ auch
        // die Saves aller FOLGENDEN Monitore scheitern (LastCheckedAt nie persistiert →
        // Benachrichtigungen feuerten im nächsten Durchlauf doppelt). Der Fix detacht die
        // gescheiterte Entität; hier wird genau diese Schleifen-Semantik nachgestellt.
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        using var db = new AppDbContext(options);
        db.TournamentMonitors.AddRange(
            new TournamentMonitor { CrawlerTournamentId = "A", CrawlerTournamentDbId = 1, ActiveUntil = DateTime.UtcNow.AddHours(1) },
            new TournamentMonitor { CrawlerTournamentId = "B", CrawlerTournamentDbId = 2, ActiveUntil = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var monitors = await db.TournamentMonitors.OrderBy(m => m.Id).ToListAsync();

        // Paralleler Unsubscribe: Monitor A verschwindet über einen ZWEITEN Kontext.
        using (var other = new AppDbContext(options))
        {
            other.TournamentMonitors.Remove(await other.TournamentMonitors.SingleAsync(m => m.CrawlerTournamentId == "A"));
            await other.SaveChangesAsync();
        }

        monitors[0].LastCheckedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        db.Entry(monitors[0]).State = EntityState.Detached;   // der Fix im catch der Schleife

        monitors[1].LastCheckedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();                           // MUSS jetzt gelingen

        var b = await db.TournamentMonitors.AsNoTracking().SingleAsync(m => m.CrawlerTournamentId == "B");
        Assert.NotNull(b.LastCheckedAt);
    }

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

    [Fact]
    public async Task NotifyNewRound_CreatesNotificationForEachSubscriber()
    {
        _db.AppUsers.AddRange(
            new AppUser { Id = 1, Username = "a", PasswordHash = "h" },
            new AppUser { Id = 2, Username = "b", PasswordHash = "h" });
        _db.TournamentSubscriptions.AddRange(
            new TournamentSubscription { UserId = 1, CrawlerTournamentId = "T1", TournamentName = "Open 2026" },
            new TournamentSubscription { UserId = 2, CrawlerTournamentId = "T1", TournamentName = "Open 2026" },
            new TournamentSubscription { UserId = 1, CrawlerTournamentId = "OTHER", TournamentName = "Andere" });
        await _db.SaveChangesAsync();

        await RoundMonitorService.NotifyNewRoundAsync(
            _db, new NotificationService(_db), "T1", tournamentDbId: 42, round: 5, default);

        var notes = await _db.Notifications.ToListAsync();
        Assert.Equal(2, notes.Count);   // nur die beiden T1-Abonnenten
        Assert.All(notes, n => Assert.Equal(NotificationType.TournamentNewRound, n.Type));
        Assert.All(notes, n => Assert.Equal("/tournaments/42", n.Link));
        Assert.All(notes, n => Assert.Contains("Open 2026", n.DataJson ?? ""));
        Assert.Contains(notes, n => n.UserId == 1);
        Assert.Contains(notes, n => n.UserId == 2);
    }

    [Fact]
    public async Task NotifyNewRound_NoSubscribers_NoOp()
    {
        await RoundMonitorService.NotifyNewRoundAsync(
            _db, new NotificationService(_db), "NONE", tournamentDbId: 1, round: 1, default);
        Assert.Empty(await _db.Notifications.ToListAsync());
    }
}
