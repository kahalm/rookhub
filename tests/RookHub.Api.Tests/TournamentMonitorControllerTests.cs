using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>TournamentMonitorController: die Controller-eigenen Branches, die vorher unverifiziert waren —
/// Id-Validierung (400), Refresh eines bestehenden Monitors, der dbId&lt;=0→502-Fallback (Crawler liefert
/// keine auflösbare DB-Id), erfolgreiches Anlegen sowie GetStatus/Deactivate.</summary>
public class TournamentMonitorControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    public TournamentMonitorControllerTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(opts);
    }
    public void Dispose() => _db.Dispose();

    private sealed class PathHandler(Func<string, string> resp) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(resp(req.RequestUri!.AbsolutePath), Encoding.UTF8, "application/json") });
    }

    private TournamentMonitorController Controller(int userId, Func<string, string>? resp = null)
    {
        var handler = new PathHandler(resp ?? (_ => "{}"));
        var proxy = new CrawlerProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://crawler") });
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"));
        return new TournamentMonitorController(_db, proxy, NullLogger<TournamentMonitorController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    [Fact]
    public async Task Activate_RejectsInvalidId()
        => Assert.IsType<BadRequestObjectResult>(await Controller(1).Activate("bad id!"));

    [Fact]
    public async Task Activate_ExistingMonitor_RefreshesWithoutHittingCrawler()
    {
        _db.TournamentMonitors.Add(new TournamentMonitor { UserId = 1, CrawlerTournamentId = "12345", CrawlerTournamentDbId = 7, ActiveUntil = DateTime.UtcNow.AddMinutes(-5), LastKnownRounds = 2 });
        await _db.SaveChangesAsync();
        // Proxy würde werfen, wenn er doch aufgerufen würde:
        var res = await Controller(1, _ => throw new Exception("crawler should not be called")).Activate("12345");
        Assert.IsType<OkObjectResult>(res);
        var m = await _db.TournamentMonitors.FirstAsync();
        Assert.True(m.ActiveUntil > DateTime.UtcNow.AddMinutes(50));
    }

    [Fact]
    public async Task Activate_New_ReturnsBadGateway_WhenCrawlerHasNoDbId()
    {
        var res = await Controller(1, _ => "{\"totalRounds\":5}").Activate("12345"); // kein "id"
        var obj = Assert.IsType<ObjectResult>(res);
        Assert.Equal(502, obj.StatusCode);
        Assert.False(await _db.TournamentMonitors.AnyAsync());
    }

    [Fact]
    public async Task Activate_New_CreatesMonitor_WithDbIdAndKnownRounds()
    {
        Func<string, string> resp = path => path.EndsWith("/rounds/check")
            ? "{\"knownRounds\":3}"
            : "{\"id\":42,\"totalRounds\":5}";
        var res = await Controller(1, resp).Activate("12345");
        Assert.IsType<OkObjectResult>(res);
        var m = await _db.TournamentMonitors.SingleAsync();
        Assert.Equal(42, m.CrawlerTournamentDbId);
        Assert.Equal(3, m.LastKnownRounds);   // aus rounds/check, überschreibt totalRounds
    }

    /// <summary>
    /// A5-007: der Monitor meldet nur ueber LastKnownRounds hinaus. Scheitert rounds/check beim Anlegen,
    /// ist der Stand die Zahl der schon geholten Runden — nicht die geplante Rundenzahl (sonst meldete ein
    /// 9-Runden-Turnier nie eine Runde).
    /// </summary>
    [Fact]
    public async Task Activate_New_RoundsCheckFails_FallsBackToCrawledRoundsNotTotalRounds()
    {
        Func<string, string> resp = path => path.EndsWith("/rounds/check")
            ? throw new HttpRequestException("chess-results down")
            : "{\"id\":42,\"totalRounds\":9,\"knownRounds\":4}";
        var res = await Controller(1, resp).Activate("12345");
        Assert.IsType<OkObjectResult>(res);
        Assert.Equal(4, (await _db.TournamentMonitors.SingleAsync()).LastKnownRounds);
    }

    private async Task SeedActiveMonitorsAsync(int userId, int count)
    {
        for (var i = 0; i < count; i++)
            _db.TournamentMonitors.Add(new TournamentMonitor
            {
                UserId = userId, CrawlerTournamentId = (1000 + i).ToString(), CrawlerTournamentDbId = 100 + i,
                ActiveUntil = DateTime.UtcNow.AddMinutes(30),
            });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// A5-004: ein Konto darf nicht beliebig viele Monitore haben — jeder laesst den Crawler alle 30 s eine
    /// chess-results-Seite holen. Der elfte neue Monitor wird mit 409 abgewiesen, bevor der Crawler gefragt wird.
    /// </summary>
    [Fact]
    public async Task Activate_New_AtTheCap_Returns409WithoutHittingCrawler()
    {
        await SeedActiveMonitorsAsync(1, TournamentMonitorController.MaxActiveMonitorsPerUser);

        var res = await Controller(1, _ => throw new Exception("crawler should not be called")).Activate("99999");

        var conflict = Assert.IsType<ConflictObjectResult>(res);
        Assert.Contains("Maximum of 10", conflict.Value!.ToString());
        Assert.Equal(TournamentMonitorController.MaxActiveMonitorsPerUser, await _db.TournamentMonitors.CountAsync());
    }

    /// <summary>Verlaengern geht auch am Deckel, abgelaufene Monitore und die anderer Konten zaehlen nicht.</summary>
    [Fact]
    public async Task Activate_AtTheCap_ExtendingWorks_ExpiredAndForeignMonitorsDontCount()
    {
        await SeedActiveMonitorsAsync(1, TournamentMonitorController.MaxActiveMonitorsPerUser);
        Assert.IsType<OkObjectResult>(
            await Controller(1, _ => throw new Exception("crawler should not be called")).Activate("1000"));

        await SeedActiveMonitorsAsync(2, TournamentMonitorController.MaxActiveMonitorsPerUser - 1);
        _db.TournamentMonitors.Add(new TournamentMonitor
        {
            UserId = 2, CrawlerTournamentId = "5555", CrawlerTournamentDbId = 55, ActiveUntil = DateTime.UtcNow.AddMinutes(-1),
        });
        await _db.SaveChangesAsync();
        Func<string, string> resp = path => path.EndsWith("/rounds/check") ? "{\"knownRounds\":1}" : "{\"id\":42}";
        Assert.IsType<OkObjectResult>(await Controller(2, resp).Activate("12345"));
    }

    /// <summary>
    /// A5-004 (Nacharbeit): eine ABGELAUFENE Zeile, die der RoundMonitorService noch nicht weggeraeumt hat, zaehlt
    /// am Deckel wie ein neuer Monitor. Sonst: M1 ablaufen lassen, M11 anlegen, M1 per POST wiederbeleben → 11 aktiv,
    /// beliebig oft wiederholbar. 409, die Zeile bleibt unveraendert, der Crawler wird nicht gefragt.
    /// </summary>
    [Fact]
    public async Task Activate_ExpiredMonitor_AtTheCap_Returns409AndLeavesTheRowUnchanged()
    {
        await SeedActiveMonitorsAsync(1, TournamentMonitorController.MaxActiveMonitorsPerUser);
        var expiredAt = DateTime.UtcNow.AddSeconds(-1);
        _db.TournamentMonitors.Add(new TournamentMonitor
        {
            UserId = 1, CrawlerTournamentId = "7777", CrawlerTournamentDbId = 77, ActiveUntil = expiredAt, LastKnownRounds = 3,
        });
        await _db.SaveChangesAsync();

        var res = await Controller(1, _ => throw new Exception("crawler should not be called")).Activate("7777");

        var conflict = Assert.IsType<ConflictObjectResult>(res);
        Assert.Contains("Maximum of 10", conflict.Value!.ToString());
        var row = await _db.TournamentMonitors.SingleAsync(m => m.CrawlerTournamentId == "7777");
        Assert.Equal(expiredAt, row.ActiveUntil);
        Assert.Equal(TournamentMonitorController.MaxActiveMonitorsPerUser,
            await _db.TournamentMonitors.CountAsync(m => m.UserId == 1 && m.ActiveUntil >= DateTime.UtcNow));
    }

    [Fact]
    public async Task GetStatus_ExpiredMonitor_ReturnsOk()
    {
        _db.TournamentMonitors.Add(new TournamentMonitor { UserId = 1, CrawlerTournamentId = "12345", ActiveUntil = DateTime.UtcNow.AddHours(-2) });
        await _db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await Controller(1).GetStatus("12345"));
    }

    [Fact]
    public async Task Deactivate_RemovesMonitor()
    {
        _db.TournamentMonitors.Add(new TournamentMonitor { UserId = 1, CrawlerTournamentId = "12345", ActiveUntil = DateTime.UtcNow.AddHours(1) });
        await _db.SaveChangesAsync();
        Assert.IsType<NoContentResult>(await Controller(1).Deactivate("12345"));
        Assert.False(await _db.TournamentMonitors.AnyAsync());
    }
}
