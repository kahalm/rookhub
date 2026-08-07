using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// <see cref="DbIdempotency.SaveIgnoringUniqueRaceAsync"/> ersetzt das früher überall kopierte
/// »catch (DbUpdateException) { ChangeTracker.Clear(); }«. Regression: jenes Muster schluckte JEDEN
/// Persistenzfehler — ein fehlgeschlagener Solve-Insert (FK, „data too long", Timeout) ging still
/// verloren, während die Antwort Erfolg meldete.
/// </summary>
public class DbIdempotencyTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;

    private sealed class FlakySaveDbContext(DbContextOptions<AppDbContext> options, Exception failure) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken ct = default) => throw failure;
    }

    public DbIdempotencyTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);
    }

    public void Dispose() => _db.Dispose();

    private FlakySaveDbContext Flaky(Exception failure) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, failure);

    [Fact]
    public async Task Speichert_Normalfall_UndMeldetTrue()
    {
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "a", PasswordHash = "x" });
        Assert.True(await _db.SaveIgnoringUniqueRaceAsync());
        Assert.Equal(1, await _db.AppUsers.CountAsync());
    }

    [Fact]
    public async Task UniqueRace_WirdGeschluckt_UndChangeTrackerGeleert()
    {
        using var db = Flaky(new DbUpdateException("save failed",
            new Exception("Duplicate entry '1-2' for key 'IX_CoursePuzzleResults_UserId_BookPuzzleId'")));
        db.AppUsers.Add(new AppUser { Id = 2, Username = "b", PasswordHash = "x" });

        Assert.False(await db.SaveIgnoringUniqueRaceAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task EchterDbFehler_WirdDurchgereicht()
    {
        using var db = Flaky(new DbUpdateException("save failed",
            new Exception("Data too long for column 'Pgn' at row 1")));
        db.AppUsers.Add(new AppUser { Id = 3, Username = "c", PasswordHash = "x" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveIgnoringUniqueRaceAsync());
    }
}
