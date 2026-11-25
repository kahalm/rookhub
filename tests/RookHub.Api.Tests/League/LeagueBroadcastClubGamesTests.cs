using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Club;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Tests.League;

/// <summary>
/// Live übertragene Ligapartien sofort einspielen (1.017.0, Wunsch 2026-11-25): laufende Übertragungen alle 10 min, fertige Partien
/// in die Spielerkarten und — mit der eigenen Mannschaft — in die Vereins-Datenbank, mit echten Namen, Lc0 und genau einer
/// Zusammenfassung. Die Lichess-Antworten sind der Bundesliga-Übertragung vom 25.11.2026 nachgebaut (<c>WhiteTeam</c>
/// „Schachklub Schwaz“, <c>Round</c> „1.2“). Alle Ids im 99xxxx-Bereich, jeder Test mit eigener Datenbank.
/// </summary>
public class LeagueBroadcastClubGamesTests : IDisposable
{
    private const int ClubId = 990001;
    private const int Tnr = 990010;
    private const string OwnFide = "990101", OppFide = "990102";
    private const string Tour = "IjDgZV99";
    private static readonly DateTime Now = new(2026, 11, 25, 16, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private static readonly LeagueClub Schwaz = new() { Id = ClubId, Name = "SK Schwaz", TeamPrefix = "Schwaz", AnonName = "Schwaz", Region = LeagueRegions.Tirol };

    private const string Moves = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5";
    private const string OtherTail = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. g4 b5 11. O-O-O Nbd7";

    private static string Game(string result = "1-0", string date = "2026.11.25", string whiteTeam = "Schachklub Schwaz",
        string blackTeam = "Pinggau-Friedberg", string url = "https://lichess.org/broadcast/bl/round-1/L3MX2o99/1pPEJ999",
        string white = "Schmider, Niklas", string black = "Vaibhav, Suri", string whiteFide = OwnFide, string blackFide = OppFide,
        string moves = Moves) =>
        $"[Event \"1. Bundesliga AUT 2026/2027\"]\n[Site \"Mayrhofen, Austria\"]\n[Date \"{date}\"]\n[Round \"1.2\"]\n"
        + $"[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"{result}\"]\n[WhiteElo \"2398\"]\n[WhiteTitle \"IM\"]\n"
        + (whiteTeam.Length > 0 ? $"[WhiteTeam \"{whiteTeam}\"]\n" : "") + $"[WhiteFideId \"{whiteFide}\"]\n[BlackElo \"2569\"]\n[BlackTitle \"GM\"]\n"
        + (blackTeam.Length > 0 ? $"[BlackTeam \"{blackTeam}\"]\n" : "") + $"[BlackFideId \"{blackFide}\"]\n[Variant \"Standard\"]\n"
        + $"[GameURL \"{url}\"]\n\n{moves} {{ [%clk 1:30:54] }} {result}\n\n";

    private static string Detail(string rounds = "[{\"id\":\"r1\",\"name\":\"Round 1\",\"ongoing\":true,\"startsAt\":1795611600000}]") =>
        "{\"tour\":{\"id\":\"" + Tour + "\",\"name\":\"1. Bundesliga Austria 2026/2027\",\"dates\":[1795611600000,1805014800000]},\"rounds\":" + rounds + "}";

    private sealed class FakeHttp(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(answer(request.RequestUri!.ToString()));
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };

    private LeagueClubService Clubs() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private LeagueBroadcastImport Importer(FakeHttp http) =>
        new(_db, new HttpClient(http), NullLogger<LeagueBroadcastImport>.Instance, clubs: Clubs()) { Pause = TimeSpan.Zero };

    private static FakeHttp Http(string pgn, string? detail = null) => new(u => u.EndsWith(".pgn") ? Ok(pgn) : Ok(detail ?? Detail()));

    /// <summary>Bundesliga 2026/27 (laufende Saison) mit Meldeliste: Schmider für „Schachklub Schwaz“, Vaibhav für Pinggau.</summary>
    private async Task<LeagueBroadcast> SeedAsync(bool pairing = false)
    {
        _db.LeagueClubs.Add(Schwaz);
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = Tnr, Season = "2026/27", League = "1. Bundesliga", Stage = "Liga" });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = Tnr, Team = "Schachklub Schwaz", Name = "Schmider, Niklas", NameKey = LeagueNames.NameKey("Schmider, Niklas"), FideId = OwnFide });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = Tnr, Team = "Pinggau-Friedberg", Name = "Vaibhav, Suri", NameKey = LeagueNames.NameKey("Vaibhav, Suri"), FideId = OppFide });
        if (pairing)
        {
            _db.LeagueRounds.Add(new LeagueRound { Tnr = Tnr, Round = 1, Date = new DateOnly(2026, 11, 25) });
            _db.LeagueGames.Add(new LeagueGame { Tnr = Tnr, Round = 1, Board = 2, HomeTeam = "Schachklub Schwaz", AwayTeam = "Pinggau-Friedberg",
                HomePlayer = "Schmider, Niklas", AwayPlayer = "Vaibhav, Suri", HomeColor = "w", Result = "1 - 0", HomeFide = OwnFide, AwayFide = OppFide });
        }
        var b = new LeagueBroadcast { TourId = Tour, Name = "1. Bundesliga Austria 2026/2027", FoundAt = Now.AddDays(-14) };
        _db.LeagueBroadcasts.Add(b);
        await _db.SaveChangesAsync();
        return b;
    }

    private async Task ImportAsync(LeagueBroadcast b, string pgn)
    {
        await Importer(Http(pgn)).ImportAsync(b, default);
        _db.ChangeTracker.Clear();
    }

    // ── Takt ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LiveOf_laufendeRunde_begonneneRunde_naechsteRunde()
    {
        var now = Now;
        long Ms(DateTime d) => new DateTimeOffset(d).ToUnixTimeMilliseconds();
        var detail = LichessBroadcasts.ParseTourDetail(Detail("["
            + "{\"id\":\"r1\",\"name\":\"Round 1\",\"finished\":true,\"startsAt\":" + Ms(now.AddDays(-1)) + "},"
            + "{\"id\":\"r2\",\"name\":\"Round 2\",\"startsAt\":" + Ms(now.AddHours(-2)) + "},"
            + "{\"id\":\"r3\",\"name\":\"Round 3\",\"startsAt\":" + Ms(now.AddDays(1)) + "},"
            + "{\"id\":\"r4\",\"name\":\"Round 4\",\"startsAt\":" + Ms(now.AddDays(2)) + "}]"))!;
        var (next, live) = LeagueBroadcastImport.LiveOf(detail.Rounds, now);
        Assert.Equal(now.AddDays(1), next);
        Assert.Equal(now.AddHours(-2) + LeagueBroadcastImport.LiveWindow, live);   // begonnen, nicht fertig

        var ongoing = LichessBroadcasts.ParseTourDetail(Detail("[{\"id\":\"r1\",\"name\":\"Round 1\",\"ongoing\":true}]"))!;
        Assert.Equal((null, now + LeagueBroadcastImport.OngoingGrace), LeagueBroadcastImport.LiveOf(ongoing.Rounds, now));
        // Zu lange her begonnen und nie abgeschlossen: nicht mehr laufend.
        var stale = LichessBroadcasts.ParseTourDetail(Detail("[{\"id\":\"r1\",\"name\":\"Round 1\",\"startsAt\":" + Ms(now.AddDays(-2)) + "}]"))!;
        Assert.Equal(((DateTime?)null, (DateTime?)null), LeagueBroadcastImport.LiveOf(stale.Rounds, now));
    }

    [Fact]
    public async Task RunLive_nurLaufendeUebertragungen_hoechstensAlle10Minuten()
    {
        await SeedAsync();
        _db.LeagueBroadcasts.Remove(await _db.LeagueBroadcasts.SingleAsync());
        LeagueBroadcast Row(string id, DateTime? imported, DateTime? liveUntil = null, DateTime? next = null, bool finished = false) =>
            new() { TourId = id, Name = id, FoundAt = Now.AddDays(-5), ImportedAt = imported, LiveUntil = liveUntil, NextRoundAt = next, Finished = finished };
        _db.LeagueBroadcasts.AddRange(
            Row("live0001", Now.AddMinutes(-11), liveUntil: Now.AddHours(3)),                 // läuft, letzte Prüfung 11 min her → ja
            Row("live0002", Now.AddMinutes(-4), liveUntil: Now.AddHours(3)),                  // läuft, eben erst geprüft → nein
            Row("start001", Now.AddHours(-5), next: Now.AddMinutes(-3)),                      // Runde hat gerade begonnen → ja
            Row("rest0001", Now.AddHours(-5), next: Now.AddDays(3)),                          // zwischen zwei Runden → 6-h-Takt
            Row("over0001", Now.AddHours(-5), liveUntil: Now.AddMinutes(-1)),                 // Runde vorbei → 6-h-Takt
            Row("done0001", Now.AddHours(-5), liveUntil: Now.AddHours(3), finished: true));   // fertig → nie
        await _db.SaveChangesAsync();
        var http = Http("", Detail("[{\"id\":\"r1\",\"name\":\"Round 1\",\"finished\":true}]"));

        Assert.Equal(2, await Importer(http).RunLiveAsync(default, Now));

        var asked = http.Urls.Where(u => !u.EndsWith(".pgn")).Select(u => u[(u.LastIndexOf('/') + 1)..]).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "live0001", "start001" }, asked);
        Assert.Equal(2, http.Urls.Count(u => u.EndsWith(".pgn")));
    }

    // ── Vereins-Datenbank ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_nurFertigePartienDerEigenenMannschaft_inDieVereinsDatenbank()
    {
        var b = await SeedAsync();
        var pgn = Game()                                                                                     // fertig, Schwaz → ja
            + Game(result: "*", url: "https://lichess.org/broadcast/bl/round-1/L3MX2o99/run00099")         // läuft noch → nein
            + Game(whiteTeam: "SK Austria Wien", url: "https://lichess.org/broadcast/bl/round-1/L3MX2o99/oth00099",
                white: "Fremd, Fritz", whiteFide: "990103", moves: OtherTail)                                  // ohne Schwaz → nur Karten
            + Game(date: "2026.05.01", url: "https://lichess.org/broadcast/bl/round-9/L3MX2o99/old00099", moves: OtherTail);   // Vorsaison → nein

        await ImportAsync(b, pgn);

        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.Equal("https://lichess.org/broadcast/bl/round-1/L3MX2o99/1pPEJ999", g.SourceUrl);
        // Alle fertigen Partien in die Spielerkarten — wie bisher.
        var opp = await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == OppFide);
        Assert.Contains("[LeagueSource \"Lichess-Übertragung\"]", opp.Pgn);
        Assert.True(opp.GameCount >= 2);
        Assert.Contains("Fremd, Fritz", opp.Pgn);
    }

    [Theory]
    [InlineData("Schachklub Schwaz", true)]
    [InlineData("SK Schwaz", true)]
    [InlineData("Schwaz", true)]
    [InlineData("Schwaz 2", true)]
    [InlineData("Pinggau-Friedberg", false)]
    [InlineData("Mayrhofen Zell/Zillertal", false)]
    [InlineData("", false)]
    public void Involves_erkenntDieEigeneMannschaft_auchGekuerzt(string team, bool expected)
    {
        var g = new LeagueBroadcastGame(new Dictionary<string, string> { ["WhiteTeam"] = "SV Gamlitz", ["BlackTeam"] = team }, new[] { "e4" });
        Assert.Equal(expected, ClubBroadcastGames.Involves(Schwaz, g));
    }

    [Fact]
    public async Task Import_eigenerSpieler_mitEchtemNamen_FideElo_RundeUndTag()
    {
        var b = await SeedAsync();
        await ImportAsync(b, Game());

        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.Equal((ClubId, "Schmider, Niklas", OwnFide, (int?)2398, "Vaibhav, Suri", OppFide, (int?)2569, false),
            (g.ClubId, g.White, g.WhiteFide, g.WhiteElo, g.Black, g.BlackFide, g.BlackElo, g.Anonymized));
        Assert.Equal((LeagueBroadcastImport.Source, (DateOnly?)new DateOnly(2026, 11, 25), "1.2", (int?)2026, "1-0", "1. Bundesliga AUT 2026/2027"),
            (g.Source, g.PlayedOn, g.SourceRound, g.Year, g.Result, g.Event));
        Assert.Null(g.WhiteRealName);
        Assert.Null(g.UploadedByUserId);
        Assert.Contains("[Date \"2026.11.25\"]", g.Pgn);
        Assert.Contains("[Round \"1.2\"]", g.Pgn);
        Assert.Contains("[White \"Schmider, Niklas\"]", g.Pgn);
        Assert.DoesNotContain("Schwaz\"]", g.Pgn);
        Assert.DoesNotContain("%clk", g.Pgn);
    }

    [Fact]
    public async Task Import_zweimal_eineVereinspartie_genauEineZusammenfassung_auchMitLigapaarung()
    {
        var b = await SeedAsync(pairing: true);
        await ImportAsync(b, Game());
        await ImportAsync(await _db.LeagueBroadcasts.SingleAsync(), Game());   // Neueinlesen (z. B. 10 min später)

        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.NotNull(g.LeagueGameId);                                       // der normale Weg der Ligapaarung lief mit
        var s = await _db.ClubGameSummaries.SingleAsync();
        Assert.Equal((g.Id, ClubGameSummaryStatus.Requested, (int?)null), (s.ClubGameId, s.Status, s.RequestedByUserId));
        Assert.NotNull(s.AutoRequestedAt);
    }

    [Fact]
    public async Task Import_ohneLigapaarung_FrauenbundesligaGenauso_eineZusammenfassung()
    {
        var b = await SeedAsync();
        await ImportAsync(b, Game(whiteTeam: "Pinggau-Friedberg", blackTeam: "SK Schwaz", white: "Gast, Gerda", whiteFide: "990104",
            black: "Heim, Hanna", blackFide: "990105"));

        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.Null(g.LeagueGameId);
        Assert.Equal(("Gast, Gerda", "Heim, Hanna", "990105"), (g.White, g.Black, g.BlackFide));   // in keiner Meldeliste: Name aus der Übertragung
        Assert.Equal(g.Id, (await _db.ClubGameSummaries.SingleAsync()).ClubGameId);
    }

    [Fact]
    public async Task Import_Lc0AuftragEntstehtUeberDenNormalenWeg()
    {
        const int owner = 990201;
        _db.AppUsers.Add(new AppUser { Id = owner, Username = "lc0owner", PasswordHash = "x", IsAdmin = true });
        _db.ExternalEngineRegistrations.Add(new ExternalEngineRegistration
        {
            Id = "rhe_lc0bc9901", UserId = owner, Name = ClubSecondEngineScheduler.DefaultEngineName, ClientSecret = "s",
            ProviderSelector = "sel", MaxThreads = 1, MaxHash = 16, Variants = "chess",
        });
        var b = await SeedAsync();
        await ImportAsync(b, Game());
        var g = await _db.LeagueClubGames.SingleAsync();

        var scheduler = new ClubSecondEngineScheduler(null!, new ConfigurationBuilder().Build(), NullLogger<ClubSecondEngineScheduler>.Instance);
        var analyses = new GameAnalysisService(_db, new AnalysisJobService(_db), new CommentSetService(_db, NullLogger<CommentSetService>.Instance),
            NullLogger<GameAnalysisService>.Instance);
        var id = await scheduler.TickOnceAsync(_db, analyses, default);

        var a = await _db.GameAnalyses.SingleAsync(x => x.Id == id);
        Assert.Equal((GameAnalysisOrigin.Club, (int?)g.Id, "rhe_lc0bc9901"), (a.Origin, a.LeagueClubGameId, a.EngineId));
    }

    [Fact]
    public async Task Import_neuePartienKommenInDieAnalyseWarteschlangeDerLigapartien()
    {
        var b = await SeedAsync();
        await ImportAsync(b, Game(whiteTeam: "SK Austria Wien", blackTeam: "Pinggau-Friedberg", white: "Fremd, Fritz", whiteFide: "990103"));

        Assert.Empty(_db.LeagueClubGames);                                    // ohne Schwaz: nur Karten …
        var items = await LeagueAnalysisQueue.BuildAsync(_db, Now, default);
        Assert.Contains(items, i => i.Pgn.Contains("Fremd, Fritz"));           // … und von dort in die Warteschlange
    }

    // ── Doppel-Erkennung gegen Uploads ─────────────────────────────────────────────────────────

    private static List<string> SansOf(string moves) => moves.Split(' ').Where(t => !t.EndsWith('.')).ToList();

    private LeagueClubGameRequest Form(string moves) => new()
    {
        Moves = SansOf(moves), White = "Schmider, Niklas", WhiteFide = OwnFide, WhiteReplace = true, Black = "Vaibhav, Suri", BlackFide = OppFide,
        Result = "1-0", Year = 2026, Date = "2026.11.25",
    };

    [Fact]
    public async Task Formular_derselbenPartie_nachDerUebertragung_istDoppelt_keineZweiteZusammenfassung()
    {
        var b = await SeedAsync();
        await ImportAsync(b, Game());

        var same = await Clubs().AddGameAsync(Schwaz, null, Form(Moves));
        var tail = await Clubs().AddGameAsync(Schwaz, null, Form(Moves + " 12. h4 b4"));   // anders abgeschrieben, gleicher Anfang
        Assert.Equal(("duplicate", "duplicate"), (same.Reason, tail.Reason));

        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.Null(g.ArchivedAt);                                            // die übertragene Fassung bleibt
        Assert.Single(_db.ClubGameSummaries);
    }

    [Fact]
    public async Task Uebertragung_nachDemFormular_legtNichtsDoppeltAn()
    {
        var b = await SeedAsync();
        var (form, reason, _) = await Clubs().AddGameAsync(Schwaz, null, Form(Moves));
        Assert.Null(reason);
        Assert.True(form!.Anonymized);
        _db.ChangeTracker.Clear();

        await ImportAsync(await _db.LeagueBroadcasts.SingleAsync(), Game());

        Assert.Equal(form.Id, (await _db.LeagueClubGames.SingleAsync()).Id);
        Assert.Empty(_db.ClubGameSummaries);
    }

    [Fact]
    public async Task PgnImport_derselbenPartie_zaehltAlsDoppelt()
    {
        var b = await SeedAsync();
        await ImportAsync(b, Game());

        var r = await Clubs().ImportPgnAsync(Schwaz, null, Game(), null);

        Assert.Equal((0, 1), (r.Added, r.Duplicates));
        Assert.Single(_db.LeagueClubGames);
    }
}
