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
        var p = (await PrepAccountSearch.PlayerAsync(_db, Fide, default))!;
        Assert.Equal(("Prepmann, Paul", (int?)2210, false, (string?)null), (p.Name, p.Elo, p.Local, p.Fed));   // nicht MaxElo 2300
        Assert.Null(await PrepAccountSearch.PlayerAsync(_db, "990999", default));
    }

    // ── LeagueHubs eigene Wege kennen Prep-Spieler nicht (wie vor 0.637.0) ─────────────────────

    [Fact]
    public async Task LeagueHub_PrepOnlyFide_UnknownLikeBefore_PrepPathWorks()
    {
        await SeedPrepAsync();
        // Ohne Meldeliste und Liga-Karte: LeagueHub findet ihn nicht — der Scan-Endpunkt antwortet damit „unknownPlayer".
        Assert.Null(await LeagueAccountFinder.PlayerAsync(_db, Fide, default));
        var accounts = new LeagueOnlineAccountService(_db);
        Assert.Equal("unknownPlayer", (await accounts.CreateAsync(Fide, new LeagueOnlineAccountService.Input("lichess", "PaulPrepmann", true, null), default)).Reason);
        await Search(World()).ScanAsync(await PrepIdAsync(), 1, default);
        var s = await _db.LeagueAccountSuggestions.SingleAsync();
        Assert.Equal("unknownPlayer", (await accounts.AcceptSuggestionAsync(s.Id, true, default)).Reason);    // LeagueHubs Übernehmen
        Assert.Empty(await _db.LeagueOnlineAccounts.ToListAsync());
        // Auch lesen, prüfen und verwerfen über LeagueHub: den Vorschlag gibt es dort nicht — wie vor 0.637.0.
        Assert.Empty((await accounts.SuggestionsAsync(Fide, default))["items"]!.AsArray());
        var leagueChecks = new LeagueAccountChecks(_db, new HttpClient(World()), null);
        Assert.Null(await leagueChecks.ForSuggestionAsync(s.Id, default));
        Assert.False(await accounts.RejectSuggestionAsync(s.Id, default));
        // Der Weg der Spielervorbereitung schaltet ihn ausdrücklich ein: Liste, Prüfung (mit Namen), Übernehmen.
        Assert.Single((await accounts.SuggestionsAsync(Fide, default, prep: true))["items"]!.AsArray());
        Assert.Equal("Prepmann, Paul", (await Search(World()).ChecksAsync(s.Id, default)).Result!.Player);
        Assert.Null((await Search(World()).AcceptAsync(s.Id, true, "verwalter", default)).Reason);
        var acc = Assert.Single(await _db.LeagueOnlineAccounts.ToListAsync());
        // Das übernommene Konto kann LeagueHub weder ändern, löschen, abholen lassen noch prüfen.
        Assert.Equal("notFound", (await accounts.UpdateAsync(acc.Id, new LeagueOnlineAccountService.Input(null, null, false, "x"), default)).Reason);
        Assert.False(await accounts.DeleteAsync(acc.Id, default));
        Assert.Null(await accounts.RequestSyncAsync(acc.Id, default));
        Assert.Null(await leagueChecks.ForAccountAsync(acc.Id, default));
        Assert.Single(await _db.LeagueOnlineAccounts.ToListAsync());
    }

    [Fact]
    public async Task LeagueHub_SamePlayerInTheLeague_EverythingAsBefore()
    {
        // Gegenrichtung: steht derselbe Spieler in einer Meldeliste, ist alles beim Alten — LeagueHub sieht, prüft, verwirft, ändert.
        await SeedPrepAsync();
        await SeedLeagueAsync();
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Prepmann, Paul", NameKey = "prepmann, paul", FideId = Fide, Fed = "GER" });
        await _db.SaveChangesAsync();
        var accounts = new LeagueOnlineAccountService(_db);
        await Search(World()).ScanAsync(await PrepIdAsync(), 1, default);
        var s = await _db.LeagueAccountSuggestions.OrderBy(x => x.Id).FirstAsync();
        Assert.NotEmpty((await accounts.SuggestionsAsync(Fide, default))["items"]!.AsArray());
        var leagueChecks = new LeagueAccountChecks(_db, new HttpClient(World()), null);
        Assert.Equal("Prepmann, Paul", (await leagueChecks.ForSuggestionAsync(s.Id, default))!.Player);
        var (acc, why) = await accounts.AcceptSuggestionAsync(s.Id, true, default);
        Assert.Null(why);
        Assert.Equal("notFound", (await accounts.UpdateAsync(acc!.Id + 1000, new LeagueOnlineAccountService.Input(null, null, false, "x"), default)).Reason);
        Assert.Null((await accounts.UpdateAsync(acc.Id, new LeagueOnlineAccountService.Input(null, null, false, "x"), default)).Reason);
        Assert.NotNull(await accounts.RequestSyncAsync(acc.Id, default));
        Assert.NotNull(await leagueChecks.ForAccountAsync(acc.Id, default));
        Assert.True(await accounts.DeleteAsync(acc.Id, default));
    }

    // ── Föderationen ohne Liga-Bezug ───────────────────────────────────────────────────────────

    /// <summary>Die 26 Föderationen der LeagueHub-Tabelle (LeagueAccountFinder.Fed2).</summary>
    private static readonly string[] LeagueFeds =
    {
        "AUT", "GER", "ITA", "SUI", "CZE", "UKR", "HUN", "SLO", "CRO", "BIH", "TUR", "FRA", "BUL", "MAR", "POL", "SVK", "SRB", "ROU",
        "RUS", "NED", "ESP", "ENG", "USA", "IRI", "SYR", "AFG",
    };

    [Fact]
    public void Federations_FullTableOnlyWithoutLeague_SameForTheKnown26()
    {
        foreach (var fed in LeagueFeds)
        {
            var league = LeagueAccountFinder.AllowedCountries(fed, null);                 // Liga: Österreich + Föderation
            league.Remove("AT");
            if (fed == "AUT") league.Add("AT");
            Assert.Equal(league.OrderBy(x => x), LeagueAccountFinder.AllowedCountries(null, fed, local: false).OrderBy(x => x));
        }
        static string One(string fed) => Assert.Single(LeagueAccountFinder.AllowedCountries(null, fed, local: false));
        Assert.Equal(("GB", "GB", "GB"), (One("ENG"), One("SCO"), One("WLS")));
        Assert.Equal(("NL", "CH", "DE", "NO", "IN", "CN", "AM", "XK"), (One("NED"), One("SUI"), One("GER"), One("NOR"), One("IND"), One("CHN"), One("ARM"), One("KOS")));
        Assert.Empty(LeagueAccountFinder.AllowedCountries(null, "FID", local: false));      // unter FIDE-Flagge: kein Land
        Assert.Empty(LeagueAccountFinder.AllowedCountries(null, "XYZ", local: false));      // unbekannt: streng
        Assert.Empty(LeagueAccountFinder.AllowedCountries(null, null, local: false));
        // LeagueHub bleibt, wie es war: NOR kennt seine Tabelle nicht.
        Assert.Equal(new[] { "AT" }, LeagueAccountFinder.AllowedCountries("NOR", "NOR").ToArray());
        Assert.True(PrepFederations.Iso.Count >= 190);
        Assert.All(PrepFederations.Iso, kv => Assert.Matches("^[A-Z]{3}$", kv.Key));
        Assert.All(PrepFederations.Iso, kv => Assert.Matches("^[A-Z]{2}$", kv.Value));
    }

    [Fact]
    public void Judge_NorwegianWithoutLeague_NorwegianProfileFits()
    {
        var p = new LeagueAccountFinder.Player("990333", "Hansen, Ola", null, 2400, null, Local: false);
        var prof = new LeagueAccountFinder.Profile("lichess", "OlaHansen", "u", "Ola Hansen", "NO", null, null, null, null, false);
        var v = LeagueAccountFinder.Judge(p, prof, derived: true, fideFed: "NOR");
        Assert.NotNull(v);
        Assert.Contains("Land NO", v!.Evidence);
        Assert.Null(LeagueAccountFinder.Judge(p, prof with { Flag = "SE" }, derived: true, fideFed: "NOR"));   // fremdes Land: nein
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
        Assert.Equal("notFound", (await search.ChecksAsync(s.Id, default)).Reason);
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
        Assert.Equal("notFound", (await search.ChecksAsync(leagues.Id, default)).Reason);

        var (checks, why) = await search.ChecksAsync(s.Id, default);
        Assert.Null(why);
        Assert.NotNull(checks);
        Assert.Equal("Prepmann, Paul", checks!.Player);                                   // der Spieler aus dem Bestand, nicht die FIDE-ID
        Assert.DoesNotContain(checks.Items, i => i.Key == "place");                       // ohne Liga-Bezug keine Orts-Prüfung
        Assert.Equal(LeagueAccountChecks.Ok, checks.Items.Single(i => i.Key == "country").Status);

        var (acc, reason) = await search.AcceptAsync(s.Id, true, "verwalter", default);
        Assert.Null(reason);
        Assert.Equal(("PaulPrepmann", "sicher"), (acc!["user"]!.GetValue<string>(), acc["conf"]!.GetValue<string>()));
        var row = await _db.LeagueOnlineAccounts.SingleAsync();
        Assert.Equal((Fide, "verwalter"), (row.FideId, row.AddedBy));
        Assert.Empty(await _db.LeagueAccountSuggestions.Where(x => x.FideId == Fide).ToListAsync());     // der Vorschlag ist erledigt
    }

    // ── Prüfung (i) durch den Türsteher ────────────────────────────────────────────────────────

    [Fact]
    public async Task Checks_OneAtATime_AndThrottledIsRateLimited()
    {
        await SeedPrepAsync();
        await Search(World()).ScanAsync(await PrepIdAsync(), 1, default);
        var s = await _db.LeagueAccountSuggestions.SingleAsync();

        var quiet = new FakeHttp(_ => Status(HttpStatusCode.InternalServerError));
        Assert.True(_gate.TryEnterOne());                                              // es sucht oder prüft gerade jemand
        Assert.Equal("busy", (await Search(quiet).ChecksAsync(s.Id, default)).Reason);
        Assert.Empty(quiet.Urls);                                                     // ohne einen Abruf
        _gate.Exit();

        var world = World();
        var slow = new FakeHttp(r => r.RequestUri!.ToString().EndsWith("/api/users") ? Status(HttpStatusCode.TooManyRequests) : world.Answer(r));
        var (res, reason) = await Search(slow).ChecksAsync(s.Id, default);
        Assert.Null(res);
        Assert.Equal("rateLimited", reason);                                          // kein halbes Ergebnis
        Assert.True(_gate.TryEnterOne());                                             // die Sperre ist wieder frei
        _gate.Exit();
        Assert.Equal(1, _gate.Remaining(1, 2, DateTime.UtcNow));                       // gezählt hat nur die eine Suche, keine der Prüfungen
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
        // Auch je Spieler sieht LeagueHub ihn nicht (0.638.0) — die Spielervorbereitung schon.
        Assert.Empty((await new LeagueOnlineAccountService(_db).SuggestionsAsync(Fide, default))["items"]!.AsArray());
        Assert.Single((await new LeagueOnlineAccountService(_db).SuggestionsAsync(Fide, default, prep: true))["items"]!.AsArray());
        var sources = await new LeagueGameSources(_db, null).GetAsync(TestClubs.HomeId, default, new[] { Fide, "990222" });
        Assert.Equal(1, sources["onlineTotal"]!.GetValue<int>());                      // nur die Partie des Ligaspielers
        Assert.Equal(1, sources["opponent"]!["onlineTotal"]!.GetValue<int>());         // auch mit seiner FIDE-ID in der Gegner-Liste
        Assert.Equal(1, sources["opponent"]!["onlineAccounts"]!.GetValue<int>());
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

        // Prüfung (i): belegt → 409, gedrosselt → 503.
        _db.LeagueAccountSuggestions.Add(new LeagueAccountSuggestion { FideId = Fide, Site = "lichess", UserName = "PaulPrepmann", Url = "u", Score = 5,
            Evidence = "e", Status = LeagueSuggestionStatus.Open, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        var sid = (await _db.LeagueAccountSuggestions.SingleAsync()).Id;
        Assert.True(_gate.TryEnterOne());
        Assert.IsType<ConflictObjectResult>(await c.SuggestionChecks(sid, Search(World()), default));
        _gate.Exit();
        Assert.Equal(503, Assert.IsType<ObjectResult>(await c.SuggestionChecks(sid, Search(slow), default)).StatusCode);
    }

    // ── Eingetragene Konten pflegen (0.639.0) ──────────────────────────────────────────────────

    /// <summary>Ein Konto mit <paramref name="games"/> geholten Partien.</summary>
    private async Task<LeagueOnlineAccount> AccountAsync(string user, bool sure = false, int games = 2, string fide = Fide)
    {
        var a = new LeagueOnlineAccount { FideId = fide, Site = "lichess", UserName = user, Url = "https://lichess.org/@/" + user,
            Confidence = sure ? LeagueOnlineAccountService.Sure : LeagueOnlineAccountService.Unsure, Evidence = "Vorschlag der Konto-Suche: 2 Punkte",
            Manual = true, GameCount = games };
        _db.LeagueOnlineAccounts.Add(a);
        await _db.SaveChangesAsync();
        for (var i = 0; i < games; i++)
            _db.LeagueOnlineGames.Add(new LeagueOnlineGame { AccountId = a.Id, FideId = fide, ExternalId = $"{user}-{i}", PlayedAt = DateTime.UtcNow,
                Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4" });
        await _db.SaveChangesAsync();
        return a;
    }

    [Fact]
    public async Task Accounts_PrepOnlyPlayer_Reclassify_AndRemove_WithItsGames_NotSuggestedAgain()
    {
        await SeedPrepAsync();
        var wrong = await AccountAsync("PaulPrepmann");                               // ein Fehlgriff: schwacher Treffer, übernommen
        var other = await AccountAsync("PaulOnline", sure: true, games: 3);
        var search = Search(World());
        var id = await PrepIdAsync();
        var list = (await search.SuggestionsAsync(id, 1, default)).Result!;
        Assert.False(list["leagueHub"]!.GetValue<bool>());
        Assert.Equal(new[] { "PaulPrepmann", "PaulOnline" }, list["accounts"]!.AsArray().Select(a => a!["user"]!.GetValue<string>()));

        // Umstufen: gesichert, Kommentar — die Partien bleiben.
        var (json, why) = await search.UpdateAccountAsync(wrong.Id, true, "Profil passt doch", default);
        Assert.Null(why);
        Assert.Equal(LeagueOnlineAccountService.Sure, json!["conf"]!.GetValue<string>());
        Assert.Equal("Profil passt doch", json["comment"]!.GetValue<string>());
        Assert.Equal(2, await _db.LeagueOnlineGames.CountAsync(g => g.AccountId == wrong.Id));
        Assert.Null((await search.UpdateAccountAsync(wrong.Id, false, null, default)).Reason);
        Assert.Equal("Profil passt doch", (await _db.LeagueOnlineAccounts.AsNoTracking().SingleAsync(a => a.Id == wrong.Id)).Evidence);

        // Über LeagueHub geht weiter nichts (0.638.0).
        var league = new LeagueOnlineAccountService(_db);
        Assert.Equal("notFound", (await league.UpdateAsync(wrong.Id, new LeagueOnlineAccountService.Input(null, null, true, null), default)).Reason);
        Assert.False(await league.DeleteAsync(wrong.Id, default));

        // Entfernen: das Konto und SEINE geholten Partien gehen, die des anderen bleiben; die Suche schlägt es nicht wieder vor.
        Assert.Null(await search.DeleteAccountAsync(wrong.Id, default));
        Assert.Equal(new[] { "PaulOnline" }, (await _db.LeagueOnlineAccounts.ToListAsync()).Select(a => a.UserName));
        Assert.Equal(0, await _db.LeagueOnlineGames.CountAsync(g => g.AccountId == wrong.Id));
        Assert.Equal(3, await _db.LeagueOnlineGames.CountAsync(g => g.AccountId == other.Id));
        var gone = await _db.LeagueAccountSuggestions.SingleAsync(x => x.UserName == "PaulPrepmann");
        Assert.Equal(LeagueSuggestionStatus.Rejected, gone.Status);
        var (scan, _) = await search.ScanAsync(id, 1, default);
        Assert.DoesNotContain(scan!["items"]!.AsArray(), i => i!["user"]!.GetValue<string>() == "PaulPrepmann");
        Assert.Equal("notFound", await search.DeleteAccountAsync(wrong.Id, default));             // schon weg
    }

    [Fact]
    public async Task Accounts_AlsoLeaguePlayer_PrepRefuses_LeagueHubKeepsCaring()
    {
        await SeedPrepAsync();
        await SeedLeagueAsync();
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Prepmann, Paul", NameKey = "prepmann, paul", FideId = Fide, Fed = "GER" });
        await _db.SaveChangesAsync();
        var acc = await AccountAsync("PaulPrepmann");
        var search = Search(World());
        var list = (await search.SuggestionsAsync(await PrepIdAsync(), 1, default)).Result!;
        Assert.True(list["leagueHub"]!.GetValue<bool>());                                // ansehen ja, pflegen in LeagueHub
        Assert.Single(list["accounts"]!.AsArray());
        Assert.Equal("leagueHub", (await search.UpdateAccountAsync(acc.Id, true, null, default)).Reason);
        Assert.Equal("leagueHub", await search.DeleteAccountAsync(acc.Id, default));
        var kept = await _db.LeagueOnlineAccounts.AsNoTracking().SingleAsync();
        Assert.Equal(LeagueOnlineAccountService.Unsure, kept.Confidence);
        Assert.Equal(2, await _db.LeagueOnlineGames.CountAsync());
        // LeagueHub pflegt es wie bisher.
        var league = new LeagueOnlineAccountService(_db);
        Assert.Null((await league.UpdateAsync(acc.Id, new LeagueOnlineAccountService.Input(null, null, true, null), default)).Reason);
        Assert.True(await league.DeleteAsync(acc.Id, default));
    }

    [Fact]
    public async Task Accounts_Minor_NeverThroughPrep()
    {
        await SeedPrepAsync();
        _db.LeagueAccountScans.Add(new LeagueAccountScan { FideId = Fide, BirthYear = DateTime.UtcNow.Year - 12, ScannedAt = DateTime.UtcNow, Version = 7 });
        await _db.SaveChangesAsync();
        var acc = await AccountAsync("PaulPrepmann");
        var search = Search(World());
        var list = (await search.SuggestionsAsync(await PrepIdAsync(), 1, default)).Result!;
        Assert.Empty(list["accounts"]!.AsArray());
        Assert.DoesNotContain("PaulPrepmann", list.ToJsonString());
        Assert.Equal("notFound", (await search.UpdateAccountAsync(acc.Id, true, null, default)).Reason);
        Assert.Equal("notFound", await search.DeleteAccountAsync(acc.Id, default));
        Assert.Single(await _db.LeagueOnlineAccounts.ToListAsync());
        Assert.Equal(2, await _db.LeagueOnlineGames.CountAsync());
    }

    [Fact]
    public async Task Accounts_Controller_StatusCodes_AndSwitch()
    {
        await SeedPrepAsync();
        var acc = await AccountAsync("PaulPrepmann");
        var c = Controller();
        var off = Search(World(), new ConfigurationBuilder().Build());
        Assert.IsType<NotFoundObjectResult>(await c.UpdateAccount(acc.Id, new(true, null), off, default));
        Assert.IsType<NotFoundObjectResult>(await c.DeleteAccount(acc.Id, off, default));
        Assert.IsType<OkObjectResult>(await c.UpdateAccount(acc.Id, new(true, null), Search(World()), default));
        Assert.IsType<NotFoundObjectResult>(await c.UpdateAccount(acc.Id + 1000, new(true, null), Search(World()), default));
        Assert.IsType<NoContentResult>(await c.DeleteAccount(acc.Id, Search(World()), default));
        Assert.IsType<NotFoundObjectResult>(await c.DeleteAccount(acc.Id, Search(World()), default));
        await SeedLeagueAsync();
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Prepmann, Paul", NameKey = "prepmann, paul", FideId = Fide, Fed = "GER" });
        await _db.SaveChangesAsync();
        var leagueAcc = await AccountAsync("PaulOnline");
        Assert.IsType<ConflictObjectResult>(await c.UpdateAccount(leagueAcc.Id, new(true, null), Search(World()), default));
        Assert.IsType<ConflictObjectResult>(await c.DeleteAccount(leagueAcc.Id, Search(World()), default));
    }
}

