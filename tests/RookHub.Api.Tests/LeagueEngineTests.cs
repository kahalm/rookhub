using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
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

    // ---- Teilen-Links -----------------------------------------------------------------------------

    private static (AppDbContext Db, LeagueService Svc) ShareFixture()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var svc = new LeagueService(db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
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

    [Fact]
    public async Task Share_SourcesOnlyWithAValidLink()
    {
        // „Die Info auch auf den Link hin" (0.627.0): Partien je Quelle über den Teilen-Link — aber nur mit gültigem Token.
        var (db, svc) = ShareFixture();
        var s = await svc.CreateShareAsync(1, 1, "A", null, default);
        var controller = new RookHub.Api.Controllers.LeagueShareController(svc);
        var sources = new LeagueGameSources(db);
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Sources(s!.Token, sources, default));
        Assert.NotNull(((System.Text.Json.Nodes.JsonObject)ok.Value!)["boardTotal"]);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.Sources("falsch", sources, default));
        Assert.True(await svc.DeleteShareAsync(s.Token, default));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.Sources(s.Token, sources, default));
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
