using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Liga-Partien im Hintergrund analysieren (0.665.0): „erst die neuesten Partien (Fenster: LeagueAnalysisQueue.Years), dann immer bevorzugt
/// die Gegner von Schwaz nächste Runde, dann der Rest, und wenn das alles fertig ist erst wieder die Meisterpartien".
/// </summary>
public class LeagueAnalysisQueueTests : IDisposable
{
    private const int Owner = 5;
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db;

    public LeagueAnalysisQueueTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.AppUsers.Add(new AppUser { Id = Owner, Username = "kahalm", PasswordHash = "x", IsAdmin = true });
        var cred = new LichessEngineCredential { UserId = Owner, EncryptedToken = "enc", ShareAsHouseEngine = true };
        cred.SetBackgroundEngines(Enumerable.Range(1, 16).Select(i => $"rhe_t{i}"));
        _db.LichessEngineCredentials.Add(cred);
        _db.LeagueClubs.Add(TestClubs.Home);   // der eigene Verein: „Testdorf" (Mandanten-Schritt 2026-10-07)
        // Laufende Saison: Testdorf spielt Runde 2 gegen Kufstein (Runde 1 gespielt gegen Wörgl), Hall spielt woanders.
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Season = "2026/27", League = "Landesliga", Stage = "Liga" });
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 9, Season = "2025/26", League = "Landesliga", Stage = "Liga" });
        _db.LeagueMatches.AddRange(
            new LeagueMatch { Tnr = 1, Round = 1, Home = "Testdorf 1", Away = "Wörgl 1", HomePts = 3, AwayPts = 3 },
            new LeagueMatch { Tnr = 1, Round = 2, Home = "Kufstein 1", Away = "Testdorf 1" },
            new LeagueMatch { Tnr = 1, Round = 3, Home = "Testdorf 1", Away = "Hall 1" });
        _db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Gegner, Kurt", FideId = "100" },
            new LeaguePlayer { Tnr = 1, Team = "Hall 1", Name = "Rest, Hans", FideId = "200" },
            new LeaguePlayer { Tnr = 1, Team = "Wörgl 1", Name = "Ohne, Id" },
            new LeaguePlayer { Tnr = 9, Team = "Alt 1", Name = "Alt, Spieler", FideId = "300" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static string Game(string date, string moves, string? fen = null) =>
        $"[Event \"T\"]\n[Date \"{date}\"]\n[White \"A\"]\n[Black \"B\"]\n[Result \"*\"]\n"
        + (fen is null ? "" : $"[FEN \"{fen}\"]\n") + $"\n{moves} *\n\n";

    private void Profile(string fide, params string[] games)
    {
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = fide, Name = fide, Pgn = string.Concat(games) });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Build_gegnerDerNaechstenRundeZuerst_jeweilsNeuesteZuerst_nurImFenster()
    {
        Profile("200", Game("2026.09.01", "1. e4 e5 2. Nf3 Nc6"), Game("2026.??.??", "1. c4 e5 2. Nc3 Nf6"));
        Profile("100", Game("2025.05.03", "1. d4 d5 2. c4 e6"), Game("2026.03.01", "1. d4 Nf6 2. c4 g6"),
            Game("1970.01.01", "1. e4 c5 2. Nf3 d6"));   // aelter als das Fenster (LeagueAnalysisQueue.Years)
        Profile("300", Game("2026.08.01", "1. b3 e5 2. Bb2 Nc6"));   // nur in einer alten Saison

        var items = await LeagueAnalysisQueue.BuildAsync(_db, Now, default);

        Assert.Equal(new[] { "2026-03-01", "2025-05-03", "2026-09-01", "2026-01-01" },
            items.Select(i => i.Date.ToString("yyyy-MM-dd")));
        Assert.Equal(new[] { true, true, false, false }, items.Select(i => i.Opponent));
    }

    /// <summary>Die laufende Saison je REGION (Rest-Bug aus 0.712.0, behoben 0.720.0): Bayern hat noch keine 2026/27 eingespielt —
    /// seine jüngste Saison 2025/26 zählt trotzdem; Tirols Vorsaison nicht.</summary>
    [Fact]
    public async Task Build_juengsteSaisonJeRegion()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 900_000_001, Season = "2025/26", League = "Oberliga", Stage = "Liga",
            Source = LigamanagerSource.Source });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 900_000_001, Team = "SK Weiler 1", Name = "Bayer, Bert", FideId = "400" });
        _db.SaveChanges();
        Profile("400", Game("2026.07.01", "1. e4 c6 2. d4 d5"));
        Profile("300", Game("2026.08.01", "1. b3 e5 2. Bb2 Nc6"));   // Tirol 2025/26: nicht mehr laufend

        var items = await LeagueAnalysisQueue.BuildAsync(_db, Now, default);

        Assert.Equal(new[] { "2026-07-01" }, items.Select(i => i.Date.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public async Task Build_dieselbePartieInZweiProfilen_zaehltEinmal_StellungspartieNicht()
    {
        var same = Game("2026.09.01", "1. e4 e5 2. Nf3 Nc6");
        Profile("100", same, Game("2026.09.02", "1. e4 e5", fen: "8/8/8/8/8/8/8/K6k w - - 0 1"));
        Profile("200", same);

        var items = await LeagueAnalysisQueue.BuildAsync(_db, Now, default);

        Assert.Single(items);
        Assert.True(items[0].Opponent);
    }

    [Fact]
    public async Task Next_schonGerechnetePartie_wirdUebersprungen()
    {
        Profile("100", Game("2026.09.01", "1. e4 e5 2. Nf3 Nc6"), Game("2026.08.01", "1. d4 d5 2. c4 e6"));
        _db.GameAnalyses.Add(new GameAnalysis
        {
            UserId = Owner, Pgn = "x", Origin = GameAnalysisOrigin.League, Status = GameAnalysisStatus.Done,
            MovesHash = LeagueClubService.HashOf(new[] { "e4", "e5", "Nf3", "Nc6" }),
        });
        _db.SaveChanges();
        var queue = new LeagueAnalysisQueue();

        var next = await queue.NextAsync(_db, Now, default);

        Assert.Equal(LeagueClubService.HashOf(new[] { "d4", "d5", "c4", "e6" }), next!.Value.MovesHash);
        Assert.Null(await queue.NextAsync(_db, Now, default));   // nichts mehr offen
    }

    [Fact]
    public void DateOf_liestVolleUndHalbeDaten()
    {
        Assert.Equal(new DateOnly(2025, 3, 14), LeagueAnalysisQueue.DateOf("2025.03.14"));
        Assert.Equal(new DateOnly(2025, 1, 1), LeagueAnalysisQueue.DateOf("2025.??.??"));
        Assert.Null(LeagueAnalysisQueue.DateOf("????.??.??"));
        Assert.Null(LeagueAnalysisQueue.DateOf(null));
    }

    // ----- Im Takt: nach den Vereinspartien, vor den Meisterpartien -----

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
    }).Build();

    private GameAnalysisService Analyses() =>
        new(_db, new AnalysisJobService(_db), new CommentSetService(_db, NullLogger<CommentSetService>.Instance),
            NullLogger<GameAnalysisService>.Instance);

    [Fact]
    public async Task Tick_LigaPartieVorDerMeisterpartie_alsHintergrundarbeit()
    {
        var recent = DateTime.UtcNow.AddMonths(-1).ToString("yyyy.MM.dd");
        Profile("100", Game(recent, "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6"));
        _db.LibraryGames.Add(new LibraryGame { Id = 1, SourceFile = "t.pgn", Pgn = "1. e4 e5 *", CommentedPlies = 3, MovesHash = "h1" });
        _db.SaveChanges();
        var scheduler = new MasterAnalysisScheduler(null!, new QuietHours(""), Config(),
            NullLogger<MasterAnalysisScheduler>.Instance, new LeagueAnalysisQueue());

        var id = await scheduler.TickOnceAsync(_db, Analyses(), default);

        var analysis = await _db.GameAnalyses.SingleAsync(g => g.Id == id);
        Assert.Equal((GameAnalysisOrigin.League, (int?)null, Owner), (analysis.Origin, analysis.LibraryGameId, analysis.UserId));
        Assert.Equal(LeagueClubService.HashOf(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6" }), analysis.MovesHash);
        Assert.All(await _db.AnalysisJobs.ToListAsync(), j => Assert.True(j.Background));
        Assert.DoesNotContain(await Analyses().ListAsync(Owner, includeSavedGames: true), a => a.Id == id);
    }

    private void Online(string fide, string speed, DateTime playedAt, string moves, bool white = true,
        string confidence = "sicher", string opponent = "Gegner_Online")
    {
        var account = new LeagueOnlineAccount
        {
            FideId = fide, Site = "lichess", UserName = $"user{fide}", Url = "u", Confidence = confidence,
        };
        _db.LeagueOnlineAccounts.Add(account);
        _db.SaveChanges();
        _db.LeagueOnlineGames.Add(new LeagueOnlineGame
        {
            AccountId = account.Id, FideId = fide, ExternalId = Guid.NewGuid().ToString("N"), PlayedAt = playedAt,
            Speed = speed, Rated = true, White = white, Result = "1-0", Opponent = opponent,
            Line = moves, Moves = moves, Plies = moves.Split(' ').Length,
        });
        _db.SaveChanges();
    }

    /// <summary>
    /// Zweite Quelle (2026-10-05): Online-Partien derselben Spieler, aber NUR langsamer als Blitz.
    /// Bullet und Blitz sind die grosse Mehrheit des Bestands — kaemen sie mit, rechnete der Stapel
    /// monatelang an Partien, die in Minuten gespielt wurden.
    /// </summary>
    [Fact]
    public async Task Build_OnlinePartien_nurLangsamerAlsBlitz_undEntdoppelt()
    {
        Profile("100", Game("2026.09.01", "1. e4 e5 2. Nf3 Nc6"));
        Online("100", "classical", Now.AddDays(-10), "d4 Nf6 c4 g6");
        Online("100", "correspondence", Now.AddDays(-20), "c4 e5 Nc3 Nf6");
        Online("100", "blitz", Now.AddDays(-5), "e4 c5 Nf3 d6");            // zu schnell
        Online("100", "bullet", Now.AddDays(-5), "b3 d5 Bb2 Nf6");          // zu schnell
        Online("100", "rapid", Now.AddYears(-6), "g3 d5 Bg2 Nf6");          // ausserhalb des Fensters
        Online("100", "rapid", Now.AddDays(-30), "e4 e5 Nf3 Nc6");          // dieselbe Partie wie im Profil

        var items = await LeagueAnalysisQueue.BuildAsync(_db, Now, default);

        // Profilpartie + zwei langsame Online-Partien; die doppelte zaehlt einmal, die schnellen und die alte gar nicht.
        Assert.Equal(3, items.Count);
        Assert.Contains(items, i => i.Pgn.Contains("d4 Nf6") && i.Pgn.Contains("[Event \"lichess classical\"]"));
        Assert.Contains(items, i => i.Pgn.Contains("c4 e5") && i.Pgn.Contains("correspondence"));
        Assert.DoesNotContain(items, i => i.Pgn.Contains("e4 c5") || i.Pgn.Contains("b3 d5") || i.Pgn.Contains("g3 d5"));
        Assert.All(items, i => Assert.True(i.Opponent));   // Spieler 100 ist Gegner der naechsten Runde
    }

    /// <summary>Farben und Nummern: der Provider bekommt ein PGN, das ein Parser auch als Partie liest.</summary>
    [Fact]
    public async Task Build_OnlinePartie_traegtFarbenUndZugnummern()
    {
        Online("200", "rapid", Now.AddDays(-3), "e4 e5 Nf3 Nc6 Bb5", white: false, opponent: "Weisser");

        var items = await LeagueAnalysisQueue.BuildAsync(_db, Now, default);

        var pgn = Assert.Single(items).Pgn;
        Assert.Contains("[White \"Weisser\"]", pgn);
        Assert.Contains("[Black \"user200\"]", pgn);
        Assert.Contains("1. e4 e5 2. Nf3 Nc6 3. Bb5", pgn);
        Assert.Equal("1. e4 e5 2. Nf3", LeagueAnalysisQueue.Numbered(["e4", "e5", "Nf3"]));
    }
}
