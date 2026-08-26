using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Slot-Logik der schnellen (gecachten) Import-Lane: sie darf bis zu <c>MaxParallel</c> voll-gecachte
/// Importe (Phase "queued", FullyCached==true) GLEICHZEITIG anstoßen, abzüglich der bereits laufenden.
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

    private async Task SeedAsync(params (ChessableImportStatus status, ChessableImportPhase phase, bool? fullyCached)[] jobs)
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
    public async Task FreeSlots_CachedQueuedNoneInflight_UpToMaxOrQueued()
    {
        await SeedAsync((ChessableImportStatus.Running, ChessableImportPhase.Queued, true), (ChessableImportStatus.Running, ChessableImportPhase.Queued, true),
                        (ChessableImportStatus.Running, ChessableImportPhase.Queued, true), (ChessableImportStatus.Running, ChessableImportPhase.Queued, true));
        Assert.Equal(3, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 3)); // min(3, 4 wartende)
        Assert.Equal(1, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 1)); // seriell (MaxParallel=1)
    }

    [Fact]
    public async Task FreeSlots_SubtractsInflightFromMax()
    {
        await SeedAsync((ChessableImportStatus.Running, ChessableImportPhase.Queued, true), (ChessableImportStatus.Running, ChessableImportPhase.Queued, true),
                        (ChessableImportStatus.Running, ChessableImportPhase.Claimed, true), (ChessableImportStatus.Running, ChessableImportPhase.Importing, true));
        Assert.Equal(1, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 3)); // min(3-2, 2 wartende)
    }

    [Fact]
    public async Task FreeSlots_ZeroWhenMaxReached()
    {
        await SeedAsync((ChessableImportStatus.Running, ChessableImportPhase.Queued, true),
                        (ChessableImportStatus.Running, ChessableImportPhase.Fetching, true), (ChessableImportStatus.Running, ChessableImportPhase.Importing, true), (ChessableImportStatus.Running, ChessableImportPhase.Claimed, true));
        Assert.Equal(0, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 3)); // 3 laufen bereits
    }

    [Fact]
    public async Task FreeSlots_IgnoresDownloadAndUnclassified()
    {
        // Nicht-gecacht (false) und unklassifiziert (null) gehören NICHT in die Fast-Lane.
        await SeedAsync((ChessableImportStatus.Running, ChessableImportPhase.Queued, false), (ChessableImportStatus.Running, ChessableImportPhase.Queued, null));
        Assert.Equal(0, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 3));
    }

    [Fact]
    public async Task FreeSlots_ZeroOnEmptyQueue()
    {
        Assert.Equal(0, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 3));
    }
}
