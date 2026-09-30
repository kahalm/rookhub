using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Team-Suche (0.612.0): Stellungs-Fingerabdruck, das Lesen der Lichess-Antworten (Team-Suche, Mitglieder, Team-Battles,
/// Ergebnisse — Felder wie in den öffentlichen Schnittstellen, nachgesehen am 2026-09-30) und der ganze Weg vom Tiroler Team
/// zum Vorschlag, über den Klarnamen und über die Stellungen.
/// </summary>
public class LeagueTeamScoutTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private static string[] Moves(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private const string Ruy = "e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7 Re1 b5 Bb3 d6 c3 O-O h3 Nb8 d4 Nbd7";
    private const string Qgd = "d4 d5 c4 e6 Nc3 Nf6 Bg5 Be7 e3 O-O Nf3 h6 Bh4 b6 cxd5 Nxd5 Bxe7 Qxe7 Nxd5 exd5";

    // ── Fingerabdruck ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OwnPositions_AfterOwnMoves_UpToMaxPly_StopsAtIllegal()
    {
        var w = LeagueFingerprint.OwnPositions(Moves("e4 e5 Nf3 Nc6"), white: true);
        Assert.Equal(new[] { 1, 3 }, w.Keys.OrderBy(k => k));
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b", w[1]);
        Assert.Equal(new[] { 2, 4 }, LeagueFingerprint.OwnPositions(Moves("e4 e5 Nf3 Nc6"), white: false).Keys.OrderBy(k => k));
        Assert.Equal(new[] { 1 }, LeagueFingerprint.OwnPositions(Moves("e4 e4 Nf3"), white: true).Keys);     // 2…e4 geht nicht
        Assert.Equal(LeagueFingerprint.MaxPly - 1, LeagueFingerprint.OwnPositions(Moves(Ruy + " Nbd2 Bb7"), white: true).Keys.Max());
    }

    [Fact]
    public void Depth_MeanDeepestSharedPly_CountsTranspositions()
    {
        var r = new LeagueFingerprint.Repertoire();
        r.Add(Moves("e4 e5 Nf3 Nc6 Bb5"), white: true);
        r.Add(Moves("Nf3 d5 d4 Nf6"), white: true);
        var games = new List<(IReadOnlyList<string>, bool)>
        {
            (Moves("e4 e5 Nf3 Nc6 Bc4 Bc5"), true),         // bis 3.? gleich → Halbzug 3
            (Moves("d4 d5 Nf3 Nf6"), true),                 // Zugumstellung: nach 2.Nf3 dieselbe Stellung wie nach 2.d4 → Halbzug 3
            (Moves("c4 e5"), true),                          // nichts gemeinsam
            (Moves("e4 e5 Nf3 Nc6"), false),                 // Schwarz: das Repertoire hat keine Schwarzpartien
        };
        Assert.Equal((3 + 3 + 0 + 0) / 4.0, LeagueFingerprint.Depth(games, r));
        Assert.Equal(0, LeagueFingerprint.Depth(new List<(IReadOnlyList<string>, bool)>(), r));
    }

    [Fact]
    public void Usable_DropsBullet_OnlyWithEnoughOtherGames()
    {
        var many = Enumerable.Repeat("blitz", 50).Concat(Enumerable.Repeat("bullet", 10)).ToList();
        Assert.Equal(50, LeagueFingerprint.Usable(many, s => s).Count);
        var few = Enumerable.Repeat("blitz", 10).Concat(Enumerable.Repeat("bullet", 5)).ToList();
        Assert.Equal(15, LeagueFingerprint.Usable(few, s => s).Count);
    }

    [Fact]
    public void Best_RatioToSecond_AndSingleHit()
    {
        var b = LeagueFingerprint.Best(new Dictionary<string, double> { ["a"] = 4, ["b"] = 2, ["c"] = 0 })!.Value;
        Assert.Equal(("a", 4.0, 2.0, (string?)"b"), (b.Fide, b.Depth, b.Ratio, b.Second));
        var one = LeagueFingerprint.Best(new Dictionary<string, double> { ["a"] = 3, ["b"] = 0 })!.Value;
        Assert.Equal((30.0, (string?)null), (one.Ratio, one.Second));                     // ein Zweiter ohne Treffer zählt 0,1
        Assert.Null(LeagueFingerprint.Best(new Dictionary<string, double> { ["a"] = 0 }));
    }

    // ── Lesen ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClubKeys_WholeWords_BothUmlautSpellings()
    {
        var places = LeagueTeamScout.DefaultPlaces;
        Assert.Equal(new[] { "kufstein", "woergl" }, LeagueTeamScout.ClubKeys("SPG Kufstein / Wörgl", places));
        Assert.Equal(new[] { "woergl" }, LeagueTeamScout.ClubKeys("Schachklub Worgl", places));
        Assert.Equal(new[] { "innsbruck" }, LeagueTeamScout.ClubKeys("Schachklub Innsbruck-Pradl", places));
        Assert.Empty(LeagueTeamScout.ClubKeys("Hallo Schach", places));                    // „Hallo" ist nicht Hall
        Assert.True(LeagueTeamScout.IsLocalTeam("SK Hall in Tirol", places));
        Assert.False(LeagueTeamScout.IsLocalTeam("Chess Club Berlin", places));
    }

    [Fact]
    public void Parse_TeamSearchMembersBattlesAndResults()
    {
        Assert.Equal(new[] { ("sk-kufstein", "SK Kufstein") },
            LeagueTeamScout.ParseTeamSearch("""{"currentPage":1,"currentPageResults":[{"id":"sk-kufstein","name":"SK Kufstein","nbMembers":12}],"nbResults":1}"""));
        Assert.Equal(new[] { "Katzenpapa", "trigonias" }, LeagueTeamScout.ParseTeamMembers(
            "{\"id\":\"katzenpapa\",\"username\":\"Katzenpapa\"}\n{\"id\":\"trigonias\"}\nkaputt\n"));
        Assert.Equal(new[] { "tb1" }, LeagueTeamScout.ParseTeamBattles(
            "{\"id\":\"tb1\",\"teamBattle\":{\"teams\":[\"sk-kufstein\",\"x\"],\"nbLeaders\":5}}\n{\"id\":\"ar2\",\"fullName\":\"Arena\"}\n"));
        var teams = LeagueTeamScout.ParseBattleTeams("""{"id":"tb1","teamBattle":{"teams":{"sk-kufstein":["SK Kufstein",null],"x":"X Team"}}}""");
        Assert.Equal(("SK Kufstein", "X Team"), (teams["sk-kufstein"], teams["x"]));
        Assert.Empty(LeagueTeamScout.ParseBattleTeams("""{"id":"ar2"}"""));
        Assert.Equal(new[] { ("Trigonias", (string?)"sk-kufstein"), ("Solo", (string?)null) }, LeagueTeamScout.ParseResults(
            "{\"rank\":1,\"score\":20,\"username\":\"Trigonias\",\"team\":\"sk-kufstein\"}\n{\"rank\":2,\"username\":\"Solo\"}\n"));
    }

    // ── Der ganze Weg ──────────────────────────────────────────────────────────────────────────

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.Method + " " + request.RequestUri);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
    private static HttpResponseMessage Status(HttpStatusCode c) => new(c) { Content = new StringContent("") };

    /// <summary>25 Schnellpartien von Trigonias mit Weiß, alle Spanisch.</summary>
    private static string OnlineGames(string line, int n = 25) => string.Join("\n", Enumerable.Range(0, n).Select(i =>
        "{\"id\":\"g" + i + "\",\"rated\":true,\"variant\":\"standard\",\"speed\":\"rapid\",\"createdAt\":" + (1_700_000_000_000L + i * 60_000)
        + ",\"status\":\"mate\",\"winner\":\"white\",\"players\":{\"white\":{\"user\":{\"name\":\"Trigonias\",\"id\":\"trigonias\"},\"rating\":1950},"
        + "\"black\":{\"user\":{\"name\":\"Opp" + i + "\",\"id\":\"opp" + i + "\"},\"rating\":1900}},\"moves\":\"" + line + "\"}"));

    /// <summary>Lichess mit einem Tiroler Team (SK Kufstein) samt einem Team-Battle und einem fremden Team.</summary>
    private static FakeHttp World(int year = 1987, int trigRating = 1950) => new(req =>
    {
        var u = req.RequestUri!.ToString();
        if (u.Contains("/api/team/search"))
            return Ok("""{"currentPage":1,"currentPageResults":[{"id":"sk-kufstein","name":"SK Kufstein"},{"id":"ccb","name":"Chess Club Berlin"}]}""");
        if (u.Contains("/api/team/sk-kufstein/users")) return Ok("{\"id\":\"katzenpapa\",\"username\":\"Katzenpapa\"}\n{\"id\":\"trigonias\",\"username\":\"Trigonias\"}\n");
        if (u.Contains("/api/team/sk-kufstein/arena"))
            return Ok("{\"id\":\"tb1\",\"teamBattle\":{\"teams\":[\"sk-kufstein\",\"x\"]}}\n{\"id\":\"ar2\"}\n");
        if (u.EndsWith("/api/tournament/tb1"))
            return Ok("""{"id":"tb1","teamBattle":{"teams":{"sk-kufstein":["SK Kufstein",null],"x":["X Team",null]}}}""");
        if (u.Contains("/api/tournament/tb1/results"))
            return Ok("{\"username\":\"Trigonias\",\"team\":\"sk-kufstein\"}\n{\"username\":\"Fremder\",\"team\":\"x\"}\n");
        if (u.EndsWith("/api/users"))
            return Ok("""[{"id":"katzenpapa","username":"Katzenpapa","profile":{"flag":"AT","realName":"Max Muster"}},"""
                + """{"id":"trigonias","username":"Trigonias","perfs":{"rapid":{"games":200,"rating":""" + trigRating + "}}}]");
        if (u.Contains("/api/games/user/trigonias")) return Ok(OnlineGames(Ruy));
        if (u.Contains("/api/fide/player/")) return Ok("{\"id\":1,\"federation\":\"AUT\",\"year\":" + year + "}");
        return Status(HttpStatusCode.NotFound);
    });

    private LeagueTeamScout Scout(FakeHttp http) =>
        new(_db, new HttpClient(http), NullLogger<LeagueTeamScout>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LeagueOnline:TeamPlaces"] = "Kufstein" }).Build())
        { Pause = TimeSpan.Zero };

    private static string Pgn(string white, string whiteFide, string black, string line, int year) =>
        $"[Event \"Liga\"]\n[Date \"{year}.01.01\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[WhiteFideId \"{whiteFide}\"]\n[Result \"1-0\"]\n\n"
        + string.Join(" ", Moves(line).Select((m, i) => i % 2 == 0 ? $"{i / 2 + 1}. {m}" : m)) + " 1-0\n\n";

    /// <summary>Muster (222) spielt Spanisch, Huber (333) Damengambit — beide für Kufstein, Maier (444) für Innsbruck.</summary>
    private async Task SeedAsync(string hubersLine = Qgd)
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Name = "Landesliga", Season = "2026/27", League = "LL", Stage = "x" });
        _db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Muster, Max", NameKey = "muster, max", FideId = "222", Fed = "AUT", EloI = 1900 },
            new LeaguePlayer { Tnr = 1, Team = "Kufstein 2", Name = "Huber, Franz", NameKey = "huber, franz", FideId = "333", Fed = "AUT", EloI = 1800 },
            new LeaguePlayer { Tnr = 1, Team = "Innsbruck 1", Name = "Maier, Anna", NameKey = "maier, anna", FideId = "444", Fed = "AUT", EloI = 1700 });
        _db.LeaguePlayerProfiles.AddRange(
            new LeaguePlayerProfile { FideId = "222", Name = "Muster, Max", Pgn = Pgn("Muster, Max", "222", "Gegner, A", Ruy, 2024) + Pgn("Muster, Max", "222", "Gegner, B", Ruy, 2023) },
            new LeaguePlayerProfile { FideId = "333", Name = "Huber, Franz", Pgn = Pgn("Huber, Franz", "333", "Gegner, C", hubersLine, 2024) },
            new LeaguePlayerProfile { FideId = "444", Name = "Maier, Anna", Pgn = Pgn("Maier, Anna", "444", "Gegner, D", Ruy, 2024) });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Pool_TakesMembersAndBattlePlayersOfLocalTeamsOnly()
    {
        var http = World();
        Assert.Equal(2, await Scout(http).RefreshPoolAsync(default));
        var pool = await _db.LeagueScoutAccounts.OrderBy(a => a.UserName).ToListAsync();
        Assert.Equal(new[] { "katzenpapa", "trigonias" }, pool.Select(a => a.UserName));
        Assert.Equal(("SK Kufstein", (string?)null), (pool[0].Teams, pool[0].PlayedFor));
        Assert.Equal(("Trigonias", "SK Kufstein", (string?)"SK Kufstein"), (pool[1].DisplayName, pool[1].Teams, pool[1].PlayedFor));
        Assert.DoesNotContain(http.Urls, x => x.Contains("/api/team/ccb/"));                 // nicht Tirol
        Assert.DoesNotContain(http.Urls, x => x.Contains("/api/tournament/ar2"));            // kein Team-Battle
        Assert.Equal(0, await Scout(World()).RefreshPoolAsync(default));                      // derselbe Stand: nichts Neues
    }

    [Fact]
    public async Task Run_SuggestsByRealName_AndByPositionsWithinTheClub_AsTeamSuggestions()
    {
        await SeedAsync();
        var http = World();
        var scout = Scout(http);
        await scout.RefreshPoolAsync(default);
        Assert.False(await scout.RunOnceAsync(TimeSpan.FromMinutes(5), refreshPool: false, default));

        var list = await _db.LeagueAccountSuggestions.OrderBy(s => s.UserName).ToListAsync();
        Assert.Equal(new[] { "222:Katzenpapa", "222:Trigonias" }, list.Select(s => $"{s.FideId}:{s.UserName}"));
        Assert.All(list, s => Assert.Equal(LeagueTeamScout.Source, s.Source));
        Assert.Equal(1 + 3 + 1, list[0].Score);                                                 // Team + Klarname + Land
        Assert.StartsWith("Mitglied im Lichess-Team „SK Kufstein“; Klarname im Profil", list[0].Evidence);
        Assert.Equal(4, list[1].Score);                                                         // Abstand ≥ 2
        Assert.StartsWith("spielte für „SK Kufstein“ (Lichess-Team-Battle); unter 2 Spielern des Vereins passen seine Stellungen am besten zu ihm",
            list[1].Evidence);
        Assert.Contains("Lichess Schnell 1950 passt zu Elo 1900", list[1].Evidence);
        // Maier (Innsbruck) spielt dasselbe, zählt aber nicht: verglichen wird nur mit dem Verein, für den das Konto spielte.
        Assert.DoesNotContain(list, s => s.FideId == "444");

        var checkedPool = await _db.LeagueScoutAccounts.OrderBy(a => a.UserName).ToListAsync();
        Assert.All(checkedPool, a => Assert.NotNull(a.CheckedAt));
        Assert.Equal(new[] { "Klarname: 1 Vorschlag/Vorschläge", "Stellungen → Muster, Max" }, checkedPool.Select(a => a.Result));
        // Der Jahrgang steht fest, bevor ein Vorschlag entsteht — die Namenssuche bleibt für den Spieler trotzdem fällig.
        var scan = await _db.LeagueAccountScans.SingleAsync();
        Assert.Equal(("222", (int?)1987, 0), (scan.FideId, scan.BirthYear, scan.Version));

        http.Urls.Clear();
        Assert.False(await scout.RunOnceAsync(TimeSpan.FromMinutes(5), refreshPool: false, default));
        Assert.Empty(http.Urls);                                                                // alles geprüft, nichts fällig

        // Die Namenssuche findet die beiden nicht — und räumt sie trotzdem nicht weg.
        var finder = new LeagueAccountFinder(_db, new HttpClient(new FakeHttp(req => req.RequestUri!.ToString() switch
        {
            var x when x.EndsWith("/api/users") => Ok("[]"),
            var x when x.Contains("/api/player/autocomplete") => Ok("""{"result":[]}"""),
            var x when x.Contains("/api/fide/player/") => Ok("""{"federation":"AUT","year":1987}"""),
            _ => Status(HttpStatusCode.NotFound),
        })), NullLogger<LeagueAccountFinder>.Instance) { ChessComPause = TimeSpan.Zero, PlayerPause = TimeSpan.Zero };
        await finder.ScanAsync((await finder.PlayerAsync("222", default))!, default);
        Assert.Equal(2, await _db.LeagueAccountSuggestions.CountAsync());
    }

    [Fact]
    public async Task Run_NoSuggestion_WhenPositionsAreNotClear_OrRatingTooLow()
    {
        await SeedAsync(hubersLine: Ruy);                                                      // Huber spielt dasselbe → kein Abstand
        var scout = Scout(World());
        await scout.RefreshPoolAsync(default);
        await scout.RunOnceAsync(TimeSpan.FromMinutes(5), refreshPool: false, default);
        Assert.Equal("Stellungen nicht eindeutig (Abstand 1.0)",
            (await _db.LeagueScoutAccounts.SingleAsync(a => a.UserName == "trigonias")).Result!.Replace(',', '.'));
        Assert.DoesNotContain(await _db.LeagueAccountSuggestions.ToListAsync(), s => s.UserName == "Trigonias");

        _db.ChangeTracker.Clear();
        _db.LeagueScoutAccounts.RemoveRange(_db.LeagueScoutAccounts);
        _db.LeaguePlayerProfiles.Single(p => p.FideId == "333").Pgn = Pgn("Huber, Franz", "333", "Gegner, C", Qgd, 2024);
        await _db.SaveChangesAsync();
        var low = Scout(World(trigRating: 1400));                                               // 500 unter Elo 1900
        await low.RefreshPoolAsync(default);
        await low.RunOnceAsync(TimeSpan.FromMinutes(5), refreshPool: false, default);
        Assert.Equal("Stellungen → Muster, Max, aber Wertung zu niedrig",
            (await _db.LeagueScoutAccounts.SingleAsync(a => a.UserName == "trigonias")).Result);
    }

    [Fact]
    public async Task Run_MinorsSuggestionsStayHidden_AndRateLimitPauses()
    {
        await SeedAsync();
        var scout = Scout(World(year: DateTime.UtcNow.Year - 15));
        await scout.RefreshPoolAsync(default);
        await scout.RunOnceAsync(TimeSpan.FromMinutes(5), refreshPool: false, default);
        Assert.Equal(2, await _db.LeagueAccountSuggestions.CountAsync());
        Assert.Contains("222", await LeagueHiddenAccounts.FidesAsync(_db, null, default));

        _db.LeagueScoutAccounts.Add(new LeagueScoutAccount { UserName = "neu", DisplayName = "Neu", Teams = "SK Kufstein", FoundAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        var limited = Scout(new FakeHttp(_ => Status(HttpStatusCode.TooManyRequests)));
        Assert.True(await limited.RunOnceAsync(TimeSpan.FromMinutes(5), refreshPool: false, default));   // noch Arbeit offen
        Assert.Null((await _db.LeagueScoutAccounts.AsNoTracking().SingleAsync(a => a.UserName == "neu")).CheckedAt);
    }
}
