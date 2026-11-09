using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Tests.Accounts;

/// <summary>R1-015: erledigte Passwort-Reset- und Übergabe-Codes gehen 7 Tage nach ihrem Ende; gültige und frische bleiben.</summary>
public class OneTimeCodeRetentionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private void Reset(string hash, DateTime expires, DateTime? used = null) => _db.PasswordResetTokens.Add(new PasswordResetToken
        { UserId = 5, TokenHash = hash, CreatedAt = expires.AddHours(-1), ExpiresAt = expires, UsedAt = used });

    private void Handoff(string hash, DateTime expires, DateTime? used = null) => _db.AuthHandoffTokens.Add(new AuthHandoffToken
        { UserId = 5, TokenHash = hash, CreatedAt = expires.AddSeconds(-60), ExpiresAt = expires, UsedAt = used });

    [Fact]
    public async Task ErledigtUndAelterAlsFrist_geht_GueltigOderFrisch_bleibt()
    {
        Reset("abgelaufen-alt", Now.AddDays(-8));
        Reset("verbraucht-alt", Now.AddDays(-7.5), used: Now.AddDays(-8));
        Reset("abgelaufen-frisch", Now.AddDays(-2));
        Reset("gueltig", Now.AddMinutes(30));
        Handoff("h-abgelaufen-alt", Now.AddDays(-30));
        Handoff("h-verbraucht-frisch", Now.AddDays(-1), used: Now.AddDays(-1));
        await _db.SaveChangesAsync();

        Assert.Equal(3, await OneTimeCodeRetention.PruneAsync(_db, Now));

        Assert.Equal(new[] { "abgelaufen-frisch", "gueltig" },
            await _db.PasswordResetTokens.OrderBy(t => t.TokenHash).Select(t => t.TokenHash).ToArrayAsync());
        Assert.Equal("h-verbraucht-frisch", (await _db.AuthHandoffTokens.SingleAsync()).TokenHash);
    }
}
