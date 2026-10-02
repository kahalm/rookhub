using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Tests;

/// <summary>
/// Spielervorbereitung, Phase 4 (2026-10-02): Online-Konten für einen Spieler des Partiebestands suchen — mit der Konto-Suche von
/// LeagueHub, aber ohne Tirol-Bezug (nur die Föderation zählt als Land, ein Tiroler Ort gar nicht), auf Knopfdruck, eine Suche zur
/// Zeit, Obergrenze je Verwalter und Stunde, ein 429 beendet die Suche. Vorschläge eines Minderjährigen kommen nie heraus;
/// LeagueHubs Übersicht und Zähler bleiben frei von Prep-Spielern, und seine Hintergrund-Takte greifen sie nicht auf.
/// Keine echten Abrufe: Lichess und chess.com sind gefälscht. FIDE-IDs im 99xxxx-Bereich.
/// </summary>
public class PrepAccountSearchTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly PrepAccountSearchGate _gate = new();

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private const string Fide = "990777";

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        public HttpResponseMessage Answer(HttpRequestMessage r) => answer(r);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.Method + " " + request.RequestUri);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
    private static HttpResponseMessage Status(HttpStatusCode c) => new(c) { Content = new StringContent("") };

    /// <summary>
    /// Paul Prepmann (GER) auf Lichess: „PaulPrepmann" (Klarname, DE — er), „Prepmann77" (aus der Suche: nur Land AT und Schwaz) und
    /// „PPrepmann" (aus dem Namen: nur Innsbruck). Bei einem Tiroler Ligaspieler trügen AT und Tirol die beiden letzten — hier nicht.
    /// </summary>
    private static FakeHttp World(int year = 1985) => new(req =>
    {
        var u = req.RequestUri!.ToString();
        if (u.Contains("/api/fide/player/")) return Ok("{\"id\":" + Fide + ",\"federation\":\"GER\",\"year\":" + year + "}");
        if (u.Contains("/api/player/autocomplete")) return Ok("""{"result":[{"name":"Prepmann77"}]}""");
        if (u.EndsWith("/api/users"))
            return Ok("""
                [{"id":"paulprepmann","username":"PaulPrepmann","profile":{"flag":"DE","realName":"Paul Prepmann"}},
                 {"id":"prepmann77","username":"Prepmann77","profile":{"flag":"AT","location":"Schwaz"}},
                 {"id":"pprepmann","username":"PPrepmann","profile":{"location":"Innsbruck"}}]
                """);
        if (u.Contains("api.chess.com/pub/player/")) return Status(HttpStatusCode.NotFound);
        return Status(HttpStatusCode.InternalServerError);
    });

    private static string Game(string white, string black, string date, int whiteElo, string? whiteFide) =>
        $"[Event \"Open\"]\n[Site \"Berlin\"]\n[Date \"{date}\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n"
        + $"[WhiteElo \"{whiteElo}\"]\n[BlackElo \"2000\"]\n" + (whiteFide is null ? "" : $"[WhiteFideId \"{whiteFide}\"]\n")
        + $"\n1. e4 e5 2. Nf3 Nc6 3. Bb{(date[3] - '0') % 5 + 1} a6 1-0\n\n";

    /// <summary>Prepmann im Bestand: früher 2300 (MaxElo), zuletzt 2210.</summary>
    private async Task SeedPrepAsync(string fide = Fide, string name = "Prepmann, Paul")
    {
        await new PrepImportService(_db).ImportChunkAsync(PrepSources.Mega, 0, 0,
            Game(name, "Gegner, Eins", "2015.03.01", 2300, fide) + Game(name, "Gegner, Zwei", "2024.05.17", 2210, fide), default);
    }

    private async Task<int> PrepIdAsync(string fide = Fide) => (await _db.PrepPlayers.SingleAsync(p => p.FideId == fide)).Id;

    private async Task SeedLeagueAsync()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Name = "Landesliga", Season = "2026/27", League = "LL", Stage = "x" });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Muster, Max", NameKey = "muster, max", FideId = "990222", Fed = "AUT", EloI = 1900 });
        await _db.SaveChangesAsync();
    }

    private LeagueAccountFinder Finder(FakeHttp http) =>
        new(_db, new HttpClient(http), NullLogger<LeagueAccountFinder>.Instance) { ChessComPause = TimeSpan.Zero, PlayerPause = TimeSpan.Zero };

    private static IConfiguration Config(bool on = true, int perHour = 20) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { [PrepAccountSearch.EnabledKey] = on ? "true" : null, [PrepAccountSearch.PerHourKey] = perHour.ToString() }).Build();

    private PrepAccountSearch Search(FakeHttp http, IConfiguration? config = null, Func<DateTime>? now = null) =>
        new(_db, Finder(http), new LeagueOnlineAccountService(_db), new LeagueAccountChecks(_db, new HttpClient(http), _cache), _gate, config ?? Config())
        { Now = now ?? (() => DateTime.UtcNow) };

    // ── Urteil ohne Tirol ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Judge_WithoutLeague_NoAustriaNoTirol_NeverMoreGenerous()
    {
        var tirolean = new LeagueAccountFinder.Player(Fide, "Prepmann, Paul", "AUT", 2210, "Kufstein 1");
        var prep = new LeagueAccountFinder.Player(Fide, "Prepmann, Paul", null, 2210, null, Local: false);
        LeagueAccountFinder.Profile Prof(string? flag, string? location, string? real = null) =>
            new("lichess", "X", "u", real, flag, location, null, null, null, false);

        // Nur Land AT: beim Tiroler Ligaspieler ein Hinweis, beim Spieler ohne Liga-Bezug (Föderation unbekannt) ein fremdes Land.
        Assert.NotNull(LeagueAccountFinder.Judge(tirolean, Prof("AT", null), derived: true));
        Assert.Null(LeagueAccountFinder.Judge(prep, Prof("AT", null), derived: true));
        Assert.Null(LeagueAccountFinder.Judge(prep, Prof("AT", null), derived: true, fideFed: "GER"));
        Assert.NotNull(LeagueAccountFinder.Judge(prep, Prof("AT", null), derived: true, fideFed: "AUT"));   // seine Föderation ist Österreich
        // Nur ein Tiroler Ort: zählt nur für die Liga.
        Assert.NotNull(LeagueAccountFinder.Judge(tirolean, Prof(null, "Innsbruck"), derived: true));
        Assert.Null(LeagueAccountFinder.Judge(prep, Prof(null, "Innsbruck"), derived: true));
        // Seine Föderation: zählt wie bei der Liga.
        var de = LeagueAccountFinder.Judge(prep, Prof("DE", null, "Paul Prepmann"), derived: true, fideFed: "GER")!;
        Assert.Equal(1 + 3 + 1, de.Score);
        Assert.Equal(LeagueAccountFinder.AllowedCountries("AUT", null), new HashSet<string> { "AT" });
        Assert.Empty(LeagueAccountFinder.AllowedCountries(null, null, local: false));
        // Nie mehr Punkte als für einen Tiroler Ligaspieler mit derselben Föderation.
        var tiroleanGer = tirolean with { Fed = "GER" };
        foreach (var p in new[] { Prof("DE", "Schwaz", "Paul Prepmann"), Prof(null, "Innsbruck", "Paul Prepmann"), Prof("AT", "Kufstein", "Paul Prepmann") })
            Assert.True((LeagueAccountFinder.Judge(prep, p, true, "GER")?.Score ?? 0) <= (LeagueAccountFinder.Judge(tiroleanGer, p, true, "GER")?.Score ?? 0));
    }

    [Fact]
    public async Task Player_FromTheDatabase_LatestElo_NotLocal()
    {
        await SeedPrepAsync();
        var p = (await LeagueAccountFinder.PlayerAsync(_db, Fide, default))!;
        Assert.Equal(("Prepmann, Paul", (int?)2210, false, (string?)null), (p.Name, p.Elo, p.Local, p.Fed));   // nicht MaxElo 2300
        Assert.Null(await LeagueAccountFinder.PlayerAsync(_db, "990999", default));
    }

    // ── Suchen ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scan_PrepPlayer_OnlyTheOneThatFitsWithoutTirol_InTheLeagueTables()
    {
        await SeedPrepAsync();
        var http = World();
        var (res, reason) = await Search(http).ScanAsync(await PrepIdAsync(), 1, default);
        Assert.Null(reason);
        Assert.Equal(1, res!["found"]!.GetValue<int>());
        var s = await _db.LeagueAccountSuggestions.SingleAsync();
        Assert.Equal((Fide, "lichess", "PaulPrepmann"), (s.FideId, s.Site, s.UserName));
        Assert.DoesNotContain("Österreich", s.Evidence);
        Assert.Equal("PaulPrepmann", res["items"]![0]!["user"]!.GetValue<string>());
        Assert.Equal(19, res["remaining"]!.GetValue<int>());
        var scan = await _db.LeagueAccountScans.SingleAsync(x => x.FideId == Fide);
        Assert.Equal((int?)1985, scan.BirthYear);                                       // der Jahrgang kam vor dem Vorschlag
        Assert.Equal("GER", scan.Federation);
    }

    [Fact]
    public async Task Scan_NoFideOrUnknown_NoRequest()
    {
        await new PrepImportService(_db).ImportChunkAsync(PrepSources.Mega, 0, 0, Game("Ohne, Fide", "Gegner, Eins", "2020.01.01", 2000, null), default);
        var http = World();
        var noFide = (await _db.PrepPlayers.SingleAsync(p => p.NameKey == "ohne, fide")).Id;
        Assert.Equal("noFide", (await Search(http).ScanAsync(noFide, 1, default)).Reason);
        Assert.Equal("notFound", (await Search(http).ScanAsync(987654, 1, default)).Reason);
        Assert.Empty(http.Urls);
    }

    [Fact]
    public async Task Scan_429_EndsTheSearch_NoRetry_GateFree()
    {
        await SeedPrepAsync();
        var world = World();
        var http = new FakeHttp(r => r.RequestUri!.ToString().EndsWith("/api/users") ? Status(HttpStatusCode.TooManyRequests) : world.Answer(r));
        var (res, reason) = await Search(http).ScanAsync(await PrepIdAsync(), 1, default);
        Assert.Null(res);
        Assert.Equal("rateLimited", reason);
        Assert.Single(http.Urls, u => u.EndsWith("/api/users"));                       // kein zweiter Versuch in der Anfrage
        Assert.DoesNotContain(http.Urls, u => u.Contains("chess.com"));               // die Suche endet sofort
        Assert.Empty(await _db.LeagueAccountSuggestions.ToListAsync());
        Assert.Null(_gate.TryEnter(2, 20, DateTime.UtcNow));                           // die nächste darf wieder
        _gate.Exit();
    }

    [Fact]
    public void Gate_OneAtATime_AndPerUserPerHour()
    {
        var t0 = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);
        Assert.Null(_gate.TryEnter(1, 2, t0));
        Assert.Equal("busy", _gate.TryEnter(2, 2, t0));                               // eine Suche zur Zeit, für alle
        _gate.Exit();
        Assert.Null(_gate.TryEnter(1, 2, t0.AddMinutes(1)));
        _gate.Exit();
        Assert.Equal("limit", _gate.TryEnter(1, 2, t0.AddMinutes(2)));                 // zwei je Stunde
        Assert.Equal(0, _gate.Remaining(1, 2, t0.AddMinutes(2)));
        Assert.Null(_gate.TryEnter(2, 2, t0.AddMinutes(2)));                           // ein anderer Verwalter darf
        _gate.Exit();
        Assert.Null(_gate.TryEnter(1, 2, t0.AddMinutes(61)));                          // eine Stunde später wieder
        _gate.Exit();
    }

    [Fact]
    public async Task Scan_Limit_PerHour()
    {
        await SeedPrepAsync();
        var now = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);
        var search = Search(World(), Config(perHour: 1), () => now);
        Assert.Null((await search.ScanAsync(await PrepIdAsync(), 7, default)).Reason);
        Assert.Equal("limit", (await search.ScanAsync(await PrepIdAsync(), 7, default)).Reason);
        now = now.AddHours(1).AddMinutes(1);
        Assert.Null((await search.ScanAsync(await PrepIdAsync(), 7, default)).Reason);
    }

    // ── Minderjährige ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Minor_NothingComesOut_NotEvenForManagers()
    {
        await SeedPrepAsync();
        var http = World(year: DateTime.UtcNow.Year - 12);
        var search = Search(http);
        var (res, _) = await search.ScanAsync(await PrepIdAsync(), 1, default);
        // Gesucht wurde (wie bei LeagueHub, der Jahrgang kam vor dem Vorschlag) — aber hier steht nichts davon.
        var s = await _db.LeagueAccountSuggestions.SingleAsync();
        Assert.Empty(res!["items"]!.AsArray());
        Assert.DoesNotContain("PaulPrepmann", res.ToJsonString());
        Assert.Empty((await search.SuggestionsAsync(await PrepIdAsync(), 1, default)).Result!["items"]!.AsArray());
        Assert.Equal("notFound", (await search.AcceptAsync(s.Id, true, "verwalter", default)).Reason);
        Assert.False(await search.RejectAsync(s.Id, default));
        Assert.Null(await search.ChecksAsync(s.Id, default));
        Assert.Empty(await _db.LeagueOnlineAccounts.ToListAsync());
    }

    // ── Übernehmen, Prüfen, Verwerfen ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Accept_PrepPlayer_AccountInTheLeagueTable_OnlyOwnSuggestions()
    {
        await SeedPrepAsync();
        await SeedLeagueAsync();
        var http = World();
        var search = Search(http);
        await search.ScanAsync(await PrepIdAsync(), 1, default);
        var s = await _db.LeagueAccountSuggestions.SingleAsync();
        var leagues = new LeagueAccountSuggestion { FideId = "990222", Site = "lichess", UserName = "MaxMuster", Url = "u", Score = 3, Evidence = "e",
            Status = LeagueSuggestionStatus.Open, CreatedAt = DateTime.UtcNow };
        _db.LeagueAccountSuggestions.Add(leagues);
        await _db.SaveChangesAsync();

        Assert.Equal("notFound", (await search.AcceptAsync(leagues.Id, true, "verwalter", default)).Reason);   // kein Spieler des Bestands
        Assert.Null(await search.ChecksAsync(leagues.Id, default));

        var checks = await search.ChecksAsync(s.Id, default);
        Assert.NotNull(checks);
        Assert.Equal("Prepmann, Paul", checks!.Player);                                   // der Spieler aus dem Bestand, nicht die FIDE-ID
        Assert.DoesNotContain(checks.Items, i => i.Key == "tirol");                       // ohne Liga-Bezug keine Tirol-Prüfung
        Assert.Equal(LeagueAccountChecks.Ok, checks.Items.Single(i => i.Key == "country").Status);

        var (acc, reason) = await search.AcceptAsync(s.Id, true, "verwalter", default);
        Assert.Null(reason);
        Assert.Equal(("PaulPrepmann", "sicher"), (acc!["user"]!.GetValue<string>(), acc["conf"]!.GetValue<string>()));
        var row = await _db.LeagueOnlineAccounts.SingleAsync();
        Assert.Equal((Fide, "verwalter"), (row.FideId, row.AddedBy));
        Assert.Empty(await _db.LeagueAccountSuggestions.Where(x => x.FideId == Fide).ToListAsync());     // der Vorschlag ist erledigt
    }

    [Fact]
    public async Task Reject_StaysRejected_NotSuggestedAgain()
    {
        await SeedPrepAsync();
        var search = Search(World());
        await search.ScanAsync(await PrepIdAsync(), 1, default);
        var s = await _db.LeagueAccountSuggestions.SingleAsync();
        Assert.True(await search.RejectAsync(s.Id, default));
        var (res, _) = await search.ScanAsync(await PrepIdAsync(), 1, default);
        Assert.Empty(res!["items"]!.AsArray());
        Assert.Equal(0, res["found"]!.GetValue<int>());
    }

    // ── LeagueHub bleibt frei von Prep-Spielern ────────────────────────────────────────────────

    [Fact]
    public async Task LeagueHub_OverviewAndCounters_WithoutPrepPlayers()
    {
        await SeedPrepAsync();
        await SeedLeagueAsync();
        await Search(World()).ScanAsync(await PrepIdAsync(), 1, default);
        _db.LeagueAccountSuggestions.Add(new LeagueAccountSuggestion { FideId = "990222", Site = "lichess", UserName = "MaxMuster", Url = "u",
            Score = 3, Evidence = "e", Status = LeagueSuggestionStatus.Open, CreatedAt = DateTime.UtcNow });
        var prepAcc = new LeagueOnlineAccount { FideId = Fide, Site = "lichess", UserName = "PaulOnline", Url = "u", Confidence = "sicher" };
        var leagueAcc = new LeagueOnlineAccount { FideId = "990222", Site = "lichess", UserName = "MaxOnline", Url = "u", Confidence = "sicher" };
        _db.LeagueOnlineAccounts.AddRange(prepAcc, leagueAcc);
        await _db.SaveChangesAsync();
        _db.LeagueOnlineGames.AddRange(
            new LeagueOnlineGame { AccountId = prepAcc.Id, FideId = Fide, ExternalId = "p1", PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4" },
            new LeagueOnlineGame { AccountId = prepAcc.Id, FideId = Fide, ExternalId = "p2", PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4" },
            new LeagueOnlineGame { AccountId = leagueAcc.Id, FideId = "990222", ExternalId = "l1", PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "d4", Moves = "d4" });
        await _db.SaveChangesAsync();

        var all = await new LeagueOnlineAccountService(_db).SuggestionsAsync(null, default);
        Assert.Equal(new[] { "990222" }, all["items"]!.AsArray().Select(i => i!["fide"]!.GetValue<string>()));
        Assert.Single((await new LeagueOnlineAccountService(_db).SuggestionsAsync(Fide, default))["items"]!.AsArray());   // je Spieler weiter da
        var sources = await new LeagueGameSources(_db, null).GetAsync(default);
        Assert.Equal(1, sources["onlineTotal"]!.GetValue<int>());                      // nur die Partie des Ligaspielers
    }

    [Fact]
    public async Task LeagueHub_BackgroundRescan_DoesNotPickUpPrepPlayers()
    {
        await SeedPrepAsync();
        await SeedLeagueAsync();
        var http = new FakeHttp(r => r.RequestUri!.ToString().Contains("/api/fide/player/") ? Ok("{\"federation\":\"AUT\",\"year\":1980}")
            : r.RequestUri!.ToString().EndsWith("/api/users") ? Ok("[]") : Status(HttpStatusCode.NotFound));
        Assert.False(await Finder(http).RunOnceAsync(TimeSpan.FromMinutes(5), default));
        Assert.NotEmpty(http.Urls);
        Assert.DoesNotContain(http.Urls, u => u.Contains(Fide) || u.Contains("prepmann", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "990222" }, await _db.LeagueAccountScans.Select(s => s.FideId).ToListAsync());
    }

    [Fact]
    public async Task LeagueHub_TeamScout_DoesNotPickUpPrepPlayers()
    {
        // Ein Mitglied eines Tiroler Lichess-Teams, dessen Klarname NUR zu einem Spieler des Bestands passt.
        await SeedPrepAsync();
        await SeedLeagueAsync();
        var http = new FakeHttp(req =>
        {
            var u = req.RequestUri!.ToString();
            if (u.Contains("/api/team/search")) return Ok("""{"currentPage":1,"currentPageResults":[{"id":"sk-kufstein","name":"SK Kufstein"}]}""");
            if (u.Contains("/api/team/sk-kufstein/users")) return Ok("{\"id\":\"paulprepmann\",\"username\":\"PaulPrepmann\"}\n");
            if (u.Contains("/api/team/sk-kufstein/arena")) return Ok("");
            if (u.EndsWith("/api/users"))
                return Ok("""[{"id":"paulprepmann","username":"PaulPrepmann","profile":{"flag":"AT","realName":"Paul Prepmann","location":"Kufstein"}}]""");
            if (u.Contains("/api/fide/player/")) return Ok("{\"federation\":\"AUT\",\"year\":1980}");
            return Status(HttpStatusCode.NotFound);
        });
        var scout = new LeagueTeamScout(_db, new HttpClient(http), NullLogger<LeagueTeamScout>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LeagueOnline:TeamPlaces"] = "Kufstein" }).Build())
        { Pause = TimeSpan.Zero, RetryPause = TimeSpan.Zero, PoolPause = TimeSpan.Zero, RateLimitCooldown = TimeSpan.Zero };
        await scout.RefreshPoolAsync(default);
        Assert.NotEmpty(await _db.LeagueScoutAccounts.ToListAsync());                  // das Konto ist im Bestand der Team-Suche
        http.Urls.Clear();
        await scout.RunOnceAsync(TimeSpan.FromMinutes(5), refreshPool: false, default);
        Assert.Contains(http.Urls, u => u.EndsWith("/api/users"));                     // das Profil wurde geholt und bewertet …
        Assert.Empty(await _db.LeagueAccountSuggestions.ToListAsync());               // … aber nur gegen die Meldelisten
        Assert.DoesNotContain(await _db.LeagueAccountScans.Select(s => s.FideId).ToListAsync(), f => f == Fide);
    }

    // ── Controller: Recht und Schalter ─────────────────────────────────────────────────────────

    private static PrepController Controller() => new(new PrepImportService(new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1") }, "t")) },
        },
    };

    [Fact]
    public async Task Switch_OffByDefault_Everything404Disabled()
    {
        await SeedPrepAsync();
        var http = World();
        var off = Search(http, new ConfigurationBuilder().Build());
        Assert.False(off.Enabled);
        var c = Controller();
        var id = await PrepIdAsync();
        foreach (var r in new[]
                 {
                     await c.Suggestions(id, off, default), await c.ScanSuggestions(id, off, default),
                     await c.AcceptSuggestion(1, new PrepController.AcceptRequest(true), off, default),
                     await c.RejectSuggestion(1, off, default), await c.SuggestionChecks(1, off, default),
                 })
            Assert.Contains("disabled", Assert.IsType<NotFoundObjectResult>(r).Value!.ToString());
        Assert.Empty(http.Urls);
        Assert.Empty(await _db.LeagueAccountSuggestions.ToListAsync());
    }

    [Fact]
    public async Task Controller_BusyLimitAndRateLimited_AsStatusCodes()
    {
        await SeedPrepAsync();
        var c = Controller();
        var id = await PrepIdAsync();
        Assert.Null(_gate.TryEnter(99, 20, DateTime.UtcNow));                          // jemand sucht gerade
        Assert.IsType<ConflictObjectResult>(await c.ScanSuggestions(id, Search(World()), default));
        _gate.Exit();
        var world = World();
        var slow = new FakeHttp(r => r.RequestUri!.ToString().EndsWith("/api/users") ? Status(HttpStatusCode.TooManyRequests) : world.Answer(r));
        Assert.Equal(503, Assert.IsType<ObjectResult>(await c.ScanSuggestions(id, Search(slow), default)).StatusCode);
        var limited = Search(World(), Config(perHour: 1));
        Assert.Equal(429, Assert.IsType<ObjectResult>(await c.ScanSuggestions(id, limited, default)).StatusCode);   // die 503 zählte schon
    }
}

