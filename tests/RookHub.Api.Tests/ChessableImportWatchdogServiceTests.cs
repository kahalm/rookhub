using Microsoft.EntityFrameworkCore;
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
}
