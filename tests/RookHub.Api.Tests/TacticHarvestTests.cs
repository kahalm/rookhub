using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Tactics;
using Cand = RookHub.Api.Services.Tactics.TacticHarvest.Cand;

namespace RookHub.Api.Tests;

/// <summary>Taktik-Ernte (0.657.0): Regeln nach dem Lichess-Puzzler, Verlängern der Lösung, Kurs mit Kapitel je Ligarunde.</summary>
public class TacticHarvestTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private static Cand C(string uci, int? cp = null, int? mate = null) => new(uci, cp, mate, new[] { uci });
    private static Cand M(string uci, int mate, params string[] pv) => new(uci, null, mate, pv.Length > 0 ? pv : new[] { uci });

    // ── Regeln ──

    [Fact]
    public void Detect_BlunderThenOneClearlyBestMove_IsATactic_ElseNot()
    {
        var before = new[] { C("a2a3", cp: 20) };                         // Gegner am Zug, ausgeglichen
        var here = new[] { C("d5e7", cp: 650), C("d5c7", cp: 40) };       // jetzt: ein Zug gewinnt, der zweite nicht
        var f = TacticHarvest.Detect(before, here);
        Assert.NotNull(f);
        Assert.Equal(("material", "d5e7"), (f!.Kind, f.Best.Uci));

        Assert.Null(TacticHarvest.Detect(before, new[] { C("d5e7", cp: 650), C("d5c7", cp: 600) }));   // zwei gute Züge
        Assert.Null(TacticHarvest.Detect(before, new[] { C("d5e7", cp: 150), C("d5c7", cp: -300) }));  // Vorteil zu klein
        Assert.Null(TacticHarvest.Detect(new[] { C("a2a3", cp: -500) }, here));                         // stand schon klar besser

        var mate = TacticHarvest.Detect(before, new[] { C("a1a8", mate: 1), C("g1f2", cp: 50) });
        Assert.Equal(("mate", 1), (mate!.Kind, mate.MateIn));
        Assert.Null(TacticHarvest.Detect(before, new[] { C("a1a8", mate: 1), C("a1b1", mate: 3) }));   // zweites Matt
    }

    [Fact]
    public void Themes_Fork_Hanging_Mate()
    {
        Assert.Contains("fork", TacticHarvest.Themes("2q3k1/8/8/3N4/8/8/8/4K3 w - - 0 1", new[] { "d5e7" }, "material"));
        Assert.Contains("check", TacticHarvest.Themes("2q3k1/8/8/3N4/8/8/8/4K3 w - - 0 1", new[] { "d5e7" }, "material"));
        var hang = TacticHarvest.Themes("4k3/8/8/3q4/8/8/3R4/4K3 w - - 0 1", new[] { "d2d5" }, "material");
        Assert.Contains("hangingPiece", hang);
        Assert.Contains("oneMove", hang);
        Assert.Contains("mateIn1", TacticHarvest.Themes("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1", new[] { "a1a8" }, "mate"));
    }

    // ── Verlängern ──

    [Fact]
    public void Start_MateInOne_IsDoneAtOnce_FoundWhenTheGamePlayedIt()
    {
        var a = new GameAnalysis { Id = 1, Origin = GameAnalysisOrigin.Club };
        var f = new TacticHarvest.Found("mate", 1, C("a1a8", mate: 1));
        var c = TacticHarvestService.Start(a, "6k1/5ppp/8/8/8/8/7P/R5K1 b - - 0 1", "g8h8", 3, "7k/5ppp/8/8/8/8/7P/R5K1 w - - 1 2", "a1a8", f, DateTime.UtcNow);
        Assert.NotNull(c);
        Assert.Equal(TacticCandidateStatus.Done, c!.Status);
        Assert.True(c.Found);
        Assert.Equal("a1a8", c.Moves);
        Assert.Contains("mateIn1", c.Themes);
    }

    [Fact]
    public void Step_ExtendsWhileUnique_StopsAtTheLastUniqueMove()
    {
        // Matt in 2: 1. Qh7+?? — einfacher: Turm-Leiter. Weiß: Ta1, Tb2; Schwarz Kh8.
        const string fen = "7k/8/8/8/8/8/1R6/R5K1 w - - 0 1";
        var a = new GameAnalysis { Id = 1, Origin = GameAnalysisOrigin.Club };
        var f = new TacticHarvest.Found("mate", 2, M("b2b7", 2, "b2b7", "h8g8", "a1a8"));
        var c = TacticHarvestService.Start(a, "7k/8/8/8/8/8/1R6/R5K1 b - - 0 1", "h8h8", 1, fen, "g1g2", f, DateTime.UtcNow);
        Assert.Null(c);   // Fehler des Gegners geht nicht (h8h8) → kein Kandidat

        var prev = "6k1/8/8/8/8/8/1R6/R5K1 b - - 0 1";
        c = TacticHarvestService.Start(a, prev, "g8h8", 1, fen, "g1g2", f, DateTime.UtcNow)!;
        Assert.Equal(TacticCandidateStatus.Verifying, c.Status);
        Assert.False(c.Found);
        Assert.Equal("h8g8", c.PendingReplyUci);
        Assert.Equal("6k1/1R6/8/8/8/8/8/R5K1 w - - 2 2", c.NextFen);

        TacticHarvestService.Step(c, new[] { C("a1a8", mate: 1), C("b7b8", mate: 1) });   // zwei Matts: nicht eindeutig
        Assert.Equal(TacticCandidateStatus.Done, c.Status);
        Assert.Equal("b2b7", c.Moves);                                                     // endet beim letzten eindeutigen Zug
        Assert.DoesNotContain("mate", c.Themes);                                           // kein Matt erreicht
    }

    [Fact]
    public void Step_UniqueMate_AppendsReplyAndMove_AndFinishesOnMate()
    {
        var a = new GameAnalysis { Id = 1, Origin = GameAnalysisOrigin.Library };
        var c = TacticHarvestService.Start(a, "6k1/8/8/8/8/8/1R6/R5K1 b - - 0 1", "g8h8", 1, "7k/8/8/8/8/8/1R6/R5K1 w - - 0 1", "b2b7",
            new TacticHarvest.Found("mate", 2, M("b2b7", 2, "b2b7", "h8g8", "a1a8")), DateTime.UtcNow)!;
        Assert.True(c.Found);
        TacticHarvestService.Step(c, new[] { M("a1a8", 1, "a1a8"), C("b7b1", cp: 900) });
        Assert.Equal(TacticCandidateStatus.Done, c.Status);
        Assert.Equal("b2b7 h8g8 a1a8", c.Moves);
        Assert.Contains("mateIn2", c.Themes);
    }

    // ── Kurs ──

    private async Task<(GameAnalysis A, LeagueClubGame G)> SeedClubAsync()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" });
        _db.LeagueGames.Add(new LeagueGame
        {
            Tnr = 7, Round = 1, MatchNo = 1, Board = 4, HomeTeam = "Testdorf", AwayTeam = "Spg Fügen-Zillertal/Rattenberg",
            HomePlayer = "Streiter, Gerhard", AwayPlayer = "Moser, Axel", HomeFide = "1621530", AwayFide = "24602094",
            HomeColor = "s", Result = "½ - ½",
        });
        var g = new LeagueClubGame { Id = 158, Year = 2026, White = "Moser, Axel", WhiteFide = "24602094", Black = "Testdorf",
            ClubId = TestClubs.HomeId, Result = "1/2-1/2", Anonymized = true, Pgn = "", MovesHash = "h" };
        _db.LeagueClubGames.Add(g);
        var a = new GameAnalysis { Id = 5, UserId = 1, Origin = GameAnalysisOrigin.Club, LeagueClubGameId = 158, Pgn = "", StartFen = "x",
            Status = GameAnalysisStatus.Done, White = "Moser, Axel", Black = "Testdorf" };
        _db.GameAnalyses.Add(a);
        var role = new Role { Id = 3, Key = "verein" };
        _db.Roles.Add(role);
        _db.RolePermissions.Add(new RolePermission { RoleId = 3, Permission = Permissions.LeagueView });
        _db.Groups.Add(new Group { Id = 9, Name = "SK Testdorf" });
        _db.GroupRoles.Add(new GroupRole { GroupId = 9, RoleId = 3 });
        // Gruppe 9 gehört dem Verein; Gruppe 10 (auch league.view) einem anderen — sie sieht den Kurs nicht
        _db.Groups.Add(new Group { Id = 10, Name = "SK Weiler" });
        _db.GroupRoles.Add(new GroupRole { GroupId = 10, RoleId = 3 });
        _db.LeagueClubs.AddRange(TestClubs.Home, TestClubs.Other);
        _db.LeagueClubMembers.AddRange(new LeagueClubMember { ClubId = TestClubs.HomeId, GroupId = 9 },
            new LeagueClubMember { ClubId = TestClubs.OtherId, GroupId = 10 });
        await _db.SaveChangesAsync();
        return (a, g);
    }

    [Fact]
    public async Task LeagueRound_FromPlayersColoursAndSeason_AnonymousSideMatchesOwnClub()
    {
        var (_, g) = await SeedClubAsync();
        Assert.Equal("2026/27 · Landesliga · Runde 1", await TacticHarvestService.LeagueRoundChapterAsync(_db, g, TestClubs.Home, default));
        g.Year = 2019;   // andere Saison
        Assert.Null(await TacticHarvestService.LeagueRoundChapterAsync(_db, g, TestClubs.Home, default));
        g.Year = 2026; g.White = "Testdorf"; g.WhiteFide = null; g.Black = "Moser, Axel"; g.BlackFide = "24602094";   // Farben vertauscht
        Assert.Null(await TacticHarvestService.LeagueRoundChapterAsync(_db, g, TestClubs.Home, default));
    }

    private TacticHarvestService Svc() => new(_db, new AnalysisJobService(_db), new QuietHours("", "UTC"),
        new ConfigurationBuilder().Build(), NullLogger<TacticHarvestService>.Instance);

    [Fact]
    public async Task Publish_ClubCourse_ChapterPerLeagueRound_VisibleToTheClubGroup_RetiresOrphans()
    {
        var (a, _) = await SeedClubAsync();
        var c = new TacticCandidate
        {
            GameAnalysisId = a.Id, Ply = 41, Origin = GameAnalysisOrigin.Club, PrevFen = "6k1/8/8/8/8/8/1R6/R5K1 b - - 0 1",
            BlunderUci = "g8h8", Fen = "7k/8/8/8/8/8/1R6/R5K1 w - - 0 1", GameMoveUci = "g1g2", Found = false, Kind = "mate",
            Moves = "b2b7 h8g8 a1a8", Status = TacticCandidateStatus.Done, Themes = "mateIn2,short", EvalText = "#2",
        };
        _db.TacticCandidates.Add(c);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await Svc().PublishAsync(default));
        var book = await _db.Books.SingleAsync();
        Assert.Equal(("tactics-club-1.pgn", "Taktiken aus Vereinspartien – SK Testdorf", (int?)null), (book.FileName, book.DisplayName, book.OwnerUserId));
        Assert.Equal(9, (await _db.BookGroupAccesses.SingleAsync()).GroupId);
        var p = await _db.BookPuzzles.SingleAsync();
        Assert.Equal("2026/27 · Landesliga · Runde 1", p.Chapter);
        Assert.Equal("Moser, Axel – Streiter, Gerhard (2026), Zug 21", p.Title);   // Schwazer Spieler aus der Ligapaarung
        Assert.Equal(("g8h8 b2b7 h8g8 a1a8", 0, "6k1/8/8/8/8/8/1R6/R5K1 b - - 0 1"), (p.Moves, p.StartPly, p.Fen));
        Assert.StartsWith("In der Partie verpasst — gespielt wurde Kg2.", p.Comment);
        Assert.Contains("verpasst", p.Tags);
        Assert.Equal(TacticCandidateStatus.Published, (await _db.TacticCandidates.SingleAsync()).Status);

        // schon veröffentlicht mit dem Vereinsnamen (vor 0.657.2) → beim nächsten Lauf umbenannt
        p.Title = "Moser, Axel – Testdorf (2026), Zug 21";
        await _db.SaveChangesAsync();
        await Svc().PublishAsync(default);
        Assert.Equal("Moser, Axel – Streiter, Gerhard (2026), Zug 21", (await _db.BookPuzzles.SingleAsync()).Title);

        // Partie weg → Analyse weg → Taktik weg → Aufgabe stillgelegt
        _db.TacticCandidates.Remove(await _db.TacticCandidates.SingleAsync());
        await _db.SaveChangesAsync();
        await Svc().PublishAsync(default);
        Assert.True((await _db.BookPuzzles.SingleAsync()).Retired);
    }

    [Fact]
    public async Task Scan_FindsTacticsOnlyInClubMasterAndOwnGames_OncePerAnalysis()
    {
        void Analysis(int id, GameAnalysisOrigin origin)
        {
            _db.GameAnalyses.Add(new GameAnalysis { Id = id, UserId = 1, Origin = origin, Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done });
            _db.GameAnalysisPositions.AddRange(
                new GameAnalysisPosition { GameAnalysisId = id, Ply = 0, Fen = "6k1/8/8/8/8/8/1R6/R5K1 b - - 0 1", GameMoveUci = "g8h8", GameMoveSan = "Kh8",
                    CandidatesJson = "[{\"uci\":\"g8f8\",\"cp\":-50}]" },
                new GameAnalysisPosition { GameAnalysisId = id, Ply = 1, Fen = "7k/8/8/8/8/8/1R6/R5K1 w - - 0 1", GameMoveUci = "b2b7", GameMoveSan = "Rb7",
                    CandidatesJson = "[{\"uci\":\"b2b7\",\"mate\":2,\"pv\":[\"b2b7\",\"h8g8\",\"a1a8\"]},{\"uci\":\"g1g2\",\"cp\":800}]" });
        }
        Analysis(1, GameAnalysisOrigin.Club);
        Analysis(2, GameAnalysisOrigin.Guess);      // Punktepartie: privat, wird nicht geerntet
        await _db.SaveChangesAsync();

        Assert.Equal(1, await Svc().ScanAsync(default));
        var c = await _db.TacticCandidates.SingleAsync();
        Assert.Equal((1, 1, true, "b2b7"), (c.GameAnalysisId, c.Ply, c.Found, c.Moves));
        Assert.Equal(0, await Svc().ScanAsync(default));                                      // einmal je Analyse
        Assert.Null((await _db.GameAnalyses.SingleAsync(x => x.Id == 2)).TacticsScannedAt);
    }

    [Fact]
    public async Task Scan_ClubGamesFirst_NewestFirst()
    {
        for (var i = 1; i <= TacticHarvestService.ScanBatch + 2; i++)
            _db.GameAnalyses.Add(new GameAnalysis { Id = i, UserId = 1, Origin = GameAnalysisOrigin.Library, Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done });
        _db.GameAnalyses.Add(new GameAnalysis { Id = 500, UserId = 1, Origin = GameAnalysisOrigin.Club, Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done });
        _db.GameAnalyses.Add(new GameAnalysis { Id = 600, UserId = 1, Origin = GameAnalysisOrigin.Club, Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done });
        await _db.SaveChangesAsync();
        await Svc().ScanAsync(default);
        var scanned = await _db.GameAnalyses.Where(a => a.TacticsScannedAt != null).Select(a => a.Id).ToListAsync();
        Assert.Contains(500, scanned);
        Assert.Contains(600, scanned);
        Assert.DoesNotContain(1, scanned);                       // die ältesten Meisterpartien warten
        Assert.Equal(TacticHarvestService.ScanBatch, scanned.Count);
    }

    [Fact]
    public void Detect_RelaxedAcceptsWhatStrictRejects()
    {
        var before = new[] { C("g8f8", cp: -20) };
        var here = new[] { C("b2b7", cp: 250), C("g1g2", cp: 20) };
        Assert.Null(TacticHarvest.Detect(before, here));
        var found = TacticHarvest.Detect(before, here, TacticHarvest.Relaxed);
        Assert.Equal("material", found!.Kind);
        Assert.True(TacticHarvest.IsUnique(here, false, TacticHarvest.Relaxed));
        Assert.False(TacticHarvest.IsUnique(here, false));
    }

    [Fact]
    public async Task Scan_RelaxedAndLc0Variants_NoDuplicateForTheSameSpot()
    {
        void Analysis(int id, int gameId, string? engine)
        {
            _db.GameAnalyses.Add(new GameAnalysis { Id = id, UserId = 1, Origin = GameAnalysisOrigin.Club, LeagueClubGameId = gameId, EngineId = engine,
                Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done });
            _db.GameAnalysisPositions.AddRange(
                new GameAnalysisPosition { GameAnalysisId = id, Ply = 0, Fen = "6k1/8/8/8/8/8/1R6/R5K1 b - - 0 1", GameMoveUci = "g8h8", GameMoveSan = "Kh8",
                    CandidatesJson = "[{\"uci\":\"g8f8\",\"cp\":-20}]" },
                new GameAnalysisPosition { GameAnalysisId = id, Ply = 1, Fen = "7k/8/8/8/8/8/1R6/R5K1 w - - 0 1", GameMoveUci = "b2b7", GameMoveSan = "Rb7",
                    CandidatesJson = "[{\"uci\":\"b2b7\",\"cp\":250,\"pv\":[\"b2b7\",\"h8g8\"]},{\"uci\":\"g1g2\",\"cp\":20}]" });
        }
        Analysis(1, 10, null);          // Stockfish: nur locker → "relaxed"
        Analysis(2, 10, "rhe_lc0");     // Lc0 derselben Partie, dieselbe Stelle → nicht noch einmal
        Analysis(3, 11, "rhe_lc0");     // Lc0 einer anderen Partie → "lc0", Lösung = der eine erste Zug, gleich fertig
        await _db.SaveChangesAsync();

        Assert.Equal(2, await Svc().ScanAsync(default));
        var all = await _db.TacticCandidates.OrderBy(c => c.GameAnalysisId).ToListAsync();
        Assert.Equal(new[] { 1, 3 }, all.Select(c => c.GameAnalysisId));
        Assert.Equal("relaxed", all[0].Variant);
        Assert.Equal(TacticCandidateStatus.Verifying, all[0].Status);
        Assert.Equal("lc0", all[1].Variant);
        Assert.Equal((TacticCandidateStatus.Done, "b2b7"), (all[1].Status, all[1].Moves));
    }
}
