using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

public class AnalysisJobServiceTests : IDisposable
{
    private const string START = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly FakeControl _control = new();
    private readonly AnalysisJobService _svc;

    private sealed class FakeControl : IAnalysisJobControl
    {
        public List<int> Interrupted { get; } = new();
        public List<string> Preempted { get; } = new();
        public void Interrupt(int jobId) => Interrupted.Add(jobId);
        public void PreemptBackground(string engineId) => Preempted.Add(engineId);
    }

    public AnalysisJobServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build();
        _encryption = new EncryptionService(config);
        _svc = new AnalysisJobService(_db, _control);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sp?.Dispose();
    }

    private async Task<int> UserWithBackgroundEngineAsync(int id = 5, string? engine = "eei_bg")
    {
        _db.AppUsers.Add(new AppUser { Id = id, Username = $"u{id}", PasswordHash = "x" });
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = id, EncryptedToken = _encryption.Encrypt("lip_tok"), BackgroundEngineIds = engine,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    // ── Vorrang (2026-09-28): ein normaler Auftrag verdraengt Hintergrundarbeit auf seiner Engine ─────────────

    [Fact]
    public async Task Create_normalerAuftrag_verdraengtDenHintergrundAufSeinerEngine()
    {
        var u = await UserWithBackgroundEngineAsync();
        await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 1 });
        Assert.Equal(new[] { "eei_bg" }, _control.Preempted);
    }

    [Fact]
    public async Task CreateMany_normaleAuftraege_verdraengenDenHintergrundEinmal_wieDerEinzelauftrag()
    {
        // A4-007: die Mehrfachauswahl auf dem Analysebrett legte normale Aufträge an, ohne zu verdrängen — sie warteten,
        // bis die laufende Vertiefung (Tiefe 30, fünf Linien) fertig war.
        var u = await UserWithBackgroundEngineAsync();
        var res = await _svc.CreateManyAsync(u, new CreateAnalysisJobsBatchRequest
        {
            Fens = [START, "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1"], TargetDepth = 20, MultiPv = 1,
        });
        Assert.Equal(2, res.Created.Count);
        Assert.Equal(new[] { "eei_bg" }, _control.Preempted);

        // Nichts angelegt (alles Dubletten) → nichts verdrängt.
        _control.Preempted.Clear();
        await _svc.CreateManyAsync(u, new CreateAnalysisJobsBatchRequest { Fens = [START], TargetDepth = 20, MultiPv = 1 });
        Assert.Empty(_control.Preempted);
    }

    [Fact]
    public async Task Create_HintergrundAuftrag_verdraengtNichts()
    {
        var u = await UserWithBackgroundEngineAsync();
        await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 30, MultiPv = 5 },
            remember: false, background: true);
        Assert.Empty(_control.Preempted);
    }

    [Fact]
    public async Task List_zeigtKeineAuftraegeDerMeisterpartienAnalyse()
    {
        var u = await UserWithBackgroundEngineAsync();
        var own = await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 1 });
        var master = await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 5 },
            remember: false, background: true);
        var analysis = new GameAnalysis { UserId = u, Title = "Meister", Pgn = "1. e4 *", Origin = GameAnalysisOrigin.Library };
        analysis.Positions.Add(new GameAnalysisPosition { Ply = 0, Fen = START, GameMoveUci = "e2e4", AnalysisJobId = master.Id });
        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync();

        var list = await _svc.ListAsync(u);
        Assert.Equal(new[] { own.Id }, list.Select(j => j.Id));
    }

    /// <summary>
    /// Die Unterabfrage der Liste sieht nur die Stapel-Analysen des EIGENEN Kontos (Codereview A4-016): die Pumpe
    /// legt deren Auftraege mit <c>analysis.UserId</c> an, und nur so traegt der Index (UserId, CreatedAt) — ohne die
    /// Einschraenkung lief jeder Aufruf per Semijoin ueber alle Meister-/Vereinsstellungen. Echte Kennungen kollidieren
    /// nicht; der Test haengt die Kennung des eigenen Auftrags deshalb absichtlich an eine FREMDE Stapel-Stellung
    /// (wie ein verwaister Verweis — AnalysisJobId hat keinen Fremdschluessel).
    /// </summary>
    [Fact]
    public async Task List_StapelAnalyseEinesAnderenKontos_blendetNichtsAus()
    {
        var u = await UserWithBackgroundEngineAsync();
        var house = await UserWithBackgroundEngineAsync(id: 6);
        var own = await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 1 });
        foreach (var origin in new[] { GameAnalysisOrigin.Library, GameAnalysisOrigin.Club })
        {
            var analysis = new GameAnalysis { UserId = house, Title = "Stapel", Pgn = "1. e4 *", Origin = origin };
            analysis.Positions.Add(new GameAnalysisPosition { Ply = 0, Fen = START, GameMoveUci = "e2e4", AnalysisJobId = own.Id });
            _db.GameAnalyses.Add(analysis);
        }
        await _db.SaveChangesAsync();

        var list = await _svc.ListAsync(u);
        Assert.Equal(new[] { own.Id }, list.Select(j => j.Id));
    }

    /// <summary>
    /// Die Deckel, die CLAUDE.md an mehreren Stellen nennt (Codereview A4-012 fand dort fuenf veraltete Zahlen):
    /// wer hier dreht, zieht CLAUDE.md mit („Hintergrund-Analyseauftraege", „Punktepartie", REST-Tabelle). Und der
    /// Deckel je Nutzer muss ueber dem Block je Partie bleiben — sonst bindet er, und die Blockgroesse ist wirkungslos.
    /// </summary>
    [Fact]
    public void Deckel_wieInClaudeMd_undNutzerdeckelUeberDemBlock()
    {
        Assert.Equal(150, AnalysisJobService.MaxOpenJobsPerUser);
        Assert.Equal(32, GameAnalysisDefaults.MaxOpenJobsPerGame);
        Assert.Equal(5, AnalysisJobService.MaxMultiPv);
        Assert.True(AnalysisJobService.MaxOpenJobsPerUser > GameAnalysisDefaults.MaxOpenJobsPerGame);
    }

    [Fact]
    public async Task Create_UsesBackgroundEngine_AndDefaults()
    {
        var u = await UserWithBackgroundEngineAsync();
        var dto = await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 30, MultiPv = 3 });

        Assert.Equal("eei_bg", dto.EngineId);
        Assert.Equal("queued", dto.Status);
        Assert.Equal(0, dto.ReachedDepth);
        Assert.Null(dto.ResultJson);
    }

    [Fact]
    public async Task Create_AddsRememberedPosition_OnceperPosition_WithTitleAndInternalLink()
    {
        var u = await UserWithBackgroundEngineAsync();
        await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 30, MultiPv = 3, Title = "Kritisch" });
        var remembered = await _db.RememberedPositions.SingleAsync();
        Assert.Equal(START, remembered.Fen);
        Assert.Equal("Kritisch", remembered.CourseName);
        Assert.Equal(AnalysisJobService.RememberedSourceUrl, remembered.SourceUrl);

        // Zweiter Auftrag für dieselbe Stellung (andere Zugzähler) → kein zweiter Merk-Eintrag.
        var sameButCounters = START.Replace(" 0 1", " 3 7");
        await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = sameButCounters, TargetDepth = 40, MultiPv = 1 });
        Assert.Equal(1, await _db.RememberedPositions.CountAsync());
        Assert.Equal(2, await _db.AnalysisJobs.CountAsync());
    }

    [Fact]
    public async Task Create_ReusesExistingRememberedPosition_FromChessable()
    {
        var u = await UserWithBackgroundEngineAsync();
        _db.RememberedPositions.Add(new RememberedPosition { UserId = u, Fen = START, CourseId = "12345", CourseName = "Kurs" });
        await _db.SaveChangesAsync();

        await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 30, MultiPv = 3 });

        var remembered = await _db.RememberedPositions.SingleAsync();
        Assert.Equal("Kurs", remembered.CourseName);        // Chessable-Eintrag bleibt, wie er war
    }

    [Fact]
    public void EvalTextOf_FormatsCpAndMate_FromMainLine()
    {
        Assert.Equal("+0.35", AnalysisJobService.EvalTextOf("{\"pvs\":[{\"cp\":35},{\"cp\":-12}]}"));
        Assert.Equal("-1.20", AnalysisJobService.EvalTextOf("{\"pvs\":[{\"cp\":-120}]}"));
        Assert.Equal("0.00", AnalysisJobService.EvalTextOf("{\"pvs\":[{\"cp\":0}]}"));
        Assert.Equal("#-3", AnalysisJobService.EvalTextOf("{\"pvs\":[{\"mate\":-3}]}"));
        Assert.Null(AnalysisJobService.EvalTextOf("{\"pvs\":[]}"));
        Assert.Null(AnalysisJobService.EvalTextOf(null));
        Assert.Null(AnalysisJobService.EvalTextOf("kaputt"));
    }

    [Fact]
    public async Task CreateMany_CreatesInOrder_SkipsInvalidDuplicateAndOverLimit()
    {
        var u = await UserWithBackgroundEngineAsync();
        const string other = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
        // Bestehender Auftrag zu START → Dublette; im Batch selbst START noch einmal mit anderen Zählern → Dublette
        await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 30, MultiPv = 3 });

        var res = await _svc.CreateManyAsync(u, new CreateAnalysisJobsBatchRequest
        {
            Fens = new List<string> { other, "kein fen", START.Replace(" 0 1", " 2 4"), other },
            TargetDepth = 35, MultiPv = 2,
        });

        Assert.Single(res.Created);
        Assert.Equal(other, res.Created[0].Fen);
        Assert.Equal(35, res.Created[0].TargetDepth);
        Assert.Equal(new[] { "invalid", "duplicate", "duplicate" }, res.Skipped.Select(x => x.Reason).ToArray());
        Assert.Equal(2, await _db.RememberedPositions.CountAsync());   // START (vom Einzelauftrag) + other, je einmal
    }

    [Fact]
    public async Task CreateMany_RespectsOpenJobLimit()
    {
        var u = await UserWithBackgroundEngineAsync();
        for (var i = 0; i < AnalysisJobService.MaxOpenJobsPerUser; i++)
            _db.AnalysisJobs.Add(new AnalysisJob { UserId = u, Fen = $"8/8/8/8/8/8/{i}", EngineId = "e", TargetDepth = 30, MultiPv = 1, Status = AnalysisJobStatus.Queued });
        await _db.SaveChangesAsync();

        var res = await _svc.CreateManyAsync(u, new CreateAnalysisJobsBatchRequest { Fens = new List<string> { START } });

        Assert.Empty(res.Created);
        Assert.Equal("limit", res.Skipped.Single().Reason);
    }

    [Fact]
    public async Task Create_RejectsMoreThanFiveLines_ProtocolMaximum()
    {
        // Das Lichess-Protokoll erlaubt work.multiPv nur 1..5; mehr würde der Broker abweisen und der
        // Auftrag liefe endlos in die Wiederholung.
        var u = await UserWithBackgroundEngineAsync();
        Assert.Equal(5, AnalysisJobService.MaxMultiPv);
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, MultiPv = 6 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateManyAsync(u, new CreateAnalysisJobsBatchRequest { Fens = [START], MultiPv = 10 }));
        var job = await DoneJobAsync(u);
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { MultiPv = 6 }));
    }

    [Fact]
    public async Task CreateMany_NullFens_IsBadRequestNotCrash()
    {
        var u = await UserWithBackgroundEngineAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.CreateManyAsync(u, new CreateAnalysisJobsBatchRequest { Fens = null }));
    }

    [Fact]
    public async Task Update_MoreLines_LiftsTargetToAtLeastReachedDepth()
    {
        // Sonst suchte der Neustart bis zu einer Tiefe UNTER dem Erreichten — der Worker übernähme keine
        // einzige Zeile (ShouldPersist), die Engine-Zeit wäre verschenkt.
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u, reached: 40, multiPv: 2);
        job.TargetDepth = 30; await _db.SaveChangesAsync();

        var dto = await _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { MultiPv = 4 });

        Assert.Equal("queued", dto!.Status);
        Assert.Equal(40, dto.TargetDepth);
    }

    [Fact]
    public async Task Update_DepthUp_AlsoRevivesFailedJob_AndResetsCounters()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u, reached: 20);
        job.Status = AnalysisJobStatus.Failed; job.LastError = "Engine weg"; job.FruitlessAttempts = 3;
        await _db.SaveChangesAsync();

        var dto = await _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { TargetDepth = 35 });

        Assert.Equal("queued", dto!.Status);
        Assert.Null(dto.LastError);
        Assert.Equal(0, (await _db.AnalysisJobs.FindAsync(job.Id))!.FruitlessAttempts);
    }

    [Fact]
    public async Task Create_TrimsOldestFinishedJobs_OverTheTotalCap()
    {
        var u = await UserWithBackgroundEngineAsync();
        var now = DateTime.UtcNow;
        for (var i = 0; i < AnalysisJobService.MaxJobsPerUser; i++)
            _db.AnalysisJobs.Add(new AnalysisJob { UserId = u, Fen = $"8/8/8/8/8/8/8/K6k w - - 0 {i}", EngineId = "e",
                TargetDepth = 30, MultiPv = 1, Status = AnalysisJobStatus.Done, FinishedAt = now.AddMinutes(-i) });
        await _db.SaveChangesAsync();
        var oldestId = await _db.AnalysisJobs.OrderBy(j => j.FinishedAt).Select(j => j.Id).FirstAsync();

        await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 30, MultiPv = 3 });

        Assert.Equal(AnalysisJobService.MaxJobsPerUser, await _db.AnalysisJobs.CountAsync(j => j.UserId == u));
        Assert.Null(await _db.AnalysisJobs.FindAsync(oldestId));   // der älteste FERTIGE ist gewichen
    }

    [Fact]
    public async Task Create_WithoutBackgroundEngine_Throws()
    {
        var u = await UserWithBackgroundEngineAsync(engine: null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START }));
    }

    [Fact]
    public async Task Create_RejectsIllegalFenAndBounds()
    {
        var u = await UserWithBackgroundEngineAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = "kein fen" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 99 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, MultiPv = 0 }));
    }

    private async Task<AnalysisJob> DoneJobAsync(int userId, int reached = 40, int multiPv = 3)
    {
        var job = new AnalysisJob
        {
            UserId = userId, Fen = START, EngineId = "eei_bg", TargetDepth = reached, MultiPv = multiPv,
            Status = AnalysisJobStatus.Done, ReachedDepth = reached, FinishedAt = DateTime.UtcNow,
            ResultJson = "{\"time\":1,\"depth\":" + reached + ",\"nodes\":9,\"pvs\":[{\"cp\":1},{\"cp\":2},{\"cp\":3}]}",
        };
        _db.AnalysisJobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task Update_DepthUp_RequeuesDoneJob_KeepingResult()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u);

        var dto = await _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { TargetDepth = 50 });

        Assert.Equal("queued", dto!.Status);
        Assert.Equal(50, dto.TargetDepth);
        Assert.Equal(40, dto.ReachedDepth);        // Fortsetzung ab hier
        Assert.NotNull(dto.ResultJson);
        Assert.Null(dto.FinishedAt);
        Assert.Empty(_control.Interrupted);
    }

    [Fact]
    public async Task Update_DepthDownToReached_MakesPausedJobDone()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u, reached: 30);
        job.Status = AnalysisJobStatus.Paused; job.TargetDepth = 45; job.FinishedAt = null;
        await _db.SaveChangesAsync();

        var dto = await _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { TargetDepth = 28 });

        Assert.Equal("done", dto!.Status);
        Assert.NotNull(dto.FinishedAt);
    }

    [Fact]
    public async Task Update_FewerLines_TruncatesResult_NoRestart()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u, multiPv: 3);

        var dto = await _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { MultiPv = 2 });

        Assert.Equal("done", dto!.Status);           // kein Neustart nötig
        Assert.Equal(40, dto.ReachedDepth);
        Assert.Contains("{\"cp\":2}", dto.ResultJson);
        Assert.DoesNotContain("{\"cp\":3}", dto.ResultJson);
        Assert.Empty(_control.Interrupted);
    }

    [Fact]
    public async Task Update_MoreLines_RestartsSearch_KeepsOldResultVisible()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u, multiPv: 3);
        job.Status = AnalysisJobStatus.Running;
        await _db.SaveChangesAsync();

        var dto = await _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { MultiPv = 5 });

        Assert.Equal("queued", dto!.Status);
        Assert.Equal(5, dto.MultiPv);
        Assert.Equal(40, dto.ReachedDepth);          // bleibt als Anzeige — der Worker überschreibt erst ab Tiefe 40
        Assert.Contains("{\"cp\":3}", dto.ResultJson);
        Assert.Equal([job.Id], _control.Interrupted); // laufende Suche wird abgebrochen
    }

    [Fact]
    public async Task Update_EngineChange_RequeuesAndInterrupts_KeepingResult()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u, reached: 25);
        job.Status = AnalysisJobStatus.Running; job.TargetDepth = 40; await _db.SaveChangesAsync();

        var dto = await _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { EngineId = "eei_stark" });

        Assert.Equal("eei_stark", dto!.EngineId);
        Assert.Equal("queued", dto.Status);          // andere Engine = anderer Prozess → neu einreihen
        Assert.Equal(25, dto.ReachedDepth);          // Ergebnis bleibt, die neue setzt dort an
        Assert.Equal([job.Id], _control.Interrupted);
    }

    [Fact]
    public async Task Update_SameEngine_DoesNotRestart()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u, reached: 25);
        job.Status = AnalysisJobStatus.Paused; job.TargetDepth = 40; await _db.SaveChangesAsync();

        var dto = await _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { EngineId = job.EngineId });

        Assert.Equal("paused", dto!.Status);
        Assert.Empty(_control.Interrupted);
    }

    [Fact]
    public async Task Restart_RequeuesAndClearsCounters_KeepsResult()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u, reached: 38);
        job.Status = AnalysisJobStatus.Failed; job.TargetDepth = 40; job.FruitlessAttempts = 3;
        job.LastError = "Engine lieferte keine tiefere Bewertung"; job.NextAttemptAt = DateTime.UtcNow.AddMinutes(5);
        await _db.SaveChangesAsync();

        var dto = await _svc.RestartAsync(u, job.Id);

        Assert.Equal("queued", dto!.Status);
        Assert.Null(dto.LastError);
        Assert.Equal(38, dto.ReachedDepth);          // Fortsetzung, kein Neubeginn
        var fresh = await _db.AnalysisJobs.FindAsync(job.Id);
        Assert.Equal(0, fresh!.FruitlessAttempts);
        Assert.Null(fresh.NextAttemptAt);
    }

    [Fact]
    public async Task Restart_LeavesAFinishedJobAlone()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u, reached: 40);   // Ziel erreicht → nichts zu rechnen

        var dto = await _svc.RestartAsync(u, job.Id);

        Assert.Equal("done", dto!.Status);
        Assert.Empty(_control.Interrupted);
        Assert.Null(await _svc.RestartAsync(999, job.Id));   // fremder Auftrag
    }

    [Fact]
    public async Task Get_LiefertNurDenEigenenAuftrag_mitErgebnis()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u);

        var dto = await _svc.GetAsync(u, job.Id);
        Assert.NotNull(dto);
        Assert.Equal(job.Id, dto!.Id);
        Assert.Equal("done", dto.Status);
        Assert.NotNull(dto.ResultJson);

        Assert.Null(await _svc.GetAsync(999, job.Id));
        Assert.Null(await _svc.GetAsync(u, job.Id + 1000));
    }

    [Fact]
    public async Task Delete_InterruptsAndRemoves_OnlyOwnJobs()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u);
        Assert.False(await _svc.DeleteAsync(999, job.Id));
        Assert.True(await _svc.DeleteAsync(u, job.Id));
        Assert.Equal([job.Id], _control.Interrupted);
        Assert.Empty(_db.AnalysisJobs);
    }

    /// <summary>Hintergrundarbeit (Vertiefung, 0.523.0) kommt erst dran, wenn kein normaler Auftrag wartet — auch wenn
    /// sie aelter ist und zuletzt lief (warme Hashtabelle zaehlt nur innerhalb derselben Stufe).</summary>
    [Fact]
    public async Task PickNextForEngine_NormalVorHintergrund_auchGegenAelterUndZuletztGelaufen()
    {
        var u = await UserWithBackgroundEngineAsync();
        var now = DateTime.UtcNow;
        var refine = new AnalysisJob { UserId = u, Fen = START, EngineId = "e", TargetDepth = 25, MultiPv = 5, Status = AnalysisJobStatus.Paused, CreatedAt = now.AddMinutes(-30), LastRunAt = now.AddMinutes(-1), Background = true };
        var fresh = new AnalysisJob { UserId = u, Fen = START, EngineId = "e", TargetDepth = 20, MultiPv = 1, Status = AnalysisJobStatus.Queued, CreatedAt = now };
        _db.AnalysisJobs.AddRange(refine, fresh);
        await _db.SaveChangesAsync();

        Assert.Equal(fresh.Id, (await _svc.PickNextForEngineAsync("e", now))!.Id);

        fresh.Status = AnalysisJobStatus.Done; await _db.SaveChangesAsync();
        Assert.Equal(refine.Id, (await _svc.PickNextForEngineAsync("e", now))!.Id);   // sonst die Vertiefung
    }

    [Fact]
    public async Task PickNextForEngine_PrefersLastRunJob_ThenOldest_SkipsBackoff()
    {
        var u = await UserWithBackgroundEngineAsync();
        var now = DateTime.UtcNow;
        var older = new AnalysisJob { UserId = u, Fen = START, EngineId = "e", TargetDepth = 30, MultiPv = 1, Status = AnalysisJobStatus.Queued, CreatedAt = now.AddMinutes(-10) };
        var sticky = new AnalysisJob { UserId = u, Fen = START, EngineId = "e", TargetDepth = 50, MultiPv = 1, Status = AnalysisJobStatus.Queued, CreatedAt = now.AddMinutes(-5), LastRunAt = now.AddMinutes(-1) };
        var backoff = new AnalysisJob { UserId = u, Fen = START, EngineId = "e", TargetDepth = 30, MultiPv = 1, Status = AnalysisJobStatus.Paused, CreatedAt = now.AddMinutes(-20), NextAttemptAt = now.AddMinutes(5) };
        _db.AnalysisJobs.AddRange(older, sticky, backoff);
        await _db.SaveChangesAsync();

        var pick = await _svc.PickNextForEngineAsync("e", now);
        Assert.Equal(sticky.Id, pick!.Id);           // warme Hashtabelle zuerst

        sticky.Status = AnalysisJobStatus.Done; await _db.SaveChangesAsync();
        pick = await _svc.PickNextForEngineAsync("e", now);
        Assert.Equal(older.Id, pick!.Id);            // sonst FIFO; Backoff-Auftrag übersprungen

        older.Status = AnalysisJobStatus.Done; await _db.SaveChangesAsync();
        Assert.Null(await _svc.PickNextForEngineAsync("e", now));
        Assert.NotNull(await _svc.PickNextForEngineAsync("e", now.AddMinutes(6)));   // Backoff abgelaufen
    }

    [Fact]
    public async Task JobsOnDifferentEngines_AreIndependentQueues()
    {
        // Die knappe Ressource ist die ENGINE, nicht der Nutzer: zwei Aufträge auf zwei Engines laufen parallel.
        var u = await UserWithBackgroundEngineAsync();
        var now = DateTime.UtcNow;
        _db.AnalysisJobs.AddRange(
            new AnalysisJob { UserId = u, Fen = START, EngineId = "eei_hier", TargetDepth = 30, MultiPv = 1, Status = AnalysisJobStatus.Running, CreatedAt = now.AddMinutes(-9) },
            new AnalysisJob { UserId = u, Fen = START, EngineId = "eei_dort", TargetDepth = 30, MultiPv = 1, Status = AnalysisJobStatus.Queued, CreatedAt = now.AddMinutes(-8) });
        await _db.SaveChangesAsync();

        var engines = await _svc.EnginesWithRunnableJobsAsync(now);
        Assert.Equal(["eei_dort"], engines);                                  // nur die mit wartender Arbeit
        Assert.NotNull(await _svc.PickNextForEngineAsync("eei_dort", now));   // läuft, obwohl der User schon rechnet
        Assert.Null(await _svc.PickNextForEngineAsync("eei_hier", now));      // dort ist der Auftrag bereits Running
    }

    [Fact]
    public async Task ResetInterrupted_TurnsRunningIntoPaused()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u); job.Status = AnalysisJobStatus.Running; await _db.SaveChangesAsync();
        Assert.Equal(1, await _svc.ResetInterruptedAsync());
        Assert.Equal(AnalysisJobStatus.Paused, (await _db.AnalysisJobs.SingleAsync()).Status);
    }

    [Fact]
    public void TruncatePvs_KeepsFirstN_AndSurvivesGarbage()
    {
        Assert.Equal("{\"pvs\":[{\"cp\":1}]}", AnalysisJobService.TruncatePvs("{\"pvs\":[{\"cp\":1},{\"cp\":2}]}", 1));
        Assert.Equal("{\"pvs\":[{\"cp\":1}]}", AnalysisJobService.TruncatePvs("{\"pvs\":[{\"cp\":1}]}", 3));
        Assert.Equal("kaputt", AnalysisJobService.TruncatePvs("kaputt", 1));
        Assert.Null(AnalysisJobService.TruncatePvs(null, 1));
    }

    // ── Haus-Engine (Codereview 2026-09-29, A4-001): Tiefe/Linien/Engine eines Auftrags auf fremder Rechenzeit
    //    ändert nur ein Admin — sonst war PUT /api/analysis-jobs/{id} der Umweg um GameAnalysisController.Create ──

    /// <summary>Ein Auftrag wie ihn die Punktepartie anlegt: gehört dem Nutzer, rechnet auf der Engine des Haus-Kontos.</summary>
    private async Task<AnalysisJob> HouseJobAsync(int userId, int houseOwner = 77)
    {
        var job = new AnalysisJob
        {
            UserId = userId, EngineOwnerUserId = houseOwner, Fen = START, EngineId = "eei_haus",
            TargetDepth = 20, MultiPv = 5, Status = AnalysisJobStatus.Queued,
        };
        _db.AnalysisJobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task Update_HausAuftrag_NichtAdmin_kannTiefeLinienUndEngineNichtAendern()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await HouseJobAsync(u);

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { TargetDepth = 60 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { MultiPv = 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { EngineId = "eei_bg" }));

        var fresh = await _db.AnalysisJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(20, fresh.TargetDepth);
        Assert.Equal(5, fresh.MultiPv);
        Assert.Equal("eei_haus", fresh.EngineId);
        Assert.Empty(_control.Interrupted);
    }

    [Fact]
    public async Task Update_HausAuftrag_TitelUndUnveraenderteWerte_bleibenErlaubt()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await HouseJobAsync(u);

        // Die Auftragsseite schickt Tiefe/Linien/Engine immer mit — gleiche Werte sind keine Änderung.
        var dto = await _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest
        {
            TargetDepth = 20, MultiPv = 5, EngineId = " eei_haus ", Title = "Zug 12",
        });

        Assert.Equal("Zug 12", dto!.Title);
        Assert.Equal(20, dto.TargetDepth);
        Assert.Equal("queued", dto.Status);
        Assert.True(dto.HouseEngine);
    }

    [Fact]
    public async Task Update_HausAuftrag_Admin_darfDieTiefeAendern()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await HouseJobAsync(u);

        var dto = await _svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { TargetDepth = 30 }, isAdmin: true);

        Assert.Equal(30, dto!.TargetDepth);
    }

    [Fact]
    public async Task List_markiertAuftraegeAufDerHausEngine()
    {
        var u = await UserWithBackgroundEngineAsync();
        var own = await _svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 1 });
        var house = await HouseJobAsync(u);

        var list = await _svc.ListAsync(u);

        Assert.False(list.Single(j => j.Id == own.Id).HouseEngine);
        Assert.True(list.Single(j => j.Id == house.Id).HouseEngine);
    }

    // ── Genannte Engine (Codereview 2026-09-29, A4-003): nur eine Engine des Engine-Besitzers, sonst 400 und KEIN
    //    Verdrängen — vorher brach jede bekannte Kennung (z. B. die der Haus-Engine aus der eigenen Auftragsliste)
    //    fremde Hintergrundarbeit ab, und der eigene Auftrag scheiterte danach nur still ──

    private const string OwnLichessEngines = """
        [{"id":"eei_bg","name":"BG","clientSecret":"ees_x","maxThreads":4,"maxHash":256},
         {"id":"eei_live","name":"Live","clientSecret":"ees_y","maxThreads":8,"maxHash":512}]
        """;

    private sealed class LichessStub : HttpMessageHandler
    {
        public string Json = OwnLichessEngines;
        public bool Fail;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Fail) throw new HttpRequestException("lichess down");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private readonly LichessStub _lichessStub = new();
    private ServiceProvider? _sp;

    /// <summary>Der Service, wie ihn die API verdrahtet: MIT <see cref="EngineRegistry"/> (<c>rhe_</c> aus der
    /// Registrierung, <c>eei_</c> über den Lichess-Token — hier ein Stub mit den Engines des Nutzers).</summary>
    private AnalysisJobService WithRegistry()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build();
        var lichess = new LichessEngineService(new HttpClient(_lichessStub), new MemoryCache(new MemoryCacheOptions()), config,
            NullLogger<LichessEngineService>.Instance);
        _sp ??= new ServiceCollection().BuildServiceProvider();
        var directory = new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>());
        return new AnalysisJobService(_db, _control,
            new EngineRegistry(_db, _encryption, lichess, directory, new LocalBrokerOptions()));
    }

    /// <summary>Das Haus-Konto: Hintergrund-Engines, die in den Punktepartie-Aufträgen anderer Nutzer stehen.</summary>
    private async Task<(int Id, string LocalEngine)> HouseOwnerAsync(int id = 77)
    {
        await UserWithBackgroundEngineAsync(id, engine: "eei_haus");
        var reg = new ExternalEngineRegistration
        {
            Id = ProviderSecrets.NewEngineId(), UserId = id, Name = "Haus 1", ClientSecret = "cs",
            ProviderSelector = ProviderSecrets.Selector("secret-0123456789abc"), MaxThreads = 4, MaxHash = 256,
        };
        _db.ExternalEngineRegistrations.Add(reg);
        await _db.SaveChangesAsync();
        return (id, reg.Id);
    }

    [Fact]
    public async Task Create_fremdeEngineKennung_wirdAbgewiesen_undVerdraengtNichts()
    {
        var u = await UserWithBackgroundEngineAsync();
        var house = await HouseOwnerAsync();
        var svc = WithRegistry();

        foreach (var foreign in new[] { "eei_haus", house.LocalEngine })
            await Assert.ThrowsAsync<ArgumentException>(() =>
                svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 1, EngineId = foreign }));

        Assert.Empty(_control.Preempted);
        Assert.Empty(_db.AnalysisJobs);
    }

    [Fact]
    public async Task CreateMany_fremdeEngineKennung_wirdAbgewiesen()
    {
        var u = await UserWithBackgroundEngineAsync();
        await HouseOwnerAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => WithRegistry().CreateManyAsync(u,
            new CreateAnalysisJobsBatchRequest { Fens = [START], TargetDepth = 20, MultiPv = 1, EngineId = "eei_haus" }));

        Assert.Empty(_db.AnalysisJobs);
    }

    [Fact]
    public async Task Update_EngineWechsel_nurAufEineEngineDesBesitzers()
    {
        var u = await UserWithBackgroundEngineAsync();
        await HouseOwnerAsync();
        var svc = WithRegistry();
        var job = await svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 1 });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { EngineId = "eei_haus" }));
        Assert.Equal("eei_bg", (await _db.AnalysisJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).EngineId);
        Assert.Empty(_control.Interrupted);

        // Eine eigene Engine ausserhalb der Hintergrund-Liste bleibt wählbar.
        var moved = await svc.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { EngineId = "eei_live" });
        Assert.Equal("eei_live", moved!.EngineId);
        Assert.Equal(new[] { job.Id }, _control.Interrupted);
    }

    [Fact]
    public async Task Create_eigeneGenannteEngine_wirdAngelegt_undVerdraengtDortDenHintergrund()
    {
        var u = await UserWithBackgroundEngineAsync();
        var own = new ExternalEngineRegistration
        {
            Id = ProviderSecrets.NewEngineId(), UserId = u, Name = "Heim-PC", ClientSecret = "cs",
            ProviderSelector = ProviderSecrets.Selector("secret-own-0123456789"), MaxThreads = 4, MaxHash = 256,
        };
        _db.ExternalEngineRegistrations.Add(own);
        await _db.SaveChangesAsync();
        var svc = WithRegistry();

        // Live-Engine des eigenen Lichess-Kontos (nicht in der Hintergrund-Liste) und eigene rhe_-Registrierung.
        var live = await svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 1, EngineId = "eei_live" });
        var local = await svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 1, EngineId = own.Id });

        Assert.Equal("eei_live", live.EngineId);
        Assert.Equal(own.Id, local.EngineId);
        Assert.Equal(new[] { "eei_live", own.Id }, _control.Preempted);
    }

    [Fact]
    public async Task Create_HausEngineDesEngineBesitzers_verdraengtWeiter()
    {
        // Der Weg der Punktepartie (GameAnalysisService): Auftrag beim Nutzer, Engine beim Haus-Konto — gewählt oder
        // gepinnt steht sie in dessen Hintergrund-Liste, und der normale Auftrag hat dort weiter Vorrang.
        var u = await UserWithBackgroundEngineAsync();
        var house = await HouseOwnerAsync();
        var svc = WithRegistry();

        await svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 5 },
            remember: false, engineOwnerUserId: house.Id);
        await svc.CreateAsync(u, new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 5, EngineId = "eei_haus" },
            remember: false, engineOwnerUserId: house.Id);

        Assert.Equal(new[] { "eei_haus", "eei_haus" }, _control.Preempted);
    }

    [Fact]
    public async Task CreateMany_LichessNichtErreichbar_legtAn_verdraengtAberNichts()
    {
        var u = await UserWithBackgroundEngineAsync();
        _lichessStub.Fail = true;

        var res = await WithRegistry().CreateManyAsync(u,
            new CreateAnalysisJobsBatchRequest { Fens = [START], TargetDepth = 20, MultiPv = 1, EngineId = "eei_live" });

        Assert.Single(res.Created);
        Assert.Empty(_control.Preempted);
    }

    [Fact]
    public async Task Create_LichessNichtErreichbar_legtAn_verdraengtAberNichts()
    {
        // Nicht prüfbar ist kein Nein: ein kurzer Lichess-Ausfall soll keinen Auftrag abweisen — verdrängt wird aber
        // nur auf einer geprüften Engine.
        var u = await UserWithBackgroundEngineAsync();
        _lichessStub.Fail = true;

        var dto = await WithRegistry().CreateAsync(u,
            new CreateAnalysisJobRequest { Fen = START, TargetDepth = 20, MultiPv = 1, EngineId = "eei_live" });

        Assert.Equal("eei_live", dto.EngineId);
        Assert.Empty(_control.Preempted);
    }

    /// <summary>Den Lichess-Token entschlüsselt allein <see cref="EngineRegistry.TokenOf"/> (Codereview A4-010): die
    /// aufruferlose Kopie <c>TokenAsync</c> hier samt eigener <see cref="EncryptionService"/>-Abhängigkeit hätte eine
    /// geänderte Token-Regel (z. B. „leerer EncryptedToken = kein Token") nicht mitbekommen.</summary>
    [Fact]
    public void TokenDecryption_LivesOnlyInEngineRegistry()
    {
        var t = typeof(AnalysisJobService);
        Assert.Null(t.GetMethod("TokenAsync"));
        Assert.DoesNotContain(t.GetConstructors().SelectMany(c => c.GetParameters()),
            p => p.ParameterType == typeof(EncryptionService));
    }
}
