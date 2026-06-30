using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Circuit-Breaker für den Chessable-Bearer: Klassifikation „Bearer tot vs. IP-Block",
/// Öffnen/Schließen, und Wiederaufnahme der wegen des Breakers pausierten Importe.
/// </summary>
public class ChessableBearerBreakerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CountingQueue _queue = new();
    private readonly ChessableBearerBreaker _breaker;

    public ChessableBearerBreakerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _breaker = new ChessableBearerBreaker(_db, _queue, NullLogger<ChessableBearerBreaker>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // --- IsBearerFatal: die eigentliche Klassifikationslogik ---

    [Theory]
    [InlineData("Chessable: User is banned or deleted")]
    [InlineData("Chessable: account banned")]
    [InlineData("Chessable-Token ist abgelaufen — bitte den Bearer neu hinterlegen.")]
    [InlineData("Chessable-Token abgelaufen/ungültig (Expired token) — bitte den Bearer neu hinterlegen.")]
    public void IsBearerFatal_DeadBearer_True(string message)
        => Assert.True(ChessableBearerBreaker.IsBearerFatal(message));

    [Theory]
    // IP-/Cloudflare-/VPN-Block → NICHT der Bearer, Breaker bleibt zu.
    [InlineData("Zugriff von Chessable/Cloudflare blockiert (HTTP 403). Der Token ist nicht abgelaufen → sehr wahrscheinlich ist die VPN-Ausgangs-IP gesperrt: IP rotieren bzw. VPN-Server wechseln.")]
    [InlineData("Chessable lieferte kein gültiges JSON (Token ungültig oder Zugriff blockiert) — bitte den Bearer neu hinterlegen bzw. die VPN-IP prüfen.")]
    [InlineData("Course fetch hit proxy tunnel 503 (VPN reconnecting), retry 1/3")]
    [InlineData("Connection refused")]
    [InlineData("")]
    [InlineData(null)]
    public void IsBearerFatal_NotTheBearer_False(string? message)
        => Assert.False(ChessableBearerBreaker.IsBearerFatal(message));

    // --- TripAsync ---

    [Fact]
    public async Task TripAsync_OpensBreaker_AndIsIdempotent()
    {
        await SeedCredentialAsync(7);

        var first = await _breaker.TripAsync(7, "Chessable: User is banned or deleted");
        Assert.True(first);
        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 7);
        Assert.NotNull(cred.BlockedAt);
        Assert.Contains("banned", cred.BlockedReason);

        // Zweites Auslösen verändert die ursprüngliche Ursache nicht und meldet „nicht neu geöffnet".
        var second = await _breaker.TripAsync(7, "andere Ursache");
        Assert.False(second);
        cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 7);
        Assert.Contains("banned", cred.BlockedReason);
        Assert.True(await _breaker.IsOpenAsync(7));
    }

    [Fact]
    public async Task TripAsync_NoCredential_NoOp()
        => Assert.False(await _breaker.TripAsync(999, "Chessable: User is banned or deleted"));

    // --- ClearAndResumeAsync ---

    [Fact]
    public async Task ClearAndResumeAsync_ResumesBearerBlockedImports_AndQueuesDownloads()
    {
        await SeedCredentialAsync(7, blocked: true);
        // Ein Download-Import (FullyCached=false) + ein Fast-Lane-Import (FullyCached=true), beide
        // wegen des Breakers pausiert. Ein vom USER pausierter Import darf NICHT mitgenommen werden.
        var dl = await SeedImportAsync(7, status: "paused", phase: "bearer-blocked", fullyCached: false);
        var fast = await SeedImportAsync(7, status: "paused", phase: "bearer-blocked", fullyCached: true);
        var userPaused = await SeedImportAsync(7, status: "paused", phase: "fetching", fullyCached: false);

        var resumed = await _breaker.ClearAndResumeAsync(7);

        Assert.Equal(2, resumed);
        Assert.Null((await _db.ChessableCredentials.SingleAsync(c => c.UserId == 7)).BlockedAt);
        Assert.Equal(("running", "queued"), await StatusOf(dl.Id));
        Assert.Equal(("running", "queued"), await StatusOf(fast.Id));
        // Vom User pausierter Import bleibt unangetastet.
        Assert.Equal(("paused", "fetching"), await StatusOf(userPaused.Id));
        // Nur der Download-Import bekommt ein Queue-Ticket (Fast-Lane treibt ihr eigener Loop).
        Assert.Equal(1, _queue.Count);
    }

    [Fact]
    public async Task ClearAndResumeAsync_NotOpen_NoOp()
    {
        await SeedCredentialAsync(7, blocked: false);
        Assert.Equal(0, await _breaker.ClearAndResumeAsync(7));
        Assert.Equal(0, _queue.Count);
    }

    [Fact]
    public async Task ClearAndResumeAsync_AdminImport_MatchesByBearerUser()
    {
        // Admin (UserId=1) importiert mit dem Bearer von User 7 (BearerUserId=7); Breaker hängt an 7.
        await SeedCredentialAsync(7, blocked: true);
        var adminImp = await SeedImportAsync(1, status: "paused", phase: "bearer-blocked", fullyCached: false, bearerUserId: 7);

        var resumed = await _breaker.ClearAndResumeAsync(7);

        Assert.Equal(1, resumed);
        Assert.Equal(("running", "queued"), await StatusOf(adminImp.Id));
    }

    // --- Helpers ---

    private async Task SeedCredentialAsync(int userId, bool blocked = false)
    {
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId))
            _db.AppUsers.Add(new AppUser { Id = userId, Username = $"u{userId}", PasswordHash = "x" });
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = userId,
            EncryptedBearer = "enc",
            BlockedAt = blocked ? DateTime.UtcNow : null,
            BlockedReason = blocked ? "Chessable: User is banned or deleted" : null,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<ChessableImport> SeedImportAsync(
        int userId, string status, string phase, bool fullyCached, int? bearerUserId = null)
    {
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId))
            _db.AppUsers.Add(new AppUser { Id = userId, Username = $"u{userId}", PasswordHash = "x" });
        var imp = new ChessableImport
        {
            UserId = userId, BearerUserId = bearerUserId, Bid = "b1", CourseName = "C", Target = "repertoire",
            Status = status, Phase = phase, FullyCached = fullyCached, CreatedAt = DateTime.UtcNow,
        };
        _db.ChessableImports.Add(imp);
        await _db.SaveChangesAsync();
        return imp;
    }

    private async Task<(string status, string phase)> StatusOf(int id)
    {
        var imp = await _db.ChessableImports.FindAsync(id);
        return (imp!.Status, imp.Phase);
    }

    private sealed class CountingQueue : IBackgroundTaskQueue
    {
        public int Count { get; private set; }
        public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
        public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }
}
