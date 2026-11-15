using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Controllers.Accounts;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using Xunit;

namespace RookHub.Api.Tests.Accounts;

/// <summary>
/// Besucher-Herkunft: was der Sammler zählt und was nicht, das Zusammenführen beim Wegschreiben, die Auswertung
/// (eindeutige Benutzer je Land), die Frist und die Rechte des Endpunkts (Filter nur für Admins).
/// </summary>
public class VisitorOriginTests : IDisposable
{
    private const string Browser = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36";
    private static readonly IPAddress Vienna = IPAddress.Parse("81.223.10.20");
    private static readonly IPAddress Berlin = IPAddress.Parse("91.64.5.6");

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTime _now = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    private VisitorOriginRecorder Recorder(IConfiguration? config = null) =>
        new(config ?? Config(("Jwt:Key", "test-key-test-key-test-key-test-key")),
            ip => ip.Equals(Vienna) ? "AT" : ip.Equals(Berlin) ? "DE" : null, () => _now);

    private static DateOnly Day(int day) => new(2026, 11, day);

    private void Row(int day, string hash, string country, int hits = 1, string surface = "rookhub") =>
        _db.VisitorDays.Add(new VisitorDay { Day = Day(day), VisitorHash = hash, Country = country, Hits = hits, Surface = surface });

    // ── Sammler ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Record_CountsHitsPerVisitorDayAndSurface_WithCountryButWithoutIp()
    {
        var r = Recorder();
        Assert.True(r.Record("u:anna", Vienna, "/api/puzzles/next", Browser, "rookhub.example.org"));
        Assert.True(r.Record("u:anna", Vienna, "/api/puzzles/next", Browser, "rookhub.example.org"));
        Assert.True(r.Record("u:anna", Vienna, "/api/league/clubs", Browser, "leaguehub-dev.example.org"));
        Assert.True(r.Record("a:0123456789abcdef", Berlin, "/api/endless/progress", Browser, "kidhub.example.org"));

        var deltas = r.Drain().OrderBy(d => d.Surface).ToList();
        Assert.Equal(3, deltas.Count);
        Assert.Equal(("kidhub", "DE", 1), (deltas[0].Surface, deltas[0].Country, deltas[0].Hits));
        Assert.Equal(("leaguehub", "AT", 1), (deltas[1].Surface, deltas[1].Country, deltas[1].Hits));
        Assert.Equal(("rookhub", "AT", 2), (deltas[2].Surface, deltas[2].Country, deltas[2].Hits));
        Assert.All(deltas, d => Assert.Equal(Day(15), d.Day));
        // Pseudonym: 32 Hex-Zeichen, weder Name noch Adresse darin; derselbe Besucher = dasselbe Pseudonym.
        Assert.All(deltas, d => Assert.Matches("^[0-9a-f]{32}$", d.VisitorHash));
        Assert.Equal(deltas[1].VisitorHash, deltas[2].VisitorHash);
        Assert.NotEqual(deltas[0].VisitorHash, deltas[1].VisitorHash);
        Assert.Empty(r.Drain());
    }

    [Theory]
    [InlineData(null, "81.223.10.20", "/api/puzzles/next", Browser)]                    // ohne Besucher-Kennung
    [InlineData("u:anna", "192.168.1.20", "/api/puzzles/next", Browser)]                // Heimnetz
    [InlineData("u:anna", "10.0.0.7", "/api/puzzles/next", Browser)]                    // intern
    [InlineData("u:anna", "172.20.0.3", "/api/puzzles/next", Browser)]                  // Docker-Netz
    [InlineData("u:anna", "127.0.0.1", "/api/puzzles/next", Browser)]                   // Loopback
    [InlineData("u:anna", "::ffff:192.168.1.20", "/api/puzzles/next", Browser)]         // IPv4 in IPv6
    [InlineData("u:anna", "81.223.10.20", "/health", Browser)]                          // nicht /api
    [InlineData("u:anna", "81.223.10.20", "/api/menu", Browser)]                        // System-Anfrage
    [InlineData("u:anna", "81.223.10.20", "/api/notifications/unread-count", Browser)]  // Zähler-Poll
    [InlineData("u:anna", "81.223.10.20", "/api/external-engine/work", Browser)]        // Engine-Provider
    [InlineData("u:anna", "81.223.10.20", "/api/client-log", Browser)]                  // Client-Heartbeat
    [InlineData("u:anna", "81.223.10.20", "/api/puzzles/next", "Googlebot/2.1 (+http://www.google.com/bot.html)")]
    [InlineData("u:anna", "81.223.10.20", "/api/puzzles/next", "curl/8.5.0")]
    [InlineData("u:anna", "81.223.10.20", "/api/puzzles/next", "")]                     // ohne User-Agent
    public void Record_SkipsSystemBotsAndInternalAddresses(string? visitor, string ip, string path, string ua)
    {
        var r = Recorder();
        Assert.False(r.Record(visitor, IPAddress.Parse(ip), path, ua, "rookhub.example.org"));
        Assert.Empty(r.Drain());
    }

    [Fact]
    public void Record_Disabled_CountsNothing()
    {
        var r = Recorder(Config(("VisitorStats:Enabled", "false")));
        Assert.False(r.Enabled);
        Assert.False(r.Record("u:anna", Vienna, "/api/puzzles/next", Browser, "rookhub.example.org"));
    }

    [Fact]
    public void Record_CapsNewVisitorsPerAddress_ButKeepsCountingKnownOnes()
    {
        var r = Recorder();
        for (var i = 0; i < VisitorOriginRecorder.MaxNewVisitorsPerIpPerDay; i++)
            Assert.True(r.Record($"a:{i:x16}", Vienna, "/api/puzzles/next", Browser, "rookhub.example.org"));
        Assert.False(r.Record("a:ffffffffffffffff", Vienna, "/api/puzzles/next", Browser, "rookhub.example.org"));
        Assert.True(r.Record($"a:{0:x16}", Vienna, "/api/puzzles/next", Browser, "rookhub.example.org"));
        // Eine andere Adresse ist nicht betroffen; am nächsten Tag ist der Zähler wieder leer.
        Assert.True(r.Record("a:ffffffffffffffff", Berlin, "/api/puzzles/next", Browser, "rookhub.example.org"));
        _now = _now.AddDays(1);
        Assert.True(r.Record("a:eeeeeeeeeeeeeeee", Vienna, "/api/puzzles/next", Browser, "rookhub.example.org"));
    }

    [Fact]
    public void Pseudonym_DependsOnServerSecret()
    {
        var a = Recorder(Config(("VisitorStats:Salt", "eins"))).Pseudonym("u:anna");
        var b = Recorder(Config(("VisitorStats:Salt", "zwei"))).Pseudonym("u:anna");
        Assert.NotEqual(a, b);
        Assert.Equal(a, Recorder(Config(("VisitorStats:Salt", "eins"))).Pseudonym("u:anna"));
        Assert.DoesNotContain("anna", a);
    }

    [Theory]
    [InlineData("rookhub.oberschmid.homes", "rookhub")]
    [InlineData("rookhub-dev.oberschmid.homes", "rookhub")]
    [InlineData("LeagueHub.oberschmid.homes", "leaguehub")]
    [InlineData("kidhub-dev.oberschmid.homes", "kidhub")]
    [InlineData("clubhub.oberschmid.homes", "clubhub")]
    [InlineData("turnier.oberschmid.homes", "turnier")]
    [InlineData("tournament.example.org", "turnier")]
    [InlineData("localhost", "rookhub")]
    [InlineData(null, "rookhub")]
    public void SurfaceOf_ReadsTheHost(string? host, string expected) =>
        Assert.Equal(expected, VisitorOriginRecorder.SurfaceOf(host));

    [Fact]
    public void Surfaces_AreTheLiteralList()
    {
        // Handgespiegelt im Frontend (features/visitors/visitor-origins.service.ts → VISITOR_SURFACES).
        Assert.Equal(new[] { "rookhub", "leaguehub", "kidhub", "clubhub", "turnier" }, VisitorOriginRecorder.Surfaces);
        Assert.Equal(new[] { 7, 30, 90, 365 }, VisitorOriginService.AllowedDays);
    }

    [Fact]
    public void Record_FromHttpContext_SkipsRejectedRequests()
    {
        var r = Recorder();
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = Vienna;
        ctx.Request.Path = "/api/puzzles/next";
        ctx.Request.Host = new HostString("rookhub.example.org");
        ctx.Request.Headers.UserAgent = Browser;
        ctx.Response.StatusCode = 401;
        r.Record(ctx, "u:anna");
        Assert.Empty(r.Drain());
        ctx.Response.StatusCode = 200;
        r.Record(ctx, "u:anna");
        Assert.Single(r.Drain());
    }

    // ── Wegschreiben ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flush_MergesIntoExistingRows_AndFillsMissingCountry()
    {
        var service = new VisitorOriginService(_db);
        await service.FlushAsync([
            new VisitorDayDelta(Day(15), "aaaa", "rookhub", "", 2),
            new VisitorDayDelta(Day(15), "bbbb", "rookhub", "DE", 1),
        ]);
        // Zweiter Schub: derselbe Besucher (jetzt mit Land), doppelt im Schub, dazu ein anderer Tag und eine andere Oberfläche.
        await service.FlushAsync([
            new VisitorDayDelta(Day(15), "aaaa", "rookhub", "AT", 3),
            new VisitorDayDelta(Day(15), "aaaa", "rookhub", "", 1),
            new VisitorDayDelta(Day(16), "aaaa", "rookhub", "AT", 1),
            new VisitorDayDelta(Day(15), "aaaa", "kidhub", "AT", 4),
            new VisitorDayDelta(Day(15), "bbbb", "rookhub", "FR", 1),
        ]);

        var rows = await _db.VisitorDays.OrderBy(v => v.VisitorHash).ThenBy(v => v.Day).ThenBy(v => v.Surface).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.Equal((Day(15), "kidhub", "AT", 4), (rows[0].Day, rows[0].Surface, rows[0].Country, rows[0].Hits));
        Assert.Equal((Day(15), "rookhub", "AT", 6), (rows[1].Day, rows[1].Surface, rows[1].Country, rows[1].Hits));
        Assert.Equal((Day(16), "rookhub", "AT", 1), (rows[2].Day, rows[2].Surface, rows[2].Country, rows[2].Hits));
        // Ein schon bekanntes Land bleibt (erstes Land des Tages).
        Assert.Equal(("bbbb", "DE", 2), (rows[3].VisitorHash, rows[3].Country, rows[3].Hits));
    }

    [Fact]
    public async Task FlushWorker_WritesBatch_AndRestoresItWhenTheDatabaseFails()
    {
        var recorder = Recorder();
        recorder.Record("u:anna", Vienna, "/api/puzzles/next", Browser, "rookhub.example.org");

        var failing = new ServiceCollection().BuildServiceProvider();   // kein VisitorOriginService → Schreiben scheitert
        var worker = new VisitorOriginFlushWorker(failing.GetRequiredService<IServiceScopeFactory>(), recorder,
            new TestLogger<VisitorOriginFlushWorker>());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => worker.FlushAsync(failing, default));

        recorder.Record("u:anna", Vienna, "/api/puzzles/next", Browser, "rookhub.example.org");
        var services = new ServiceCollection().AddSingleton(new VisitorOriginService(_db)).BuildServiceProvider();
        Assert.Equal(1, await worker.FlushAsync(services, default));
        var row = await _db.VisitorDays.SingleAsync();
        Assert.Equal(("AT", 2), (row.Country, row.Hits));
        Assert.Equal(0, await worker.FlushAsync(services, default));
    }

    // ── Auswertung ───────────────────────────────────────────────────────────────────────────────────

    private async Task SeedAsync()
    {
        Row(15, "anna", "AT", hits: 5);
        Row(14, "anna", "AT", hits: 3);
        Row(15, "anna", "AT", hits: 2, surface: "leaguehub");
        Row(15, "bert", "AT", hits: 1);
        Row(10, "carl", "DE", hits: 7);
        Row(15, "carl", "CH", hits: 1);                     // derselbe Besucher aus zwei Ländern
        Row(1, "dora", "US", hits: 4, surface: "kidhub");
        Row(15, "emil", "", hits: 9);                       // Land unbekannt
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_AllTime_CountsDistinctVisitorsPerCountry()
    {
        await SeedAsync();
        var dto = await new VisitorOriginService(_db).GetAsync(null, null, withHits: false, Day(15));

        Assert.False(dto.Admin);
        Assert.Equal(5, dto.UniqueUsers);          // anna, bert, carl, dora, emil — carl nur einmal
        Assert.Equal(4, dto.Countries);
        Assert.Equal(1, dto.UnknownUsers);
        Assert.Null(dto.Hits);
        Assert.Null(dto.Days);
        Assert.Null(dto.Since);                    // nichts Zeitbezogenes für normale Konten
        Assert.Equal(new[] { ("AT", 2), ("CH", 1), ("DE", 1), ("US", 1) }, dto.ByCountry.Select(c => (c.Country, c.Users)));
        Assert.All(dto.ByCountry, c => Assert.Null(c.Hits));
    }

    [Fact]
    public async Task Get_AdminFilter_LimitsPeriodAndSurface_AndAddsHits()
    {
        await SeedAsync();
        var service = new VisitorOriginService(_db);

        var week = await service.GetAsync(7, null, withHits: true, Day(15));      // 09.–15.11.
        Assert.True(week.Admin);
        Assert.Equal(4, week.UniqueUsers);          // dora (01.11.) fällt heraus
        Assert.Equal(new[] { ("AT", 2, 11L), ("CH", 1, 1L), ("DE", 1, 7L) },
            week.ByCountry.Select(c => (c.Country, c.Users, c.Hits!.Value)));
        Assert.Equal(28, week.Hits);
        Assert.Equal(Day(1), week.Since);           // „seit“ bleibt der älteste Tag überhaupt

        var league = await service.GetAsync(null, "leaguehub", withHits: true, Day(15));
        Assert.Equal(1, league.UniqueUsers);
        Assert.Equal(("AT", 1, 2L), (league.ByCountry.Single().Country, league.ByCountry.Single().Users, league.ByCountry.Single().Hits!.Value));
    }

    [Fact]
    public async Task Get_EmptyTable_IsZeroWithoutSince()
    {
        var dto = await new VisitorOriginService(_db).GetAsync(null, null, withHits: false, Day(15));
        Assert.Equal(0, dto.UniqueUsers);
        Assert.Empty(dto.ByCountry);
        Assert.Null(dto.Since);
    }

    /// <summary>InMemory prüft die SQL-Übersetzung nicht — COUNT(DISTINCT …) je Gruppe muss der MariaDB-Provider können.</summary>
    [Fact]
    public void ByCountry_TranslatesToCountDistinctOnMariaDb()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql("server=localhost;database=x;user=x;password=x", DbServerVersion.Current).Options);
        var service = new VisitorOriginService(db);
        var q = service.Filtered(30, "rookhub", Day(15));

        var sql = VisitorOriginService.ByCountry(q).ToQueryString();
        Assert.Contains("COUNT(DISTINCT (`v`.`VisitorHash`))", sql);
        Assert.Contains("GROUP BY `v`.`Country`", sql);
        Assert.Contains("`v`.`Day` >=", sql);
        Assert.Contains("SELECT DISTINCT `v`.`VisitorHash`", q.Select(v => v.VisitorHash).Distinct().ToQueryString());
    }

    // ── Frist ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retention_RemovesRowsOlderThanTheLimit()
    {
        _db.VisitorDays.Add(new VisitorDay { Day = new DateOnly(2025, 10, 1), VisitorHash = "alt", Country = "AT", Hits = 1, Surface = "rookhub" });
        _db.VisitorDays.Add(new VisitorDay { Day = new DateOnly(2025, 10, 12), VisitorHash = "grenze", Country = "AT", Hits = 1, Surface = "rookhub" });
        Row(15, "neu", "AT");
        await _db.SaveChangesAsync();

        // 15.11.2026 − 400 Tage = 11.10.2025: alles davor geht.
        Assert.Equal(1, await VisitorOriginRetention.PruneAsync(_db, _now, new RetentionOptions()));
        Assert.Equal(new[] { "grenze", "neu" }, await _db.VisitorDays.OrderBy(v => v.VisitorHash).Select(v => v.VisitorHash).ToArrayAsync());
        Assert.Equal(400, new RetentionOptions().VisitorDays);
        Assert.Equal(30, RetentionOptions.From(Config(("Retention:VisitorDays", "30"))).VisitorDays);
    }

    // ── Endpunkt ─────────────────────────────────────────────────────────────────────────────────────

    private VisitorOriginsController Controller(bool admin)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "990815") };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return new VisitorOriginsController(new VisitorOriginService(_db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
    }

    private void SeedToday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.VisitorDays.Add(new VisitorDay { Day = today, VisitorHash = "anna", Country = "AT", Hits = 5, Surface = "rookhub" });
        _db.VisitorDays.Add(new VisitorDay { Day = today.AddDays(-40), VisitorHash = "dora", Country = "US", Hits = 4, Surface = "kidhub" });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Controller_NonAdmin_IgnoresFilterParameters_AndGetsNoHits()
    {
        SeedToday();
        var result = await Controller(admin: false).Get(days: 7, surface: "rookhub", default);
        var dto = Assert.IsType<VisitorOriginsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(dto.Admin);
        Assert.Null(dto.Days);
        Assert.Null(dto.Surface);
        Assert.Null(dto.Hits);
        Assert.Null(dto.Since);
        Assert.Equal(2, dto.UniqueUsers);                      // alles Erfasste, alle Oberflächen — trotz ?days=7&surface=rookhub
        Assert.All(dto.ByCountry, c => Assert.Null(c.Hits));

        // Auch Unsinn in den Parametern ist für Nicht-Admins kein Fehler: sie werden gar nicht gelesen.
        Assert.IsType<OkObjectResult>((await Controller(admin: false).Get(days: 5, surface: "nope", default)).Result);
    }

    [Fact]
    public async Task Controller_Admin_AppliesFilter_AndRejectsUnknownValues()
    {
        SeedToday();
        var result = await Controller(admin: true).Get(days: 7, surface: " RookHub ", default);
        var dto = Assert.IsType<VisitorOriginsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(dto.Admin);
        Assert.Equal((7, "rookhub", 1, 5L), (dto.Days!.Value, dto.Surface, dto.UniqueUsers, dto.Hits!.Value));

        var all = Assert.IsType<VisitorOriginsDto>(Assert.IsType<OkObjectResult>((await Controller(admin: true).Get(null, null, default)).Result).Value);
        Assert.Equal(2, all.UniqueUsers);

        Assert.IsType<BadRequestObjectResult>((await Controller(admin: true).Get(days: 5, surface: null, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await Controller(admin: true).Get(days: null, surface: "nope", default)).Result);
    }
}
