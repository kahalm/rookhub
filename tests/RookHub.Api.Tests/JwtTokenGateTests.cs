using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
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

    // --- A10-001: Fehlschläge vor dem Rate-Limiter gedrosselt, mit Client-IP -------------------

    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static LogThrottle NewThrottle() => new(TimeSpan.FromMinutes(1), maxEntries: 100);

    [Fact]
    public void FalscheSignatur_inSchleife_warntEinmalJeMinuteUndIp_RestDebug()
    {
        // 'Bearer <fremd signiert>' in einer Schleife: vorher je Anfrage eine Warnung, auch für die, die der
        // Limiter danach mit 429 abweist.
        var throttle = NewThrottle();
        var ex = new SecurityTokenInvalidSignatureException("IDX10503: Signature validation failed.");

        for (var i = 0; i < 1000; i++)
            JwtTokenGate.LogAuthenticationFailure(_log, ex, "/api/x", "203.0.113.7", throttle, T0 + TimeSpan.FromMilliseconds(i));

        Assert.Single(_log.Events, e => e.Level == LogLevel.Warning);
        Assert.Equal(999, _log.Events.Count(e => e.Level == LogLevel.Debug));
        var warning = _log.Events.First();
        Assert.Equal("203.0.113.7", warning.State["IpAddress"]);
        Assert.StartsWith("JwtAuth:", warning.Message);

        // Eine zweite Quelle wird nicht verschluckt, und nach dem Fenster warnt dieselbe wieder.
        JwtTokenGate.LogAuthenticationFailure(_log, ex, "/api/x", "198.51.100.9", throttle, T0 + TimeSpan.FromSeconds(2));
        JwtTokenGate.LogAuthenticationFailure(_log, ex, "/api/x", "203.0.113.7", throttle, T0 + TimeSpan.FromMinutes(1));
        Assert.Equal(3, _log.Events.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public void KaputtesFormat_istNurInformation_undGedrosselt()
    {
        // 'Authorization: Bearer x' = SecurityTokenMalformedException — Rauschen, keine Warnung.
        var throttle = NewThrottle();
        var ex = new SecurityTokenMalformedException("IDX14100: JWT is not well formed.");

        for (var i = 0; i < 50; i++)
            JwtTokenGate.LogAuthenticationFailure(_log, ex, "/nirgendwo", "203.0.113.7", throttle, T0 + TimeSpan.FromSeconds(i));

        Assert.DoesNotContain(_log.Events, e => e.Level == LogLevel.Warning);
        var info = Assert.Single(_log.Events, e => e.Level == LogLevel.Information);
        Assert.Equal("203.0.113.7", info.State["IpAddress"]);
        Assert.Equal(nameof(SecurityTokenMalformedException), info.State["ExceptionType"]);
    }

    [Fact]
    public void Ausnahmetypen_werdenGetrenntGedrosselt_AbgelaufenBleibtInformation()
    {
        var throttle = NewThrottle();

        JwtTokenGate.LogAuthenticationFailure(_log, new SecurityTokenMalformedException("m"), "/a", "203.0.113.7", throttle, T0);
        JwtTokenGate.LogAuthenticationFailure(_log, new SecurityTokenExpiredException("e"), "/a", "203.0.113.7", throttle, T0);
        JwtTokenGate.LogAuthenticationFailure(_log, new SecurityTokenInvalidSignatureException("s"), "/a", "203.0.113.7", throttle, T0);

        Assert.Equal(
            new[] { LogLevel.Information, LogLevel.Information, LogLevel.Warning },
            _log.Events.Select(e => e.Level));
        Assert.Contains("abgelaufenes Token", _log.Events[1].Message);
    }

    [Fact]
    public void Drossel_TabelleBleibtUnterDemDeckel()
    {
        var throttle = new LogThrottle(TimeSpan.FromMinutes(1), maxEntries: 50);
        for (var i = 0; i < 500; i++)
            throttle.ShouldLog($"10.0.{i / 256}.{i % 256}|X", T0);
        Assert.True(throttle.Count <= 50, $"Tabelle über dem Deckel: {throttle.Count}");

        throttle.ShouldLog("spaet|X", T0 + TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        Assert.Equal(1, throttle.Count);
    }

    private sealed class SingleLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }

    [Fact]
    public async Task Ereignis_nimmtDieAufgeloesteClientIp_ausDerVerbindung()
    {
        // Verdrahtung: UseForwardedHeaders hat RemoteIpAddress beim Authentifizieren schon auf die echte
        // Client-IP gesetzt; genau die muss im Event stehen. Eigene, sonst nirgends benutzte Adresse,
        // weil FailureThrottle prozessweit geteilt ist.
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(new SingleLoggerFactory(_log))
                .BuildServiceProvider(),
        };
        http.Request.Path = "/api/profile";
        http.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8:a10:1::77");
        var ctx = new AuthenticationFailedContext(http,
            new AuthenticationScheme("Jwt", null, typeof(JwtBearerHandler)), new JwtBearerOptions())
        {
            Exception = new SecurityTokenInvalidSignatureException("IDX10503"),
        };

        await JwtTokenGate.OnAuthenticationFailedAsync(ctx);
        await JwtTokenGate.OnAuthenticationFailedAsync(ctx);

        Assert.Equal(new[] { LogLevel.Warning, LogLevel.Debug }, _log.Events.Select(e => e.Level));
        Assert.Equal("2001:db8:a10:1::77", _log.Events[0].State["IpAddress"]);
        Assert.Equal("/api/profile", _log.Events[0].State["Path"]);
    }
}
