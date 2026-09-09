using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Der Kern der Token-Nachprüfung (<see cref="JwtTokenGate.RejectionReasonAsync"/>): Grund
/// statt bool, Fail-open bei Datenbankfehlern, Abbruch bleibt Abbruch.</summary>
public class JwtTokenGateTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly CapturingLogger<JwtTokenGateTests> _log = new();

    public JwtTokenGateTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() { _db.Dispose(); _cache.Dispose(); }

    private async Task<int> AddUserAsync(DateTime? deletedAt = null, string? stamp = null)
    {
        var u = new AppUser { Username = "u", Email = "u@t.com", PasswordHash = "h", DeletedAt = deletedAt, SecurityStamp = stamp };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private Task<string?> ReasonAsync(int uid, string? tokenStamp, CancellationToken ct = default)
        => JwtTokenGate.RejectionReasonAsync(_db, _cache, uid, tokenStamp, _log, ct);

    [Fact]
    public async Task AktiverNutzer_mitPassendemStempel_bleibtGueltig()
    {
        var id = await AddUserAsync(stamp: "s1");
        Assert.Null(await ReasonAsync(id, "s1"));
        Assert.Empty(_log.Events);
    }

    [Fact]
    public async Task AltesTokenOhneStempel_bleibtGueltig()
    {
        var id = await AddUserAsync(stamp: "s1");
        Assert.Null(await ReasonAsync(id, tokenStamp: null));
    }

    [Fact]
    public async Task GeloeschtesKonto_wirdAbgelehnt()
    {
        var id = await AddUserAsync(deletedAt: DateTime.UtcNow);
        Assert.Equal("inactive-user", await ReasonAsync(id, null));
    }

    [Fact]
    public async Task UnbekannterNutzer_wirdAbgelehnt()
    {
        Assert.Equal("inactive-user", await ReasonAsync(4711, null));
    }

    [Fact]
    public async Task RotierterStempel_wirdAbgelehnt()
    {
        var id = await AddUserAsync(stamp: "neu");
        Assert.Equal("stamp-mismatch", await ReasonAsync(id, "alt"));
    }

    [Fact]
    public async Task Datenbankfehler_laesstDasTokenDurch_undWarnt()
    {
        // Ein Schluckauf der Datenbank ist kein Urteil über das Token: vorher wurde daraus ein 401
        // und der Client warf seine Sitzung weg.
        var id = await AddUserAsync(stamp: "s1");
        _db.Dispose();   // jeder Zugriff wirft ObjectDisposedException

        Assert.Null(await ReasonAsync(id, "s1"));
        var warning = Assert.Single(_log.Events);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("übersprungen", warning.Message);
    }

    [Fact]
    public async Task Abbruch_wirdNichtAlsDatenbankfehlerVerschluckt()
    {
        var id = await AddUserAsync(stamp: "s1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReasonAsync(id, "s1", cts.Token));
        Assert.Empty(_log.Events);
    }
}
