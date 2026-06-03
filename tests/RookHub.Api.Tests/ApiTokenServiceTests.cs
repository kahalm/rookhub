using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class ApiTokenServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ApiTokenService _svc;

    public ApiTokenServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _svc = new ApiTokenService(_db, NullLogger<ApiTokenService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string name = "alice")
    {
        var u = new AppUser { Username = name, Email = name + "@x.com", PasswordHash = "h" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    [Fact]
    public void GenerateRawToken_HasPrefixAndDecentEntropy()
    {
        var t1 = ApiTokenService.GenerateRawToken();
        var t2 = ApiTokenService.GenerateRawToken();
        Assert.StartsWith("rkh_", t1);
        // Base64URL ohne Padding, 32 byte → 43 Char + 4 Char Prefix = 47.
        Assert.Equal(47, t1.Length);
        Assert.NotEqual(t1, t2);
        // Keine Padding-/unsafe-Chars.
        Assert.DoesNotContain('=', t1);
        Assert.DoesNotContain('/', t1);
        Assert.DoesNotContain('+', t1);
    }

    [Fact]
    public void ComputeHash_DeterministicSHA256()
    {
        Assert.Equal(64, ApiTokenService.ComputeHash("rkh_test").Length);
        Assert.Equal(ApiTokenService.ComputeHash("rkh_x"), ApiTokenService.ComputeHash("rkh_x"));
        Assert.NotEqual(ApiTokenService.ComputeHash("rkh_x"), ApiTokenService.ComputeHash("rkh_y"));
    }

    [Fact]
    public async Task CreateAsync_ReturnsRawTokenOnceAndStoresHash()
    {
        var uid = await CreateUserAsync();
        var created = await _svc.CreateAsync(uid, "Chess.com Extension", null, null);

        Assert.StartsWith("rkh_", created.RawToken);
        Assert.Equal(47, created.RawToken.Length);
        Assert.Equal("Chess.com Extension", created.Name);
        Assert.Equal("extension", created.Scope);
        Assert.Null(created.ExpiresAt);
        Assert.Null(created.LastUsedAt);
        Assert.Equal(created.RawToken[..12], created.Prefix);

        // In der DB ist nur der Hash gespeichert, NICHT der Raw-Token.
        var stored = await _db.UserApiTokens.SingleAsync();
        Assert.Equal(ApiTokenService.ComputeHash(created.RawToken), stored.TokenHash);
        Assert.NotEqual(created.RawToken, stored.TokenHash);
    }

    [Fact]
    public async Task CreateAsync_AppliesExpiresInDays()
    {
        var uid = await CreateUserAsync();
        var before = DateTime.UtcNow;
        var created = await _svc.CreateAsync(uid, "name", null, expiresInDays: 30);
        Assert.NotNull(created.ExpiresAt);
        Assert.InRange((created.ExpiresAt!.Value - before).TotalDays, 29.9, 30.1);
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownScope()
    {
        var uid = await CreateUserAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(uid, "n", "admin", null));
    }

    [Fact]
    public async Task CreateAsync_EnforcesPerUserLimit()
    {
        var uid = await CreateUserAsync();
        for (int i = 0; i < ApiTokenService.MaxTokensPerUser; i++)
            await _svc.CreateAsync(uid, "n" + i, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(uid, "overflow", null, null));
    }

    [Fact]
    public async Task ListAsync_OnlyReturnsOwnTokens_NewestFirst()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");
        await _svc.CreateAsync(alice, "alice-old", null, null);
        await Task.Delay(10);
        await _svc.CreateAsync(alice, "alice-new", null, null);
        await _svc.CreateAsync(bob, "bobs-token", null, null);

        var list = await _svc.ListAsync(alice);
        Assert.Equal(2, list.Count);
        Assert.Equal("alice-new", list[0].Name); // neueste zuerst
        Assert.DoesNotContain(list, t => t.Name == "bobs-token");
    }

    [Fact]
    public async Task RevokeAsync_RemovesTokenForOwner_RejectsForeign()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");
        var alicesToken = await _svc.CreateAsync(alice, "a", null, null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.RevokeAsync(bob, alicesToken.Id));
        Assert.Equal(1, await _db.UserApiTokens.CountAsync());

        await _svc.RevokeAsync(alice, alicesToken.Id);
        Assert.Equal(0, await _db.UserApiTokens.CountAsync());
    }

    [Fact]
    public async Task ValidateAsync_ReturnsToken_WhenValid()
    {
        var uid = await CreateUserAsync();
        var created = await _svc.CreateAsync(uid, "x", null, null);

        var validated = await _svc.ValidateAsync(created.RawToken);
        Assert.NotNull(validated);
        Assert.Equal(uid, validated!.UserId);
        Assert.NotNull(validated.LastUsedAt); // wurde gesetzt
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNull_WhenWrongOrMissingPrefix()
    {
        Assert.Null(await _svc.ValidateAsync(""));
        Assert.Null(await _svc.ValidateAsync("not_a_token"));
        Assert.Null(await _svc.ValidateAsync("rkh_wrong"));
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNull_WhenExpired()
    {
        var uid = await CreateUserAsync();
        var created = await _svc.CreateAsync(uid, "x", null, expiresInDays: 30);
        // Ablauf manuell in die Vergangenheit ziehen
        var token = await _db.UserApiTokens.SingleAsync();
        token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        var validated = await _svc.ValidateAsync(created.RawToken);
        Assert.Null(validated);
    }
}
