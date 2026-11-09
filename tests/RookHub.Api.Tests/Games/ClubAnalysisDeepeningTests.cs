using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Library;
using RookHub.Api.DTOs.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>
/// Vereinspartien vertiefen auf 30 (0.909.0, Wunsch 2026-11-08: „ja, vertiefe Ligapartien auf 30 und vergleiche auch 30"):
/// neue Hauptanalysen bekommen die Vertiefung wie „Partie analysieren", der Altbestand per Datenmigration; die Vertiefung
/// läuft als Stapel-Hintergrundarbeit, die eigene Partie des Haus-Kontos steht nicht dahinter; Glocke und Zugvergleiche
/// kommen erst nach der Vertiefung, die Vergleiche mit Tiefe 30.
/// </summary>
public class ClubAnalysisDeepeningTests : IDisposable
{
    private const int Owner = 5;
    private const int Fan = 7;
    private readonly AppDbContext _db;

    private const string Short = "[White \"Morphy\"]\n[Black \"Duke\"]\n[Result \"*\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 *";

    public ClubAnalysisDeepeningTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.AppUsers.Add(new AppUser { Id = Owner, Username = "kahalm", PasswordHash = "x", IsAdmin = true });
        _db.AppUsers.Add(new AppUser { Id = Fan, Username = "fan", PasswordHash = "x" });
        var cred = new LichessEngineCredential { UserId = Owner, EncryptedToken = "enc", ShareAsHouseEngine = true };
        cred.SetBackgroundEngines(Enumerable.Range(1, 2).Select(i => $"rhe_t{i}"));
        _db.LichessEngineCredentials.Add(cred);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private GameAnalysisService Analyses() =>
        new(_db, new AnalysisJobService(_db), new CommentSetService(_db, NullLogger<CommentSetService>.Instance),
            NullLogger<GameAnalysisService>.Instance, quiet: new QuietHours(""));

    private LeagueClubGame ClubGame()
    {
        var g = new LeagueClubGame { ClubId = TestClubs.HomeId, White = "Morphy", Black = "Duke", Pgn = Short, Plies = 10, Year = 2026,
            MovesHash = Guid.NewGuid().ToString("N") };
        _db.LeagueClubGames.Add(g);
        _db.SaveChanges();
        return g;
    }

    private async Task<List<AnalysisJob>> OpenJobsAsync(int analysisId)
    {
        var ids = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == analysisId && p.AnalysisJobId != null).Select(p => p.AnalysisJobId!.Value).ToListAsync();
        return await _db.AnalysisJobs.AsNoTracking().Where(j => ids.Contains(j.Id)).ToListAsync();
    }

    /// <summary>Alle offenen Aufträge der Analyse mit einem ruhigen Ergebnis (gespielter Zug = bester) abschließen, dann pumpen.</summary>
    private async Task FinishRoundAsync(GameAnalysisService svc, int analysisId, int depth)
    {
        foreach (var pos in await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == analysisId && p.AnalysisJobId != null).ToListAsync())
        {
            var job = await _db.AnalysisJobs.FirstAsync(j => j.Id == pos.AnalysisJobId);
            var best = string.IsNullOrEmpty(pos.GameMoveUci) ? "a2a3" : pos.GameMoveUci;
            job.Status = AnalysisJobStatus.Done;
            job.ReachedDepth = depth;
            job.ResultJson = "{\"depth\":" + depth + ",\"pvs\":[{\"depth\":" + depth + ",\"cp\":10,\"moves\":[\"" + best + "\"]}]}";
        }
        await _db.SaveChangesAsync();
        await svc.PumpOneAsync(analysisId);
        _db.ChangeTracker.Clear();
    }

    private Task<GameAnalysis> HeadAsync(int id) => _db.GameAnalyses.AsNoTracking().FirstAsync(g => g.Id == id);

    [Fact]
    public async Task NeueVereinspartie_hatVertiefung30_Lc0UndLigaNicht()
    {
        var svc = Analyses();
        var club = ClubGame();

        var main = await HeadAsync((await svc.CreateClubBatchAsync(Owner, club.Id, Short)).Id);
        Assert.Equal((20, 5, (int?)30, (int?)5), (main.TargetDepth, main.MultiPv, main.RefineDepth, main.RefineMultiPv));
        Assert.Equal(GameAnalysisDefaults.SavedGameTargetDepth, GameAnalysisDefaults.ClubRefineDepth);
        Assert.Null(main.RefinedAt);

        // Die Lc0-Zweitanalyse legt ClubSecondEngineScheduler über CreateAsync mit fester Engine an — ohne Vertiefung.
        var lc0 = await HeadAsync((await svc.CreateAsync(Owner, new CreateGameAnalysisRequest { Pgn = Short, EngineId = "rhe_t1" },
            default, GameAnalysisOrigin.Club, engineOwnerUserId: Owner, leagueClubGameId: club.Id)).Id);
        Assert.Null(lc0.RefineDepth);

        var league = await HeadAsync((await svc.CreateLeagueBatchAsync(Owner, Short, "hash-league")).Id);
        Assert.Null(league.RefineDepth);
    }

    [Fact]
    public async Task Vertiefung_alsStapelHintergrund_GlockeUndZugvergleicheErstDanach_Klassen30()
    {
        var svc = Analyses();
        var club = ClubGame();
        await new GameAnalysisWatchers(_db).WatchClubGamesAsync(Fan, [club.Id]);
        var id = (await svc.CreateClubBatchAsync(Owner, club.Id, Short)).Id;

        for (var round = 0; round < 5 && (await HeadAsync(id)).Status != GameAnalysisStatus.Done; round++)
            await FinishRoundAsync(svc, id, 20);
        var head = await HeadAsync(id);
        Assert.Equal(GameAnalysisStatus.Done, head.Status);
        Assert.Null(head.RefinedAt);
        Assert.Null(head.MistakeComparisonsAt);                                       // keine Vergleiche auf Tiefe-20-Zahlen
        Assert.Empty(_db.Notifications);                                              // keine Glocke nach dem ersten Durchgang
        Assert.Single(_db.GameAnalysisWatchers);

        await svc.PumpOneAsync(id);                                                   // legt die Vertiefung an
        _db.ChangeTracker.Clear();
        var refine = await OpenJobsAsync(id);
        Assert.NotEmpty(refine);
        Assert.All(refine, j => { Assert.True(j.Background); Assert.True(j.Batch); Assert.Equal(30, j.TargetDepth); Assert.Equal(5, j.MultiPv); });

        for (var round = 0; round < 5 && (await HeadAsync(id)).RefinedAt == null; round++)
            await FinishRoundAsync(svc, id, 30);
        head = await HeadAsync(id);
        Assert.NotNull(head.RefinedAt);
        Assert.NotNull(head.MistakeComparisonsAt);                                    // eingereiht am endgültigen Ende
        Assert.Equal(GameMoveClasses.Version, head.MoveClassesVersion);              // Klassen aus den Zahlen der Tiefe 30
        Assert.All(await _db.GameAnalysisPositions.AsNoTracking().Where(p => p.GameAnalysisId == id).ToListAsync(),
            p => Assert.True(p.Refined));
        var bell = Assert.Single(await _db.Notifications.ToListAsync());
        Assert.Equal((Fan, NotificationType.GameAnalysisFinished), (bell.UserId, bell.Type));
    }

    /// <summary>Eine fertige Analyse mit offener Vertiefung, Stellungen gerechnet, nichts vertieft.</summary>
    private async Task<GameAnalysis> RefiningAsync(GameAnalysisOrigin origin, DateTime created, int? clubGameId = null, bool expedited = false)
    {
        var a = new GameAnalysis
        {
            UserId = Owner, EngineOwnerUserId = origin == GameAnalysisOrigin.SavedGame ? Owner : null, Pgn = Short, StartFen = "start",
            Origin = origin, Status = GameAnalysisStatus.Done, RefineDepth = 30, RefineMultiPv = 5, LeagueClubGameId = clubGameId, Expedited = expedited,
            PlyCount = 2, CreatedAt = created, FinishedAt = created,
            Positions =
            {
                new GameAnalysisPosition { Ply = 0, Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", GameMoveUci = "e2e4", CandidatesJson = "[]" },
                new GameAnalysisPosition { Ply = 1, Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", GameMoveUci = "e7e5", CandidatesJson = "[]" },
                new GameAnalysisPosition { Ply = 2, Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", GameMoveUci = "", CandidatesJson = "[]" },
            },
        };
        _db.GameAnalyses.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    [Fact]
    public async Task EigenePartieDesHausKontos_wartetNichtAufDieVertiefungDerVereinspartien()
    {
        var svc = Analyses();
        await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow.AddDays(-10), ClubGame().Id);   // Altbestand, älter
        var own = await RefiningAsync(GameAnalysisOrigin.SavedGame, DateTime.UtcNow);

        await svc.PumpOneAsync(own.Id);
        _db.ChangeTracker.Clear();
        var jobs = await OpenJobsAsync(own.Id);
        Assert.NotEmpty(jobs);
        Assert.All(jobs, j => Assert.False(j.Batch));
    }

    [Fact]
    public async Task Vereinspartien_wartenAufDieEigeneVertiefung_unterEinanderDieJuengsteZuerst()
    {
        var svc = Analyses();
        var old = await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow.AddDays(-10), ClubGame().Id);
        var fresh = await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow.AddDays(-1), ClubGame().Id);

        await svc.PumpOneAsync(old.Id);
        await svc.PumpOneAsync(fresh.Id);
        _db.ChangeTracker.Clear();
        Assert.Empty(await OpenJobsAsync(old.Id));                                    // hinter der jüngeren (3 Stellungen ≥ 2 Engines)
        Assert.NotEmpty(await OpenJobsAsync(fresh.Id));

        // Eine eigene Partie mit offener Vertiefung geht beiden vor.
        var own = await RefiningAsync(GameAnalysisOrigin.SavedGame, DateTime.UtcNow);
        var club3 = await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow.AddMinutes(1), ClubGame().Id);
        await svc.PumpOneAsync(club3.Id);
        _db.ChangeTracker.Clear();
        Assert.Empty(await OpenJobsAsync(club3.Id));
        Assert.NotNull(own);
    }

    [Fact]
    public async Task EilendeVereinspartie_wirdSofortVertieft_trotzJuengererPartienDesStapels()
    {
        var svc = Analyses();
        var expedited = await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow.AddDays(-10), ClubGame().Id, expedited: true);
        var waiting = await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow.AddDays(-9), ClubGame().Id);
        await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow.AddDays(-1), ClubGame().Id);

        await svc.PumpOneAsync(expedited.Id);
        await svc.PumpOneAsync(waiting.Id);
        _db.ChangeTracker.Clear();

        var jobs = await OpenJobsAsync(expedited.Id);
        Assert.NotEmpty(jobs);
        Assert.All(jobs, j => Assert.Equal((true, false, JobPriority.Waiting), (j.Background, j.Batch, j.Priority)));
        Assert.Empty(await OpenJobsAsync(waiting.Id));                                // ohne Eile weiter hinter der jüngeren
    }

    [Fact]
    public async Task EilendeVereinspartie_wartetNichtAufDenErstenDurchgangEigenerPartien()
    {
        var svc = Analyses();
        var expedited = await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow.AddDays(-10), ClubGame().Id, expedited: true);
        var waiting = await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow.AddDays(-1), ClubGame().Id);
        var own = await RefiningAsync(GameAnalysisOrigin.SavedGame, DateTime.UtcNow);
        own.Status = GameAnalysisStatus.Running;                                      // 3 offene Stellungen ≥ 2 Engines
        foreach (var p in own.Positions) p.CandidatesJson = null;
        await _db.SaveChangesAsync();

        await svc.PumpOneAsync(expedited.Id);
        await svc.PumpOneAsync(waiting.Id);
        _db.ChangeTracker.Clear();

        Assert.NotEmpty(await OpenJobsAsync(expedited.Id));
        Assert.Empty(await OpenJobsAsync(waiting.Id));
    }

    [Fact]
    public async Task Vertiefung_derVereinspartie_inDerSperrzeit_reihtNichtsEin()
    {
        var quiet = new QuietHours(QuietHours.DefaultSpec, "Europe/Vienna",
            new QuietHoursTests.ManualTime { Now = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero) });
        var svc = new GameAnalysisService(_db, new AnalysisJobService(_db), new CommentSetService(_db, NullLogger<CommentSetService>.Instance),
            NullLogger<GameAnalysisService>.Instance, quiet: quiet);
        var a = await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow, ClubGame().Id);

        await svc.PumpOneAsync(a.Id);
        _db.ChangeTracker.Clear();
        Assert.Empty(await OpenJobsAsync(a.Id));
    }

    [Fact]
    public async Task ZugvergleichNachtrag_ueberspringtVereinspartienMitOffenerVertiefung()
    {
        var a = await RefiningAsync(GameAnalysisOrigin.Club, DateTime.UtcNow, ClubGame().Id);
        var svc = new MoveComparisonService(_db, new AnalysisJobService(_db), new NoLlmClient(), new MoveComparisonExplainJobs(),
            null!, NullLogger<MoveComparisonService>.Instance);
        var club = new ClubMistakeComparisons(_db, svc, NullLogger<ClubMistakeComparisons>.Instance);

        Assert.False(await club.BackfillStepAsync(default));
        Assert.Null((await HeadAsync(a.Id)).MistakeComparisonsAt);
        Assert.Equal(30, ClubMistakeComparisons.DefaultDepth);
    }

    /// <summary>Der Altbestand: die Datenmigration trifft genau die Hauptanalysen der Vereinspartien ohne Vertiefung.</summary>
    [Fact]
    public void Datenmigration_setztVertiefung_nurAufHauptanalysenDerVereinspartien()
    {
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var migration = new RookHub.Api.Migrations.DeepenClubAnalyses();
        typeof(Migration).GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        var sql = Assert.IsType<SqlOperation>(Assert.Single(builder.Operations)).Sql;
        Assert.Contains("SET RefineDepth = 30, RefineMultiPv = 5, RefinedAt = NULL", sql);
        Assert.Contains("Origin = 4", sql);
        Assert.Contains("EngineId IS NULL", sql);
        Assert.Contains("RefineDepth IS NULL", sql);
        Assert.Equal(4, (int)GameAnalysisOrigin.Club);
    }

    private sealed class NoLlmClient : IClaudeJsonClient
    {
        public bool IsConfigured => false;
        public bool IsLocal => false;
        public string TranslationModel => "none";
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, System.Text.Json.Nodes.JsonNode schema, int maxTokens,
            CancellationToken ct = default) => Task.FromResult<string?>(null);
    }
}
