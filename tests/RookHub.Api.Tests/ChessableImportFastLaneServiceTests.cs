using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Entscheidungslogik der schnellen (gecachten) Import-Lane: sie greift NUR, wenn ein voll-gecachter
/// Import wartet (Phase "queued", FullyCached==true) UND gerade kein gecachter läuft (seriell).
/// Nicht-gecachte/unklassifizierte (null) Jobs ignoriert sie (die laufen in der Download-Lane).
/// </summary>
public class ChessableImportFastLaneServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessableImportFastLaneServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync(params (string status, string phase, bool? fullyCached)[] jobs)
    {
        foreach (var (status, phase, fc) in jobs)
            _db.ChessableImports.Add(new ChessableImport
            {
                UserId = 5, Bid = "b", CourseName = "C", Target = "repertoire",
                Status = status, Phase = phase, FullyCached = fc, CreatedAt = DateTime.UtcNow,
            });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ready_WhenCachedJobQueuedAndNoneInflight()
    {
        await SeedAsync(("running", "queued", true), ("completed", "done", true));
        Assert.True(await ChessableImportFastLaneService.IsFastLaneReadyAsync(_db));
    }

    [Theory]
    [InlineData("claimed")]
    [InlineData("fetching")]
    [InlineData("importing")]
    public async Task NotReady_WhenACachedJobIsInflight(string inflightPhase)
    {
        await SeedAsync(("running", "queued", true), ("running", inflightPhase, true));
        Assert.False(await ChessableImportFastLaneService.IsFastLaneReadyAsync(_db));
    }

    [Fact]
    public async Task NotReady_WhenOnlyDownloadOrUnclassifiedQueued()
    {
        // Nicht-gecacht (false) und unklassifiziert (null) gehören NICHT in die Fast-Lane.
        await SeedAsync(("running", "queued", false), ("running", "queued", null));
        Assert.False(await ChessableImportFastLaneService.IsFastLaneReadyAsync(_db));
    }

    [Fact]
    public async Task NotReady_OnEmptyQueue()
    {
        Assert.False(await ChessableImportFastLaneService.IsFastLaneReadyAsync(_db));
    }
}
