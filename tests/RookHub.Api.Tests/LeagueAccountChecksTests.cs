using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Konto-Prüfung (i) (0.619.0): die einzelnen Prüfungen mit ihren Grenzen (Online-Wertung 100–300 über der Elo = passend,
/// Repertoire-Anteil, Name, Nutzername, Land), der ganze Weg für ein eingetragenes Konto und einen Vorschlag (Selbstmeldung,
/// Online-TMM 2021, Repertoire aus gespeicherten bzw. geholten Partien, anderer Spieler) und das Einspielen der Selbstmeldungen.
/// </summary>
public class LeagueAccountChecksTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private static readonly LeagueAccountFinder.Player Max = new("222", "Muster, Max", "AUT", 1900, "Kufstein 1");

    private static LeagueAccountFinder.Profile Prof(string? real = null, string? flag = null, string? loc = null, int? fide = null,
        IReadOnlyList<LeagueAccountFinder.Rating>? ratings = null, DateTime? seen = null, bool closed = false) =>
        new("lichess", "MaxMuster", "https://lichess.org/@/MaxMuster", real, flag, loc, null, fide, seen, closed, Ratings: ratings);

    private static string Status(LeagueAccountFinder.Rating r, int? elo = 1900) => LeagueAccountChecks.RatingCheck(r, elo).Status;

    // ── Einzelne Prüfungen ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rating_100To300AboveTheElo_IsOptimal_ElseAWeakerHit_FarBelowExcludes()
    {
        LeagueAccountFinder.Rating R(int v, int games = 50, bool reliable = true) => new("Lichess Blitz", v, games, reliable);
        Assert.Equal(LeagueAccountChecks.Ok, Status(R(2000)));                 // +100
        Assert.Equal(LeagueAccountChecks.Ok, Status(R(2100)));                 // +200, der Normalfall
        Assert.Equal(LeagueAccountChecks.Ok, Status(R(2200)));                 // +300
        // Wunsch: „das Band zeigt die optimalen Treffer, 400 unter FIDE schließt aus, alles andere ist halt Treffer, aber schwächer".
        Assert.Equal(LeagueAccountChecks.Weak, Status(R(2201)));               // höher als das Band
        Assert.Equal(LeagueAccountChecks.Weak, Status(R(1950)));               // knapp darüber
        Assert.Equal(LeagueAccountChecks.Weak, Status(R(1500)));               // darunter, aber noch in der Grenze der Suche
        Assert.Equal(LeagueAccountChecks.Fail, Status(R(1499)));               // mehr als 400 darunter
        Assert.Equal(LeagueAccountChecks.None, Status(R(1200, 3, reliable: false)));   // zählt nicht
        Assert.Equal(LeagueAccountChecks.Info, Status(R(2100), elo: null));
        Assert.Equal("2100 (50 Partien) — 200 über der Elo 1900: optimal (100–300 darüber)", LeagueAccountChecks.RatingCheck(R(2100), 1900).Text);
        Assert.Equal("2500 (50 Partien) — 600 über der Elo 1900: Treffer, aber schwächer — optimal wären 100–300 darüber",
            LeagueAccountChecks.RatingCheck(R(2500), 1900).Text);
        Assert.Equal("1400 (1.234 Partien) — 500 unter der Elo 1900: mehr als 400 darunter — schließt ihn aus",
            LeagueAccountChecks.RatingCheck(R(1400, 1234), 1900).Text);
        // Je Kategorie eine Zeile; ohne Wertungen eine Zeile „keine Wertung".
        var rows = LeagueAccountChecks.RatingChecks(Prof(ratings: new[] { R(2100), new LeagueAccountFinder.Rating("Lichess Schnell", 2050, 20, true) }), 1900);
        Assert.Equal(new[] { "rating:Lichess Blitz", "rating:Lichess Schnell" }, rows.Select(x => x.Key));
        Assert.Equal(LeagueAccountChecks.None, Assert.Single(LeagueAccountChecks.RatingChecks(Prof(), 1900)).Status);
    }

    [Fact]
    public void Name_FullInitialLastOnlyOtherFirstOtherLastNone()
    {
        string S(string? real) => LeagueAccountChecks.NameCheck(Max, Prof(real)).Status;
        Assert.Equal(LeagueAccountChecks.Ok, S("Max Muster"));
        Assert.Equal(LeagueAccountChecks.Weak, S("M. Muster"));
        Assert.Equal(LeagueAccountChecks.Weak, S("Muster"));
        Assert.Equal(LeagueAccountChecks.Fail, S("Moritz Muster"));
        Assert.Equal(LeagueAccountChecks.Fail, S("Max Mustermann"));
        Assert.Equal(LeagueAccountChecks.None, S(null));
        Assert.Equal("„Max Muster“ — Vor- und Nachname passen", LeagueAccountChecks.NameCheck(Max, Prof("Max Muster")).Text);
    }

    [Fact]
    public void UserName_FromTheName_OtherFirstName_ContainsLastName_Unrelated()
    {
        var firsts = new HashSet<string> { "max", "moritz" };
        string S(string user) => LeagueAccountChecks.UserNameCheck(Max, user, firsts).Status;
        Assert.Equal(LeagueAccountChecks.Ok, S("max_muster"));
        Assert.Equal(LeagueAccountChecks.Fail, S("MoritzMuster"));
        Assert.Equal(LeagueAccountChecks.Weak, S("Muster1987"));
        Assert.Equal(LeagueAccountChecks.None, S("Katzenpapa"));
    }

    [Fact]
    public void Country_AustriaOrFederation_ElseFails()
    {
        string S(string? flag, string? fideFed = null) => LeagueAccountChecks.CountryCheck(Max, Prof(flag: flag), fideFed).Status;
        Assert.Equal(LeagueAccountChecks.Ok, S("AT"));
        Assert.Equal(LeagueAccountChecks.Ok, S("DE", "GER"));                  // Föderation laut FIDE
        Assert.Equal(LeagueAccountChecks.Fail, S("IT"));
        Assert.Equal(LeagueAccountChecks.None, S(null));
        Assert.Equal("IT — weder Österreich noch seine Föderation (AUT)", LeagueAccountChecks.CountryCheck(Max, Prof(flag: "IT"), null).Text);
    }

    [Fact]
    public void Repertoire_ShareOfGamesFollowingThreeOwnMoves()
    {
        var r = new LeagueFingerprint.Repertoire();
        r.Add("e4 e5 Nf3 Nc6 Bb5 a6".Split(' '), white: true);
        r.Add("e4 c5 Nf3 d6 d4 cxd4".Split(' '), white: false);
        var games = new List<(IReadOnlyList<string>, bool)>
        {
            ("e4 e5 Nf3 Nc6 Bb5 Nf6".Split(' '), true),        // 3 eigene Züge (Halbzug 5) gemeinsam
            ("e4 e5 Nf3 Nc6 Bc4 Bc5".Split(' '), true),        // nur 2 (Halbzug 3)
            ("e4 c5 Nf3 d6 d4 cxd4".Split(' '), false),        // Schwarz: 3 eigene Züge = Halbzug 6
            ("d4 d5".Split(' '), true),                         // nichts
        };
        Assert.Equal((4, 2, (5 + 3 + 6 + 0) / 4.0), LeagueFingerprint.Coverage(games, r));

        Assert.Equal(LeagueAccountChecks.Ok, LeagueAccountChecks.RepertoireItem((100, 35, 6.1), 40).Status);
        Assert.Equal(LeagueAccountChecks.Weak, LeagueAccountChecks.RepertoireItem((100, 10, 3.0), 40).Status);
        Assert.Equal(LeagueAccountChecks.Warn, LeagueAccountChecks.RepertoireItem((100, 9, 1.0), 40).Status);
        Assert.Equal(LeagueAccountChecks.None, LeagueAccountChecks.RepertoireItem((9, 9, 9.0), 40).Status);
        Assert.Equal("58 % seiner letzten 100 Online-Partien folgen mindestens 3 eigene Züge weit einer Stellung aus seinen 40 Brettpartien "
                     + "(gemeinsam im Schnitt bis Halbzug 7,2)", LeagueAccountChecks.RepertoireItem((100, 58, 7.2), 40).Text);
    }

    [Fact]
    public void EventSeries_DropsRoundAndTeamBattle()
    {
        Assert.Equal("Online TMM 2021", LeagueTeamScout.EventSeries("Online TMM 2021 Runde 3 Team Battle"));
        Assert.Equal("Lichess Quarantäne-Liga 7C", LeagueTeamScout.EventSeries("Lichess Quarantäne-Liga 7C Team Battle"));
        Assert.Equal("Tiroler Blitz", LeagueTeamScout.EventSeries("Tiroler Blitz Round 12"));
        Assert.Null(LeagueTeamScout.EventSeries("  "));
    }

    // ── Der ganze Weg ──────────────────────────────────────────────────────────────────────────

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();
        public HttpResponseMessage Answer(HttpRequestMessage r) => answer(r);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };

    private const string Ruy = "e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7";

    private static string LichessGames(string user, int n) => string.Join("\n", Enumerable.Range(0, n).Select(i =>
        "{\"id\":\"g" + i + "\",\"rated\":true,\"variant\":\"standard\",\"speed\":\"blitz\",\"createdAt\":" + (1_700_000_000_000L + i * 60_000)
        + ",\"status\":\"mate\",\"winner\":\"white\",\"players\":{\"white\":{\"user\":{\"name\":\"" + user + "\",\"id\":\"" + user.ToLowerInvariant()
        + "\"},\"rating\":2100},\"black\":{\"user\":{\"name\":\"Opp\",\"id\":\"opp\"},\"rating\":2000}},\"moves\":\"" + Ruy + "\"}"));

    private static FakeHttp World() => new(req =>
    {
        var u = req.RequestUri!.ToString();
        if (u.EndsWith("/api/users"))
            return Ok("[{\"id\":\"maxmuster\",\"username\":\"MaxMuster\",\"seenAt\":1790000000000,"
                      + "\"profile\":{\"flag\":\"AT\",\"realName\":\"Max Muster\",\"location\":\"Kufstein\"},"
                      + "\"perfs\":{\"blitz\":{\"games\":300,\"rating\":2100},\"bullet\":{\"games\":4,\"rating\":1600,\"prov\":true},"
                      + "\"rapid\":{\"games\":0,\"rating\":1500}}},"
                      + "{\"id\":\"trigonias\",\"username\":\"Trigonias\",\"perfs\":{\"rapid\":{\"games\":80,\"rating\":2150}}}]");
        if (u.Contains("/api/games/user/Trigonias")) return Ok(LichessGames("Trigonias", 30));
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
    });

    private LeagueAccountChecks Checks(FakeHttp http) =>
        new(_db, new HttpClient(http), new MemoryCache(new MemoryCacheOptions()),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LeagueOnline:TeamPlaces"] = "Kufstein" }).Build());

    private static string Pgn(string white, string whiteFide, string line, int year) =>
        $"[Event \"Liga\"]\n[Date \"{year}.01.01\"]\n[White \"{white}\"]\n[Black \"Gegner, X\"]\n[WhiteFideId \"{whiteFide}\"]\n[Result \"1-0\"]\n\n"
        + string.Join(" ", line.Split(' ').Select((m, i) => i % 2 == 0 ? $"{i / 2 + 1}. {m}" : m)) + " 1-0\n\n";

    private async Task<LeagueOnlineAccount> SeedAsync()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Name = "Landesliga", Season = "2026/27", League = "LL", Stage = "x" });
        _db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Muster, Max", NameKey = "muster, max", FideId = "222", Fed = "AUT", EloI = 1900 },
            new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Kind, Klein", NameKey = "kind, klein", FideId = "555", Fed = "AUT", EloI = 1200 },
            new LeaguePlayer { Tnr = 1, Team = "Innsbruck 1", Name = "Huber, Franz", NameKey = "huber, franz", FideId = "333", Fed = "AUT", EloI = 1800 });
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile
        {
            FideId = "222", Name = "Muster, Max", Pgn = string.Concat(Enumerable.Range(0, 6).Select(i => Pgn("Muster, Max", "222", Ruy, 2020 + i))),
        });
        _db.LeagueAccountScans.Add(new LeagueAccountScan { FideId = "555", BirthYear = DateTime.UtcNow.Year - 12 });
        var acc = new LeagueOnlineAccount
        {
            FideId = "222", Site = "lichess", UserName = "MaxMuster", Url = "https://lichess.org/@/MaxMuster", Confidence = "sicher", Manual = true,
            SyncedAt = DateTime.UtcNow,
        };
        _db.LeagueOnlineAccounts.Add(acc);
        await _db.SaveChangesAsync();
        _db.LeagueOnlineGames.AddRange(Enumerable.Range(0, 20).Select(i => new LeagueOnlineGame
        {
            AccountId = acc.Id, FideId = "222", ExternalId = "x" + i, PlayedAt = DateTime.UtcNow.AddDays(-i), Speed = "blitz", Rated = true,
            White = true, Result = "1-0", Line = i < 12 ? Ruy : "d4 d5 c4 e6", Moves = i < 12 ? Ruy : "d4 d5 c4 e6", Plies = 10,
        }));
        _db.LeagueScoutAccounts.Add(new LeagueScoutAccount
        {
            UserName = "maxmuster", DisplayName = "MaxMuster", Teams = "SK Kufstein", PlayedFor = "SK Kufstein",
            Events = "Online TMM 2021; Lichess Quarantäne-Liga 7C", FoundAt = DateTime.UtcNow,
        });
        _db.LeagueSelfReports.Add(new LeagueSelfReport
        {
            FideId = "222", Site = "lichess", UserName = "maxmuster", Source = "Meldeliste Online-TMM 2021", Team = "SK Kufstein", CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return acc;
    }

    private static LeagueAccountChecks.Item Of(LeagueAccountChecks.Result r, string key) => r.Items.Single(i => i.Key == key);

    [Fact]
    public async Task Account_AllChecks_FromProfileStoredGamesSelfReportAndTeamBattles()
    {
        var acc = await SeedAsync();
        var http = World();
        var r = (await Checks(http).ForAccountAsync(acc.Id, default))!;

        Assert.True(r.ProfileLoaded);
        Assert.Equal(("Muster, Max", (int?)1900, "https://lichess.org/@/MaxMuster"), (r.Player, r.Elo, r.Url));
        // Reihenfolge wie im Wunsch: Selbstmeldung, TMM 2021, Name, Land, Repertoire, Elo …
        Assert.Equal(new[] { "self", "tmm2021", "name", "username", "country", "repertoire", "rating:Lichess Bullet", "rating:Lichess Blitz" },
            r.Items.Take(8).Select(i => i.Key));
        Assert.Equal((LeagueAccountChecks.Ok, "selbst gemeldet (Meldeliste Online-TMM 2021, für „SK Kufstein“)"), (Of(r, "self").Status, Of(r, "self").Text));
        Assert.Equal((LeagueAccountChecks.Ok, "spielte in der Online-TMM 2021, für „SK Kufstein“ — seinen Verein"),
            (Of(r, "tmm2021").Status, Of(r, "tmm2021").Text));
        Assert.Equal(LeagueAccountChecks.Ok, Of(r, "name").Status);
        Assert.Equal(LeagueAccountChecks.Ok, Of(r, "username").Status);
        Assert.Equal(LeagueAccountChecks.Ok, Of(r, "country").Status);
        Assert.Equal(LeagueAccountChecks.Ok, Of(r, "repertoire").Status);                      // 12 von 20 = 60 %
        Assert.StartsWith("60 % seiner letzten 20 Online-Partien", Of(r, "repertoire").Text);
        Assert.Equal(LeagueAccountChecks.None, Of(r, "rating:Lichess Bullet").Status);          // vorläufig
        Assert.Equal(LeagueAccountChecks.Ok, Of(r, "rating:Lichess Blitz").Status);            // +200
        Assert.DoesNotContain(r.Items, i => i.Key == "rating:Lichess Schnell");                // nie gespielt
        Assert.Equal((LeagueAccountChecks.Ok, "„Kufstein“"), (Of(r, "tirol").Status, Of(r, "tirol").Text));
        Assert.Equal(LeagueAccountChecks.Ok, Of(r, "teams").Status);
        Assert.Contains("Lichess Quarantäne-Liga 7C", Of(r, "teams").Text);
        Assert.Equal(LeagueAccountChecks.Ok, Of(r, "closed").Status);
        Assert.Equal(LeagueAccountChecks.Ok, Of(r, "elsewhere").Status);
        // Gleicher Name auf chess.com: gibt es hier nicht.
        Assert.Equal((LeagueAccountChecks.None, "kein Konto „MaxMuster“"), (Of(r, "twin").Status, Of(r, "twin").Text));
        Assert.Equal("Gleicher Name auf chess.com", Of(r, "twin").Label);
        // Die Partien des Kontos liegen schon da — geholt werden nur das Profil und der gleiche Name auf der anderen Seite.
        Assert.Equal(new[] { "https://lichess.org/api/users", "https://api.chess.com/pub/player/maxmuster" }, http.Urls);

        // Zweimal aufklappen = einmal fragen.
        await Checks(http).ForAccountAsync(acc.Id, default);
        Assert.Equal(4, http.Urls.Count);                                                      // neuer Dienst = neuer Speicher
        var checks = Checks(http);
        await checks.ForAccountAsync(acc.Id, default);
        await checks.ForAccountAsync(acc.Id, default);
        Assert.Equal(6, http.Urls.Count);
    }

    [Fact]
    public async Task Twin_ExistsAndFits_OrExistsButDoesNotFit_OrIsAlreadyEntered()
    {
        var acc = await SeedAsync();
        FakeHttp With(string chessComBody) => new(req =>
        {
            var u = req.RequestUri!.ToString();
            if (u.EndsWith("/pub/player/maxmuster")) return Ok(chessComBody);
            return World().Answer(req);
        });
        var fits = (await Checks(With("""{"username":"maxmuster","name":"Max Muster","country":"https://api.chess.com/pub/country/AT"}"""))
            .ForAccountAsync(acc.Id, default))!;
        Assert.Equal((LeagueAccountChecks.Ok, "„maxmuster“ gibt es und es passt: Klarname im Profil („Max Muster“); Land Österreich"),
            (Of(fits, "twin").Status, Of(fits, "twin").Text));
        var other = (await Checks(With("""{"username":"maxmuster","name":"Moritz Muster","country":"https://api.chess.com/pub/country/AT"}"""))
            .ForAccountAsync(acc.Id, default))!;
        Assert.Equal(LeagueAccountChecks.Info, Of(other, "twin").Status);
        _db.LeagueOnlineAccounts.Add(new LeagueOnlineAccount { FideId = "222", Site = "chess.com", UserName = "MaxMuster", Url = "u", Confidence = "sicher" });
        await _db.SaveChangesAsync();
        var entered = (await Checks(With("x")).ForAccountAsync(acc.Id, default))!;
        Assert.Equal((LeagueAccountChecks.Ok, "„MaxMuster“ ist dort auch als sein Konto eingetragen"), (Of(entered, "twin").Status, Of(entered, "twin").Text));
    }

    [Fact]
    public async Task Account_ReportedByAnotherPlayer_AndEnteredElsewhere_Fails_MinorsAreNotNamed()
    {
        var acc = await SeedAsync();
        _db.LeagueSelfReports.RemoveRange(_db.LeagueSelfReports);
        _db.LeagueSelfReports.Add(new LeagueSelfReport { FideId = "555", Site = "lichess", UserName = "MAXMUSTER", Source = "Liste", CreatedAt = DateTime.UtcNow });
        _db.LeagueOnlineAccounts.Add(new LeagueOnlineAccount { FideId = "333", Site = "lichess", UserName = "maxmuster", Url = "u", Confidence = "wahrscheinlich" });
        await _db.SaveChangesAsync();
        var r = (await Checks(World()).ForAccountAsync(acc.Id, default))!;
        Assert.Equal((LeagueAccountChecks.Fail, "Liste: gemeldet von einem anderen Spieler — nicht von ihm"), (Of(r, "self").Status, Of(r, "self").Text));
        Assert.Equal((LeagueAccountChecks.Fail, "auch eingetragen bei Huber, Franz"), (Of(r, "elsewhere").Status, Of(r, "elsewhere").Text));
    }

    [Fact]
    public async Task HiddenAccount_IsNotChecked()
    {
        await SeedAsync();
        var kid = new LeagueOnlineAccount { FideId = "555", Site = "lichess", UserName = "KleinKind", Url = "u", Confidence = "sicher" };
        _db.LeagueOnlineAccounts.Add(kid);
        var sugg = new LeagueAccountSuggestion { FideId = "555", Site = "lichess", UserName = "KleinKind", Url = "u", Evidence = "x", CreatedAt = DateTime.UtcNow };
        _db.LeagueAccountSuggestions.Add(sugg);
        await _db.SaveChangesAsync();
        var http = World();
        Assert.Null(await Checks(http).ForAccountAsync(kid.Id, default));
        Assert.Null(await Checks(http).ForSuggestionAsync(sugg.Id, default));
        Assert.Null(await Checks(http).ForAccountAsync(9999, default));
        Assert.Empty(http.Urls);
    }

    [Fact]
    public async Task Suggestion_FetchesGames_ShowsEvidence_WithoutProfileSaysNotChecked()
    {
        await SeedAsync();
        var sugg = new LeagueAccountSuggestion
        {
            FideId = "222", Site = "lichess", UserName = "Trigonias", Url = "https://lichess.org/@/Trigonias", Evidence = "spielte für „SK Kufstein“",
            Status = LeagueSuggestionStatus.Open, CreatedAt = DateTime.UtcNow, Source = LeagueTeamScout.Source,
        };
        _db.LeagueAccountSuggestions.Add(sugg);
        await _db.SaveChangesAsync();
        var http = World();
        var r = (await Checks(http).ForSuggestionAsync(sugg.Id, default))!;
        Assert.Equal(LeagueAccountChecks.Ok, Of(r, "repertoire").Status);
        Assert.StartsWith("100 % seiner letzten 30 Online-Partien", Of(r, "repertoire").Text);    // alle 30 Spanisch, geholt
        Assert.Contains(http.Urls, x => x.Contains("/api/games/user/Trigonias"));
        // Gemeldet hat er ein ANDERES Lichess-Konto — das macht stutzig.
        Assert.Equal((LeagueAccountChecks.Warn, "selbst gemeldet hat er ein anderes Lichess-Konto („maxmuster“, Meldeliste Online-TMM 2021)"),
            (Of(r, "self").Status, Of(r, "self").Text));
        Assert.Equal(LeagueAccountChecks.None, Of(r, "tmm2021").Status);                        // nicht im Bestand der Team-Suche
        Assert.Equal(LeagueAccountChecks.None, Of(r, "name").Status);                           // kein Klarname
        Assert.Equal(LeagueAccountChecks.Ok, Of(r, "rating:Lichess Schnell").Status);           // 2150 = 250 über 1900
        Assert.Equal("spielte für „SK Kufstein“", Of(r, "evidence").Text);

        // Seite nicht erreichbar: die Profil-Prüfungen sagen es, der Rest steht trotzdem da.
        var down = new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("") });
        var d = (await Checks(down).ForSuggestionAsync(sugg.Id, default))!;
        Assert.False(d.ProfileLoaded);
        Assert.Equal((LeagueAccountChecks.None, "nicht geprüft — Lichess nicht erreichbar"), (Of(d, "name").Status, Of(d, "name").Text));
        Assert.Equal((LeagueAccountChecks.None, "Partien gerade nicht abrufbar"), (Of(d, "repertoire").Status, Of(d, "repertoire").Text));
        Assert.Equal(LeagueAccountChecks.None, Of(d, "username").Status);                      // braucht kein Profil
    }

    // ── Selbstmeldungen einspielen ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelfReports_ReplaceTheSource_DryRunWritesNothing_SkipsUnknownAndInvalid()
    {
        await SeedAsync();                                                                       // eine Meldung „maxmuster" liegt schon
        _db.LeagueSelfReports.Add(new LeagueSelfReport { FideId = "333", Site = "lichess", UserName = "alt", Source = "Meldeliste Online-TMM 2021", CreatedAt = DateTime.UtcNow });
        _db.LeagueSelfReports.Add(new LeagueSelfReport { FideId = "333", Site = "lichess", UserName = "andere", Source = "Andere Quelle", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        var req = new LeagueSelfReportImport.Request("Meldeliste Online-TMM 2021", new()
        {
            new("222", "lichess", "MaxMuster", "SK Kufstein 1"),                     // geändert: Schreibweise + Team
            new("333", "Lichess", "https://lichess.org/@/FranzH", null),              // neu, Profiladresse
            new("999", "lichess", "Fremd", null),                                     // unbekannter Spieler
            new("333", "myspace", "x", null),                                         // unlesbar
            new("333", "lichess", "franzh", null),                                    // doppelt
            new("abc", "lichess", "y", null),
        });
        var (dry, _) = await LeagueSelfReportImport.ImportAsync(_db, req, dryRun: true, default);
        Assert.Equal((1, 1, 0, 1, true), (dry!.Added, dry.Updated, dry.Unchanged, dry.Removed, dry.DryRun));
        Assert.Equal(new[] { (2, "unknownPlayer"), (3, "invalidUser"), (4, "duplicate"), (5, "invalidFide") }, dry.Skipped.Select(p => (p.Index, p.Reason)));
        Assert.Equal(3, await _db.LeagueSelfReports.CountAsync());                             // nichts geschrieben

        var (real, _) = await LeagueSelfReportImport.ImportAsync(_db, req, dryRun: false, default);
        Assert.Equal((1, 1, 0, 1), (real!.Added, real.Updated, real.Unchanged, real.Removed));
        var rows = await _db.LeagueSelfReports.OrderBy(r => r.Source).ThenBy(r => r.UserName).Select(r => $"{r.Source}|{r.FideId}|{r.UserName}|{r.Team}").ToListAsync();
        Assert.Equal(new[] { "Andere Quelle|333|andere|", "Meldeliste Online-TMM 2021|333|FranzH|", "Meldeliste Online-TMM 2021|222|MaxMuster|SK Kufstein 1" }, rows);

        var (again, _) = await LeagueSelfReportImport.ImportAsync(_db, req, dryRun: false, default);
        Assert.Equal((0, 0, 2, 0), (again!.Added, again.Updated, again.Unchanged, again.Removed));
        Assert.Equal("noSource", (await LeagueSelfReportImport.ImportAsync(_db, new(" ", new()), false, default)).Reason);
    }
}
