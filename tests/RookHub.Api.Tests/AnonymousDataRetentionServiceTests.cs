using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Der anonyme Endless-Pfad ist offen und die Session-Id ein frei wählbares Feld: jede neue
/// Kennung legt eine Zeile mit bis zu 1 MB Spielstand an. Ohne Verfallsdatum wuchs das unbegrenzt.</summary>
public class AnonymousDataRetentionServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public AnonymousDataRetentionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Prune_RemovesOldAnonymousRows_KeepsFreshOnesAndAllUserRows()
    {
        var old = DateTime.UtcNow.AddDays(-90);
        var fresh = DateTime.UtcNow.AddDays(-1);
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "u", PasswordHash = "x" });
        _db.EndlessProgresses.AddRange(
            new EndlessProgress { AnonymousSessionId = "alt", UpdatedAt = old, ActiveGameState = "{}" },
            new EndlessProgress { AnonymousSessionId = "neu", UpdatedAt = fresh },
            // Angemeldeter Nutzer: bleibt IMMER, auch wenn die Zeile alt ist (das ist seine Statistik).
            new EndlessProgress { UserId = 7, UpdatedAt = old });
        _db.EndlessSessions.AddRange(
            new EndlessSession { AnonymousSessionId = "alt", CreatedAt = old, Timestamp = 1 },
            new EndlessSession { AnonymousSessionId = "neu", CreatedAt = fresh, Timestamp = 2 },
            new EndlessSession { UserId = 7, CreatedAt = old, Timestamp = 3 });
        await _db.SaveChangesAsync();

        var removed = await AnonymousDataRetentionService.PruneAsync(
            _db, DateTime.UtcNow - AnonymousDataRetentionService.AnonymousEndlessMaxAge);

        Assert.Equal(2, removed);
        Assert.Equal(new[] { "neu" }, await _db.EndlessProgresses
            .Where(p => p.UserId == null).Select(p => p.AnonymousSessionId).ToArrayAsync());
        Assert.Equal(new[] { "neu" }, await _db.EndlessSessions
            .Where(s => s.UserId == null).Select(s => s.AnonymousSessionId).ToArrayAsync());
        Assert.Equal(1, await _db.EndlessProgresses.CountAsync(p => p.UserId == 7));
        Assert.Equal(1, await _db.EndlessSessions.CountAsync(s => s.UserId == 7));
    }

    [Fact]
    public async Task Prune_EmptyDatabase_IsNoOp()
        => Assert.Equal(0, await AnonymousDataRetentionService.PruneAsync(_db, DateTime.UtcNow));
}
