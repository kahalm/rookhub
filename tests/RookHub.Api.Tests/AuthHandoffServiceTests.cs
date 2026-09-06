using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Anmelde-Uebergabe zwischen den Oberflaechen (RookHub ↔ Turnierseite). Geprueft wird vor allem,
/// dass ein Code GENAU EINMAL wirkt und nur kurz lebt — er wandert durch die URL und landet damit
/// in Verlauf und Proxy-Logs.
/// </summary>
public class AuthHandoffServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuthHandoffService _svc;

    public AuthHandoffServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "TestSecretKeyThatIsAtLeast32Characters!",
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
        }).Build();

        var auth = new AuthService(_db, config, new CapturingLogger<AuthService>());
        _svc = new AuthHandoffService(_db, auth, new CapturingLogger<AuthHandoffService>());
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> UserAsync(string name = "u", DateTime? deletedAt = null)
    {
        var u = new AppUser { Username = name, Email = $"{name}@t.com", PasswordHash = "h", DeletedAt = deletedAt };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task Redeem_GivesAFreshLoginForTheSameUser()
    {
        var user = await UserAsync("kahalm");

        var code = await _svc.IssueAsync(user.Id);
        var res = await _svc.RedeemAsync(code);

        Assert.NotNull(res);
        Assert.Equal(user.Id, res!.UserId);
        Assert.Equal("kahalm", res.Username);
        Assert.False(string.IsNullOrWhiteSpace(res.Token));
    }

    [Fact]
    public async Task Redeem_WorksExactlyOnce()
    {
        var user = await UserAsync();
        var code = await _svc.IssueAsync(user.Id);

        Assert.NotNull(await _svc.RedeemAsync(code));
        Assert.Null(await _svc.RedeemAsync(code));       // zweiter Versuch: verbraucht
    }

    [Fact]
    public async Task Redeem_RejectsExpiredUnknownAndEmpty()
    {
        var user = await UserAsync();
        var code = await _svc.IssueAsync(user.Id);

        // Ablauf vorziehen statt zu warten.
        var row = await _db.AuthHandoffTokens.FirstAsync(t => t.UserId == user.Id);
        row.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await _db.SaveChangesAsync();

        Assert.Null(await _svc.RedeemAsync(code));
        Assert.Null(await _svc.RedeemAsync("gibtesnicht"));
        Assert.Null(await _svc.RedeemAsync(""));
        Assert.Null(await _svc.RedeemAsync(null));
    }

    [Fact]
    public async Task Redeem_RejectsADeletedAccount()
    {
        // Das Konto kann zwischen Ausstellen und Einloesen geloescht werden — dann darf der Sprung
        // keine Anmeldung mehr erzeugen.
        var user = await UserAsync();
        var code = await _svc.IssueAsync(user.Id);

        user.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        Assert.Null(await _svc.RedeemAsync(code));
    }

    [Fact]
    public async Task Issue_ClearsOutSpentCodesOfTheSameUser()
    {
        // 60-Sekunden-Zeilen duerfen sich nicht ansammeln; ein eigener Aufraeumdienst waere dafuer
        // zu viel, also raeumt das Ausstellen mit auf.
        var user = await UserAsync();
        var first = await _svc.IssueAsync(user.Id);
        await _svc.RedeemAsync(first);                    // verbraucht

        await _svc.IssueAsync(user.Id);

        var rows = await _db.AuthHandoffTokens.Where(t => t.UserId == user.Id).ToListAsync();
        Assert.Single(rows);
        Assert.Null(rows[0].UsedAt);
    }

    [Fact]
    public async Task Issue_RefusesAnUnknownOrDeletedAccount()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.IssueAsync(9999));
        var gone = await UserAsync("weg", DateTime.UtcNow);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.IssueAsync(gone.Id));
    }

    [Fact]
    public async Task Lifetime_IsShort()
    {
        // Der Code ueberbrueckt einen Klick. Waere er lang, stuende eine gueltige Anmeldung im
        // Browserverlauf — genau das soll er verhindern.
        Assert.True(AuthHandoffService.Lifetime <= TimeSpan.FromMinutes(2));
    }
}
