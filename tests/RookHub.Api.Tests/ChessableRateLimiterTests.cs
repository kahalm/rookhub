using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Tages-Zeilenlimit pro Chessable-Bearer-User: Fenster-Auffrischung, Limit-Prüfung,
/// Verbuchung tatsächlich abgerufener Zeilen.
/// </summary>
public class ChessableRateLimiterTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessableRateLimiterTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private ChessableRateLimiter Limiter(int? dailyLimit = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Chessable:DailyLineLimitPerUser"] = dailyLimit?.ToString(),
        }).Build();
        return new ChessableRateLimiter(_db, config);
    }

    [Fact]
    public void DailyLimit_DefaultsTo2000_WhenNotConfigured()
        => Assert.Equal(2000, Limiter().DailyLimit);

    [Fact]
    public void DailyLimit_UsesConfiguredValue()
        => Assert.Equal(500, Limiter(500).DailyLimit);

    [Fact]
    public void EnsureFreshWindow_InitializesWindow_WhenNeverSet()
    {
        var cred = new ChessableCredential { UserId = 1, EncryptedBearer = "enc", RateLimitLinesUsed = 42 };
        var now = DateTime.UtcNow;

        Limiter().EnsureFreshWindow(cred, now);

        Assert.Equal(now, cred.RateLimitWindowStartedAt);
        Assert.Equal(0, cred.RateLimitLinesUsed);
    }

    [Fact]
    public void EnsureFreshWindow_ResetsExpiredWindow()
    {
        var cred = new ChessableCredential
        {
            UserId = 1, EncryptedBearer = "enc",
            RateLimitWindowStartedAt = DateTime.UtcNow.AddHours(-25), RateLimitLinesUsed = 1900,
        };
        var now = DateTime.UtcNow;

        Limiter().EnsureFreshWindow(cred, now);

        Assert.Equal(now, cred.RateLimitWindowStartedAt);
        Assert.Equal(0, cred.RateLimitLinesUsed);
    }

    [Fact]
    public void EnsureFreshWindow_KeepsFreshWindowUntouched()
    {
        var started = DateTime.UtcNow.AddHours(-1);
        var cred = new ChessableCredential
        {
            UserId = 1, EncryptedBearer = "enc", RateLimitWindowStartedAt = started, RateLimitLinesUsed = 1500,
        };

        Limiter().EnsureFreshWindow(cred, DateTime.UtcNow);

        Assert.Equal(started, cred.RateLimitWindowStartedAt);
        Assert.Equal(1500, cred.RateLimitLinesUsed);
    }

    [Theory]
    [InlineData(1999, false)]
    [InlineData(2000, true)]
    [InlineData(2500, true)]
    public void IsOverLimit_ComparesAgainstDailyLimit(int used, bool expected)
    {
        var cred = new ChessableCredential { UserId = 1, EncryptedBearer = "enc", RateLimitLinesUsed = used };
        Assert.Equal(expected, Limiter().IsOverLimit(cred));
    }

    [Fact]
    public async Task RecordUsageAsync_AccumulatesWithinWindow()
    {
        await SeedCredentialAsync(7);
        var limiter = Limiter();

        await limiter.RecordUsageAsync(7, 300);
        await limiter.RecordUsageAsync(7, 150);

        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 7);
        Assert.Equal(450, cred.RateLimitLinesUsed);
        Assert.NotNull(cred.RateLimitWindowStartedAt);
    }

    [Fact]
    public async Task RecordUsageAsync_ResetsExpiredWindow_BeforeAdding()
    {
        await SeedCredentialAsync(7);
        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 7);
        cred.RateLimitWindowStartedAt = DateTime.UtcNow.AddHours(-25);
        cred.RateLimitLinesUsed = 1900;
        await _db.SaveChangesAsync();

        await Limiter().RecordUsageAsync(7, 50);

        var reloaded = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 7);
        Assert.Equal(50, reloaded.RateLimitLinesUsed); // altes Fenster verworfen, nicht draufaddiert
    }

    [Fact]
    public async Task RecordUsageAsync_NoCredential_NoOp()
        => await Limiter().RecordUsageAsync(999, 100); // wirft nicht, tut nichts

    [Fact]
    public async Task RecordUsageAsync_ZeroOrNegativeLines_NoOp()
    {
        await SeedCredentialAsync(7);
        await Limiter().RecordUsageAsync(7, 0);
        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 7);
        Assert.Equal(0, cred.RateLimitLinesUsed);
        Assert.Null(cred.RateLimitWindowStartedAt); // No-op fasst das Fenster gar nicht erst an
    }

    private async Task SeedCredentialAsync(int userId)
    {
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId))
            _db.AppUsers.Add(new AppUser { Id = userId, Username = $"u{userId}", PasswordHash = "x" });
        _db.ChessableCredentials.Add(new ChessableCredential { UserId = userId, EncryptedBearer = "enc" });
        await _db.SaveChangesAsync();
    }
}
