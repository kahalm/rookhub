using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>LeagueHub-Aktualisieren: Seiten ersetzen + Abgleich mit der Meldeliste, veraltete Partien, Spielerkarte.</summary>
public class LeagueRefreshTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private LeagueRefresh Refresh(HttpMessageHandler? handler = null)
    {
        var client = new HttpClient(handler ?? new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))
            { BaseAddress = new Uri("http://crawler/") };
        var factory = new OneClientFactory(client);
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        return new LeagueRefresh(_db, league, factory, NullLogger<LeagueRefresh>.Instance, () => Now);
    }

    private static LeagueRefresh.Pages SamplePages() => new(7,
        new() { new(1, 1, "Schwaz", "Absam", 1, 5, "04.10.2025", "14:00 Uhr", "Kursaal") },
        new()
        {
            new(1, 1, 1, "Schwaz", "Absam", "Binder, Moriz", "Hengl, Philip", null, null, "w", "½ - ½", .5, .5, 0, null),
            new(1, 1, 2, "Schwaz", "Absam", "Brett nicht besetzt", "Schnabl, Andreas", null, null, "s", "- - +", 0, 1, 1, null),
        },
        new() { [1] = "04.10.2025" },
        new()
        {
            new(1, null, "Binder, Moriz", "111", 2201, null, "AUT", "Schwaz", 2),
            new(2, null, "Hengl, Philip", "222", 2172, 2214, "AUT", "Absam", 1),
            new(3, null, "Schnabl, Andreas Dr.", "333", null, 1990, "AUT", "Absam", 10),
        },
        new() { new("Absam", 10, "Schnabl, Andreas Dr.", 1, 1, 2100) });

    [Fact]
    public async Task Replace_LinksBoardGamesToTheRoster()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2025/26", Level = 1, League = "Landesliga" });
        _db.LeagueGames.Add(new LeagueGame { Tnr = 7, Round = 9, HomeTeam = "alt", AwayTeam = "alt" });   // wird ersetzt
        await _db.SaveChangesAsync();
        await Refresh().ReplaceAsync(SamplePages(), default);

        var games = _db.LeagueGames.Where(g => g.Tnr == 7).OrderBy(g => g.Board).ToList();
        Assert.Equal(2, games.Count);
        Assert.Equal(("111", 2, 2201), (games[0].HomeFide, games[0].HomeRb, games[0].HomeElo));
        Assert.Equal("222", games[0].AwayFide);
        Assert.Null(games[1].HomePlayer);                        // „Brett nicht besetzt" → leer
        Assert.Equal("333", games[1].AwayFide);                  // Paarung ohne „Dr." trifft die Meldeliste mit „Dr."
        Assert.Equal(1990, games[1].AwayElo);                    // ohne FIDE-Elo die nationale
        var schnabl = _db.LeaguePlayers.Single(p => p.FideId == "333");
        Assert.Equal("schnabl, andreas", schnabl.NameKey);
        Assert.Equal(2100, schnabl.EloPerf);                     // Statistik aus art=20
        Assert.Equal(new DateOnly(2025, 10, 4), _db.LeagueRounds.Single(r => r.Tnr == 7).Date);
        Assert.Equal(Now, _db.LeagueTournaments.Find(7)!.UpdatedAt);
    }

    private const string Lumbra = "[Event \"Tirol Open\"]\n[Date \"2024.05.01\"]\n[White \"Binder, Moriz\"]\n[Black \"X, Y\"]\n[Result \"1-0\"]\n[WhiteFideId \"111\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 cxd4 1-0";
    private const string ChessResults = "[Event \"Tirol Open\"]\n[Date \"2024.05.01\"]\n[White \"Binder, Moriz\"]\n[Black \"X, Y\"]\n[Result \"1-0\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 cxd4 1-0\n\n"
        + "[Event \"TMM\"]\n[Date \"2026.09.26\"]\n[White \"Z, Z\"]\n[Black \"Binder Moriz\"]\n[Result \"0-1\"]\n\n1. d4 Nf6 2. c4 g6 0-1";

    [Fact]
    public async Task MergeGames_DeduplicatesAndBuildsTheCard()
    {
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "111", Name = "Binder, Moriz", Pgn = Lumbra, GameCount = 1 });
        await _db.SaveChangesAsync();
        await Refresh().MergeGamesAsync("111", ChessResults, default);

        var row = _db.LeaguePlayerProfiles.Find("111")!;
        Assert.Equal(2, row.GameCount);                          // die doppelte Partie zählt einmal
        Assert.Equal(Now, row.CrFetchedAt);
        var card = System.Text.Json.Nodes.JsonNode.Parse(row.ProfileJson)!;
        Assert.Equal(1, card["src"]!["beide"]!.GetValue<int>());
        Assert.Equal(1, card["src"]!["chess-results"]!.GetValue<int>());
        Assert.Equal("e4", card["white"]!["first"]![0]![0]!.GetValue<string>());
        Assert.Equal("Nf6", card["black_d4"]!["first"]![0]![0]!.GetValue<string>());   // Name ohne Komma (2022/23-Schreibweise)
        Assert.Equal("2026.09.26", card["recent"]![0]!["date"]!.GetValue<string>());   // neueste zuerst
        Assert.Equal(1.0, card["recent"]![0]!["score"]!.GetValue<double>());
    }

    [Fact]
    public async Task StalePlayers_OnlyLikelyOpponents_OldestFirstCapped()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Season = "2026/27", Level = 1, League = "Landesliga" });
        _db.LeagueViews.Add(new LeagueView
        {
            Tnr = 1,
            Json = "{\"fixtures\":{\"A\":{\"1\":{\"status\":\"open\",\"roster\":[{\"fide\":\"1\",\"p\":0.8},{\"fide\":\"2\",\"p\":0.1},{\"fide\":\"3\",\"p\":0.5}]},"
                 + "\"2\":{\"status\":\"locked\"}}}}",
        });
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "3", CrFetchedAt = Now.AddDays(-2) });   // frisch
        await _db.SaveChangesAsync();
        var stale = await Refresh().StalePlayersAsync("2026/27", default);
        Assert.Equal(new[] { "1" }, stale);   // 2 unter 15 %, 3 erst vor zwei Tagen geholt
    }

    [Fact]
    public async Task StalePlayers_OpponentsOfOwnClubFirst_ThenByProbability()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Season = "2026/27", Level = 1, League = "Landesliga" });
        // „A" ist ein fremder Verein, Schwaz der eigene: dessen Gegner (Spieler 5, nur 20 %) kommt vor den 90 % von A.
        _db.LeagueViews.Add(new LeagueView
        {
            Tnr = 1,
            Json = "{\"fixtures\":{\"A\":{\"1\":{\"status\":\"open\",\"roster\":[{\"fide\":\"4\",\"p\":0.9},{\"fide\":\"6\",\"p\":0.7}]}},"
                 + "\"Schwaz\":{\"1\":{\"status\":\"open\",\"roster\":[{\"fide\":\"5\",\"p\":0.2},{\"fide\":\"6\",\"p\":0.3}]}}}}",
        });
        await _db.SaveChangesAsync();
        var stale = await Refresh().StalePlayersAsync("2026/27", default);
        // 6 steht bei A (70 %) UND gegen Schwaz (30 %): der Schwaz-Rang zählt
        Assert.Equal(new[] { "6", "5", "4" }, stale);
    }

    [Fact]
    public async Task Run_FetchesPagesAndGamesThroughTheCrawler()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" });
        await _db.SaveChangesAsync();
        var asked = new List<string>();
        var json = System.Text.Json.JsonSerializer.Serialize(SamplePages(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var handler = new StubHandler(req =>
        {
            asked.Add(req.RequestUri!.AbsolutePath);
            var body = req.RequestUri.AbsolutePath == "/api/league/7" ? json : "";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        });
        var msg = await Refresh(handler).RunAsync(default);
        Assert.Contains("/api/league/7", asked);
        Assert.Contains("1 Ligen neu geholt", msg);
        Assert.Equal(2, _db.LeagueGames.Count(g => g.Tnr == 7));
        Assert.True(_db.LeagueViews.Any(v => v.Tnr == 7));
    }

    [Fact]
    public async Task Replace_EmptyPages_KeepTheLeague()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2025/26", Level = 1, League = "Landesliga" });
        _db.LeagueGames.Add(new LeagueGame { Tnr = 7, Round = 1, HomeTeam = "A", AwayTeam = "B" });
        await _db.SaveChangesAsync();
        var empty = new LeagueRefresh.Pages(7, new(), new(), new(), new(), new());   // Fehl-/Drosselseite von chess-results
        await Assert.ThrowsAsync<InvalidOperationException>(() => Refresh().ReplaceAsync(empty, default));
        Assert.Equal(1, _db.LeagueGames.Count(g => g.Tnr == 7));
    }

    [Theory]
    [InlineData("matches")]
    [InlineData("games")]
    [InlineData("roster")]
    [InlineData("stats")]
    public async Task Replace_OneEmptyPageWithExistingRows_KeepsTheLeague(string emptyPage)
    {
        // Bestand aus einem früheren Lauf: alle vier Seiten hatten Zeilen
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2025/26", Level = 1, League = "Landesliga" });
        await _db.SaveChangesAsync();
        await Refresh().ReplaceAsync(SamplePages(), default);
        _db.ChangeTracker.Clear();

        // Nur EINE Seite kommt als Drosselseite (200 ohne Tabelle) leer zurück
        var s = SamplePages();
        var pages = emptyPage switch
        {
            "matches" => s with { Matches = new() },
            "games" => s with { Games = new(), RoundDates = new() },
            "roster" => s with { Roster = new() },
            _ => s with { Stats = new() },
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Refresh().ReplaceAsync(pages, default));
        Assert.Equal(1, _db.LeagueMatches.Count(m => m.Tnr == 7));
        Assert.Equal(2, _db.LeagueGames.Count(g => g.Tnr == 7));
        Assert.Equal(3, _db.LeaguePlayers.Count(p => p.Tnr == 7));
        Assert.Equal(2100, _db.LeaguePlayers.Single(p => p.FideId == "333").EloPerf);
    }

    [Fact]
    public async Task Replace_EmptyPageWithoutExistingRows_ReplacesAsBefore()
    {
        // Saisonbeginn: noch keine Brettpaarungen und keine Statistik — weder neu noch im Bestand
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2026/27", Level = 1, League = "Landesliga" });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = "Schwaz", Name = "alt", NameKey = "alt" });   // ohne Statistik
        await _db.SaveChangesAsync();
        var pages = SamplePages() with { Games = new(), RoundDates = new(), Stats = new() };
        await Refresh().ReplaceAsync(pages, default);
        Assert.Equal(3, _db.LeaguePlayers.Count(p => p.Tnr == 7));
        Assert.DoesNotContain(_db.LeaguePlayers, p => p.Name == "alt");
        Assert.Equal(0, _db.LeagueGames.Count(g => g.Tnr == 7));
    }

    [Fact]
    public async Task Run_EmptyRosterPage_ReportsTheLeagueAsNotUpdated()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 6, Season = "2026/27", Level = 2, League = "1. Klasse", Stage = "Liga" });
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 6, Team = "Schwaz", Name = "Binder, Moriz", NameKey = "binder, moriz", FideId = "111" });
        await _db.SaveChangesAsync();
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var six = System.Text.Json.JsonSerializer.Serialize(SamplePages() with { Tnr = 6, Roster = new() }, web);
        var seven = System.Text.Json.JsonSerializer.Serialize(SamplePages(), web);
        var handler = new StubHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.AbsolutePath switch { "/api/league/6" => six, "/api/league/7" => seven, _ => "" },
                Encoding.UTF8, "application/json"),
        });
        var msg = await Refresh(handler).RunAsync(default);
        Assert.Contains("1 Ligen neu geholt", msg);
        Assert.Contains("nicht aktualisiert: Liga 6", msg);
        Assert.Equal("111", _db.LeaguePlayers.Single(p => p.Tnr == 6).FideId);   // Meldeliste der Liga 6 bleibt
        Assert.Equal(0, _db.LeagueGames.Count(g => g.Tnr == 6));
    }

    [Fact]
    public async Task Run_OneLeagueFails_TheOthersAndTheViewsStillUpdate()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 6, Season = "2026/27", Level = 2, League = "1. Klasse", Stage = "Liga" });
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" });
        await _db.SaveChangesAsync();
        var json = System.Text.Json.JsonSerializer.Serialize(SamplePages(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/league/6" => new HttpResponseMessage(HttpStatusCode.BadGateway),
            "/api/league/7" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") },
        });
        var msg = await Refresh(handler).RunAsync(default);
        Assert.Contains("1 Ligen neu geholt", msg);
        Assert.Contains("nicht aktualisiert: Liga 6", msg);
        Assert.Equal(2, _db.LeagueGames.Count(g => g.Tnr == 7));
        Assert.True(_db.LeagueViews.Any(v => v.Tnr == 7));
    }

    [Fact]
    public async Task Run_EveryLeagueFails_IsAFailedRun()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" });
        await _db.SaveChangesAsync();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Refresh(handler).RunAsync(default));
    }

    [Fact]
    public async Task MergeGames_ALaterUnplayableMove_KeepsTheOpening()
    {
        // Zug 3 von Weiß ist auf dem Brett nicht spielbar — die Eröffnung davor zählt trotzdem (wie games_build.py)
        const string broken = "[Event \"Open\"]\n[Date \"2025.01.05\"]\n[White \"Binder, Moriz\"]\n[Black \"X, Y\"]\n[Result \"1-0\"]\n\n"
            + "1. e4 c5 2. Nf3 d6 3. Qxh8 Nf6 1-0";
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "111", Name = "Binder, Moriz" });
        await _db.SaveChangesAsync();
        await Refresh().MergeGamesAsync("111", broken, default);
        var card = System.Text.Json.Nodes.JsonNode.Parse(_db.LeaguePlayerProfiles.Find("111")!.ProfileJson)!;
        Assert.Equal(1, card["with_moves"]!.GetValue<int>());
        Assert.Equal("e4", card["white"]!["first"]![0]![0]!.GetValue<string>());
        Assert.Equal("1.e4 c5 2.Nf3 d6", card["recent"]![0]!["opening"]!.GetValue<string>());
    }

    [Fact]
    public async Task Import_ReplacesTheLeagueData_AndUpsertsProfiles()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 99, Season = "2019/20", League = "alt" });
        _db.LeagueGames.Add(new LeagueGame { Tnr = 99, HomeTeam = "alt", AwayTeam = "alt" });
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "111", Name = "alt", GameCount = 1 });
        await _db.SaveChangesAsync();
        var bundle = new LeagueImportService.Bundle(
            new() { new(7, "TMM Landesliga 2026/2027", "2026/27", 1, "Landesliga", null, "Liga", false, null, null, 11) },
            new() { new(7, 1, "03.10.2026") },
            new() { new(7, 1, 1, "Schwaz", "Absam", null, null, null, null, null) },
            new() { new(7, 1, 1, 1, "Schwaz", "Absam", "Binder, Moriz", "Hengl, Philip", null, null, "w", "½ - ½", .5, .5, 0, "111", "222", 2, 1, 2201, 2172, null) },
            new() { new(7, "Schwaz", 2, 1, null, "Binder, Moriz", "binder, moriz", "111", 2201, null, "AUT", null, null, null) },
            new() { new("111", "lichess", "moriz", "https://lichess.org/@/moriz", "sicher", null) },
            new() { new("111", "Binder, Moriz", 3, new System.Text.Json.Nodes.JsonObject { ["n"] = 3 }, "[Event \"x\"]", null) });
        var res = await new LeagueImportService(_db).ImportAsync(bundle, default);
        Assert.Equal(1, res["tournaments"]!.GetValue<int>());
        Assert.Equal(new[] { 7 }, _db.LeagueTournaments.Select(t => t.Tnr).ToArray());
        Assert.Single(_db.LeagueGames);                                   // die alte Liga ist ersetzt
        Assert.Equal(new DateOnly(2026, 10, 3), _db.LeagueRounds.Single().Date);
        Assert.Single(_db.LeagueOnlineAccounts);
        var prof = _db.LeaguePlayerProfiles.Find("111")!;
        Assert.Equal(("Binder, Moriz", 3), (prof.Name, prof.GameCount));   // vorhandenes Profil aktualisiert
    }

    [Fact]
    public async Task Update_AfterARun_NotAgainWithinTwoMinutes()
    {
        // Ohne registriertes LeagueRefresh scheitert der Lauf sofort — genug, um die Sperre danach zu prüfen.
        var sp = new ServiceCollection().BuildServiceProvider();
        var svc = new LeagueUpdateService(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<LeagueUpdateService>.Instance, new StubLifetime());
        Assert.Equal(LeagueUpdateService.StartResult.Started, svc.TryStart());
        static System.Text.Json.JsonElement St(LeagueUpdateService u) => System.Text.Json.JsonSerializer.SerializeToElement(u.Status());
        for (var i = 0; i < 200 && St(svc).GetProperty("running").GetBoolean(); i++) await Task.Delay(20);
        Assert.False(St(svc).GetProperty("running").GetBoolean());
        Assert.False(St(svc).GetProperty("ok").GetBoolean());
        Assert.Equal(LeagueUpdateService.StartResult.TooSoon, svc.TryStart());
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class OneClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(f(request));
    }
}
