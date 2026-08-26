using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

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
        public void Interrupt(int jobId) => Interrupted.Add(jobId);
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
        _svc = new AnalysisJobService(_db, _encryption, _control);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> UserWithBackgroundEngineAsync(int id = 5, string? engine = "eei_bg")
    {
        _db.AppUsers.Add(new AppUser { Id = id, Username = $"u{id}", PasswordHash = "x" });
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = id, EncryptedToken = _encryption.Encrypt("lip_tok"), BackgroundEngineId = engine,
        });
        await _db.SaveChangesAsync();
        return id;
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
    public async Task Delete_InterruptsAndRemoves_OnlyOwnJobs()
    {
        var u = await UserWithBackgroundEngineAsync();
        var job = await DoneJobAsync(u);
        Assert.False(await _svc.DeleteAsync(999, job.Id));
        Assert.True(await _svc.DeleteAsync(u, job.Id));
        Assert.Equal([job.Id], _control.Interrupted);
        Assert.Empty(_db.AnalysisJobs);
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
}
