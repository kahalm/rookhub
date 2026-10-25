using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Tests.Accounts;

/// <summary>
/// Die geteilte Anmeldung ueber beide Oberflaechen. Der Wert wandert als Cookie durch den Browser
/// und lebt 30 Tage — geprueft wird deshalb vor allem, WAS er nicht oeffnet: kein fremder Adressat,
/// kein geloeschtes Konto, kein entwerteter Stempel, und nichts ohne eingerichtete Elterndomaene.
/// </summary>
public class SharedSessionServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly SharedSessionService _svc;

    private const string Key = "TestSecretKeyThatIsAtLeast32Characters!";

    public SharedSessionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _config = Config(".example.test");
        _svc = Service(_config);
    }

    public void Dispose() => _db.Dispose();

    private static IConfiguration Config(string? domain, string? cookie = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Key,
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
            ["Auth:SharedSessionDomain"] = domain,
            ["Auth:SharedSessionCookie"] = cookie,
        }).Build();

    private SharedSessionService Service(IConfiguration config) =>
        new(_db, new AuthService(_db, config, new CapturingLogger<AuthService>()), config);

    private async Task<AppUser> UserAsync(string name = "u", DateTime? deletedAt = null, string? stamp = null)
    {
        var u = new AppUser
        {
            Username = name, Email = $"{name}@t.com", PasswordHash = "h",
            DeletedAt = deletedAt, SecurityStamp = stamp,
        };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task Redeem_GivesAFreshLoginForTheSameUser()
    {
        var user = await UserAsync("kahalm");

        var cookie = await _svc.IssueAsync(user.Id);
        var res = await _svc.RedeemAsync(cookie);

        Assert.NotNull(res);
        Assert.Equal(user.Id, res!.UserId);
        Assert.Equal("kahalm", res.Username);
        Assert.False(string.IsNullOrWhiteSpace(res.Token));
    }

    [Fact]
    public async Task Redeem_WorksTwice_UnlikeAHandoffCode()
    {
        // Der Unterschied zum Uebergabe-Code ist der Zweck: der ueberbrueckt EINEN Klick, das
        // Cookie steht fuer eine bestehende Sitzung und wird bei jedem Seitenaufruf gebraucht.
        var user = await UserAsync();
        var cookie = await _svc.IssueAsync(user.Id);

        Assert.NotNull(await _svc.RedeemAsync(cookie));
        Assert.NotNull(await _svc.RedeemAsync(cookie));
    }

    [Fact]
    public async Task Issue_IsOffWithoutAParentDomain()
    {
        // Auf localhost oder einer IP gibt es keine gemeinsame Domaene. Ein Cookie dorthin waere
        // ein stiller Fehlschlag — lieber gar keins.
        var user = await UserAsync();
        var svc = Service(Config(null));

        Assert.False(svc.IsEnabled);
        Assert.Null(svc.CookieDomain);
        Assert.Null(await svc.IssueAsync(user.Id));
    }

    [Fact]
    public async Task Redeem_RefusesEverythingWithoutAParentDomain()
    {
        // Auch ein gueltiges Cookie aus einer frueheren Konfiguration darf dann nichts oeffnen.
        var user = await UserAsync();
        var cookie = await _svc.IssueAsync(user.Id);

        Assert.Null(await Service(Config(null)).RedeemAsync(cookie));
    }

    [Fact]
    public async Task Redeem_RefusesANormalAccessToken()
    {
        // DER Kern: das Cookie hat einen eigenen Adressaten. Ein normales Zugriffstoken darf hier
        // nicht durchgehen — sonst waere jedes abgegriffene Token auch ein Dauerschluessel.
        var user = await UserAsync();
        var auth = new AuthService(_db, _config, new CapturingLogger<AuthService>());
        var normal = await auth.IssueTokenAsync(user);

        Assert.Null(await _svc.RedeemAsync(normal.Token));
    }

    [Fact]
    public async Task Redeem_RefusesADeletedAccount()
    {
        var user = await UserAsync("weg");
        var cookie = await _svc.IssueAsync(user.Id);

        user.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        Assert.Null(await _svc.RedeemAsync(cookie));
    }

    [Fact]
    public async Task Redeem_RefusesACookieFromBeforeThePasswordChanged()
    {
        // Der Sicherheits-Stempel entwertet das normale Token; ohne dieselbe Pruefung holte man
        // sich ueber das Cookie ein frisches zurueck.
        var user = await UserAsync("dreh", stamp: "alt");
        var cookie = await _svc.IssueAsync(user.Id);

        user.SecurityStamp = "neu";
        await _db.SaveChangesAsync();

        Assert.Null(await _svc.RedeemAsync(cookie));
    }

    [Fact]
    public async Task Redeem_RefusesAnExpiredCookie()
    {
        var user = await UserAsync();
        var expired = Signed(user.Id, SharedSessionService.Audience, "TestIssuer",
            DateTime.UtcNow.AddDays(-2));

        Assert.Null(await _svc.RedeemAsync(expired));
    }

    [Fact]
    public async Task Redeem_RefusesACookieSignedWithAnotherKey()
    {
        var user = await UserAsync();
        var foreign = Signed(user.Id, SharedSessionService.Audience, "TestIssuer",
            DateTime.UtcNow.AddDays(1), key: "CompletelyDifferentKeyOfAtLeast32Chars!");

        Assert.Null(await _svc.RedeemAsync(foreign));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("kein.jwt")]
    public async Task Redeem_RefusesJunkWithoutThrowing(string? value)
    {
        Assert.Null(await _svc.RedeemAsync(value));
    }

    // ── Absolute Obergrenze (P-14/A1-007): auth_time wird weitergereicht, nie verlaengert ──────────────────

    private static (DateTime? AuthTime, DateTime Expires) Read(string jwt)
    {
        var t = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        var claim = t.Claims.FirstOrDefault(c => c.Type == SessionLifetime.AuthTimeClaim)?.Value;
        return (claim is null ? null : DateTimeOffset.FromUnixTimeSeconds(long.Parse(claim)).UtcDateTime, t.ValidTo);
    }

    [Fact]
    public async Task Redeem_InheritsTheOriginalAuthTime_AndCapsTheTokenAtTheLimit()
    {
        var user = await UserAsync();
        var loggedIn = DateTime.UtcNow.AddDays(-80);
        var cookie = await _svc.IssueAsync(user.Id, loggedIn);

        Assert.NotNull(cookie);
        var (cookieAuth, cookieExp) = Read(cookie!);
        Assert.Equal(loggedIn, cookieAuth!.Value, TimeSpan.FromSeconds(2));
        Assert.Equal(loggedIn.AddDays(90), cookieExp, TimeSpan.FromSeconds(2));   // nicht +30 Tage ab jetzt

        var res = await _svc.RedeemAsync(cookie);
        Assert.NotNull(res);
        Assert.Equal(loggedIn, res!.AuthTime!.Value, TimeSpan.FromSeconds(2));
        var (tokenAuth, tokenExp) = Read(res.Token);
        Assert.Equal(loggedIn, tokenAuth!.Value, TimeSpan.FromSeconds(2));
        Assert.Equal(loggedIn.AddDays(90), tokenExp, TimeSpan.FromSeconds(2));
        Assert.Equal(loggedIn.AddDays(90), _svc.ExpiresAt(res.AuthTime.Value), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SessionOlderThanTheLimit_IsNeitherIssuedNorRedeemed()
    {
        var user = await UserAsync();
        var tooOld = DateTime.UtcNow.AddDays(-91);

        Assert.Null(await _svc.IssueAsync(user.Id, tooOld));
        // Auch ein noch nicht abgelaufenes Cookie mit zu altem auth_time wird nicht mehr eingeloest.
        var forged = Signed(user.Id, SharedSessionService.Audience, "TestIssuer", DateTime.UtcNow.AddDays(5),
            extra: SessionLifetime.ToClaim(tooOld));
        Assert.Null(await _svc.RedeemAsync(forged));
        Assert.Null(await _svc.ValidateAsync(forged));
    }

    /// <summary>Vor der Obergrenze ausgegebene Cookies tragen kein auth_time — sie gelten als „jetzt angemeldet", damit der
    /// Deploy niemanden abmeldet; ab dem Einloesen laeuft die Frist.</summary>
    [Fact]
    public async Task LegacyCookieWithoutAuthTime_CountsAsLoggedInNow()
    {
        var user = await UserAsync();
        var legacy = Signed(user.Id, SharedSessionService.Audience, "TestIssuer", DateTime.UtcNow.AddDays(20));

        var res = await _svc.RedeemAsync(legacy);

        Assert.NotNull(res);
        Assert.Equal(DateTime.UtcNow, res!.AuthTime!.Value, TimeSpan.FromSeconds(5));
        Assert.Equal(DateTime.UtcNow, Read(res.Token).AuthTime!.Value, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SessionMaxDays_IsConfigurable()
    {
        var user = await UserAsync();
        var cfg = new ConfigurationBuilder().AddConfiguration(_config)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:SessionMaxDays"] = "10" }).Build();
        var svc = Service(cfg);

        Assert.Null(await svc.IssueAsync(user.Id, DateTime.UtcNow.AddDays(-11)));
        var cookie = await svc.IssueAsync(user.Id, DateTime.UtcNow.AddDays(-5));
        Assert.Equal(DateTime.UtcNow.AddDays(5), Read(cookie!).Expires, TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromDays(90), SessionLifetime.MaxAge(_config));   // Vorgabe
    }

    [Fact]
    public void CookieName_IsConfigurableSoDevAndProdDoNotOverwriteEachOther()
    {
        // Beide Umgebungen teilen sich die Elterndomaene. Mit demselben Namen ueberschreiben sie
        // einander, und das Cookie der einen ist in der anderen nur ein ungueltiges Token.
        Assert.Equal("rh_session", _svc.CookieName);
        Assert.Equal("rh_session_dev", Service(Config(".example.test", "rh_session_dev")).CookieName);
    }

    private static string Signed(int userId, string audience, string issuer, DateTime expires,
        string key = Key, params Claim[] extra)
    {
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), .. extra],
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
