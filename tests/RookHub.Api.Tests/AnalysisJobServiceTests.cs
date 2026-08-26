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
    public async Task PickNext_PrefersLastRunJob_ThenOldest_SkipsBackoff()
    {
        var u = await UserWithBackgroundEngineAsync();
        var now = DateTime.UtcNow;
        var older = new AnalysisJob { UserId = u, Fen = START, EngineId = "e", TargetDepth = 30, MultiPv = 1, Status = AnalysisJobStatus.Queued, CreatedAt = now.AddMinutes(-10) };
        var sticky = new AnalysisJob { UserId = u, Fen = START, EngineId = "e", TargetDepth = 50, MultiPv = 1, Status = AnalysisJobStatus.Queued, CreatedAt = now.AddMinutes(-5), LastRunAt = now.AddMinutes(-1) };
        var backoff = new AnalysisJob { UserId = u, Fen = START, EngineId = "e", TargetDepth = 30, MultiPv = 1, Status = AnalysisJobStatus.Paused, CreatedAt = now.AddMinutes(-20), NextAttemptAt = now.AddMinutes(5) };
        _db.AnalysisJobs.AddRange(older, sticky, backoff);
        await _db.SaveChangesAsync();

        var pick = await _svc.PickNextAsync(u, now);
        Assert.Equal(sticky.Id, pick!.Id);           // warme Hashtabelle zuerst

        sticky.Status = AnalysisJobStatus.Done; await _db.SaveChangesAsync();
        pick = await _svc.PickNextAsync(u, now);
        Assert.Equal(older.Id, pick!.Id);            // sonst FIFO; Backoff-Auftrag übersprungen

        older.Status = AnalysisJobStatus.Done; await _db.SaveChangesAsync();
        Assert.Null(await _svc.PickNextAsync(u, now));
        Assert.NotNull(await _svc.PickNextAsync(u, now.AddMinutes(6)));   // Backoff abgelaufen
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
