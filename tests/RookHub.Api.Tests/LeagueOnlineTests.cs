using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Online-Konten der Ligaspieler, der Abruf ihrer Partien und die Filter des Eröffnungsbaums (0.605.0). Die Antworten von
/// Lichess und chess.com sind nachgebaut — Felder und Werte wie in den öffentlichen Schnittstellen (Lichess-Export ndjson,
/// chess.com-Monatsarchiv), nachgesehen am 2026-09-30.
/// </summary>
public class LeagueOnlineTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private static long Ms(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    private static readonly DateTime Recent = DateTime.UtcNow.Date.AddDays(-30);

    private async Task SeedPlayerAsync(string fide = "222")
    {
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Muster, Max", NameKey = LeagueNames.NameKey("Muster, Max"), FideId = fide });
        await _db.SaveChangesAsync();
    }

    private LeagueOnlineAccountService Accounts(LeagueOnlineSyncSignal? signal = null) => new(_db, signal);

    // ── Seiten ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sites_ParseNamesAndProfileAddresses()
    {
        Assert.Equal((LeagueOnlineSites.Lichess, "Max_Muster"), LeagueOnlineSites.Parse("lichess", " Max_Muster "));
        Assert.Equal((LeagueOnlineSites.Lichess, "Max_Muster"), LeagueOnlineSites.Parse(null, "https://lichess.org/@/Max_Muster/"));
        Assert.Equal((LeagueOnlineSites.ChessCom, "maxm"), LeagueOnlineSites.Parse("lichess", "https://www.chess.com/member/maxm"));   // die Adresse gewinnt
        Assert.Equal((LeagueOnlineSites.ChessCom, "maxm"), LeagueOnlineSites.Parse("Chess.com", "@maxm"));
        Assert.Null(LeagueOnlineSites.Parse("chess.com", "ab"));                                   // chess.com: mindestens 3 Zeichen
        Assert.Null(LeagueOnlineSites.Parse("lichess", "Max Muster"));
        Assert.Null(LeagueOnlineSites.Parse("playchess", "Max"));
        Assert.Null(LeagueOnlineSites.Parse(null, "Max"));
        Assert.Equal("https://lichess.org/@/Max_Muster", LeagueOnlineSites.ProfileUrl(LeagueOnlineSites.Lichess, "Max_Muster"));
        Assert.Equal("https://www.chess.com/member/maxm", LeagueOnlineSites.ProfileUrl(LeagueOnlineSites.ChessCom, "maxm"));
    }

    // ── Konten pflegen ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ChecksPlayerSiteNameAndDuplicates_AndWakesTheSync()
    {
        await SeedPlayerAsync();
        var signal = new LeagueOnlineSyncSignal();
        var svc = Accounts(signal);
        Assert.Equal("unknownPlayer", (await svc.CreateAsync("999", new("lichess", "Max", true, null), default)).Reason);
        Assert.Equal("invalidSite", (await svc.CreateAsync("222", new("playchess", "Max", true, null), default)).Reason);
        Assert.Equal("invalidUser", (await svc.CreateAsync("222", new("lichess", "Max Muster", true, null), default)).Reason);

        var (acc, reason) = await svc.CreateAsync("222", new("lichess", "Max_Muster", true, "  Profil nennt den Verein  "), default);
        Assert.Null(reason);
        Assert.Equal(("sicher", "Profil nennt den Verein", true, "https://lichess.org/@/Max_Muster"),
            (acc!.Confidence, acc.Evidence, acc.Manual, acc.Url));
        Assert.True(await signal.WaitAsync(TimeSpan.Zero, default));

        Assert.Equal("duplicate", (await svc.CreateAsync("222", new("lichess", "max_muster", false, null), default)).Reason);
        var (unsure, _) = await svc.CreateAsync("222", new(null, "https://www.chess.com/member/maxm", false, null), default);
        Assert.Equal(("chess.com", "wahrscheinlich"), (unsure!.Site, unsure.Confidence));
    }

    private async Task<LeagueOnlineAccount> AccountWithGamesAsync(string site = "lichess", string user = "Max_Muster", string conf = "sicher")
    {
        var acc = new LeagueOnlineAccount { FideId = "222", Site = site, UserName = user, Url = "u", Confidence = conf, Manual = true,
            SyncedAt = DateTime.UtcNow, SyncCursor = 5, GameCount = 1 };
        _db.LeagueOnlineAccounts.Add(acc);
        await _db.SaveChangesAsync();
        _db.LeagueOnlineGames.Add(new LeagueOnlineGame { AccountId = acc.Id, FideId = "222", ExternalId = "g1", PlayedAt = Recent,
            Speed = "blitz", White = true, Result = "1-0", Line = "e4 e5", Moves = "e4 e5", Plies = 2 });
        await _db.SaveChangesAsync();
        return acc;
    }

    [Fact]
    public async Task Update_CommentKeepsTheGames_ADifferentNameStartsOver()
    {
        await SeedPlayerAsync();
        var acc = await AccountWithGamesAsync();
        var svc = Accounts();
        var (a, _) = await svc.UpdateAsync(acc.Id, new(null, null, false, "Blitz-Konto"), default);
        Assert.Equal(("wahrscheinlich", "Blitz-Konto", 1), (a!.Confidence, a.Evidence, await _db.LeagueOnlineGames.CountAsync()));
        (a, _) = await svc.UpdateAsync(acc.Id, new(null, "max_muster", null, null), default);          // nur die Schreibweise
        Assert.Equal(("max_muster", 1), (a!.UserName, await _db.LeagueOnlineGames.CountAsync()));

        (a, _) = await svc.UpdateAsync(acc.Id, new(null, "Anderer_Name", null, null), default);
        Assert.Equal(("Anderer_Name", 0, 0L, (DateTime?)null), (a!.UserName, await _db.LeagueOnlineGames.CountAsync(), a.SyncCursor, a.SyncedAt));
        Assert.Equal("notFound", (await svc.UpdateAsync(4711, new(null, null, true, null), default)).Reason);
    }

    [Fact]
    public async Task Delete_TakesTheGamesAlong()
    {
        await SeedPlayerAsync();
        var acc = await AccountWithGamesAsync();
        Assert.True(await Accounts().DeleteAsync(acc.Id, default));
        Assert.Equal((0, 0), (await _db.LeagueOnlineAccounts.CountAsync(), await _db.LeagueOnlineGames.CountAsync()));
        Assert.False(await Accounts().DeleteAsync(acc.Id, default));
    }

    [Fact]
    public async Task Changes_ReachTheRosterOfThePrebuiltViews()
    {
        await SeedPlayerAsync();
        _db.LeagueViews.Add(new LeagueView { Tnr = 1, GeneratedAt = DateTime.UtcNow,
            Json = """{"fixtures":{"Kufstein 1":{"3":{"roster":[{"fide":"222","acc":[]},{"fide":"333","acc":[]}]}}}}""" });
        await _db.SaveChangesAsync();
        await Accounts().CreateAsync("222", new("lichess", "Max_Muster", false, "geheim"), default);
        var roster = JsonNode.Parse((await _db.LeagueViews.AsNoTracking().SingleAsync()).Json)!["fixtures"]!["Kufstein 1"]!["3"]!["roster"]!.AsArray();
        var acc = roster[0]!["acc"]!.AsArray().Single()!;
        Assert.Equal(("Max_Muster", "wahrscheinlich"), (acc["user"]!.GetValue<string>(), acc["conf"]!.GetValue<string>()));
        Assert.Null(acc["comment"]);                                          // der Kommentar steht nie in der Ansicht
        Assert.Empty(roster[1]!["acc"]!.AsArray());
    }

    [Fact]
    public void Json_CommentAndSyncStateOnlyWhenLoggedIn()
    {
        var a = new LeagueOnlineAccount { Id = 7, Site = "lichess", UserName = "M", Url = "u", Confidence = "sicher", Evidence = "x", GameCount = 3 };
        var full = LeagueOnlineAccountService.ToJson(a, full: true);
        Assert.Equal((7, "x", 3), (full["id"]!.GetValue<int>(), full["comment"]!.GetValue<string>(), full["games"]!.GetValue<int>()));
        var shared = LeagueOnlineAccountService.ToJson(a, full: false);
        Assert.Equal(new[] { "conf", "site", "url", "user" }, shared.Select(kv => kv.Key).OrderBy(k => k));
    }

    // ── Lesen ──────────────────────────────────────────────────────────────────────────────────

    private static string LichessLine(string id, DateTime created, string speed, string white, string black, string? winner,
        string status = "mate", string variant = "standard", string moves = "e4 e5 Nf3 Nc6")
    {
        JsonObject Side(string name, int rating) => new() { ["user"] = new JsonObject { ["name"] = name, ["id"] = name.ToLowerInvariant() }, ["rating"] = rating };
        var o = new JsonObject
        {
            ["id"] = id, ["rated"] = true, ["variant"] = variant, ["speed"] = speed, ["perf"] = speed,
            ["createdAt"] = Ms(created), ["lastMoveAt"] = Ms(created) + 60000, ["status"] = status,
            ["players"] = new JsonObject { ["white"] = Side(white, 1900), ["black"] = Side(black, 1800) }, ["moves"] = moves,
        };
        if (winner is not null) o["winner"] = winner;
        return o.ToJsonString();
    }

    [Fact]
    public void Lichess_ColorsResultsSpeeds_AndSkipsVariantsAndAborted()
    {
        var t = Recent;
        var ndjson = string.Join("\n",
            LichessLine("a1", t, "blitz", "Max_Muster", "Other", "white"),
            LichessLine("a2", t.AddMinutes(1), "ultraBullet", "Other", "Max_Muster", null, status: "stalemate"),
            LichessLine("a3", t.AddMinutes(2), "correspondence", "Other", "Max_Muster", "white"),
            LichessLine("a4", t.AddMinutes(3), "blitz", "Max_Muster", "Other", null, status: "aborted"),
            LichessLine("a5", t.AddMinutes(4), "blitz", "Max_Muster", "Other", "black", variant: "chess960"),
            LichessLine("a6", t.AddMinutes(5), "blitz", "Somebody", "Other", "black"), "");
        var p = LeagueOnlineSync.ParseLichess(ndjson, "max_muster");
        Assert.Equal((6, Ms(t.AddMinutes(5))), (p.Rows, p.Cursor));                // der Stand zählt auch Übersprungenes
        Assert.Equal(new[] { "a1:blitz:W:1-0", "a2:bullet:S:1/2-1/2", "a3:correspondence:S:1-0" },
            p.Games.Select(g => $"{g.ExternalId}:{g.Speed}:{(g.White ? "W" : "S")}:{g.Result}"));
        Assert.Equal(("Other", 1800, 1900), (p.Games[0].Opponent, p.Games[0].OpponentRating, p.Games[0].PlayerRating));
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6" }, p.Games[0].Moves);
    }

    private static string ChessComGame(string id, DateTime end, string timeClass, string white, string whiteResult, string black,
        string blackResult, string rules = "chess", string extraHeaders = "") => new JsonObject
    {
        ["url"] = $"https://www.chess.com/game/live/{id}",
        ["pgn"] = $"[Event \"Live Chess\"]\n[UTCDate \"{end:yyyy.MM.dd}\"]\n[UTCTime \"{end:HH:mm:ss}\"]\n{extraHeaders}[White \"{white}\"]\n[Black \"{black}\"]\n\n"
                  + "1. d4 {[%clk 0:04:59.9]} 1... d5 {[%clk 0:04:58]} 2. c4 *",
        ["time_class"] = timeClass, ["rules"] = rules, ["rated"] = true, ["end_time"] = Ms(end.AddMinutes(10)) / 1000,
        ["white"] = new JsonObject { ["username"] = white, ["rating"] = 1700, ["result"] = whiteResult },
        ["black"] = new JsonObject { ["username"] = black, ["rating"] = 1600, ["result"] = blackResult },
    }.ToJsonString();

    [Fact]
    public void ChessCom_Archive_ResultsFromTheResultCodes_DailyIsCorrespondence()
    {
        var t = Recent;
        var json = "{\"games\":[" + string.Join(",",
            ChessComGame("1", t, "blitz", "MaxM", "agreed", "x", "agreed"),
            ChessComGame("2", t.AddHours(1), "daily", "x", "win", "maxm", "checkmated"),
            ChessComGame("3", t.AddHours(2), "rapid", "maxm", "timeout", "x", "win"),
            ChessComGame("4", t.AddHours(3), "blitz", "maxm", "win", "x", "resigned", rules: "chess960"),
            ChessComGame("5", t.AddHours(4), "blitz", "maxm", "win", "x", "resigned", extraHeaders: "[SetUp \"1\"]\n[FEN \"8/8/8/8/8/8/8/K1k5 w - - 0 1\"]\n")) + "]}";
        var p = LeagueOnlineSync.ParseChessCom(json, "maxm");
        Assert.Equal(5, p.Rows);
        Assert.Equal(new[] { "1:blitz:W:1/2-1/2", "2:correspondence:S:1-0", "3:rapid:W:0-1" },
            p.Games.Select(g => $"{g.ExternalId}:{g.Speed}:{(g.White ? "W" : "S")}:{g.Result}"));
        Assert.Equal(new[] { "d4", "d5", "c4" }, p.Games[0].Moves);                // ohne Uhr-Kommentare
        Assert.Equal(t, p.Games[0].PlayedAt);                                     // Beginn aus UTCDate/UTCTime
        Assert.Equal(("x", 1600), (p.Games[0].Opponent, p.Games[0].OpponentRating));
    }

    [Fact]
    public void ChessCom_ArchivesFromAMonthOn_Sorted()
    {
        const string json = """{"archives":["https://api.chess.com/pub/player/m/games/2026/03","https://api.chess.com/pub/player/m/games/2020/12","https://api.chess.com/pub/player/m/games/2026/01"]}""";
        Assert.Equal(new[] { "2026/01", "2026/03" }, LeagueOnlineSync.ArchivesFrom(json, 2026, 1).Select(u => u[^7..]));
    }

    // ── Abruf ──────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };

    private LeagueOnlineSync Sync(FakeHttp http) => new(_db, new HttpClient(http), NullLogger<LeagueOnlineSync>.Instance);

    [Fact]
    public async Task Lichess_StoresNewGames_FromTheCursorOn_WithoutDuplicates()
    {
        await SeedPlayerAsync();
        var acc = new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "Max_Muster", Url = "u", Confidence = "sicher" };
        _db.LeagueOnlineAccounts.Add(acc);
        await _db.SaveChangesAsync();
        var body = string.Join("\n", LichessLine("a1", Recent, "blitz", "Max_Muster", "O", "white"),
            LichessLine("a2", Recent.AddHours(1), "rapid", "O", "Max_Muster", "white"),
            LichessLine("old", DateTime.UtcNow.AddYears(-8), "blitz", "Max_Muster", "O", "white"));   // älter als der Horizont
        var http = new FakeHttp(_ => Ok(body));
        var sync = Sync(http);

        Assert.False(await sync.SyncAccountAsync(acc, default));
        Assert.Equal((2, (string?)null, false), (acc.GameCount, acc.SyncError, acc.SyncMore));
        Assert.Equal(Ms(Recent.AddHours(1)), acc.SyncCursor);
        Assert.Contains("sort=dateAsc", http.Urls[0]);
        var since = long.Parse(System.Text.RegularExpressions.Regex.Match(http.Urls[0], @"since=(\d+)").Groups[1].Value);
        Assert.InRange(since, Ms(DateTime.UtcNow.AddYears(-5)) - 60_000, Ms(DateTime.UtcNow.AddYears(-5)));   // der Horizont

        await sync.SyncAccountAsync(acc, default);                                // dieselben Partien noch einmal
        Assert.Equal(2, await _db.LeagueOnlineGames.CountAsync());
        Assert.Contains($"since={acc.SyncCursor + 1}", http.Urls[1]);
        var g = await _db.LeagueOnlineGames.AsNoTracking().OrderBy(x => x.PlayedAt).FirstAsync();
        Assert.Equal(("222", "e4 e5 Nf3 Nc6", 4, true), (g.FideId, g.Line, g.Plies, g.White));
    }

    [Fact]
    public async Task Lichess_FullPage_LeavesARest_UnknownAccountIsNamed()
    {
        await SeedPlayerAsync();
        var acc = new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "Max_Muster", Url = "u", Confidence = "sicher" };
        var gone = new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "Weg_Weg", Url = "u", Confidence = "sicher" };
        _db.LeagueOnlineAccounts.AddRange(acc, gone);
        await _db.SaveChangesAsync();
        var page = 0;
        var http = new FakeHttp(r =>
        {
            if (r.RequestUri!.AbsolutePath.Contains("Weg_Weg")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            var start = Recent.AddDays(-20).AddHours(page++ * 100);
            return Ok(string.Join("\n", Enumerable.Range(0, LeagueOnlineSync.LichessPageSize)
                .Select(i => LichessLine($"p{page}-{i}", start.AddMinutes(i), "bullet", "Max_Muster", "O", "black"))));
        });
        Assert.True(await Sync(http).SyncAccountAsync(acc, default));            // voller Deckel: noch mehr da
        Assert.Equal((LeagueOnlineSync.LichessPagesPerCall * LeagueOnlineSync.LichessPageSize, true), (acc.GameCount, acc.SyncMore));
        Assert.False(await Sync(http).SyncAccountAsync(gone, default));
        Assert.Equal("Konto nicht gefunden", gone.SyncError);
    }

    [Fact]
    public async Task RunOnce_StopsWhenThrottled_WithoutMarkingTheAccount()
    {
        await SeedPlayerAsync();
        _db.LeagueOnlineAccounts.Add(new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "Max_Muster", Url = "u", Confidence = "sicher" });
        await _db.SaveChangesAsync();
        var http = new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        Assert.True(await Sync(http).RunOnceAsync(TimeSpan.FromHours(12), TimeSpan.FromMinutes(1), default));
        Assert.Null((await _db.LeagueOnlineAccounts.AsNoTracking().SingleAsync()).SyncedAt);
    }

    [Fact]
    public async Task ChessCom_FetchesTheArchivesSinceTheHorizon()
    {
        await SeedPlayerAsync();
        var acc = new LeagueOnlineAccount { FideId = "222", Site = "chess.com", UserName = "MaxM", Url = "u", Confidence = "sicher" };
        _db.LeagueOnlineAccounts.Add(acc);
        await _db.SaveChangesAsync();
        var month = $"{Recent:yyyy}/{Recent:MM}";
        var http = new FakeHttp(r => r.RequestUri!.AbsolutePath.EndsWith("/archives")
            ? Ok($"{{\"archives\":[\"https://api.chess.com/pub/player/maxm/games/2015/01\",\"https://api.chess.com/pub/player/maxm/games/{month}\"]}}")
            : Ok("{\"games\":[" + ChessComGame("77", Recent, "blitz", "MaxM", "win", "x", "resigned") + "]}"));
        Assert.False(await Sync(http).SyncAccountAsync(acc, default));
        Assert.Equal(new[] { "https://api.chess.com/pub/player/maxm/games/archives", $"https://api.chess.com/pub/player/maxm/games/{month}" }, http.Urls);
        Assert.Equal(1, acc.GameCount);
    }

    // ── Eröffnungsbaum mit Filtern ─────────────────────────────────────────────────────────────

    private async Task TreeSeedAsync()
    {
        await SeedPlayerAsync();
        var store = new LeagueProfileStore(_db);
        static string Game(string moves, string date, string result) =>
            $"[LeagueSource \"Mega\"]\n[Event \"Open\"]\n[Date \"{date}\"]\n[White \"Muster, Max\"]\n[Black \"A, B\"]\n[Result \"{result}\"]\n[WhiteFideId \"222\"]\n\n{moves} {result}\n";
        await store.ImportGamesAsync(Game("1. d4 d5", $"{DateTime.UtcNow.Year}.01.01", "1-0") + "\n" + Game("1. e4 e5", "2010.01.01", "0-1"), "Mega", default);
        var sure = new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "Max_Muster", Url = "u", Confidence = "sicher" };
        var unsure = new LeagueOnlineAccount { FideId = "222", Site = "chess.com", UserName = "maxm", Url = "u", Confidence = "wahrscheinlich" };
        _db.LeagueOnlineAccounts.AddRange(sure, unsure);
        await _db.SaveChangesAsync();
        LeagueOnlineGame G(LeagueOnlineAccount a, string id, string line, string speed, DateTime at, string result, bool white = true) =>
            new() { AccountId = a.Id, FideId = "222", ExternalId = id, Line = line, Moves = line, Plies = line.Split(' ').Length,
                Speed = speed, PlayedAt = at, Result = result, White = white };
        _db.LeagueOnlineGames.AddRange(
            G(sure, "1", "e4 c5 Nf3", "blitz", Recent, "1-0"),
            G(sure, "2", "e4 e5", "bullet", Recent, "0-1"),
            G(sure, "3", "e4 c5", "rapid", DateTime.UtcNow.AddYears(-4), "1/2-1/2"),
            G(unsure, "4", "c4", "blitz", Recent, "1-0"),
            G(sure, "5", "d4", "blitz", Recent, "1-0", white: false));            // mit Schwarz — zählt für Weiß nicht
        await _db.SaveChangesAsync();
    }

    private static (int Total, int Board, int Online, string Moves) Summary(JsonObject t) =>
        (t["total"]!.GetValue<int>(), t["board"]!.GetValue<int>(), t["online"]!.GetValue<int>(),
            string.Join(" ", t["moves"]!.AsArray().OrderByDescending(m => m!["n"]!.GetValue<int>()).ThenBy(m => m!["san"]!.GetValue<string>(), StringComparer.Ordinal)
                .Select(m => $"{m!["san"]}:{m["n"]}")));

    [Fact]
    public async Task Tree_Board_Both_Online_SpeedsYears_AndSharedLinksOnlySureAccounts()
    {
        await TreeSeedAsync();
        var store = new LeagueProfileStore(_db);
        Task<JsonObject?> Tree(string? line, string? source, string? speeds = null, int? years = null, bool onlySure = false) =>
            store.TreeAsync("222", "w", line, default, LeagueProfileStore.TreeFilter.Parse(source, speeds, years, onlySure));

        Assert.Equal((2, 2, 0, "d4:1 e4:1"), Summary((await store.TreeAsync("222", "w", null, default))!));   // Vorgabe: wie bisher
        Assert.Equal((6, 2, 4, "e4:4 c4:1 d4:1"), Summary((await Tree(null, "both"))!));
        Assert.Equal((4, 0, 4, "e4:3 c4:1"), Summary((await Tree(null, "online"))!));
        Assert.Equal((2, 0, 2, "c4:1 e4:1"), Summary((await Tree(null, "online", "blitz,unbekannt"))!));
        Assert.Equal((4, 1, 3, "e4:2 c4:1 d4:1"), Summary((await Tree(null, "both", years: 1))!));   // 2010 und vor 4 Jahren fallen weg
        Assert.Equal((3, 0, 3, "e4:3"), Summary((await Tree(null, "online", onlySure: true))!));
        var e4 = (await Tree("e4", "online"))!;
        Assert.Equal((3, 0, 3, "c5:2 e5:1"), Summary(e4));
        Assert.Equal(75, e4["moves"]!.AsArray()[0]!["score"]!.GetValue<int>());      // c5: 1 + ½ aus 2 Partien
    }

    /// <summary>0.612.0 (Wunsch „unsichere standardmäßig nicht in den Baum, über einen Schalter dazu"): angemeldet zählt der Baum
    /// ohne Angabe nur gesicherte Konten, <c>unsure=true</c> nimmt die unsicheren dazu; die Karte nennt, wie viele das wären.</summary>
    [Fact]
    public async Task TreeEndpoint_UnsureAccountsOnlyOnRequest_AndCardCountsThem()
    {
        await TreeSeedAsync();
        foreach (var a in _db.LeagueOnlineAccounts) a.GameCount = a.Confidence == "sicher" ? 4 : 1;
        await _db.SaveChangesAsync();
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var ctl = new LeagueController(league, null!, null!);
        static JsonObject Body(IActionResult r) => (JsonObject)((OkObjectResult)r).Value!;

        Assert.Equal((3, 0, 3, "e4:3"), Summary(Body(await ctl.Tree("222", "w", null, "online", null, null, null, default))));
        Assert.Equal((3, 0, 3, "e4:3"), Summary(Body(await ctl.Tree("222", "w", null, "online", null, null, false, default))));
        Assert.Equal((4, 0, 4, "e4:3 c4:1"), Summary(Body(await ctl.Tree("222", "w", null, "online", null, null, true, default))));

        var card = (await league.CardAsync("222", onlySure: false, default))!;
        Assert.Equal((5, 1), (card["online"]!.GetValue<int>(), card["onlineUnsure"]!.GetValue<int>()));
    }

    /// <summary>Codereview N4-001: Lichess schreibt Schach und Matt mit („Bb4+", „Qxf7#"), die Brettpartien laufen über CleanSan
    /// ohne. Gespeichert wird die Lichess-Partie deshalb in derselben Schreibweise — sonst stünde nach 3.Nf3 „Bb4" UND „Bb4+" im
    /// Baum, und der Klick auf einen der beiden verlöre die andere Quelle.</summary>
    [Fact]
    public async Task Tree_CheckAndMateFromLichessAndBoard_AreOneNode_AndTheSubtreeKeepsBothSources()
    {
        await SeedPlayerAsync();
        static string Game(string black, string moves, string result) =>
            $"[LeagueSource \"Mega\"]\n[Event \"Open\"]\n[Date \"{DateTime.UtcNow.Year}.01.01\"]\n[White \"Muster, Max\"]\n[Black \"{black}\"]\n[Result \"{result}\"]\n[WhiteFideId \"222\"]\n\n{moves} {result}\n";
        var store = new LeagueProfileStore(_db);
        await store.ImportGamesAsync(Game("A, B", "1. d4 Nf6 2. c4 e6 3. Nf3 Bb4+ 4. Bd2 Be7", "1-0") + "\n"
            + Game("C, D", "1. e4 e5 2. Bc4 Nc6 3. Qh5 Nf6 4. Qxf7#", "1-0"), "Mega", default);
        var acc = new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "Max_Muster", Url = "u", Confidence = "sicher" };
        _db.LeagueOnlineAccounts.Add(acc);
        await _db.SaveChangesAsync();
        var body = string.Join("\n",
            LichessLine("b1", Recent, "blitz", "Max_Muster", "O", "white", moves: "d4 Nf6 c4 e6 Nf3 Bb4+ Nbd2 O-O"),
            LichessLine("b2", Recent.AddHours(1), "blitz", "Max_Muster", "O", "white", moves: "e4 e5 Bc4 Nc6 Qh5 Nf6 Qxf7#"));
        await Sync(new FakeHttp(_ => Ok(body))).SyncAccountAsync(acc, default);

        var stored = await _db.LeagueOnlineGames.AsNoTracking().OrderBy(g => g.PlayedAt).Select(g => g.Line + " | " + g.Moves).ToListAsync();
        Assert.Equal(new[] { "d4 Nf6 c4 e6 Nf3 Bb4 Nbd2 O-O | d4 Nf6 c4 e6 Nf3 Bb4 Nbd2 O-O",
            "e4 e5 Bc4 Nc6 Qh5 Nf6 Qxf7 | e4 e5 Bc4 Nc6 Qh5 Nf6 Qxf7" }, stored);

        Task<JsonObject?> Tree(string line) =>
            store.TreeAsync("222", "w", line, default, LeagueProfileStore.TreeFilter.Parse("both", null, null, false));
        Assert.Equal((2, 1, 1, "Bb4:2"), Summary((await Tree("d4 Nf6 c4 e6 Nf3"))!));     // EIN Knoten mit beiden Partien
        Assert.Equal((2, 1, 1, "Bd2:1 Nbd2:1"), Summary((await Tree("d4 Nf6 c4 e6 Nf3 Bb4"))!));   // danach fehlt keine Quelle
        Assert.Equal((2, 1, 1, "Qxf7:2"), Summary((await Tree("e4 e5 Bc4 Nc6 Qh5 Nf6"))!));
    }

    /// <summary>0.617.0 (Wunsch „auch an der Stelle will ich die vollen Filtermöglichkeiten"): das Eröffnungsprofil der Karte über
    /// dieselben Filter wie der Baum — Brett/online, Tempo, Jahre, unsichere Konten nur auf Wunsch.</summary>
    [Fact]
    public async Task Profile_FilteredLikeTheTree_BoardOnlineSpeedsYearsAndUnsure()
    {
        await TreeSeedAsync();
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var ctl = new LeagueController(league, null!, null!);
        async Task<JsonObject> P(string? source, string? speeds = null, int? years = null, bool? unsure = null) =>
            (JsonObject)((OkObjectResult)await ctl.Profile("222", source, speeds, years, unsure, default)).Value!;
        static string First(JsonObject o, string section) => string.Join(" ", o[section]!["first"]!.AsArray()
            .Select(r => $"{r![0]}:{r[1]}:{r[2]?.ToString() ?? "-"}").OrderBy(x => x, StringComparer.Ordinal));
        static (int N, int Board, int Online, int White) Head(JsonObject o) =>
            (o["n"]!.GetValue<int>(), o["board"]!.GetValue<int>(), o["online"]!.GetValue<int>(), o["white"]!["n"]!.GetValue<int>());

        var board = await P(null);                                                   // Vorgabe: nur Brett, wie die gespeicherte Karte
        Assert.Equal((2, 2, 0, 2), Head(board));
        Assert.Equal("d4:1:100 e4:1:0", First(board, "white"));

        var both = await P("both");                                                  // ohne unsure: nur gesicherte Konten
        Assert.Equal((6, 2, 4, 5), Head(both));
        Assert.Equal("d4:1:100 e4:4:38", First(both, "white"));                     // e4: 0 + 1 + 0 + ½ aus 4
        Assert.Equal(1, both["black_d4"]!["n"]!.GetValue<int>());

        Assert.Equal((5, 0, 5, 4), Head(await P("online", unsure: true)));           // + die Partie des unsicheren Kontos (1.c4)
        Assert.Equal("c4:1:100 e4:3:50", First(await P("online", unsure: true), "white"));
        Assert.Equal((4, 1, 3, 3), Head(await P("both", years: 1)));                 // 2010 und vor vier Jahren fallen weg
        Assert.Equal((2, 0, 2, 1), Head(await P("online", "blitz")));                // nur Blitz: 1.e4 mit Weiß, 1.d4 mit Schwarz
        Assert.IsType<NotFoundResult>(await ctl.Profile("999", null, null, null, null, default));
    }

    [Fact]
    public async Task Tree_OnlyOnlineAccounts_NoBoardGames_StillAnswers()
    {
        await SeedPlayerAsync();
        _db.LeagueOnlineAccounts.Add(new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "Max_Muster", Url = "u", Confidence = "sicher" });
        await _db.SaveChangesAsync();
        var t = await new LeagueProfileStore(_db).TreeAsync("222", "w", null, default);
        Assert.NotNull(t);
        Assert.Equal(0, t!["total"]!.GetValue<int>());
        Assert.Null(await new LeagueProfileStore(_db).TreeAsync("999", "w", null, default));
    }

    [Fact]
    public void TreeFilter_UnknownValuesFallBack()
    {
        var f = LeagueProfileStore.TreeFilter.Parse("alles", "blitz, rapid,blitz,x", 0, false);
        Assert.Equal(("board", (int?)null), (f.Source, f.Years));
        Assert.Equal(new[] { "blitz", "rapid" }, f.Speeds);
        Assert.Equal(3, LeagueProfileStore.TreeFilter.Parse("online", null, 3, false).Years);
    }
}
