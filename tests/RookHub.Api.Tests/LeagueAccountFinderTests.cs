using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Konto-Vorschläge (0.607.0): Nutzernamen aus dem Namen, das Urteil über ein Profil, das Lesen der Antworten und die Suche
/// samt Übernehmen/Verwerfen. Die Antworten sind nachgebaut — Felder wie in den öffentlichen Schnittstellen von Lichess
/// (<c>/api/users</c>, <c>/api/player/autocomplete</c>, <c>/api/fide/player</c>) und chess.com (<c>/pub/player</c>),
/// nachgesehen am 2026-09-30.
/// </summary>
public class LeagueAccountFinderTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private static readonly LeagueAccountFinder.Player Max = new("222", "Muster, Max", "AUT", 1900, "Kufstein 1");

    private static LeagueAccountFinder.Profile Prof(string user, string? real = null, string? flag = null, string? loc = null,
        int? fide = null, bool closed = false, string site = "lichess", int? rating = null) =>
        new(site, user, "u/" + user, real, flag, loc, null, fide, null, closed, rating, rating is null ? null : "Lichess Blitz");

    // ── Namen ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Variants_FromName_UmlautsTitlesAndOrder()
    {
        Assert.Equal(new[] { "MaxMuster", "Max_Muster", "Max-Muster", "MusterMax", "Muster_Max", "MMuster", "MusterM" },
            LeagueAccountFinder.Variants("Muster, Max"));
        Assert.Equal("JoergMueller", LeagueAccountFinder.Variants("Müller, Jörg Peter")[0]);        // erster Vorname
        Assert.Equal("FranzHuber", LeagueAccountFinder.Variants("Dr. Huber, Franz")[0]);             // Titel weg
        Assert.Equal("FranzHuber", LeagueAccountFinder.Variants("FM Huber, Franz")[0]);
        Assert.Equal("PhilipHengl", LeagueAccountFinder.Variants("Hengl Philip")[0]);                // ohne Komma: Nachname zuerst
        Assert.Equal("AnaPerezRodriguez", LeagueAccountFinder.Variants("Perez Rodriguez, Ana")[0]);   // Leerzeichen im Nachnamen weg
        Assert.Contains("Hans-PeterGruber", LeagueAccountFinder.Variants("Gruber, Hans-Peter"));
        Assert.Empty(LeagueAccountFinder.Variants("Muster"));
        Assert.Equal("Rene", LeagueAccountFinder.Plain("René"));
    }

    // ── Urteil ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Judge_NameCountryRatingAndPlace_AddUp()
    {
        var v = LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", "AT", "Kufstein", 1950), derived: true)!;
        Assert.Equal(1 + 3 + 1 + 2 + 2, v.Score);
        Assert.Equal(new[] { "Nutzername aus dem Namen", "Klarname im Profil („Max Muster“)", "Land Österreich",
            "FIDE-Wertung im Profil 1950 (Liste 1900)", "Tiroler Ort im Profil" }, v.Evidence);
        Assert.Equal(2, LeagueAccountFinder.Judge(Max, Prof("MaxMuster", flag: "AT"), derived: true)!.Score);
        Assert.Equal(2, LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "M. Muster"), derived: true)!.Score);   // nur Nachname
        Assert.Contains("Nachname und Initiale im Profil", LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "M. Muster"), derived: true)!.Evidence[1]);
        Assert.Contains("Nachname im Profil („IM Muster“)", LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "IM Muster"), derived: true)!.Evidence[1]);
    }

    [Fact]
    public void Judge_RejectsStrangersAndWeakHits()
    {
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Mustermann"), derived: true));   // anderer Name
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", "BR"), derived: true));   // anderes Land
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("MaxMuster"), derived: true));                      // gar kein Hinweis
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", closed: true), derived: true));
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("MaxMuster", fide: 2300), derived: true));          // Wertung zu weit weg
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("Muster1987", flag: "AT"), derived: false));        // Suche: Land reicht nicht
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("Muster1987", loc: "Forum Hallo"), derived: false)); // kein Tiroler Ort
        var searched = LeagueAccountFinder.Judge(Max, Prof("Muster1987", "Max Muster"), derived: false)!;
        Assert.Equal(3, searched.Score);
        Assert.Equal("Nutzername beginnt mit dem Nachnamen", searched.Evidence[0]);
        // Land der FIDE-Föderation ist kein Widerspruch.
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", "DE"), derived: true));
        Assert.NotNull(LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", "DE"), derived: true, fideFed: "GER"));
    }

    [Fact]
    public void Judge_AnotherFirstNameInTheProfile_IsAnotherPerson()
    {
        // Gesehen in der ersten vollen Suche (2026-09-30).
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Andreas Muster", "AT"), derived: true));
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("MMuster", "F. Muster", "AT"), derived: true));        // fremde Initiale
        Assert.NotNull(LeagueAccountFinder.Judge(Max, Prof("MMuster", "M Muster", "AT"), derived: true));
        // Zweiter Vorname zählt auch.
        var two = new LeagueAccountFinder.Player("5", "Muster, Max Peter", "AUT", 1900, "x");
        Assert.Equal(1 + 3 + 1, LeagueAccountFinder.Judge(two, Prof("MaxMuster", "Peter Muster", "AT"), derived: true)!.Score);
        Assert.Equal(LeagueAccountFinder.NameFit.LastOnly,
            LeagueAccountFinder.FirstNameMatch(new[] { "fm", "muster" }, new[] { "muster" }, new[] { "max" }));
        Assert.Equal(LeagueAccountFinder.NameFit.Other,
            LeagueAccountFinder.FirstNameMatch(new[] { "galin", "georgiev" }, new[] { "georgiev" }, new[] { "georgi" }));
    }

    [Fact]
    public void Judge_OnlineRatingFarBelowTheElo_IsSomeoneElse_HigherIsFine()
    {
        // Max: Elo 1900. 400 darunter ist die Grenze (Wunsch: „alles droppen, was 400 niedriger ist").
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", "AT", rating: 500), derived: true));
        Assert.Null(LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", "AT", rating: 1499), derived: true));
        Assert.NotNull(LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", "AT", rating: 1500), derived: true));
        var high = LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", "AT", rating: 2900), derived: true)!;   // höher ist ok
        Assert.Equal(1 + 3 + 1, high.Score);                                          // … aber kein Hinweis
        var fits = LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", "AT", rating: 2100), derived: true)!;
        Assert.Equal(1 + 3 + 1 + 1, fits.Score);
        Assert.Contains("Lichess Blitz 2100 liegt 200 über der Elo 1900 (üblich: 100–300)", fits.Evidence);
        // Band 100–300 darüber (0.619.0, Wunsch „normal ist online ca. 200 höher"): knapp darüber oder darunter ist kein Hinweis.
        Assert.Equal(1 + 3 + 1, LeagueAccountFinder.Judge(Max, Prof("MaxMuster", "Max Muster", "AT", rating: 1950), derived: true)!.Score);
        Assert.True(LeagueAccountFinder.RatingFits(2000, 1900));
        Assert.True(LeagueAccountFinder.RatingFits(2200, 1900));
        Assert.False(LeagueAccountFinder.RatingFits(1999, 1900));
        Assert.False(LeagueAccountFinder.RatingFits(2201, 1900));
        Assert.False(LeagueAccountFinder.RatingFits(2000, null));
        // Ohne Elo oder ohne Wertung kein Einwand.
        Assert.NotNull(LeagueAccountFinder.Judge(Max with { Elo = null }, Prof("MaxMuster", "Max Muster", "AT", rating: 500), derived: true));
        Assert.True(LeagueAccountFinder.RatingPlausible(Prof("x"), 2000));
    }

    // ── Antworten lesen ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_LichessUsers_ChessComPlayer_Autocomplete_Fide()
    {
        var users = LeagueAccountFinder.ParseLichessUsers("""
            [{"id":"maxmuster","username":"MaxMuster","seenAt":1790764836059,
              "profile":{"flag":"AT","location":"Schwaz","realName":"Max Muster","fideRating":1950}},
             {"id":"max_muster","username":"Max_Muster","disabled":true},
             {"id":"mustermax","username":"MusterMax","profile":{"firstName":"Max","lastName":"Muster","country":"AT"}}]
            """);
        Assert.Equal(3, users.Count);
        Assert.Equal(("MaxMuster", "Max Muster", "AT", "Schwaz", 1950, false),
            (users[0].User, users[0].RealName, users[0].Flag, users[0].Location, users[0].FideRating, users[0].Closed));
        Assert.Equal("https://lichess.org/@/MaxMuster", users[0].Url);
        Assert.Equal(new DateTime(2026, 9, 30), users[0].LastActive!.Value.Date);
        Assert.True(users[1].Closed);
        Assert.Equal(("Max Muster", "AT"), (users[2].RealName, users[2].Flag));

        var cc = LeagueAccountFinder.ParseChessComPlayer("""
            {"url":"https://www.chess.com/member/MaxMuster","name":"Max Muster","username":"maxmuster",
             "country":"https://api.chess.com/pub/country/AT","last_online":1760214650,"status":"premium","location":"Tirol"}
            """)!;
        Assert.Equal(("chess.com", "MaxMuster", "Max Muster", "AT", "Tirol", false), (cc.Site, cc.User, cc.RealName, cc.Flag, cc.Location, cc.Closed));
        Assert.Equal("https://www.chess.com/member/MaxMuster", cc.Url);
        Assert.True(LeagueAccountFinder.ParseChessComPlayer("""{"username":"x1","status":"closed:fair_play_violations"}""")!.Closed);

        var rated = LeagueAccountFinder.ParseLichessUsers("""
            [{"id":"p","username":"P","perfs":{"bullet":{"games":3,"rating":2400,"prov":true},"blitz":{"games":11759,"rating":1630},
              "rapid":{"games":0,"rating":1500,"prov":true},"classical":{"games":9,"rating":2100}}},
             {"id":"q","username":"Q","perfs":{"rapid":{"games":0,"rating":1500,"prov":true}}}]
            """);
        Assert.Equal(((int?)1630, "Lichess Blitz"), (rated[0].Rating, rated[0].RatingLabel));     // vorläufig/zu wenige zählen nicht
        Assert.Null(rated[1].Rating);
        Assert.Equal(((int?)2693, "chess.com Blitz", (int?)2350), LeagueAccountFinder.ParseChessComStats("""
            {"chess_bullet":{"last":{"rating":2640},"record":{"win":3,"loss":2,"draw":0}},
             "chess_blitz":{"last":{"rating":2693},"record":{"win":377,"loss":386,"draw":76}},
             "chess_rapid":{"last":{"rating":2500},"record":{"win":10,"loss":5,"draw":1}},"fide":2350}
            """));
        Assert.Equal(((int?)null, (string?)null, (int?)null), LeagueAccountFinder.ParseChessComStats("{}"));

        Assert.Equal(new[] { "Muster1987", "musterm" },
            LeagueAccountFinder.ParseAutocomplete("""{"result":[{"name":"Muster1987","id":"muster1987"},{"name":"musterm","id":"musterm"}]}"""));
        Assert.Equal(((int?)1987, "AUT"), LeagueAccountFinder.ParseFidePlayer("""{"id":222,"name":"Muster, Max","federation":"AUT","year":1987}"""));
    }

    // ── Suchen ─────────────────────────────────────────────────────────────────────────────────

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

    private LeagueAccountFinder Finder(FakeHttp http) =>
        new(_db, new HttpClient(http), NullLogger<LeagueAccountFinder>.Instance) { ChessComPause = TimeSpan.Zero, PlayerPause = TimeSpan.Zero };

    /// <summary>Lichess kennt MaxMuster (Klarname, AT) und Muster1987 (Suche, Klarname); chess.com kennt Max_Muster (AT).</summary>
    private static FakeHttp World(int year = 1987) => new(req =>
    {
        var u = req.RequestUri!.ToString();
        if (u.Contains("/api/fide/player/")) return Ok($$"""{"id":222,"federation":"AUT","year":{{year}}}""");
        if (u.Contains("/api/player/autocomplete")) return Ok("""{"result":[{"name":"Muster1987"},{"name":"MusterFan"}]}""");
        if (u.EndsWith("/api/users"))
            return Ok("""
                [{"id":"maxmuster","username":"MaxMuster","profile":{"flag":"AT","realName":"Max Muster"}},
                 {"id":"muster1987","username":"Muster1987","profile":{"realName":"Max Muster","location":"Schwaz"}},
                 {"id":"musterfan","username":"MusterFan","profile":{"flag":"AT"}}]
                """);
        if (u.EndsWith("/pub/player/max_muster/stats"))
            return Ok("""{"chess_rapid":{"last":{"rating":2050},"record":{"win":30,"loss":20,"draw":5}}}""");
        if (u.EndsWith("/pub/player/max_muster"))
            return Ok("""{"url":"https://www.chess.com/member/Max_Muster","username":"max_muster","country":"https://api.chess.com/pub/country/AT"}""");
        if (u.Contains("api.chess.com/pub/player/")) return Status(HttpStatusCode.NotFound);
        return Status(HttpStatusCode.InternalServerError);
    });

    private async Task SeedAsync(string season = "2026/27")
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Name = "Landesliga", Season = season, League = "LL", Stage = "x" });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Muster, Max", NameKey = "muster, max", FideId = "222", Fed = "AUT", EloI = 1900 });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Scan_CreatesSuggestions_FromBothSites_AndNotTwice()
    {
        await SeedAsync();
        var http = World();
        var finder = Finder(http);
        var p = (await finder.PlayerAsync("222", default))!;
        Assert.Equal(("Muster, Max", "AUT", (int?)1900, "Kufstein 1"), (p.Name, p.Fed, p.Elo, p.Team));

        var r = await finder.ScanAsync(p, default);
        Assert.Equal((3, (string?)null), (r.Found, r.Skipped));
        var list = await _db.LeagueAccountSuggestions.OrderByDescending(s => s.Score).ThenBy(s => s.Id).ToListAsync();
        Assert.Equal(new[] { "lichess:MaxMuster", "lichess:Muster1987", "chess.com:Max_Muster" }, list.Select(s => $"{s.Site}:{s.UserName}"));
        Assert.Equal(1 + 3 + 1, list[0].Score);
        Assert.StartsWith("Nutzername aus dem Namen; Klarname im Profil", list[0].Evidence);
        Assert.Equal("https://www.chess.com/member/Max_Muster", list[2].Url);
        Assert.DoesNotContain(list, s => s.UserName == "MusterFan");       // Suche + nur Land = zu wenig
        var scan = await _db.LeagueAccountScans.SingleAsync();
        Assert.Equal((1987, "AUT", 3), (scan.BirthYear, scan.Federation, scan.Found));
        // Nur die abgeleiteten Namen gehen an chess.com, alle Kandidaten gesammelt an Lichess.
        Assert.Equal(7 + 1, http.Urls.Count(x => x.Contains("api.chess.com")));     // 7 Namen + Wertungen des einen Treffers
        Assert.StartsWith("Nutzername aus dem Namen; Land Österreich; chess.com Schnell 2050 liegt 150 über der Elo 1900", list[2].Evidence);
        Assert.Single(http.Urls, x => x.StartsWith("POST") && x.EndsWith("/api/users"));

        http.Urls.Clear();
        Assert.Equal(0, (await finder.ScanAsync(p, default)).Found);           // derselbe Stand: nichts Neues
        Assert.DoesNotContain(http.Urls, x => x.Contains("/api/fide/player/")); // Jahrgang ist gemerkt
        Assert.Equal(3, await _db.LeagueAccountSuggestions.CountAsync());
    }

    [Fact]
    public void Hides_OnlyKnownMinors()
    {
        var now = new DateTime(2026, 9, 30);
        Assert.False(LeagueHiddenAccounts.Hides(null, now));                                   // unbekannt = sichtbar (0.616.0)
        Assert.True(LeagueHiddenAccounts.Hides(2009, now));                                    // 17
        Assert.False(LeagueHiddenAccounts.Hides(2008, now));                                   // 18
    }

    /// <summary>0.610.0 (Wunsch: „du linkst sie, aber zeigst niemandem den Namen/Account"): Minderjährige werden gesucht,
    /// aber weder Vorschlag noch Konto noch Karte noch Meldeliste verraten Seite, Nutzernamen, Adresse oder Profilangaben.</summary>
    [Fact]
    public async Task Minors_AreSearched_ButNothingIdentifyingLeavesTheServer()
    {
        await SeedAsync();
        _db.LeagueViews.Add(new LeagueView { Tnr = 1, GeneratedAt = DateTime.UtcNow,
            Json = """{"fixtures":{"Kufstein 1":{"1":{"roster":[{"n":"Muster, Max","fide":"222","acc":[]}]}}}}""" });
        await _db.SaveChangesAsync();
        var finder = Finder(World(year: DateTime.UtcNow.Year - 15));
        var r = await finder.ScanAsync((await finder.PlayerAsync("222", default))!, default);
        Assert.Equal((3, (string?)null), (r.Found, r.Skipped));
        Assert.StartsWith("verborgen", (await _db.LeagueAccountScans.SingleAsync()).Note);

        var svc = new LeagueOnlineAccountService(_db);
        var overview = await svc.SuggestionsAsync(null, default);
        var item = overview["items"]![0]!;
        Assert.Equal((true, "Muster, Max"), (item["hidden"]!.GetValue<bool>(), item["name"]!.GetValue<string>()));
        Assert.Null(item["user"]); Assert.Null(item["url"]); Assert.Null(item["site"]); Assert.Null(item["profileName"]);
        Assert.False(string.IsNullOrEmpty(item["evidence"]!.GetValue<string>()));                // entschieden wird nach den Hinweisen

        var first = await _db.LeagueAccountSuggestions.FirstAsync(x => x.UserName == "MaxMuster");
        var (acc, _) = await svc.AcceptSuggestionAsync(first.Id, sure: true, default);
        var json = await svc.JsonAsync(acc!, default);
        Assert.Equal((true, (string?)null, (string?)null), (json["hidden"]!.GetValue<bool>(), (string?)json["user"], (string?)json["comment"]));

        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var card = (await league.CardAsync("222", onlySure: false, default))!;
        var shared = (await league.CardAsync("222", onlySure: true, default))!;
        Assert.Single(card["accounts"]!.AsArray());
        Assert.Empty(shared["accounts"]!.AsArray());                                              // über den Link nicht einmal, DASS es eins gibt
        var view = (await _db.LeagueViews.AsNoTracking().SingleAsync()).Json;
        // Nirgends der Nutzername — auch nicht in der Meldeliste.
        foreach (var text in new[] { overview.ToJsonString(), json.ToJsonString(), card.ToJsonString(), shared.ToJsonString(), view })
            foreach (var name in new[] { "MaxMuster", "Max_Muster", "Muster1987", "max_muster", "maxmuster" })
                Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scan_WithoutBirthYear_SearchesAndShows_AndRateLimitThrows()
    {
        await SeedAsync();
        var world = World();
        var noYear = Finder(new FakeHttp(req => req.RequestUri!.ToString().Contains("/api/fide/player/") ? Status(HttpStatusCode.NotFound)
            : world.Answer(req)));
        Assert.Equal(3, (await noYear.ScanAsync(Max, default)).Found);
        Assert.DoesNotContain("222", await LeagueHiddenAccounts.FidesAsync(_db, null, default));   // Jahrgang unbekannt = sichtbar
        Assert.Null((await _db.LeagueAccountScans.SingleAsync()).Note);

        var limited = Finder(new FakeHttp(_ => Status(HttpStatusCode.TooManyRequests)));
        await Assert.ThrowsAsync<LeagueOnlineSync.RateLimitedException>(() => limited.ScanAsync(Max with { Fide = "333" }, default));
    }

    [Fact]
    public async Task Scan_LeavesOutExistingAccountsAndRejectedSuggestions()
    {
        await SeedAsync();
        _db.LeagueOnlineAccounts.Add(new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "maxmuster", Url = "u", Confidence = "sicher" });
        _db.LeagueAccountSuggestions.Add(new LeagueAccountSuggestion
        {
            FideId = "222", Site = "chess.com", UserName = "Max_Muster", Url = "u", Evidence = "e", Status = LeagueSuggestionStatus.Rejected,
        });
        await _db.SaveChangesAsync();
        var r = await Finder(World()).ScanAsync(Max, default);
        Assert.Equal(1, r.Found);
        Assert.Equal("Muster1987", (await _db.LeagueAccountSuggestions.SingleAsync(s => s.Status == LeagueSuggestionStatus.Open)).UserName);
    }

    [Fact]
    public async Task Rescan_DropsOpenSuggestionsTheRulesNoLongerCarry_KeepsRejected()
    {
        await SeedAsync();
        _db.LeagueAccountSuggestions.AddRange(
            new LeagueAccountSuggestion { FideId = "222", Site = "lichess", UserName = "Muster500", Url = "u", Evidence = "alt" },
            new LeagueAccountSuggestion { FideId = "222", Site = "lichess", UserName = "MusterAlt", Url = "u", Evidence = "alt",
                Status = LeagueSuggestionStatus.Rejected });
        await _db.SaveChangesAsync();
        await Finder(World()).ScanAsync(Max, default);
        var left = await _db.LeagueAccountSuggestions.Select(s => s.UserName).ToListAsync();
        Assert.DoesNotContain("Muster500", left);                                     // nicht mehr gefunden → weg
        Assert.Contains("MusterAlt", left);                                          // verworfen bleibt
        Assert.Equal(LeagueAccountFinder.CurrentVersion, (await _db.LeagueAccountScans.SingleAsync()).Version);
    }

    [Fact]
    public async Task RunOnce_OlderRuleVersion_IsDueAgain()
    {
        await SeedAsync();
        _db.LeagueAccountScans.Add(new LeagueAccountScan { FideId = "222", BirthYear = 1987, ScannedAt = DateTime.UtcNow, Version = 1 });
        await _db.SaveChangesAsync();
        var http = World();
        Assert.False(await Finder(http).RunOnceAsync(TimeSpan.FromMinutes(5), default));
        Assert.Contains(http.Urls, u => u.EndsWith("/api/users"));
        Assert.Equal(LeagueAccountFinder.CurrentVersion, (await _db.LeagueAccountScans.AsNoTracking().SingleAsync()).Version);
    }

    [Fact]
    public async Task RunOnce_OnlyCurrentSeason_NotRecentlyScanned()
    {
        await SeedAsync("2026/27");
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 2, Name = "Alt", Season = "2024/25", League = "LL", Stage = "x" });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 2, Team = "Alt 1", Name = "Alt, Otto", NameKey = "alt, otto", FideId = "444" });
        await _db.SaveChangesAsync();
        var http = World();
        Assert.False(await Finder(http).RunOnceAsync(TimeSpan.FromMinutes(5), default));
        Assert.Equal(new[] { "222" }, await _db.LeagueAccountScans.Select(s => s.FideId).ToListAsync());
        http.Urls.Clear();
        Assert.False(await Finder(http).RunOnceAsync(TimeSpan.FromMinutes(5), default));   // eben abgesucht: nichts fällig
        Assert.Empty(http.Urls);
    }

    // ── Übernehmen / Verwerfen ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Accept_CreatesAccountWithComment_Reject_Stays_Delete_RemembersAsRejected()
    {
        await SeedAsync();
        await Finder(World()).ScanAsync(Max, default);
        var svc = new LeagueOnlineAccountService(_db);

        var overview = await svc.SuggestionsAsync(null, default);
        var items = overview["items"]!.AsArray();
        Assert.Equal((3, 1, 1), (items.Count, (int)overview["scanned"]!, (int)overview["total"]!));
        Assert.Equal(("Muster, Max", "Kufstein 1"), ((string)items[0]!["name"]!, (string)items[0]!["team"]!));

        var first = await _db.LeagueAccountSuggestions.SingleAsync(s => s.UserName == "MaxMuster");
        var (acc, reason) = await svc.AcceptSuggestionAsync(first.Id, sure: false, default);
        Assert.Null(reason);
        Assert.Equal(("lichess", "MaxMuster", "wahrscheinlich", true), (acc!.Site, acc.UserName, acc.Confidence, acc.Manual));
        Assert.StartsWith("Vorschlag der Konto-Suche: Nutzername aus dem Namen", acc.Evidence);
        Assert.False(await _db.LeagueAccountSuggestions.AnyAsync(s => s.Id == first.Id));
        Assert.Equal("notFound", (await svc.AcceptSuggestionAsync(first.Id, true, default)).Reason);

        var second = await _db.LeagueAccountSuggestions.SingleAsync(s => s.UserName == "Muster1987");
        Assert.True(await svc.RejectSuggestionAsync(second.Id, default));
        Assert.False(await svc.RejectSuggestionAsync(second.Id, default));
        Assert.Single((await svc.SuggestionsAsync("222", default))["items"]!.AsArray());          // nur noch chess.com

        // Ein von Hand eingetragenes Konto erledigt den passenden Vorschlag …
        await svc.CreateAsync("222", new("chess.com", "max_muster", true, null), default);
        Assert.Empty((await svc.SuggestionsAsync("222", default))["items"]!.AsArray());
        // … und ein entferntes kommt bei der nächsten Suche nicht wieder.
        Assert.True(await svc.DeleteAsync(acc.Id, default));
        Assert.Equal(LeagueSuggestionStatus.Rejected,
            (await _db.LeagueAccountSuggestions.SingleAsync(s => s.Site == "lichess" && s.UserName == "MaxMuster")).Status);
        Assert.Equal(0, (await Finder(World()).ScanAsync(Max, default)).Found);
    }

    [Fact]
    public void SuggestionJson_CarriesTheFieldsTheCardShows()
    {
        var o = LeagueOnlineAccountService.SuggestionJson(new LeagueAccountSuggestion
        {
            Id = 5, FideId = "222", Site = "lichess", UserName = "MaxMuster", Url = "u", Score = 4, Evidence = "e", ProfileName = "Max Muster",
            LastActive = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        Assert.Equal((5, "MaxMuster", 4, "Max Muster", "2026-09-01T00:00:00.0000000Z"),
            ((int)o["id"]!, (string)o["user"]!, (int)o["score"]!, (string)o["profileName"]!, (string)o["lastActive"]!));
        Assert.Null(o["name"]);
    }
}
