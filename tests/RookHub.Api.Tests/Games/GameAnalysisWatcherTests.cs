using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;
using RookHub.Api.DTOs.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>
/// Glocke „Analyse fertig" (0.904.0): wer „Partie analysieren" klickt oder eine Partie hochlädt (RookHub oder LeagueHub),
/// bekommt GENAU EINE Glocke, sobald die Analyse endgültig — samt Vertiefung — durch ist; nicht nach dem schnellen ersten
/// Durchgang, nicht für schon fertige Analysen, nicht für Stapel-/Lc0-Analysen ohne Interessenten.
/// </summary>
public class GameAnalysisWatcherTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GameAnalysisService _analyses;
    private readonly SavedGameService _games;

    public GameAnalysisWatcherTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _analyses = TestServices.GameAnalyses(_db);
        _games = TestServices.SavedGames(_db, _analyses);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> UserAsync(string name, bool engine = true)
    {
        var user = new AppUser { Username = name, Email = $"{name}@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        if (engine)
        {
            _db.LichessEngineCredentials.Add(new LichessEngineCredential { UserId = user.Id, EncryptedToken = "enc", BackgroundEngineIds = $"eei_{name}" });
            await _db.SaveChangesAsync();
        }
        return user;
    }

    private Task<SavedGameDetailDto> SaveAsync(int userId) => _games.SaveAsync(userId, new SaveGameInputDto
    {
        Source = "lichess", Moves = new() { "e4", "c5", "Nf3", "d6" }, White = "Anna", Black = "Bert", Result = "1-0", ExternalId = "g-1",
    });

    /// <summary>Erster Durchgang fertig: jede Stellung hat Kandidaten, kein Auftrag offen — dann ein Pumpenschritt.</summary>
    private async Task FinishFirstPassAsync(int analysisId)
    {
        foreach (var p in await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == analysisId).ToListAsync())
            (p.CandidatesJson, p.AnalysisJobId) = ("[]", null);
        await _db.SaveChangesAsync();
        await _analyses.PumpOneAsync(analysisId);
    }

    /// <summary>Vertiefung fertig: jede Stellung vertieft — dann ein Pumpenschritt.</summary>
    private async Task FinishRefinementAsync(int analysisId)
    {
        foreach (var p in await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == analysisId).ToListAsync())
            (p.Refined, p.AnalysisJobId) = (true, null);
        await _db.SaveChangesAsync();
        await _analyses.PumpOneAsync(analysisId);
    }

    private Task<List<Notification>> BellsAsync() =>
        _db.Notifications.Where(n => n.Type == NotificationType.GameAnalysisFinished).OrderBy(n => n.UserId).ToListAsync();

    /// <summary>Eine Analyse wie vom Stapel: Stellungen mit Kandidaten, Status „läuft", ohne Vertiefung.</summary>
    private async Task<GameAnalysis> BatchAnalysisAsync(int ownerId, GameAnalysisOrigin origin, int? clubGameId = null, string? engineId = null)
    {
        var a = new GameAnalysis
        {
            UserId = ownerId, Pgn = "1. e4 e5 *", White = "Schwaz", Black = "Gegner", StartFen = "start", Origin = origin,
            Status = GameAnalysisStatus.Running, LeagueClubGameId = clubGameId, EngineId = engineId, PlyCount = 2,
            Positions =
            {
                new GameAnalysisPosition { Ply = 0, Fen = "f0", GameMoveUci = "e2e4", GameMoveSan = "e4", CandidatesJson = "[]" },
                new GameAnalysisPosition { Ply = 1, Fen = "f1", GameMoveUci = "e7e5", GameMoveSan = "e5", CandidatesJson = "[]" },
            },
        };
        _db.GameAnalyses.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    [Fact]
    public void IsFinal_needsDone_andNoPendingRefinement()
    {
        Assert.True(GameAnalysisWatchers.IsFinal(GameAnalysisStatus.Done, null, null));
        Assert.True(GameAnalysisWatchers.IsFinal(GameAnalysisStatus.Done, 30, DateTime.UtcNow));
        Assert.False(GameAnalysisWatchers.IsFinal(GameAnalysisStatus.Done, 30, null));
        Assert.False(GameAnalysisWatchers.IsFinal(GameAnalysisStatus.Running, null, null));
        Assert.False(GameAnalysisWatchers.IsFinal(GameAnalysisStatus.Failed, null, null));
    }

    [Fact]
    public async Task AnalyzeClick_notifiesOnce_onlyAfterTheRefinement()
    {
        var anna = await UserAsync("anna");
        var saved = await SaveAsync(anna.Id);

        var result = (await _games.AnalyzeAsync(anna.Id, saved.Id))!;
        var analysisId = result.Analysis!.Id;
        Assert.Single(await _db.GameAnalysisWatchers.ToListAsync());

        await FinishFirstPassAsync(analysisId);
        Assert.Empty(await BellsAsync());                                         // erster Durchgang ist noch nicht „tief"

        await FinishRefinementAsync(analysisId);
        var bell = Assert.Single(await BellsAsync());
        Assert.Equal((anna.Id, $"/games/{saved.Id}"), (bell.UserId, bell.Link));
        Assert.Contains("\"white\":\"Anna\"", bell.DataJson);
        Assert.Empty(await _db.GameAnalysisWatchers.ToListAsync());

        await _analyses.PumpOneAsync(analysisId);
        Assert.Single(await BellsAsync());                                        // genau einmal
    }

    [Fact]
    public async Task AnalyzeClick_onAFinishedAnalysis_ringsNothing()
    {
        var anna = await UserAsync("anna");
        var saved = await SaveAsync(anna.Id);
        var analysisId = (await _games.AnalyzeAsync(anna.Id, saved.Id))!.Analysis!.Id;
        await FinishFirstPassAsync(analysisId);
        await FinishRefinementAsync(analysisId);
        _db.Notifications.RemoveRange(_db.Notifications);
        await _db.SaveChangesAsync();

        var again = (await _games.AnalyzeAsync(anna.Id, saved.Id))!;

        Assert.True(again.Reused);
        Assert.Empty(await _db.GameAnalysisWatchers.ToListAsync());
        await _analyses.PumpOneAsync(analysisId);
        Assert.Empty(await BellsAsync());
    }

    [Fact]
    public async Task SharedAnalysis_twoUsers_bothNotified_eachWithTheirOwnLink()
    {
        var anna = await UserAsync("anna");
        var gast = await UserAsync("gast");
        var saved = await SaveAsync(anna.Id);
        var analysisId = (await _games.AnalyzeAsync(anna.Id, saved.Id))!.Analysis!.Id;

        var guest = (await _games.AnalyzeSharedAsync(gast.Id, saved.ShareToken!))!;
        Assert.Equal(analysisId, guest.Analysis!.Id);                             // dieselbe Rechnung
        await FinishFirstPassAsync(analysisId);
        await FinishRefinementAsync(analysisId);

        var bells = await BellsAsync();
        Assert.Equal(new[] { (anna.Id, $"/games/{saved.Id}"), (gast.Id, $"/g/{saved.ShareToken}") },
            bells.Select(b => (b.UserId, b.Link!)).OrderBy(x => x.UserId).ToArray());
    }

    [Fact]
    public async Task BatchAnalysis_withoutInterestedUser_ringsNothing()
    {
        var owner = await UserAsync("stapel", engine: false);
        var a = await BatchAnalysisAsync(owner.Id, GameAnalysisOrigin.Library);

        await _analyses.PumpOneAsync(a.Id);

        Assert.Equal(GameAnalysisStatus.Done, (await _db.GameAnalyses.SingleAsync()).Status);
        Assert.Empty(await BellsAsync());
    }

    [Fact]
    public async Task LeagueHubUpload_schwazGame_uploaderNotified_butNotByTheLc0SecondAnalysis()
    {
        var owner = await UserAsync("stapel", engine: false);
        var me = await SeedLeagueAsync();
        var r = await new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance, () => Now)
            .ImportPgnAsync(TestClubs.Home, me, LeaguePgn, null);
        Assert.Equal((1, 1), (r.Added, r.Anonymized));
        var game = await _db.LeagueClubGames.SingleAsync();
        Assert.Null(game.UploadedByUserId);                                       // „Schwaz": der Hochladende steht nicht an der Partie …
        var row = await _db.GameAnalysisWatchers.SingleAsync();                   // … aber die Glocke ist vorgemerkt
        Assert.Equal((me, game.Id, $"/club-games/{game.Id}"), (row.UserId, row.LeagueClubGameId, row.Link));

        var lc0 = await BatchAnalysisAsync(owner.Id, GameAnalysisOrigin.Club, game.Id, engineId: "eei_lc0");
        await _analyses.PumpOneAsync(lc0.Id);
        Assert.Empty(await BellsAsync());                                         // die Zweitanalyse meldet nie

        var main = await BatchAnalysisAsync(owner.Id, GameAnalysisOrigin.Club, game.Id);
        await _analyses.PumpOneAsync(main.Id);
        var bell = Assert.Single(await BellsAsync());
        Assert.Equal((me, $"/club-games/{game.Id}"), (bell.UserId, bell.Link));
        Assert.Empty(await _db.GameAnalysisWatchers.ToListAsync());
    }

    [Fact]
    public async Task Erasure_removesTheUsersWatchRows()
    {
        var anna = await UserAsync("anna");
        var saved = await SaveAsync(anna.Id);
        await _games.AnalyzeAsync(anna.Id, saved.Id);
        Assert.Single(await _db.GameAnalysisWatchers.ToListAsync());

        await TestServices.Admin(_db).DeleteUserAsync(anna.Id, currentUserId: 999_001);

        Assert.Empty(await _db.GameAnalysisWatchers.ToListAsync());
    }

    [Fact]
    public async Task PurgeStale_dropsRowsOlderThanSixtyDays()
    {
        var anna = await UserAsync("anna");
        var a = await BatchAnalysisAsync(anna.Id, GameAnalysisOrigin.Manual);
        var now = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
        _db.GameAnalysisWatchers.AddRange(
            new GameAnalysisWatcher { UserId = anna.Id, GameAnalysisId = a.Id, Link = "/a", CreatedAt = now.AddDays(-61) },
            new GameAnalysisWatcher { UserId = anna.Id, LeagueClubGameId = 5, Link = "/b", CreatedAt = now.AddDays(-1) });
        await _db.SaveChangesAsync();

        Assert.Equal(1, await new GameAnalysisWatchers(_db).PurgeStaleAsync(now));
        Assert.Equal("/b", (await _db.GameAnalysisWatchers.SingleAsync()).Link);
    }

    // ── LeagueHub-Upload (wie LeagueClubServiceTests) ──

    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private const string LeaguePgn =
        "[Event \"Vereinsmeisterschaft\"]\n[Date \"2024.05.12\"]\n[White \"Oberschmid, Patrik\"]\n[Black \"Hengl, Philip\"]\n[Result \"1-0\"]\n\n"
        + "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 1-0\n";

    private async Task<int> SeedLeagueAsync()
    {
        void Player(string team, string name, string fide) =>
            _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = team, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });
        Player("Testdorf", "Oberschmid, Patrik", "900");
        Player("Absam", "Hengl, Philip", "222");
        var u = new AppUser { Username = "patrik", Email = "p@test", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        _db.UserProfiles.Add(new UserProfile { UserId = u.Id, LastName = "Oberschmid", FirstName = "Patrik" });
        await _db.SaveChangesAsync();
        return u.Id;
    }
}
