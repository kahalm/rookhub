using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// LeagueHub-Rechenkern. Die Vergleichswerte der Rechenfunktionen sind mit der Python-Fassung gerechnet
/// (~/claude/league-analyzer, model.normalize / model.board_probs / parse.name_key) — wer eine Seite ändert,
/// prüft die andere.
/// </summary>
public class LeagueEngineTests
{
    private static readonly double[] Logits = { 0.1, -0.5, 1.2, -2.0, 0.3, 0.8, -1.1, 0.0 };

    [Fact]
    public void Normalize_MatchesPython()
    {
        var p = LeagueModel.Normalize(Logits, 6);
        double[] py = { 0.8250151437, 0.7212563958, 0.9340542637, 0.3660273651, 0.8520413616, 0.9047110975, 0.5867878763, 0.8101064963 };
        for (var i = 0; i < py.Length; i++) Assert.Equal(py[i], p[i], 8);
        Assert.Equal(6.0, p.Sum(), 6);
    }

    [Fact]
    public void BoardProbs_MatchesPython_AndIsConsistent()
    {
        var p = LeagueModel.Normalize(Logits, 6);
        var m = LeagueModel.BoardProbs(p, 6);
        Assert.Equal(0.8249973303, m[0, 0], 6);
        Assert.Equal(0.0, m[2, 5], 9);          // Spieler 3 kann nicht an Brett 6 sitzen (nur 5 Schlechtere)
        Assert.Equal(0.8100902001, m[7, 5], 6);
        double row2 = 0;
        for (var k = 0; k < 6; k++) row2 += m[2, k];
        Assert.Equal(0.9341500019, row2, 6);
        for (var k = 0; k < 6; k++)             // jedes Brett ist genau einmal besetzt
        {
            double col = 0;
            for (var i = 0; i < p.Length; i++) col += m[i, k];
            Assert.Equal(1.0, col, 6);
        }
    }

    [Theory]
    [InlineData("Schnabl, Andreas Dr.", "schnabl, andreas")]
    [InlineData("Tabernig, Bernhard Di Dr.", "tabernig, bernhard")]
    [InlineData("Irschick, Christoph M.Mag.", "irschick, christoph")]
    [InlineData("Prantl, Dietmar Mag. (Fh)", "prantl, dietmar")]
    [InlineData("Reitberger, Peter Dr.Ing.", "reitberger, peter")]
    [InlineData("Heinrich, Vanessa Msc", "heinrich, vanessa")]
    [InlineData("Müller, Hans Peter", "müller, hans peter")]
    [InlineData("Von Schlippe, Nikolai", "von schlippe, nikolai")]
    public void NameKey_StripsAcademicTitles(string name, string key) => Assert.Equal(key, LeagueNames.NameKey(name));

    [Theory]
    [InlineData("Spg Hall\\Mils", "Rum/Hall/Mils")]
    [InlineData("Rochade Rum", "Rum/Hall/Mils")]
    [InlineData("SPG Kufstein/Wörgl 2", "Kufstein/Wörgl")]
    [InlineData("Spg Fügen-Mayrhofen/Zillertal/", "Fügen/Zillertal/Rattenberg")]
    [InlineData("Sparkasse Jenbach 1", "Jenbach")]
    [InlineData("Sk Telfs", "Telfs")]
    [InlineData("Schwaz", "Schwaz")]
    [InlineData("Völs & Hak Ibk", "Völs & Hak Ibk")]
    public void Club_MergesRenamedTeams(string team, string club) => Assert.Equal(club, LeagueNames.Club(team));

    [Fact]
    public void Model_LoadsEmbeddedWeights()
    {
        var m = LeagueModel.FromEmbedded();
        Assert.Equal("const", m.Features[0]);
        Assert.Equal(m.Features.Count, m.Weights.Length);
        Assert.Contains("yest_played", m.Features);
        // „wer gestern spielte, spielt heute" muss positiv wirken, der Termin-Konflikt negativ
        Assert.True(m.Weights[m.Features.ToList().IndexOf("yest_played")] > 0);
        Assert.True(m.Weights[m.Features.ToList().IndexOf("conflict_hi")] < 0);
    }

    [Theory]
    [InlineData(4.5, "4½")]
    [InlineData(0.5, "½")]
    [InlineData(3.0, "3")]
    [InlineData(0.0, "0")]
    public void ScoreStr_UsesHalfSign(double x, string s) => Assert.Equal(s, LeagueViewBuilder.ScoreStr(x));

    [Fact]
    public void FmtDate_UsesGermanWeekday() =>
        Assert.Equal("Sa 03.10.2026", LeagueViewBuilder.FmtDate(new DateOnly(2026, 10, 3)));

    [Fact]
    public void ShareExpires_SevenDaysAfterRound()
    {
        Assert.Equal(new DateOnly(2026, 10, 10), LeagueService.ExpiresFor("Sa 03.10.2026", new DateOnly(2026, 9, 27)));
        Assert.Equal(new DateOnly(2026, 10, 27), LeagueService.ExpiresFor(null, new DateOnly(2026, 9, 27)));
        // Runde längst gespielt: der frische Link gilt trotzdem sieben Tage ab heute, statt tot anzukommen
        Assert.Equal(new DateOnly(2026, 10, 27), LeagueService.ExpiresFor("Sa 03.10.2026", new DateOnly(2026, 10, 20)));
    }

    [Fact]
    public void Token_IsLongAndUrlSafe()
    {
        var t = LeagueService.NewToken();
        Assert.Equal(24, t.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", t);
        Assert.NotEqual(t, LeagueService.NewToken());
    }

    // ---- kleine Liga: Merkmale, Freigabe-Regel, Brett-Prognose -------------------------------------

    /// <summary>Landesliga mit vier Teams: Runde 1 gespielt (Sa), Runde 2 am Tag danach (So), Runde 3 später.</summary>
    internal static LeagueWorld TinyWorld(bool round2Played = false)
    {
        var t = new LeagueTournament { Tnr = 1, Name = "TMM Landesliga 2026/2027", Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" };
        var rounds = new[]
        {
            new LeagueRound { Tnr = 1, Round = 1, Date = new DateOnly(2026, 10, 3) },
            new LeagueRound { Tnr = 1, Round = 2, Date = new DateOnly(2026, 10, 4) },
            new LeagueRound { Tnr = 1, Round = 3, Date = new DateOnly(2026, 11, 7) },
        };
        var matches = new List<LeagueMatch>
        {
            new() { Id = 1, Tnr = 1, Round = 1, Home = "A", Away = "B", HomePts = 1, AwayPts = 1 },
            new() { Id = 2, Tnr = 1, Round = 1, Home = "C", Away = "D", HomePts = 2, AwayPts = 0 },
            new() { Id = 3, Tnr = 1, Round = 2, Home = "B", Away = "C", HomePts = round2Played ? 1 : null, AwayPts = round2Played ? 1 : null },
            new() { Id = 4, Tnr = 1, Round = 2, Home = "D", Away = "A", HomePts = round2Played ? 1 : null, AwayPts = round2Played ? 1 : null },
            new() { Id = 5, Tnr = 1, Round = 3, Home = "A", Away = "C" },
            new() { Id = 6, Tnr = 1, Round = 3, Home = "D", Away = "B" },
        };
        var players = new List<LeaguePlayer>();
        var id = 1;
        foreach (var team in new[] { "A", "B", "C", "D" })
            for (var rb = 1; rb <= 4; rb++)
                players.Add(new LeaguePlayer
                {
                    Id = id++, Tnr = 1, Team = team, RosterBoard = rb, Name = $"{team}spieler, Nr{rb}",
                    NameKey = $"{team.ToLowerInvariant()}spieler, nr{rb}", FideId = $"{team}{rb}", EloI = 2000 - rb * 50,
                });
        var games = new List<LeagueGame>();
        var gid = 1;
        void Match(int rnd, int no, string h, string a, bool played)
        {
            for (var b = 1; b <= 2; b++)
                games.Add(new LeagueGame
                {
                    Id = gid++, Tnr = 1, Round = rnd, MatchNo = no, Board = b, HomeTeam = h, AwayTeam = a,
                    HomePlayer = played ? $"{h}spieler, Nr{b + 1}" : null, AwayPlayer = played ? $"{a}spieler, Nr{b}" : null,
                    HomeFide = played ? $"{h}{b + 1}" : null, AwayFide = played ? $"{a}{b}" : null,
                    Result = played ? "½ - ½" : "", HomeScore = played ? .5 : null, AwayScore = played ? .5 : null,
                    HomeColor = b % 2 == 1 ? "w" : "s",
                });
        }
        Match(1, 1, "A", "B", true);
        Match(1, 2, "C", "D", true);
        Match(2, 1, "B", "C", round2Played);
        Match(2, 2, "D", "A", round2Played);
        Match(3, 1, "A", "C", false);
        Match(3, 2, "D", "B", false);
        return new LeagueWorld(new[] { t }, rounds, matches, games, players);
    }

    [Fact]
    public void RowsFor_UsesOnlyKnowledgeBeforeTheRound()
    {
        var w = TinyWorld();
        var rows = LeagueFeatures.RowsFor(w, 1, "A", 2);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.N));                 // Runde 1 ist bekannt
        Assert.Equal(1, rows.Single(r => r.Pid == "A2").Last);         // A2 spielte Runde 1
        Assert.Equal(0, rows.Single(r => r.Pid == "A1").Last);
        Assert.All(rows, r => Assert.Equal(1, r.Yesterday));          // Sonntag nach Samstag
        var early = LeagueFeatures.RowsFor(w, 1, "A", 2, asof: new DateOnly(2026, 10, 3));
        Assert.All(early, r => Assert.Equal(0, r.N));                 // vorab: Samstag noch unbekannt
    }

    [Fact]
    public void View_OpensSaturdayAndSundayTogether_AndLocksLater()
    {
        var w = TinyWorld();
        var v = new LeagueViewBuilder(w, LeagueModel.FromEmbedded(), new Dictionary<string, int>(), new Dictionary<string, List<LeagueOnlineAccount>>())
            .Build(1, w.Games);
        var fx = v["fixtures"]!["A"]!.AsObject();
        Assert.Equal("played", fx["1"]!["status"]!.GetValue<string>());
        Assert.Equal("open", fx["2"]!["status"]!.GetValue<string>());
        Assert.Equal("locked", fx["3"]!["status"]!.GetValue<string>());
        Assert.Equal(2, fx["3"]!["unlock_after"]!.GetValue<int>());
        Assert.Equal("So nach Sa", fx["2"]!["phase"]!.GetValue<string>());   // Samstag gespielt → Sonntag mit Samstag
        // Heim hat an ungeraden Brettern Weiß: A spielt Runde 3 heim → Gegner an Brett 1 mit Schwarz
        var boards = fx["2"]!["boards"]!.AsArray();
        Assert.Equal(2, boards.Count);
        Assert.Equal("w", boards[0]!["opp_color"]!.GetValue<string>());      // A auswärts in Runde 2
        var sum = fx["2"]!["roster"]!.AsArray().Sum(r => r!["p"]!.GetValue<double>());
        Assert.Equal(2.0, sum, 2);                                              // Summe der Einsätze = Bretter
    }

    [Fact]
    public void View_PlayedFixture_CarriesItsHits_ConsistentWithTheBoards()
    {
        var w = TinyWorld();
        var v = new LeagueViewBuilder(w, LeagueModel.FromEmbedded(), new Dictionary<string, int>(), new Dictionary<string, List<LeagueOnlineAccount>>())
            .Build(1, w.Games);
        var fx = v["fixtures"]!["A"]!.AsObject();
        var ev = fx["1"]!["eval"]!;
        Assert.Equal(2, ev["of"]!.GetValue<int>());                                         // zwei besetzte Bretter
        Assert.InRange(ev["players"]!.GetValue<int>(), 0, 2);
        var firstPlace = fx["1"]!["boards"]!.AsArray().Count(b => b!["actual"]?["rank"]?.GetValue<int>() == 1);
        Assert.Equal(firstPlace, ev["boards"]!.GetValue<int>());                            // „genau am Brett" = Platz 1 der Anzeige
        Assert.Null(fx["2"]!["eval"]);                                                      // offen: noch nichts zu zählen
    }

    /// <summary>Mit dem echten Cache der LeagueHub-Karten (Größengrenze, wie in Program.cs): bis 0.657.2 warf Set ohne Size
    /// „Cache entry must specify a value for Size when SizeLimit is set", der Endpunkt antwortete immer 500. Der zweite Aufruf
    /// kommt aus dem Cache.</summary>
    [Fact]
    public async Task ForecastStats_WithTheSizeLimitedLeagueCache_StoresAndServesFromCache()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" });
        db.LeagueViews.Add(new LeagueView { Tnr = 1, GeneratedAt = DateTime.UtcNow,
            Json = "{\"fixtures\":{\"A\":{\"1\":{\"eval\":{\"players\":6,\"boards\":3,\"of\":8}}}}}" });
        await db.SaveChangesAsync();
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions
            { SizeLimit = LeagueProfileStore.CacheSizeLimit });
        var svc = new LeagueService(db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance, cache);

        var first = await svc.ForecastStatsAsync(default);
        Assert.Equal(1, cache.Count);
        var second = await svc.ForecastStatsAsync(default);
        Assert.Equal(first.ToJsonString(), second.ToJsonString());
        Assert.Equal(6, second["total"]!["players"]!.GetValue<int>());
    }

    [Fact]
    public async Task ForecastStats_PerRoundLeagueAndTotal_OverAllFixtures_OnlyCurrentSeason()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.LeagueTournaments.AddRange(
            new LeagueTournament { Tnr = 1, Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" },
            new LeagueTournament { Tnr = 2, Season = "2026/27", Level = 2, League = "1. Klasse", Grp = "Ost", Stage = "Liga" },
            new LeagueTournament { Tnr = 9, Season = "2025/26", Level = 1, League = "Landesliga", Stage = "Liga" });
        static string Ev(int p, int b, int of) => $"{{\"eval\":{{\"players\":{p},\"boards\":{b},\"of\":{of}}}}}";
        db.LeagueViews.AddRange(
            new LeagueView { Tnr = 1, GeneratedAt = DateTime.UtcNow, Json = $"{{\"fixtures\":{{\"A\":{{\"1\":{Ev(6, 3, 8)},\"2\":{Ev(7, 4, 8)},\"3\":{{\"status\":\"open\"}}}},\"B\":{{\"1\":{Ev(5, 2, 8)}}}}}}}" },
            new LeagueView { Tnr = 2, GeneratedAt = DateTime.UtcNow, Json = $"{{\"fixtures\":{{\"C\":{{\"1\":{Ev(4, 1, 6)},\"2\":{{\"status\":\"played\"}}}}}}}}" },
            new LeagueView { Tnr = 9, GeneratedAt = DateTime.UtcNow, Json = $"{{\"fixtures\":{{\"A\":{{\"1\":{Ev(8, 8, 8)}}}}}}}" });
        await db.SaveChangesAsync();

        var s = await new LeagueService(db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance).ForecastStatsAsync(default);

        Assert.Equal("2026/27", s["season"]!.GetValue<string>());
        Assert.Equal("{\"fixtures\":4,\"players\":22,\"boards\":10,\"of\":30}", s["total"]!.ToJsonString());   // ohne Vorsaison, ohne „ohne eval"
        var r = s["rounds"]!.AsArray();
        Assert.Equal(new[] { 1, 2 }, r.Select(x => x!["round"]!.GetValue<int>()));
        Assert.Equal(3, r[0]!["fixtures"]!.GetValue<int>());                                  // Runde 1 über beide Ligen
        Assert.Equal(15, r[0]!["players"]!.GetValue<int>());
        var l = s["leagues"]!.AsArray();
        Assert.Equal(new[] { "Landesliga", "1. Klasse Ost" }, l.Select(x => x!["name"]!.GetValue<string>()));
        Assert.Equal(18, l[0]!["players"]!.GetValue<int>());
        Assert.Equal(2, l[0]!["rounds"]!.AsArray().Count);
        Assert.Single(l[1]!["rounds"]!.AsArray());
    }

    // ---- Teilen-Links -----------------------------------------------------------------------------

    private static (AppDbContext Db, LeagueService Svc) ShareFixture(IMemoryCache? cache = null)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var svc = new LeagueService(db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance, cache);
        var w = TinyWorld();
        var v = new LeagueViewBuilder(w, LeagueModel.FromEmbedded(), new Dictionary<string, int>(),
            new Dictionary<string, List<LeagueOnlineAccount>>
            {
                ["B1"] = new() { new() { FideId = "B1", Site = "lichess", UserName = "sicher1", Url = "u", Confidence = "sicher" } },
                ["B2"] = new() { new() { FideId = "B2", Site = "chess.com", UserName = "vielleicht", Url = "u", Confidence = "wahrscheinlich" } },
            }).Build(1, w.Games);
        db.LeagueViews.Add(new LeagueView { Tnr = 1, Json = v.ToJsonString(), GeneratedAt = DateTime.UtcNow });
        db.SaveChanges();
        return (db, svc);
    }

    [Fact]
    public async Task Share_IsIdempotent_AndHidesUnsureAccounts()
    {
        var (db, svc) = ShareFixture();
        var s1 = await svc.CreateShareAsync(1, 1, "A", 7, default);
        var s2 = await svc.CreateShareAsync(1, 1, "A", 7, default);
        Assert.NotNull(s1);
        Assert.Equal(s1!.Token, s2!.Token);
        var pub = await svc.PublicShareAsync(s1.Token, default);
        Assert.NotNull(pub);
        var roster = pub!["fixture"]!["roster"]!.AsArray();
        Assert.Single(roster.Single(r => r!["fide"]!.GetValue<string>() == "B1")!["acc"]!.AsArray());
        Assert.Empty(roster.Single(r => r!["fide"]!.GetValue<string>() == "B2")!["acc"]!.AsArray());
        Assert.True(await svc.ShareCoversAsync(s1.Token, "B3", default));
        Assert.False(await svc.ShareCoversAsync(s1.Token, "C1", default));  // anderer Gegner: gehört nicht zum Link
        db.Dispose();
    }

    /// <summary>Codereview 2026-09-29, N4-003: jede Karten-Anfrage über einen Teilen-Link (jeder Klick im Baum) parste die
    /// ganze Liga-Ansicht und klonte die Begegnung, nur um zu prüfen, ob der Spieler in der Meldeliste steht. Jetzt je
    /// Ansicht (<c>GeneratedAt</c>) einmal; ein widerrufener Link gilt trotzdem sofort nicht mehr.</summary>
    [Fact]
    public async Task ShareCovers_ReadsTheRosterOncePerView_LinkStillCheckedEveryTime()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = LeagueProfileStore.CacheSizeLimit });
        var (db, svc) = ShareFixture(cache);
        var s = await svc.CreateShareAsync(1, 1, "A", 7, default);
        Assert.True(await svc.ShareCoversAsync(s!.Token, "B3", default));
        Assert.False(await svc.ShareCoversAsync(s.Token, "C1", default));

        var view = db.LeagueViews.Single();
        var json = view.Json;
        view.Json = "{}";                                                 // ohne neues GeneratedAt: nicht neu gelesen
        db.SaveChanges();
        Assert.True(await svc.ShareCoversAsync(s.Token, "B3", default));

        view.GeneratedAt = view.GeneratedAt.AddMinutes(1);                // neu gerechnet: neu gelesen
        db.SaveChanges();
        Assert.False(await svc.ShareCoversAsync(s.Token, "B3", default));
        view.Json = json;
        view.GeneratedAt = view.GeneratedAt.AddMinutes(1);
        db.SaveChanges();
        Assert.True(await svc.ShareCoversAsync(s.Token, "B3", default));

        Assert.True(await svc.DeleteShareAsync(s.Token, default));
        Assert.False(await svc.ShareCoversAsync(s.Token, "B3", default));  // Link weg: der Cache hilft ihm nicht
        db.Dispose();
    }

    /// <summary>Die API baut <see cref="LeagueService"/> mit dem eigenen, begrenzten Cache (Program.cs) — mit SizeLimit wirft
    /// jeder Eintrag ohne Size, also prüft das auch, dass alle eine haben.</summary>
    [Fact]
    public async Task DependencyInjection_LeagueServiceGetsTheKeyedSizeLimitedCache()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(LeagueModel.FromEmbedded());
        services.AddKeyedSingleton<IMemoryCache>(LeagueProfileStore.CacheServiceKey, (_, _) =>
            new MemoryCache(new MemoryCacheOptions { SizeLimit = LeagueProfileStore.CacheSizeLimit }));
        services.AddScoped<LeagueService>();
        using var provider = services.BuildServiceProvider();
        using (var seed = provider.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile
            {
                FideId = "222", Name = "Hengl, Philip", UpdatedAt = DateTime.UtcNow,
                Pgn = "[Date \"2024.01.01\"]\n[White \"Hengl, Philip\"]\n[Black \"X, Y\"]\n[WhiteFideId \"222\"]\n[Result \"1-0\"]\n\n1. e4 e5 1-0\n",
            });
            db.SaveChanges();
        }

        using var scope = provider.CreateScope();
        var tree = await scope.ServiceProvider.GetRequiredService<LeagueService>().TreeAsync("222", "w", null, default);

        Assert.Equal(1, tree!["total"]!.GetValue<int>());
        Assert.Equal(1, ((MemoryCache)provider.GetRequiredKeyedService<IMemoryCache>(LeagueProfileStore.CacheServiceKey)).Count);
        Assert.Equal(0, ((MemoryCache)provider.GetRequiredService<IMemoryCache>()).Count);
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains("AddKeyedSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(RookHub.Api.Services.League.LeagueProfileStore.CacheServiceKey", src);
        Assert.Contains("SizeLimit = RookHub.Api.Services.League.LeagueProfileStore.CacheSizeLimit", src);
    }

    private static string ProgramCs([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("Program.cs nicht gefunden");
        return "";
    }

    [Fact]
    public async Task Share_SourcesOnlyWithAValidLink()
    {
        // „Die Info auch auf den Link hin" (0.627.0): Partien je Quelle über den Teilen-Link — aber nur mit gültigem Token.
        var (db, svc) = ShareFixture();
        db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = 1, Team = "A", Name = "A1, A", NameKey = "a1, a", FideId = "A1" },
            new LeaguePlayer { Tnr = 1, Team = "B", Name = "B1, B", NameKey = "b1, b", FideId = "B1" },
            new LeaguePlayer { Tnr = 2, Team = "Z", Name = "Z1, Z", NameKey = "z1, z", FideId = "Z1" });   // andere Liga
        await db.SaveChangesAsync();
        var s = await svc.CreateShareAsync(1, 1, "A", null, default);
        var controller = new RookHub.Api.Controllers.LeagueShareController(svc);
        var sources = new LeagueGameSources(db);
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Sources(s!.Token, sources, default));
        var body = (System.Text.Json.Nodes.JsonObject)ok.Value!;
        Assert.NotNull(body["boardTotal"]);
        // Gegner = die Meldeliste der GETEILTEN Begegnung, vom Server bestimmt (0.628.0).
        var pub = (await svc.PublicShareAsync(s.Token, default))!;
        var rosterFides = pub["fixture"]!["roster"]!.AsArray().Select(r => (string?)r!["fide"]).Where(f => !string.IsNullOrEmpty(f)).Distinct().Count();
        Assert.True(rosterFides > 0);
        Assert.Equal(rosterFides, body["opponent"]!["players"]!.GetValue<int>());
        // Liga = die Liga des Links (alle ihre Meldelisten), ebenfalls vom Server bestimmt.
        var leagueFides = await db.LeaguePlayers.Where(p => p.Tnr == 1 && p.FideId != null && p.FideId != "").Select(p => p.FideId).Distinct().CountAsync();
        Assert.Equal(2, leagueFides);
        Assert.Equal(leagueFides, body["league"]!["players"]!.GetValue<int>());
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.Sources("falsch", sources, default));
        Assert.True(await svc.DeleteShareAsync(s.Token, default));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.Sources(s.Token, sources, default));
        db.Dispose();
    }

    [Fact]
    public async Task Share_AddAccount_WithoutLogin_SureAndAnonymous_OnlyForPlayersOfTheLink()
    {
        // Wunsch 2026-10-01: „Hinzufügen von Online-Accounts soll auch für nicht registrierte User möglich sein — direkt als sicher,
        // beim Spieler vermerken, wer ihn hinzugefügt hat, in dem Fall dann anonym".
        var (db, svc) = ShareFixture();
        db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = 1, Team = "B", Name = "B2, B", NameKey = "b2, b", FideId = "B2" },
            new LeaguePlayer { Tnr = 1, Team = "B", Name = "B3, B", NameKey = "b3, b", FideId = "B3" },
            new LeaguePlayer { Tnr = 1, Team = "C", Name = "C1, C", NameKey = "c1, c", FideId = "C1" });
        db.LeagueOnlineAccounts.Add(new LeagueOnlineAccount { FideId = "B1", Site = "lichess", UserName = "sicher1", Url = "u", Confidence = "sicher" });
        await db.SaveChangesAsync();
        var s = (await svc.CreateShareAsync(1, 1, "A", null, default))!;
        var controller = new RookHub.Api.Controllers.LeagueShareController(svc);
        var accounts = new LeagueOnlineAccountService(db);
        var req = new RookHub.Api.Controllers.LeagueShareController.ShareAccountRequest("lichess", "https://lichess.org/@/NeuerB3", " vom Gegner selbst ");

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.AddAccount(s.Token, "B3", req, accounts, default));
        Assert.Equal("NeuerB3", (string?)((System.Text.Json.Nodes.JsonObject)ok.Value!)["user"]);
        var acc = await db.LeagueOnlineAccounts.SingleAsync(a => a.FideId == "B3");
        Assert.Equal(("sicher", "anonym", LeagueClubService.ShareHashOf(s.Token)), (acc.Confidence, acc.AddedBy, acc.AddedShareHash));
        Assert.Equal("Über einen Teilen-Link hinzugefügt (anonym): vom Gegner selbst", acc.Evidence);

        // Nur Spieler der geteilten Begegnung, nur mit gültigem Link; ein Konto eines anderen Spielers nimmt der Link nicht.
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.AddAccount(s.Token, "C1", req, accounts, default));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.AddAccount("falsch", "B3", req, accounts, default));
        var taken = Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(await controller.AddAccount(s.Token, "B2",
            new("lichess", "sicher1", null), accounts, default));
        Assert.Contains("takenElsewhere", System.Text.Json.JsonSerializer.Serialize(taken.Value));
        var twice = Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(await controller.AddAccount(s.Token, "B3", req, accounts, default));
        Assert.Contains("duplicate", System.Text.Json.JsonSerializer.Serialize(twice.Value));
        Assert.Single(await db.LeagueOnlineAccounts.Where(a => a.FideId == "B3").ToListAsync());

        // Angemeldet eingetragen: der Nutzername steht da; das Konto-JSON zeigt ihn.
        var (byUser, _) = await accounts.CreateAsync("C1", new("chess.com", "cee1", true, null), default, "patrik");
        Assert.Equal("patrik", (string?)LeagueOnlineAccountService.ToJson(byUser!, full: true)["addedBy"]);
        Assert.Null(LeagueOnlineAccountService.ToJson(byUser!, full: true, hidden: true)["addedBy"]);
        db.Dispose();
    }

    [Fact]
    public async Task Share_LockedRound_IsNotShareable_AndRevokedLinkIsGone()
    {
        var (db, svc) = ShareFixture();
        Assert.Null(await svc.CreateShareAsync(1, 3, "A", null, default));   // gesperrte Runde
        var s = await svc.CreateShareAsync(1, 2, "A", null, default);
        Assert.NotNull(s);
        Assert.True(await svc.DeleteShareAsync(s!.Token, default));
        Assert.Null(await svc.PublicShareAsync(s.Token, default));
        db.Dispose();
    }

    [Fact]
    public async Task Share_ExpiredButNotCleanedUp_IsReplacedByAFreshLink()
    {
        var (db, svc) = ShareFixture();
        db.LeagueShares.Add(new LeagueShare { Token = "abgelaufenabgelaufen1234", Tnr = 1, Round = 1, Team = "A", Expires = new DateOnly(2020, 1, 1) });
        db.SaveChanges();
        var s = await svc.CreateShareAsync(1, 1, "A", 7, default);
        Assert.NotNull(s);
        Assert.NotEqual("abgelaufenabgelaufen1234", s!.Token);
        Assert.True(s.Expires >= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(LeagueService.ShareKeepDays));
        Assert.NotNull(await svc.PublicShareAsync(s.Token, default));
        Assert.Single(db.LeagueShares.Where(x => x.Tnr == 1 && x.Round == 1 && x.Team == "A"));
        db.Dispose();
    }

    [Fact]
    public async Task Share_Expired_IsHidden()
    {
        var (db, svc) = ShareFixture();
        db.LeagueShares.Add(new LeagueShare { Token = "abcdefghijklmnopqrstuvwx", Tnr = 1, Round = 1, Team = "A", Expires = new DateOnly(2020, 1, 1) });
        db.SaveChanges();
        Assert.Null(await svc.PublicShareAsync("abcdefghijklmnopqrstuvwx", default));
        db.Dispose();
    }
}
