using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>
/// Gemeinsame Pause je Gegenseite nach einem 429 (2026-11-17, Prod-Log: 144 Drossel-Warnungen in drei Tagen, weil nach einem
/// 429 die übrigen Schritte der Runde sofort weiterfragten). Alles gegen nachgebaute Antworten, kein Netz.
/// </summary>
public class LeagueRateGateTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTime _now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
    private readonly CapturingLogger<LeagueRateGate> _log = new();
    private readonly LeagueRateGate _gate;

    public LeagueRateGateTests() => _gate = new LeagueRateGate(_log, () => _now);

    public void Dispose() => _db.Dispose();

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Urls) Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Ok(string body = "{}") => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
    private static HttpResponseMessage TooMany(TimeSpan? retryAfter = null)
    {
        var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("") };
        if (retryAfter is { } ra) r.Headers.RetryAfter = new RetryConditionHeaderValue(ra);
        return r;
    }

    private HttpClient Client(FakeHttp inner) => new(new LeagueRateGateHandler(_gate) { InnerHandler = inner });

    private List<CapturingLogger<LeagueRateGate>.Entry> Warnings => _log.Events.Where(e => e.Level == LogLevel.Warning).ToList();

    // ── Das Tor ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task After429_NoRequestToThatSiteLeaves_UntilThePauseIsOver()
    {
        var lichess429 = true;
        var http = new FakeHttp(r => lichess429 ? TooMany() : Ok());
        var client = Client(http);

        var first = await Assert.ThrowsAsync<LeagueOnlineSync.RateLimitedException>(() => client.GetAsync("https://lichess.org/api/user/a"));
        Assert.Equal((LeagueRateGate.Lichess, LeagueRateGate.BasePause), (first.Site, first.Remaining));
        Assert.True(LeagueRateGate.BasePause >= TimeSpan.FromSeconds(60));

        lichess429 = false;
        _now += TimeSpan.FromSeconds(60);                                                  // eine Minute reicht nicht (Reserve)
        var blocked = await Assert.ThrowsAsync<LeagueOnlineSync.RateLimitedException>(() => client.GetAsync("https://lichess.org/api/user/b"));
        Assert.Equal(TimeSpan.FromSeconds(15), blocked.Remaining);
        Assert.Single(http.Urls);                                                          // ohne Netzaufruf

        _now += TimeSpan.FromSeconds(15);
        (await client.GetAsync("https://lichess.org/api/user/c")).Dispose();
        Assert.Equal(2, http.Urls.Count);
        Assert.False(_gate.IsPaused(LeagueRateGate.Lichess));
    }

    [Fact]
    public async Task ConsecutivePauses_Grow_UpToTheCap_AndASuccessResetsThem()
    {
        var answer429 = true;
        var client = Client(new FakeHttp(_ => answer429 ? TooMany() : Ok()));
        var seen = new List<TimeSpan>();
        for (var i = 0; i < 6; i++)
        {
            var e = await Assert.ThrowsAsync<LeagueOnlineSync.RateLimitedException>(() => client.GetAsync("https://lichess.org/api/x"));
            seen.Add(e.Remaining!.Value);
            _now += e.Remaining.Value;                                                     // Pause abgesessen, nächster Versuch
        }
        Assert.Equal(new[] { 75, 150, 300, 600, 1200, 1200 }.Select(s => TimeSpan.FromSeconds(s)), seen);
        Assert.Equal(LeagueRateGate.MaxPause, seen[^1]);

        answer429 = false;
        (await client.GetAsync("https://lichess.org/api/x")).Dispose();                    // Erfolg setzt die Folge zurück
        answer429 = true;
        var again = await Assert.ThrowsAsync<LeagueOnlineSync.RateLimitedException>(() => client.GetAsync("https://lichess.org/api/x"));
        Assert.Equal(LeagueRateGate.BasePause, again.Remaining);
    }

    [Fact]
    public async Task RetryAfter_IsHonoured_WhenLongerThanThePause()
    {
        var client = Client(new FakeHttp(_ => TooMany(TimeSpan.FromSeconds(300))));
        var e = await Assert.ThrowsAsync<LeagueOnlineSync.RateLimitedException>(() => client.GetAsync("https://lichess.org/api/x"));
        Assert.Equal(TimeSpan.FromSeconds(300) + LeagueRateGate.RetryAfterReserve, e.Remaining);

        // Kürzer als die Mindestpause → die Mindestpause gilt.
        var gate2 = new LeagueRateGate(NullLogger<LeagueRateGate>.Instance, () => _now);
        Assert.Equal(LeagueRateGate.BasePause, gate2.RateLimited(LeagueRateGate.Lichess, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task OneWarningPerPause_NotPerAttempt_EndAsInformation()
    {
        var answer429 = true;
        var client = Client(new FakeHttp(_ => answer429 ? TooMany() : Ok()));
        await Assert.ThrowsAsync<LeagueOnlineSync.RateLimitedException>(() => client.GetAsync("https://lichess.org/api/a"));
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<LeagueOnlineSync.RateLimitedException>(() => client.GetAsync("https://lichess.org/api/b"));
        _gate.RateLimited(LeagueRateGate.Lichess, null);                                   // Antwort einer Anfrage, die schon unterwegs war
        var w = Assert.Single(Warnings);
        Assert.Equal((LeagueRateGate.Lichess, 75), (w.State["Site"], w.State["Seconds"]));

        _now += TimeSpan.FromMinutes(5);
        answer429 = false;
        (await client.GetAsync("https://lichess.org/api/c")).Dispose();
        (await client.GetAsync("https://lichess.org/api/d")).Dispose();
        Assert.Single(Warnings);
        Assert.Single(_log.Events, e => e.Level == LogLevel.Information && e.Message.Contains("Pause beendet"));
    }

    [Fact]
    public async Task ChessCom_IsUntouched_WhileLichessPauses_AndViceVersa()
    {
        var http = new FakeHttp(r => r.RequestUri!.Host.EndsWith("lichess.org") ? TooMany() : Ok());
        var client = Client(http);
        await Assert.ThrowsAsync<LeagueOnlineSync.RateLimitedException>(() => client.GetAsync("https://lichess.org/api/a"));
        (await client.GetAsync("https://api.chess.com/pub/player/x")).Dispose();
        (await client.GetAsync("https://www.chess.com/callback/member/stats/x")).Dispose();
        Assert.True(_gate.IsPaused(LeagueRateGate.Lichess));
        Assert.False(_gate.IsPaused(LeagueRateGate.ChessCom));
        Assert.Equal(3, http.Urls.Count);

        Assert.Equal(LeagueRateGate.Lichess, LeagueRateGate.SiteOf(new Uri("http://lichess.test/api/x"), "lichess.test"));
        Assert.Null(LeagueRateGate.SiteOf(new Uri("https://example.org/lichess.org")));
        Assert.Null(LeagueRateGate.SiteOf(new Uri("https://notchess.com.evil/x")));
    }

    // ── Der Takt ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scheduler_SkipsStepsOfAPausedSite_AndWaitsAtLeastUntilThePauseEnds()
    {
        Assert.Equal(LeagueOnlineSyncScheduler.BacklogPause, LeagueOnlineSyncScheduler.NextWait(true, _gate));
        Assert.Equal(LeagueOnlineSyncScheduler.IdlePoll, LeagueOnlineSyncScheduler.NextWait(false, _gate));

        _gate.RateLimited(LeagueRateGate.Lichess, TimeSpan.FromMinutes(10));
        Assert.False(LeagueOnlineSyncScheduler.CanRun(_gate, LeagueRateGate.Lichess));
        Assert.False(LeagueOnlineSyncScheduler.CanRun(_gate, LeagueRateGate.Lichess, LeagueRateGate.ChessCom));
        Assert.True(LeagueOnlineSyncScheduler.CanRun(_gate, LeagueRateGate.ChessCom));
        Assert.Equal(TimeSpan.FromMinutes(10) + LeagueRateGate.RetryAfterReserve, LeagueOnlineSyncScheduler.NextWait(true, _gate));
        Assert.Equal(LeagueOnlineSyncScheduler.IdlePoll, LeagueOnlineSyncScheduler.NextWait(false, _gate));   // 30 min > 10 min
    }

    // ── Alle Schritte respektieren die Pause ───────────────────────────────────────────────────

    [Fact]
    public async Task OnlineSync_After429OnLichess_LeavesLichessAccounts_ButFetchesChessCom()
    {
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Muster, Max", NameKey = "muster, max", FideId = "222" });
        _db.LeagueOnlineAccounts.AddRange(
            new LeagueOnlineAccount { FideId = "222", Site = LeagueOnlineSites.Lichess, UserName = "Max_A", Url = "u", Confidence = "sicher" },
            new LeagueOnlineAccount { FideId = "222", Site = LeagueOnlineSites.Lichess, UserName = "Max_B", Url = "u", Confidence = "sicher" },
            new LeagueOnlineAccount { FideId = "222", Site = LeagueOnlineSites.ChessCom, UserName = "maxm", Url = "u", Confidence = "sicher" });
        await _db.SaveChangesAsync();
        var http = new FakeHttp(r => r.RequestUri!.Host == "lichess.org" ? TooMany() : Ok("""{"archives":[]}"""));
        var sync = new LeagueOnlineSync(_db, Client(http), NullLogger<LeagueOnlineSync>.Instance);

        Assert.True(await sync.RunOnceAsync(TimeSpan.FromHours(12), TimeSpan.FromMinutes(5), default));
        Assert.Single(http.Urls, u => u.Contains("lichess.org"));                          // nur EINE Anfrage an Lichess
        Assert.Single(http.Urls, u => u.Contains("chess.com"));                            // chess.com lief weiter
        var accounts = await _db.LeagueOnlineAccounts.AsNoTracking().ToListAsync();
        Assert.All(accounts.Where(a => a.Site == LeagueOnlineSites.Lichess), a => Assert.Null(a.SyncedAt));
        Assert.NotNull(accounts.Single(a => a.Site == LeagueOnlineSites.ChessCom).SyncedAt);
        Assert.Single(Warnings);

        // Die nächste Runde während der Pause: keine Anfrage an Lichess.
        http.Urls.Clear();
        _now += TimeSpan.FromSeconds(30);
        Assert.True(await sync.RunOnceAsync(TimeSpan.FromHours(12), TimeSpan.FromMinutes(5), default));
        Assert.DoesNotContain(http.Urls, u => u.Contains("lichess.org"));
        Assert.Single(Warnings);
    }

    [Fact]
    public async Task BroadcastImport_TeamScout_AccountChecks_SendNothingWhileLichessPauses()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Name = "Landesliga", Season = "2026/27", League = "LL", Stage = "x" });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Muster, Max", NameKey = "muster, max", FideId = "222", Fed = "AUT", EloI = 1900 });
        _db.LeagueScoutAccounts.Add(new LeagueScoutAccount { UserName = "isarwolf", DisplayName = "Isarwolf", Teams = "SK Kufstein", FoundAt = DateTime.UtcNow });
        var acc = new LeagueOnlineAccount { FideId = "222", Site = LeagueOnlineSites.Lichess, UserName = "MaxMuster", Url = "u", Confidence = "sicher" };
        _db.LeagueOnlineAccounts.Add(acc);
        await _db.SaveChangesAsync();
        var http = new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LeagueOnline:TeamPlaces"] = "Kufstein" }).Build();
        _gate.RateLimited(LeagueRateGate.Lichess, null);

        var broadcasts = new LeagueBroadcastImport(_db, Client(http), NullLogger<LeagueBroadcastImport>.Instance) { Pause = TimeSpan.Zero };
        Assert.True(await broadcasts.RunOnceAsync(TimeSpan.FromMinutes(5), discover: true, default));

        var scout = new LeagueTeamScout(_db, Client(http), NullLogger<LeagueTeamScout>.Instance, config)
        { Pause = TimeSpan.Zero, RetryPause = TimeSpan.Zero, PoolPause = TimeSpan.Zero, RateLimitCooldown = TimeSpan.Zero };
        Assert.True(await scout.RunOnceAsync(TimeSpan.FromMinutes(1), refreshPool: false, default));

        // Vom Nutzer ausgelöste Prüfung: das vorhandene „bremst gerade", ohne Anfrage.
        var checks = new LeagueAccountChecks(_db, Client(http), new MemoryCache(new MemoryCacheOptions()), config);
        var r = (await checks.ForAccountAsync(acc.Id, default, reveal: true))!;
        Assert.False(r.ProfileLoaded);
        Assert.Contains(r.Items, i => i.Text.Contains("Lichess bremst gerade"));

        Assert.True(http.Urls.All(u => u.Contains("chess.com")), string.Join(", ", http.Urls));   // nur die Zwillingsprüfung auf chess.com
        Assert.Single(Warnings);

        // Gegenprobe: nach der Pause fragen dieselben Dienste wieder an.
        _now += LeagueRateGate.BasePause;
        http.Urls.Clear();
        await broadcasts.RunOnceAsync(TimeSpan.FromMinutes(5), discover: true, default);
        Assert.Contains(http.Urls, u => u.Contains("lichess.org"));
    }
}
