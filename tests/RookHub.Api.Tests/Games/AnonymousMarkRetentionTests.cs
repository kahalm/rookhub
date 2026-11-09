using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>R4-003: anonyme Markierungen und Notiz-Fassungen verfallen nach der Frist der übrigen anonymen Daten
/// (<see cref="RetentionOptions.AnonymousDays"/>) und haben denselben Gesamtdeckel; Zeilen mit Konto bleiben. Vorher räumte
/// niemand sie je ab.</summary>
public class AnonymousMarkRetentionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private void Seed(int ageDays, bool anonymous, string key)
    {
        var at = Now.AddDays(-ageDays);
        int? user = anonymous ? null : 7;
        var sid = anonymous ? "v-" + key : null;
        _db.MarkedPositions.Add(new MarkedPosition { UserId = user, AnonSessionId = sid, PositionKey = key, Fen = "x", Comment = "frei getippt", CreatedAt = at });
        _db.MarkedPositionNoteRevisions.Add(new MarkedPositionNoteRevision { UserId = user, AnonSessionId = sid, PositionKey = key, Comment = "alt", CreatedAt = at });
    }

    [Fact]
    public async Task AlteAnonyme_weg_frischeUndKontozeilen_bleiben()
    {
        Seed(91, anonymous: true, "alt");
        Seed(89, anonymous: true, "neu");
        Seed(400, anonymous: false, "konto");
        await _db.SaveChangesAsync();

        var removed = await AnonymousMarkRetention.PruneAsync(_db, Now, new RetentionOptions());

        Assert.Equal(2, removed);
        Assert.Equal(["konto", "neu"], (await _db.MarkedPositions.Select(m => m.PositionKey).ToListAsync()).Order());
        Assert.Equal(["konto", "neu"], (await _db.MarkedPositionNoteRevisions.Select(m => m.PositionKey).ToListAsync()).Order());
    }

    [Fact]
    public async Task UeberDemDeckel_gehenDieAeltestenAnonymen()
    {
        for (var i = 0; i < 5; i++) Seed(i + 1, anonymous: true, $"k{i}");
        Seed(10, anonymous: false, "konto");
        await _db.SaveChangesAsync();

        await AnonymousMarkRetention.PruneAsync(_db, Now, new RetentionOptions { AnonymousCap = 3 }, chunkSize: 2);

        Assert.Equal(["k0", "k1", "k2", "konto"], (await _db.MarkedPositions.Select(m => m.PositionKey).ToListAsync()).Order());
        Assert.Equal(4, await _db.MarkedPositionNoteRevisions.CountAsync());
    }
}
