using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Meldungen aus dem Online-Bereich des Schachkreises Zugspitze / Bezirks Oberbayern (0.716.0): Turnierliste, Ergebnisseite (Arena
/// mit „Perf", Schweizer System mit „S-B"), Lichess-ndjson und der ganze Weg bis zu Meldung, Vorschlag und (i). Die Fixtures sind
/// synthetisch im Aufbau der echten Seiten (nachgesehen 07.10.2026), alle Namen erfunden.
/// </summary>
public class ZugspitzeOnlineReportsTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private static string Html(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ZugspitzeOnline", name), Encoding.UTF8);

    // ── Lesen ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TournamentList_ResultPagesOnly_KindFromLinkOrDuration()
    {
        var list = ZugspitzeOnlineReports.ParseTournamentList(Html("onlineturniere.html"));
        Assert.Equal(new[] { "AAAAaaa1", "JJJJjjj9", "BBBBbbb2", "CCCCccc3" }, list.Select(t => t.Id));      // 4er-MM ohne Ergebnisseite
        Assert.Equal(("Online-KEM 2022 M I", "KEM2022", "swiss", false), (list[0].Name, list[0].Serie, list[0].Kind, list[0].Youth));
        Assert.True(list[1].Youth);
        Assert.Equal(("Liga", "tournament"), (list[2].Serie, list[2].Kind));
        Assert.Equal("swiss", list[3].Kind);                                                                // kein Lichess-Link: „8 Runden"
    }

    [Fact]
    public void ResultPage_SwissWithTieBreak_TeamBattleWithPerformance()
    {
        var (swiss, kind) = ZugspitzeOnlineReports.ParseResultPage(Html("ergebnis-swiss.html"));
        Assert.Equal(ZugspitzeOnlineReports.ExtraKind.TieBreak, kind);
        Assert.Equal(5, swiss.Count);
        Assert.Equal(new ZugspitzeOnlineReports.PageRow(1, "Wiesner,Konrad", "SK Weilheim", 2010, 6.5, 30.25), swiss[0]);
        Assert.Equal(("Unbekannt,Hugo", 2.0), (swiss[4].Name, swiss[4].Points));

        var (team, k2) = ZugspitzeOnlineReports.ParseResultPage(Html("ergebnis-team.html"));         // Mannschaftswertung davor
        Assert.Equal(ZugspitzeOnlineReports.ExtraKind.Performance, k2);
        Assert.Equal(6, team.Count);
        Assert.Equal(new ZugspitzeOnlineReports.PageRow(2, "Mayr,Stefan", "Schachkreis Zugspitze", 1800, 10, 1900), team[1]);
        Assert.Empty(ZugspitzeOnlineReports.ParseResultPage("<html>keine Tabelle</html>").Rows);
    }

    [Theory]
    [InlineData("8½", 8.5)]
    [InlineData("½", 0.5)]
    [InlineData("12", 12.0)]
    [InlineData("42.25", 42.25)]
    [InlineData("3,5", 3.5)]
    public void Number_HalfPoints(string s, double v) => Assert.Equal(v, ZugspitzeOnlineReports.Number(s));

    [Fact]
    public void LichessResults_ArenaAndSwiss()
    {
        var rows = ZugspitzeOnlineReports.ParseLichessResults(
            "{\"rank\":1,\"score\":17,\"rating\":2156,\"username\":\"Rolle\",\"performance\":2127,\"team\":\"x\"}\n"
            + "{\"rank\":1,\"points\":8.5,\"tieBreak\":42.25,\"rating\":2248,\"username\":\"Kombi\",\"performance\":2488}\nkaputt\n{\"rank\":3}\n");
        Assert.Equal(new[]
        {
            new ZugspitzeOnlineReports.LichessRow("Rolle", 2156, 17, 2127, null),
            new ZugspitzeOnlineReports.LichessRow("Kombi", 2248, 8.5, 2488, 42.25),
        }, rows);
    }

    [Fact]
    public void Match_ByRatingAndPoints_NeverByRank_AmbiguousDropped()
    {
        var (page, kind) = ZugspitzeOnlineReports.ParseResultPage(Html("ergebnis-swiss.html"));
        var (matched, ambiguous, missing) = ZugspitzeOnlineReports.Match(page, ZugspitzeOnlineReports.ParseLichessResults(SwissNdjson), kind);
        Assert.Equal(new Dictionary<int, string> { [0] = "KonniW", [1] = "ThesiB", [4] = "hugo_u" }, matched);   // ThesiB über S-B
        Assert.Equal((1, 1), (ambiguous, missing));                                                    // Haller: zwei gleiche; Ober: keiner
        // Ein Konto, das zwei Zeilen erklären würde, fällt für beide weg.
        var two = new[] { new ZugspitzeOnlineReports.PageRow(1, "A,B", "X", 1500, 3, null), new ZugspitzeOnlineReports.PageRow(2, "C,D", "X", 1500, 3, null) };
        var r = ZugspitzeOnlineReports.Match(two, new[] { new ZugspitzeOnlineReports.LichessRow("eins", 1500, 3, null, null) }, ZugspitzeOnlineReports.ExtraKind.None);
        Assert.Equal((0, 2), (r.Matched.Count, r.Ambiguous));
    }

    // ── Der ganze Weg ──────────────────────────────────────────────────────────────────────────

    /// <summary>Lichess zum Schweizer-System-Turnier: Haller doppelt (gleiche Wertung, Punkte, S-B), Brandl über die S-B getrennt,
    /// „Fremder" nicht auf der Seite (nicht beim Kreis angemeldet).</summary>
    private const string SwissNdjson =
        "{\"rank\":1,\"points\":6.5,\"tieBreak\":30.25,\"rating\":2010,\"username\":\"KonniW\",\"performance\":2200}\n"
        + "{\"rank\":2,\"points\":5.5,\"tieBreak\":28,\"rating\":1900,\"username\":\"Fremder\",\"performance\":2000}\n"
        + "{\"rank\":3,\"points\":5,\"tieBreak\":20,\"rating\":1850,\"username\":\"ThesiB\",\"performance\":1900}\n"
        + "{\"rank\":4,\"points\":5,\"tieBreak\":18,\"rating\":1850,\"username\":\"Andere1850\",\"performance\":1880}\n"
        + "{\"rank\":5,\"points\":4,\"tieBreak\":15.5,\"rating\":1700,\"username\":\"Gus1\",\"performance\":1750}\n"
        + "{\"rank\":6,\"points\":4,\"tieBreak\":15.5,\"rating\":1700,\"username\":\"Gus2\",\"performance\":1750}\n"
        + "{\"rank\":7,\"points\":2,\"tieBreak\":4,\"rating\":1500,\"username\":\"hugo_u\",\"performance\":1400}\n";

    private const string TeamNdjson =
        "{\"rank\":1,\"score\":17,\"rating\":1990,\"username\":\"KonniW\",\"performance\":2100,\"team\":\"sk-weilheim-und-freunde\"}\n"
        + "{\"rank\":2,\"score\":10,\"rating\":1800,\"username\":\"StefMayr\",\"performance\":1900,\"team\":\"schachkreis-zugspitze\"}\n"
        + "{\"rank\":3,\"score\":8,\"rating\":1700,\"username\":\"Wechsler\",\"performance\":1800,\"team\":\"sk-weilheim-und-freunde\"}\n"
        + "{\"rank\":4,\"score\":6,\"rating\":1650,\"username\":\"PaulF\",\"performance\":1700,\"team\":\"sk-weilheim-und-freunde\"}\n"
        + "{\"rank\":5,\"score\":5,\"rating\":1500,\"username\":\"AnnaM\",\"performance\":1550,\"team\":\"schachclub-starnberg\"}\n"
        + "{\"rank\":6,\"score\":4,\"rating\":1600,\"username\":\"KlaraW\",\"performance\":1650,\"team\":\"sk-weilheim-und-freunde\"}\n";

    private const string HauptNdjson = "{\"rank\":1,\"score\":5,\"rating\":1777,\"username\":\"Wechsler\",\"performance\":1800}\n";

    private sealed class Factory(Func<string, HttpRequestMessage, HttpResponseMessage> answer) : IHttpClientFactory
    {
        public List<string> Calls { get; } = new();
        public HttpClient CreateClient(string name) =>
            new(new Handler(r => { Calls.Add($"{name} {r.RequestUri!.PathAndQuery}"); return answer(name, r); }))
            {
                BaseAddress = new Uri(name == ZugspitzeSource.ClientName ? ZugspitzeSource.SiteUrl + "/" : "https://lichess.org/"),
            };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(f(request));
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
    private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound) { Content = new StringContent("") };

    private static Factory World(bool lichessThrottles = false, string? minorFide = null) => new((_, r) =>
    {
        var q = r.RequestUri!.PathAndQuery;
        if (q.StartsWith("/onlineturniere/?saison=20221", StringComparison.Ordinal)) return Ok(Html("onlineturniere.html"));
        if (q.StartsWith("/onlineergebnis/", StringComparison.Ordinal))
            return q.Contains("AAAAaaa1") ? Ok(Html("ergebnis-swiss.html")) : q.Contains("BBBBbbb2") ? Ok(Html("ergebnis-team.html"))
                : q.Contains("CCCCccc3") ? Ok(Html("ergebnis-haupt.html")) : NotFound();
        if (q.StartsWith("/api/", StringComparison.Ordinal) && lichessThrottles) return new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("") };
        if (q.StartsWith("/api/swiss/AAAAaaa1/results", StringComparison.Ordinal)) return Ok(SwissNdjson);
        if (q.StartsWith("/api/tournament/BBBBbbb2/results", StringComparison.Ordinal)) return Ok(TeamNdjson);
        if (q.StartsWith("/api/tournament/CCCCccc3/results", StringComparison.Ordinal)) return Ok(HauptNdjson);   // geraten war „swiss"
        if (minorFide is not null && q.StartsWith($"/api/fide/player/{minorFide}", StringComparison.Ordinal))
            return Ok($"{{\"id\":1,\"federation\":\"GER\",\"year\":{DateTime.UtcNow.Year - 12}}}");
        if (q.StartsWith("/api/fide/player/", StringComparison.Ordinal)) return Ok("{\"id\":1,\"federation\":\"GER\",\"year\":1980}");
        return NotFound();
    });

    private ZugspitzeOnlineReports Reports(IHttpClientFactory http) =>
        new(_db, http, NullLogger<ZugspitzeOnlineReports>.Instance) { Pause = TimeSpan.Zero };

    /// <summary>Bayerische Meldelisten: Weilheim II (Zugspitze) und SC Garching (Ligamanager) — Huber, Josef gibt es in beiden;
    /// dazu ein gleichnamiger Tiroler, der NICHT in Frage kommt.</summary>
    private async Task SeedAsync()
    {
        _db.LeagueTournaments.AddRange(
            new LeagueTournament { Tnr = 912_026_001, Name = "Zugspitzliga", Season = "2026/27", League = "ZL", Stage = "Liga", Source = ZugspitzeSource.Source },
            new LeagueTournament { Tnr = 900_002_573, Name = "Bezirksliga", Season = "2026/27", League = "BL", Stage = "Liga", Source = LigamanagerSource.Source },
            new LeagueTournament { Tnr = 1, Name = "Landesliga", Season = "2025/26", League = "LL", Stage = "Liga" });
        LeaguePlayer P(int tnr, string team, string name, string? fide) =>
            new() { Tnr = tnr, Team = team, Name = name, NameKey = name.ToLowerInvariant(), FideId = fide, EloN = 1800 };
        _db.LeaguePlayers.AddRange(
            P(912_026_001, "SK Weilheim II", "Wiesner, Konrad", "900"),
            P(912_026_001, "SK Weilheim II", "Mayr, Stefan", "903"),
            P(912_026_001, "SK Weilheim II", "Huber, Josef", "901"),
            P(912_026_001, "SK Weilheim III", "Fischer, Paul", null),
            P(912_026_001, "SK Weilheim III", "Fischer, Peter", "905"),
            P(912_026_001, "SC Starnberg", "Brandl, Theresa", "904"),
            P(900_002_573, "SC Garching 1", "Huber, Josef", "902"),
            P(900_002_573, "SC Garching 1", "Mayr, Anna", "906"),
            P(1, "Schwaz 1", "Wiesner, Konrad", "999"));
        _db.LeagueOnlineAccounts.Add(new LeagueOnlineAccount
        {
            FideId = "904", Site = "lichess", UserName = "ThesiB", Url = "https://lichess.org/@/ThesiB", Confidence = "sicher", Manual = true,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task DryRun_CountsAndListsWithoutWriting()
    {
        await SeedAsync();
        var http = World();
        var r = await Reports(http).ImportAsync("20221", dryRun: true, default);
        var c = r.Counts;
        Assert.Equal(("20221", "Online-Schach Oberbayern 20221", true), (r.Season, r.Source, r.DryRun));
        Assert.Equal((3, 1, 0), (c.Tournaments, c.SkippedYouth, c.Unreadable));
        Assert.Equal((12, 10, 1, 1), (c.Rows, c.Matched, c.Ambiguous, c.NotOnLichess));
        // Unbekannt (kein Ligaspieler), Wiesner Klara (nur die Initiale passt zu Konrad) — Mayr Anna (Starnberg ≠ Garching) — Fischer Paul (ohne FIDE-ID)
        Assert.Equal((2, 0, 1, 1), (c.NoRoster, c.RosterAmbiguous, c.OtherClub, c.NoFide));
        // Wiesner ×2, Brandl, Mayr Stefan (Kreis-Team: kein Widerspruch zum Verein), Huber (Verein entscheidet: Weilheim, nicht Garching), Fischer Peter
        Assert.Equal((6, 1, 3), (c.Assigned, c.Conflicting, c.Reports));                              // „Wechsler" = Huber UND Fischer → weg
        var items = r.Items!;
        Assert.Equal(new[] { ("900", "KonniW"), ("903", "StefMayr"), ("904", "ThesiB") }, items.Select(x => (x.Fide, x.User)).OrderBy(x => x.Fide));
        var konni = items.Single(x => x.User == "KonniW");
        Assert.Equal(("Wiesner, Konrad", "SK Weilheim", "Wiesner,Konrad"), (konni.Player, konni.Team, konni.PageName));
        Assert.Equal(new[] { "Online-KEM 2022 M I (Rang 1, Lichess-Wertung 2010)", "1.Kreisliga 7+3 Teamkampf (Rang 1, Lichess-Wertung 1990)" }, konni.Tournaments);
        Assert.Equal((3, 0, 0), (r.Reports!.Added, r.Reports.Updated, r.Reports.Removed));
        Assert.Equal((2, 0, 1, 0, 0, 0), (r.Accounts.Created, r.Accounts.Upgraded, r.Accounts.Unchanged, r.Accounts.Accepted,
            r.Accounts.TakenElsewhere, r.Accounts.Rejected));                                          // KonniW + StefMayr neu, ThesiB steht schon
        Assert.Equal(0, r.Suggestions);
        Assert.Empty(await _db.LeagueSelfReports.ToListAsync());
        Assert.Empty(await _db.LeagueAccountSuggestions.ToListAsync());
        Assert.Equal("ThesiB", (await _db.LeagueOnlineAccounts.SingleAsync()).UserName);               // nichts angelegt
        Assert.Null((await _db.LeagueOnlineAccounts.SingleAsync()).Evidence);                          // nichts ergänzt
        Assert.Empty(await _db.LeagueAccountScans.ToListAsync());
        Assert.DoesNotContain(http.Calls, x => x.Contains("JJJJjjj9"));                                // Jugend nie abgerufen
        Assert.DoesNotContain(http.Calls, x => x.Contains("zug-2") || x.Contains("onlinemm"));         // 4er-MM: anderer Ausrichter
        Assert.Contains($"{RookHub.Api.Services.League.LeagueOnlineSync.ClientName} /api/swiss/CCCCccc3/results?nb=1000", http.Calls);   // geraten,
        Assert.Contains($"{RookHub.Api.Services.League.LeagueOnlineSync.ClientName} /api/tournament/CCCCccc3/results?nb=1000", http.Calls); // dann die andere Art
    }

    private const string KonniNote = "Online-Schach Oberbayern 20221: Online-KEM 2022 M I (Rang 1, Lichess-Wertung 2010) + 1 weitere";

    private static (int, int, int, int, int, int, int) Tally(ZugspitzeOnlineReports.AccountCounts a) =>
        (a.Created, a.Upgraded, a.Unchanged, a.Accepted, a.TakenElsewhere, a.Rejected, a.Failed);

    [Fact]
    public async Task Import_WritesThirdPartyReports_AndSureAccounts_SecondRunIdempotent()
    {
        await SeedAsync();
        var r = await Reports(World()).ImportAsync("20221", dryRun: false, default);
        Assert.Null(r.Items);
        var reports = await _db.LeagueSelfReports.OrderBy(x => x.FideId).ToListAsync();
        Assert.Equal(3, reports.Count);
        Assert.All(reports, x => Assert.Equal(("Online-Schach Oberbayern 20221", (string?)"Schachkreis Zugspitze", "lichess"), (x.Source, x.Reporter, x.Site)));
        Assert.Equal(("900", "KonniW", (string?)"SK Weilheim", (string?)"Online-KEM 2022 M I (Rang 1, Lichess-Wertung 2010) + 1 weitere"),
            (reports[0].FideId, reports[0].UserName, reports[0].Team, reports[0].Note));

        // Konten statt Vorschlägen: KonniW + StefMayr neu „gesichert", ThesiB stand schon — nur die Meldung im Kommentar ergänzt.
        Assert.Equal((2, 0, 1, 0, 0, 0, 0), Tally(r.Accounts));
        Assert.Equal(0, r.Suggestions);
        Assert.Empty(await _db.LeagueAccountSuggestions.ToListAsync());
        var accs = await _db.LeagueOnlineAccounts.OrderBy(a => a.FideId).ToListAsync();
        Assert.Equal(new[] { ("900", "KonniW"), ("903", "StefMayr"), ("904", "ThesiB") }, accs.Select(a => (a.FideId, a.UserName)));
        var konni = accs[0];
        Assert.Equal(("lichess", "sicher", true, (string?)"Schachkreis Zugspitze", (string?)KonniNote, "https://lichess.org/@/KonniW"),
            (konni.Site, konni.Confidence, konni.Manual, konni.AddedBy, konni.Evidence, konni.Url));
        Assert.Null(konni.SyncedAt);                                                                   // der Abruf holt seine Partien
        Assert.Equal("Online-Schach Oberbayern 20221: Online-KEM 2022 M I (Rang 2, Lichess-Wertung 1850)", accs[2].Evidence);
        Assert.Null(accs[2].AddedBy);                                                                  // wer es eingetragen hat, bleibt
        Assert.Equal(1980, (await _db.LeagueAccountScans.SingleAsync(s => s.FideId == "900")).BirthYear);

        // Die (i)-Prüfung des Kontos zeigt „Gemeldet von Schachkreis Zugspitze".
        var checks = new LeagueAccountChecks(_db, new HttpClient(new Handler(_ => NotFound())), null);
        var res = (await checks.ForAccountAsync(konni.Id, default))!;
        var item = res.Items.Single(i => i.Key == "reported:Schachkreis Zugspitze");
        Assert.Equal(("Gemeldet von Schachkreis Zugspitze", LeagueAccountChecks.Ok), (item.Label, item.Status));

        // Ein zweiter Lauf ändert nichts: keine Meldung, kein Konto, kein Kommentar doppelt.
        var again = await Reports(World()).ImportAsync("20221", dryRun: false, default);
        Assert.Equal((0, 3, 0), (again.Reports!.Added, again.Reports.Unchanged, again.Suggestions));
        Assert.Equal((0, 0, 3, 0, 0, 0, 0), Tally(again.Accounts));
        var after = await _db.LeagueOnlineAccounts.AsNoTracking().OrderBy(a => a.FideId).ToListAsync();
        Assert.Equal(accs.Select(a => a.Evidence), after.Select(a => a.Evidence));
    }

    [Fact]
    public async Task UnsureAccountOfThisPlayer_BecomesSure_CommentAppended()
    {
        await SeedAsync();
        _db.LeagueOnlineAccounts.Add(new LeagueOnlineAccount
        {
            FideId = "900", Site = "lichess", UserName = "konniw", Url = "https://lichess.org/@/konniw", Confidence = "wahrscheinlich",
            Evidence = "Name passt", AddedBy = "patrik",
        });
        await _db.SaveChangesAsync();
        var r = await Reports(World()).ImportAsync("20221", dryRun: false, default);
        Assert.Equal((1, 1, 1, 0, 0, 0, 0), Tally(r.Accounts));
        var konni = await _db.LeagueOnlineAccounts.AsNoTracking().SingleAsync(a => a.FideId == "900");
        Assert.Equal(("sicher", "Name passt · " + KonniNote, (string?)"patrik", true), (konni.Confidence, konni.Evidence, konni.AddedBy, konni.Manual));
        Assert.Equal(3, await _db.LeagueOnlineAccounts.CountAsync());
    }

    [Fact]
    public async Task AccountOfAnotherPlayer_NoAccount_ButSuggestion()
    {
        await SeedAsync();
        _db.LeagueOnlineAccounts.Add(new LeagueOnlineAccount
        {
            FideId = "999", Site = "lichess", UserName = "StefMayr", Url = "https://lichess.org/@/StefMayr", Confidence = "sicher",
        });
        await _db.SaveChangesAsync();
        var dry = await Reports(World()).ImportAsync("20221", dryRun: true, default);
        Assert.Equal((1, 0, 1, 0, 1, 0, 0), Tally(dry.Accounts));
        var r = await Reports(World()).ImportAsync("20221", dryRun: false, default);
        Assert.Equal((1, 0, 1, 0, 1, 0, 0), Tally(r.Accounts));
        Assert.Equal(1, r.Suggestions);
        Assert.False(await _db.LeagueOnlineAccounts.AnyAsync(a => a.FideId == "903"));
        var sugg = await _db.LeagueAccountSuggestions.SingleAsync();
        Assert.Equal(("903", "StefMayr", ZugspitzeOnlineReports.SuggestionSource, ZugspitzeOnlineReports.SuggestionScore, LeagueSuggestionStatus.Open),
            (sugg.FideId, sugg.UserName, sugg.Source!, sugg.Score, sugg.Status));
        Assert.StartsWith("Gemeldet von Schachkreis Zugspitze (Online-Schach Oberbayern 20221): Mayr,Stefan", sugg.Evidence);
        Assert.Equal(1980, (await _db.LeagueAccountScans.SingleAsync(s => s.FideId == "903")).BirthYear);
        // Ein zweiter Lauf legt den Vorschlag nicht doppelt an.
        var again = await Reports(World()).ImportAsync("20221", dryRun: false, default);
        Assert.Equal((0, 1), (again.Suggestions, await _db.LeagueAccountSuggestions.CountAsync()));
    }

    [Fact]
    public async Task RejectedSuggestion_NoAccount()
    {
        await SeedAsync();
        _db.LeagueAccountSuggestions.Add(new LeagueAccountSuggestion
        {
            FideId = "900", Site = "lichess", UserName = "konniw", Url = "https://lichess.org/@/konniw", Evidence = "Konto entfernt",
            Status = LeagueSuggestionStatus.Rejected, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        var r = await Reports(World()).ImportAsync("20221", dryRun: false, default);
        Assert.Equal((1, 0, 1, 0, 0, 1, 0), Tally(r.Accounts));
        Assert.False(await _db.LeagueOnlineAccounts.AnyAsync(a => a.FideId == "900"));
        Assert.Equal(LeagueSuggestionStatus.Rejected, (await _db.LeagueAccountSuggestions.SingleAsync()).Status);   // bleibt verworfen
    }

    [Fact]
    public async Task OpenReportSuggestion_IsAccepted()
    {
        await SeedAsync();
        _db.LeagueAccountSuggestions.Add(new LeagueAccountSuggestion
        {
            FideId = "903", Site = "lichess", UserName = "StefMayr", Url = "https://lichess.org/@/StefMayr", Score = ZugspitzeOnlineReports.SuggestionScore,
            Evidence = "Gemeldet von Schachkreis Zugspitze (…)", Status = LeagueSuggestionStatus.Open, CreatedAt = DateTime.UtcNow,
            Source = ZugspitzeOnlineReports.SuggestionSource,
        });
        await _db.SaveChangesAsync();
        var dry = await Reports(World()).ImportAsync("20221", dryRun: true, default);
        Assert.Equal((1, 0, 1, 1, 0, 0, 0), Tally(dry.Accounts));
        Assert.Single(await _db.LeagueAccountSuggestions.ToListAsync());                              // Probelauf: bleibt
        var r = await Reports(World()).ImportAsync("20221", dryRun: false, default);
        Assert.Equal((1, 0, 1, 1, 0, 0, 0), Tally(r.Accounts));
        Assert.Empty(await _db.LeagueAccountSuggestions.ToListAsync());                               // erledigt
        var acc = await _db.LeagueOnlineAccounts.SingleAsync(a => a.FideId == "903");
        Assert.Equal(("sicher", (string?)"Schachkreis Zugspitze"), (acc.Confidence, acc.AddedBy));
        Assert.StartsWith("Online-Schach Oberbayern 20221: 1.Kreisliga", acc.Evidence);
    }

    [Fact]
    public async Task Minor_AccountCreated_ButHiddenEverywhere()
    {
        await SeedAsync();
        var r = await Reports(World(minorFide: "900")).ImportAsync("20221", dryRun: false, default);
        Assert.Equal((2, 0, 1, 0, 0, 0, 0), Tally(r.Accounts));
        var acc = await _db.LeagueOnlineAccounts.SingleAsync(a => a.FideId == "900");
        Assert.Equal("sicher", acc.Confidence);
        Assert.Contains("900", await LeagueHiddenAccounts.FidesAsync(_db, null, default));
        var json = await new LeagueOnlineAccountService(_db).JsonAsync(acc, default);
        Assert.Equal((true, null, null, null), (json["hidden"]!.GetValue<bool>(), json["user"], json["comment"], json["addedBy"]));
        Assert.DoesNotContain("KonniW", json.ToJsonString());
    }

    [Fact]
    public async Task LichessThrottle_EndsTheRun_NothingWritten()
    {
        await SeedAsync();
        await Assert.ThrowsAsync<LeagueOnlineSync.RateLimitedException>(() => Reports(World(lichessThrottles: true)).ImportAsync("20221", false, default));
        Assert.Empty(await _db.LeagueSelfReports.ToListAsync());
    }

    [Fact]
    public async Task UnknownSeason_NotFound_InvalidSeason_Throws()
    {
        await Assert.ThrowsAsync<ZugspitzeOnlineReports.NotFoundException>(() => Reports(World()).ImportAsync("20204", true, default));
        await Assert.ThrowsAsync<ArgumentException>(() => Reports(World()).ImportAsync("2021", true, default));
        Assert.True(ZugspitzeOnlineReports.ValidSeason("20213"));
        Assert.False(ZugspitzeOnlineReports.ValidSeason("20215"));
        Assert.False(ZugspitzeOnlineReports.ValidSeason(null));
    }
}
