using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AuthUserValidationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    public AuthUserValidationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() { _db.Dispose(); _cache.Dispose(); }

    private async Task<int> AddUserAsync(DateTime? deletedAt = null, string? securityStamp = null)
    {
        var u = new AppUser { Username = "u", Email = "u@t.com", PasswordHash = "h", DeletedAt = deletedAt, SecurityStamp = securityStamp };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    [Fact]
    public async Task IsActiveUser_ActiveUser_True()
    {
        var id = await AddUserAsync();
        Assert.True(await AuthUserValidation.IsActiveUserAsync(_db, _cache, id));
    }

    [Fact]
    public async Task IsActiveUser_DeletedUser_False()
    {
        var id = await AddUserAsync(deletedAt: DateTime.UtcNow);
        Assert.False(await AuthUserValidation.IsActiveUserAsync(_db, _cache, id));
    }

    [Fact]
    public async Task IsActiveUser_UnknownUser_False()
    {
        Assert.False(await AuthUserValidation.IsActiveUserAsync(_db, _cache, 99999));
    }

    [Fact]
    public async Task IsActiveUser_CachesResult_WithinTtl()
    {
        var id = await AddUserAsync();
        Assert.True(await AuthUserValidation.IsActiveUserAsync(_db, _cache, id));

        // Nach dem Cachen löschen → innerhalb der TTL liefert der Cache weiter "aktiv".
        var u = await _db.AppUsers.FirstAsync(x => x.Id == id);
        u.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        Assert.True(await AuthUserValidation.IsActiveUserAsync(_db, _cache, id));   // gecacht (true)
    }

    [Fact]
    public async Task IsTokenValid_StampMatches_True()
    {
        var id = await AddUserAsync(securityStamp: "stamp-1");
        Assert.True(await AuthUserValidation.IsTokenValidAsync(_db, _cache, id, "stamp-1"));
    }

    [Fact]
    public async Task IsTokenValid_StampMismatch_False()
    {
        var id = await AddUserAsync(securityStamp: "new-stamp");
        // Token trägt den ALTEN Stempel → nach Passwort-Reset/-Änderung ungültig.
        Assert.False(await AuthUserValidation.IsTokenValidAsync(_db, _cache, id, "old-stamp"));
    }

    [Fact]
    public async Task IsTokenValid_TokenWithoutStamp_Grandfathered_True()
    {
        var id = await AddUserAsync(securityStamp: "server-stamp");
        // Alt-Token ohne sstamp-Claim (null) → grandfathered, bleibt gültig.
        Assert.True(await AuthUserValidation.IsTokenValidAsync(_db, _cache, id, null));
    }

    [Fact]
    public async Task IsTokenValid_ServerWithoutStamp_Grandfathered_True()
    {
        var id = await AddUserAsync(securityStamp: null);
        // User hat (noch) keinen Stempel → Abgleich übersprungen.
        Assert.True(await AuthUserValidation.IsTokenValidAsync(_db, _cache, id, "whatever"));
    }

    [Fact]
    public async Task IsTokenValid_DeletedUser_False_EvenWithMatchingStamp()
    {
        var id = await AddUserAsync(deletedAt: DateTime.UtcNow, securityStamp: "s");
        Assert.False(await AuthUserValidation.IsTokenValidAsync(_db, _cache, id, "s"));
    }
}
