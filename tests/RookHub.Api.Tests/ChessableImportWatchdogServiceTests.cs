using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Entscheidungslogik des Import-Watchdogs: er soll den Drain NUR anstoßen, wenn wartende Importe
/// existieren (Phase "queued") UND keiner aktiv ist (Phase "claimed"/"fetching"/"importing").
/// Bildet den Vorfall 2026-06-29 ab (82 wartende, kein aktiver → Drain stand).
/// </summary>
public class ChessableImportWatchdogServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessableImportWatchdogServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync(params (string status, string phase)[] jobs)
    {
        foreach (var (status, phase) in jobs)
            _db.ChessableImports.Add(new ChessableImport
            {
                UserId = 5, Bid = "b", CourseName = "C", Target = "repertoire",
                Status = status, Phase = phase, CreatedAt = DateTime.UtcNow,
            });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task IsDrainStalled_True_WhenJobsQueuedButNoneInflight()
    {
        // Vorfall-Lage: viele wartende, keiner aktiv.
        await SeedAsync(("running", "queued"), ("running", "queued"), ("completed", "done"));
        Assert.True(await ChessableImportWatchdogService.IsDrainStalledAsync(_db));
    }

    [Theory]
    [InlineData("claimed")]
    [InlineData("fetching")]
    [InlineData("importing")]
    public async Task IsDrainStalled_False_WhenAJobIsInflight(string inflightPhase)
    {
        // Es läuft etwas → der normale Drain arbeitet, Watchdog hält sich raus.
        await SeedAsync(("running", "queued"), ("running", inflightPhase));
        Assert.False(await ChessableImportWatchdogService.IsDrainStalledAsync(_db));
    }

    [Fact]
    public async Task IsDrainStalled_False_WhenNothingQueued()
    {
        await SeedAsync(("completed", "done"), ("failed", "fetching"));
        Assert.False(await ChessableImportWatchdogService.IsDrainStalledAsync(_db));
    }

    [Fact]
    public async Task IsDrainStalled_False_OnEmptyQueue()
    {
        Assert.False(await ChessableImportWatchdogService.IsDrainStalledAsync(_db));
    }

    private ChessableImportWatchdogService Watchdog()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_db);
        var provider = services.BuildServiceProvider();
        return new ChessableImportWatchdogService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChessableImportWatchdogService>.Instance);
    }

    [Fact]
    public async Task ResumeExpiredRateLimited_FlipsBackToQueued_WhenPauseElapsed()
    {
        var stale = new ChessableImport
        {
            UserId = 1, Bid = "b", CourseName = "C", Target = "repertoire",
            Status = "paused", Phase = "rate-limited", RateLimitedAt = DateTime.UtcNow.AddHours(-25),
        };
        _db.ChessableImports.Add(stale);
        await _db.SaveChangesAsync();

        var count = await Watchdog().ResumeExpiredRateLimitedAsync(_db, CancellationToken.None);

        Assert.Equal(1, count);
        await _db.Entry(stale).ReloadAsync();
        Assert.Equal("running", stale.Status);
        Assert.Equal("queued", stale.Phase);
        Assert.Null(stale.RateLimitedAt);
    }

    [Fact]
    public async Task ResumeExpiredRateLimited_LeavesRecentPauseUntouched()
    {
        var recent = new ChessableImport
        {
            UserId = 1, Bid = "b", CourseName = "C", Target = "repertoire",
            Status = "paused", Phase = "rate-limited", RateLimitedAt = DateTime.UtcNow.AddHours(-1),
        };
        _db.ChessableImports.Add(recent);
        await _db.SaveChangesAsync();

        var count = await Watchdog().ResumeExpiredRateLimitedAsync(_db, CancellationToken.None);

        Assert.Equal(0, count);
        await _db.Entry(recent).ReloadAsync();
        Assert.Equal("paused", recent.Status);
        Assert.Equal("rate-limited", recent.Phase);
    }

    [Fact]
    public async Task ResumeExpiredRateLimited_IgnoresOtherPausedPhases()
    {
        // Bearer-blocked pausierte Importe werden NICHT vom Rate-Limit-Resume angefasst
        // (die nimmt nur ein erfolgreicher „Testen"-Klick wieder auf).
        var bearerBlocked = new ChessableImport
        {
            UserId = 1, Bid = "b", CourseName = "C", Target = "repertoire",
            Status = "paused", Phase = "bearer-blocked", RateLimitedAt = DateTime.UtcNow.AddHours(-25),
        };
        _db.ChessableImports.Add(bearerBlocked);
        await _db.SaveChangesAsync();

        var count = await Watchdog().ResumeExpiredRateLimitedAsync(_db, CancellationToken.None);

        Assert.Equal(0, count);
        await _db.Entry(bearerBlocked).ReloadAsync();
        Assert.Equal("bearer-blocked", bearerBlocked.Phase);
    }

    [Theory]
    [InlineData("claimed")]
    [InlineData("fetching")]
    [InlineData("importing")]
    public async Task ReclaimOrphanedInflight_RequeuesZombie_AfterSecondSighting(string phase)
    {
        // Zombie: DB sagt „läuft", aber kein Treiber im Prozess (Job hat seinen Task verloren, ohne
        // einen Terminal-Status zu schreiben) → Lane bliebe sonst dauerhaft belegt.
        // Feste, hohe Id: die Treiber-Liste ist prozessweit statisch — kleine Auto-Ids könnten sich mit
        // parallel laufenden Testklassen überschneiden.
        var zombie = new ChessableImport
        {
            Id = 990001, UserId = 1, Bid = "b1", CourseName = "C", Target = "book",
            Status = "running", Phase = phase, Attempts = 2, CreatedAt = DateTime.UtcNow,
        };
        _db.ChessableImports.Add(zombie);
        await _db.SaveChangesAsync();
        var wd = Watchdog();
        wd.OrphanGrace = TimeSpan.Zero;

        // Erste Sichtung: nur beobachten (Schutz gegen das Registrierungs-Fenster frisch geclaimter Jobs).
        Assert.Equal(0, await wd.ReclaimOrphanedInflightAsync(_db, CancellationToken.None));
        Assert.Equal(phase, (await _db.ChessableImports.FindAsync(zombie.Id))!.Phase);

        Assert.Equal(1, await wd.ReclaimOrphanedInflightAsync(_db, CancellationToken.None));
        await _db.Entry(zombie).ReloadAsync();
        Assert.Equal("queued", zombie.Phase);
        Assert.Equal(2, zombie.Attempts);   // zählt weiter gegen MaxAttempts
    }

    [Fact]
    public async Task ReclaimOrphanedInflight_LeavesLocallyDrivenImportUntouched()
    {
        var driven = new ChessableImport
        {
            Id = 990002, UserId = 1, Bid = "b2", CourseName = "C", Target = "book",
            Status = "running", Phase = "fetching", CreatedAt = DateTime.UtcNow,
        };
        _db.ChessableImports.Add(driven);
        await _db.SaveChangesAsync();
        var wd = Watchdog();
        wd.OrphanGrace = TimeSpan.Zero;

        using (ChessableImportService.TrackInflight(driven.Id))
        {
            Assert.Equal(0, await wd.ReclaimOrphanedInflightAsync(_db, CancellationToken.None));
            Assert.Equal(0, await wd.ReclaimOrphanedInflightAsync(_db, CancellationToken.None));
            await _db.Entry(driven).ReloadAsync();
            Assert.Equal("fetching", driven.Phase);
        }

        // Treiber weg → ab jetzt gilt derselbe Job als verwaist (wieder zwei Sichtungen).
        Assert.Equal(0, await wd.ReclaimOrphanedInflightAsync(_db, CancellationToken.None));
        Assert.Equal(1, await wd.ReclaimOrphanedInflightAsync(_db, CancellationToken.None));
        await _db.Entry(driven).ReloadAsync();
        Assert.Equal("queued", driven.Phase);
    }

    [Fact]
    public async Task ReclaimOrphanedInflight_IgnoresWaitingAndFinishedImports()
    {
        await SeedAsync(("running", "queued"), ("completed", "done"), ("paused", "bearer-blocked"));
        var wd = Watchdog();
        wd.OrphanGrace = TimeSpan.Zero;

        Assert.Equal(0, await wd.ReclaimOrphanedInflightAsync(_db, CancellationToken.None));
        Assert.Equal(0, await wd.ReclaimOrphanedInflightAsync(_db, CancellationToken.None));
    }
}
