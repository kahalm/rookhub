using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Gemeinsame Teilen-Mechanik (Kurs UND Repertoire nutzen <see cref="ShareServiceBatch"/>).
/// Wichtigster Punkt: der Unique-Index-Race darf GENAU EINEN zweiten Anlauf auslösen — vorher
/// rekursierte der Catch-Zweig, was bei einem dauerhaften DB-Fehler in den StackOverflow lief.
/// </summary>
public class ShareServiceBatchTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;

    /// <summary>DbContext, dessen SaveChangesAsync die vorgegebenen Fehler der Reihe nach wirft.</summary>
    private sealed class FlakySaveDbContext(DbContextOptions<AppDbContext> options, params Exception[] failures) : AppDbContext(options)
    {
        private readonly Queue<Exception> _failures = new(failures);
        public int SaveCalls { get; private set; }
        public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCalls++;
            if (_failures.Count > 0) throw _failures.Dequeue();
            return base.SaveChangesAsync(ct);
        }
    }

    private static DbUpdateException Duplicate() =>
        new("save failed", new Exception("Duplicate entry '1-2' for key 'IX_CourseShares_BookId_RecipientId'"));

    private static DbUpdateException Transient() =>
        new("save failed", new Exception("Lock wait timeout exceeded; try restarting transaction"));

    public ShareServiceBatchTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);
        _db.AppUsers.AddRange(
            new AppUser { Id = 1, Username = "owner", PasswordHash = "x" },
            new AppUser { Id = 2, Username = "friend", PasswordHash = "x" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private FlakySaveDbContext Flaky(params Exception[] failures) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, failures);

    private static Task<HashSet<int>> None(List<int> _) => Task.FromResult(new HashSet<int>());

    [Fact]
    public async Task UniqueRace_RetriesExactlyOnce_AndTreatsRecipientAsDuplicate()
    {
        using var db = Flaky(Duplicate());
        var loads = 0;
        var notified = false;

        var outcome = await ShareServiceBatch.ShareAsync(db, new FriendService(db, new NotificationService(db)),
            ownerId: 1, new List<int> { 2 }, isAdmin: true,
            loadAlreadySharedAsync: _ => Task.FromResult(loads++ == 0 ? new HashSet<int>() : new HashSet<int> { 2 }),
            addShare: rid => db.CourseShares.Add(new CourseShare { BookId = 7, OwnerId = 1, RecipientId = rid, SharedAt = DateTime.UtcNow }),
            onSharedAsync: _ => { notified = true; return Task.CompletedTask; });

        Assert.Equal(2, loads);                 // zweiter Durchlauf hat die Lage neu geladen
        Assert.Equal(0, outcome.Shared);
        Assert.Contains(outcome.Skipped, s => s.UserId == 2 && s.Reason == "duplicate");
        Assert.False(notified);                 // nichts neu geteilt → keine Benachrichtigung
    }

    [Fact]
    public async Task DauerhafterUniqueFehler_BrichtNachZweitemVersuchAb()
    {
        // Regression: früher rekursierte der Catch-Zweig bei jedem weiteren Konflikt erneut.
        using var db = Flaky(Duplicate(), Duplicate());

        await Assert.ThrowsAsync<DbUpdateException>(() => ShareServiceBatch.ShareAsync(db,
            new FriendService(db, new NotificationService(db)), ownerId: 1, new List<int> { 2 }, isAdmin: true,
            loadAlreadySharedAsync: None,
            addShare: rid => db.CourseShares.Add(new CourseShare { BookId = 7, OwnerId = 1, RecipientId = rid, SharedAt = DateTime.UtcNow }),
            onSharedAsync: _ => Task.CompletedTask));

        Assert.Equal(2, db.SaveCalls);
    }

    [Fact]
    public async Task TransienterDbFehler_WirdNichtGeschluckt()
    {
        // Kein Duplikat → kein zweiter Anlauf, kein „stiller Erfolg": der Fehler muss hochblubbern.
        using var db = Flaky(Transient());

        await Assert.ThrowsAsync<DbUpdateException>(() => ShareServiceBatch.ShareAsync(db,
            new FriendService(db, new NotificationService(db)), ownerId: 1, new List<int> { 2 }, isAdmin: true,
            loadAlreadySharedAsync: None,
            addShare: rid => db.CourseShares.Add(new CourseShare { BookId = 7, OwnerId = 1, RecipientId = rid, SharedAt = DateTime.UtcNow }),
            onSharedAsync: _ => Task.CompletedTask));

        Assert.Equal(1, db.SaveCalls);
    }
}
