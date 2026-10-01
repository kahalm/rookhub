using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Auftrags-Worker über <see cref="IEngineBroker"/>: eine <c>rhe_</c>-Engine braucht keinen Lichess-Token,
/// ein 503 wechselt wie bisher die Engine, eine vom EIGENEN Broker abgewiesene Stellung (400) scheitert sofort
/// statt im Zwei-Minuten-Takt ewig wiederholt zu werden. Vorher war der Worker ohne echten Broker gar nicht
/// instanziierbar (siehe <see cref="AnalysisJobOutcomeTests"/>).
/// </summary>
public class AnalysisJobWorkerBrokerTests : IAsyncDisposable
{
    private const string Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
    private readonly ServiceProvider _sp;
    private readonly FakeBroker _broker = new();
    private readonly AnalysisJobWorker _worker;

    public AnalysisJobWorkerBrokerTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
            ["AnalysisJobs:TickSeconds"] = "1",
            ["AnalysisJobs:IdleGraceSeconds"] = "0",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<EncryptionService>();
        services.AddSingleton(new LocalBrokerOptions());
        services.AddSingleton<EngineSelectorDirectory>();
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        services.AddSingleton(sp => new LichessEngineService(new HttpClient(new NoNetwork()), sp.GetRequiredService<IMemoryCache>(),
            config, NullLogger<LichessEngineService>.Instance));
        services.AddScoped<EngineRegistry>();
        services.AddScoped<AnalysisJobService>();
        _sp = services.BuildServiceProvider();
        _worker = new AnalysisJobWorker(_sp.GetRequiredService<IServiceScopeFactory>(), new EngineActivityTracker(), _broker,
            NullLogger<AnalysisJobWorker>.Instance, config, new AnalysisJobLive());
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.StopAsync(CancellationToken.None);
        _worker.Dispose();
        await _sp.DisposeAsync();
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no network in this test");
    }

    /// <summary>Antwortet je Engine mit einem festen Status bzw. festen Zeilen.</summary>
    private sealed class FakeBroker : IEngineBroker
    {
        public readonly Dictionary<string, (int Status, string Lines)> Answers = new();
        public readonly List<(string EngineId, EngineWork Work)> Calls = [];
        /// <summary>Engines, deren Stream nach der ersten Zeile HAENGT (rechnet „ewig") — fuer Vorrang-Tests.</summary>
        public readonly HashSet<string> Hanging = [];

        public Task<EngineAnalysisSession> AnalyseAsync(EngineRef engine, EngineWork work, CancellationToken ct)
        {
            lock (Calls) Calls.Add((engine.Id, work));
            if (Hanging.Contains(engine.Id))
                return Task.FromResult(new EngineAnalysisSession(200, new HangingStream(
                    "{\"time\":5,\"depth\":8,\"nodes\":10,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":-20,\"depth\":8}]}\n", ct)));
            var (status, lines) = Answers[engine.Id];
            return Task.FromResult(status == 200
                ? new EngineAnalysisSession(200, new MemoryStream(Encoding.UTF8.GetBytes(lines)))
                : EngineAnalysisSession.Rejected(status, "invalid work: illegal initial position: opposite check"));
        }
    }

    /// <summary>Liefert einmal <paramref name="first"/>, danach blockiert jedes Lesen, bis es abgebrochen wird — wie
    /// ein echter Broker-Stream auch ueber den Token der Sitzung (<paramref name="session"/>), nicht nur den des Lesens.</summary>
    private sealed class HangingStream(string first, CancellationToken session) : Stream
    {
        private byte[]? _first = Encoding.UTF8.GetBytes(first);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_first is { } f) { _first = null; f.CopyTo(buffer); return f.Length; }
            using var both = CancellationTokenSource.CreateLinkedTokenSource(ct, session);
            await Task.Delay(Timeout.Infinite, both.Token);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private async Task<(int UserId, string[] EngineIds)> SetupAsync(int engines = 1)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new AppUser { Username = "kahalm", PasswordHash = "x" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var ids = new List<string>();
        for (var i = 0; i < engines; i++)
        {
            var reg = new ExternalEngineRegistration
            {
                Id = ProviderSecrets.NewEngineId(), UserId = user.Id, Name = $"E{i}", ClientSecret = "cs",
                ProviderSelector = ProviderSecrets.Selector($"secret-{i}-0123456789"), MaxThreads = 2, MaxHash = 64,
            };
            db.ExternalEngineRegistrations.Add(reg);
            ids.Add(reg.Id);
        }
        var cred = new LichessEngineCredential { UserId = user.Id, EncryptedToken = "" };   // KEIN Lichess-Token
        cred.SetBackgroundEngines(ids);
        db.LichessEngineCredentials.Add(cred);
        await db.SaveChangesAsync();
        return (user.Id, [.. ids]);
    }

    private async Task<int> JobAsync(int userId, int depth = 12, bool background = false)
    {
        using var scope = _sp.CreateScope();
        var dto = await scope.ServiceProvider.GetRequiredService<AnalysisJobService>()
            .CreateAsync(userId, new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = depth, MultiPv = 2 }, remember: false,
                background: background);
        return dto.Id;
    }

    // ── Vorrang (2026-09-28): ein normaler Auftrag verdraengt einen LAUFENDEN Hintergrundauftrag ───────────────

    [Fact]
    public async Task PreemptBackground_derNormaleAuftragBekommtDieEngine_derHintergrundWartetOhneFehlversuch()
    {
        var (userId, engines) = await SetupAsync();
        _broker.Hanging.Add(engines[0]);                          // jede Suche rechnet „ewig"
        var background = await JobAsync(userId, depth: 30, background: true);
        await _worker.StartAsync(CancellationToken.None);
        await WaitForAsync(background, j => j.Status == AnalysisJobStatus.Running);

        // Ein normaler Auftrag kommt fuer DIESELBE Engine dazu — AnalysisJobService ruft dann genau das auf.
        var normal = await JobAsync(userId, depth: 20);
        _worker.PreemptBackground(engines[0]);

        await WaitForAsync(normal, j => j.Status == AnalysisJobStatus.Running);
        var paused = await WaitForAsync(background, j => j.Status == AnalysisJobStatus.Paused);
        Assert.Equal(0, paused.FruitlessAttempts);                // Verdraengung ist kein Fehlversuch
        Assert.Equal(20, _broker.Calls[^1].Work.Depth);            // die Engine rechnet jetzt den normalen Auftrag
    }

    [Fact]
    public async Task PreemptBackground_laesstEinenNormalenAuftragWeiterrechnen()
    {
        var (userId, engines) = await SetupAsync();
        _broker.Hanging.Add(engines[0]);
        var jobId = await JobAsync(userId, depth: 30, background: false);

        await _worker.StartAsync(CancellationToken.None);
        await WaitForAsync(jobId, j => j.Status == AnalysisJobStatus.Running);
        _worker.PreemptBackground(engines[0]);
        await Task.Delay(500);

        using var scope = _sp.CreateScope();
        var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AnalysisJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
        Assert.Equal(AnalysisJobStatus.Running, job.Status);
    }

    private async Task<AnalysisJob> WaitForAsync(int jobId, Func<AnalysisJob, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _sp.CreateScope();
            var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AnalysisJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
            if (done(job)) return job;
            await Task.Delay(100);
        }
        throw new TimeoutException("Auftrag kam nicht an");
    }

    [Fact]
    public async Task LocalEngine_WithoutLichessToken_ComputesTheJobToTheEnd()
    {
        var (userId, engines) = await SetupAsync();
        _broker.Answers[engines[0]] = (200,
            "{\"time\":5,\"depth\":8,\"nodes\":10,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":-20,\"depth\":8}]}\n"
            + "{\"keepalive\":true}\n"
            + "{\"time\":9,\"depth\":12,\"nodes\":90,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":-25,\"depth\":12},{\"moves\":[\"c7c5\"],\"cp\":-30,\"depth\":12}],\"bestmove\":\"e7e5\"}\n");
        var jobId = await JobAsync(userId);

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Done or AnalysisJobStatus.Failed);

        Assert.Equal(AnalysisJobStatus.Done, job.Status);
        Assert.Equal(12, job.ReachedDepth);
        Assert.Contains("\"bestmove\":\"e7e5\"", job.ResultJson);
        var (engineId, work) = Assert.Single(_broker.Calls);
        Assert.Equal(engines[0], engineId);
        Assert.Equal($"rh-bg-{userId}", work.SessionId);
        Assert.Equal(2, work.MultiPv);
        Assert.Equal(12, work.Depth);
        Assert.Equal(Fen, work.InitialFen);
    }

    [Fact]
    public async Task LocalBroker_RejectsThePosition_JobFailsInsteadOfLoopingForever()
    {
        var (userId, engines) = await SetupAsync();
        _broker.Answers[engines[0]] = (400, "");
        var jobId = await JobAsync(userId);

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Failed or AnalysisJobStatus.Paused);
        Assert.Equal(AnalysisJobStatus.Failed, job.Status);
        Assert.StartsWith("Stellung abgewiesen", job.LastError);
    }

    [Fact]
    public async Task NoProvider_503_SwitchesToTheNextBackgroundEngine()
    {
        var (userId, engines) = await SetupAsync(engines: 2);
        _broker.Answers[engines[0]] = (503, "");
        _broker.Answers[engines[1]] = (503, "");
        var jobId = await JobAsync(userId);
        string before;
        using (var scope = _sp.CreateScope())
            before = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AnalysisJobs.SingleAsync(j => j.Id == jobId)).EngineId;

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.EngineId != before);
        Assert.Equal(AnalysisJobStatus.Paused, job.Status);
        Assert.Contains("andere Engine", job.LastError);
    }

    // ── Lichess-Broker antwortet 400 (A4-006): Fehlversuch zaehlen statt ewig im Zwei-Minuten-Takt ───────────────

    /// <summary>Ein Nutzer mit einer Lichess-Engine (<c>eei_</c>); die Engine liegt schon im Cache, damit die Auflösung
    /// ohne Netz auskommt. Liefert die Auftrags-Id, mit <paramref name="fruitless"/> bisherigen Fehlversuchen.</summary>
    private async Task<int> LichessJobAsync(int fruitless)
    {
        const string engineId = "eei_cloud";
        int jobId;
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new AppUser { Username = "lichess", PasswordHash = "x" };
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
            var cred = new LichessEngineCredential
            {
                UserId = user.Id, EncryptedToken = scope.ServiceProvider.GetRequiredService<EncryptionService>().Encrypt("lip_token"),
            };
            cred.SetBackgroundEngines([engineId]);
            db.LichessEngineCredentials.Add(cred);
            await db.SaveChangesAsync();
            _sp.GetRequiredService<IMemoryCache>().Set($"lichess-engine:{user.Id}:{engineId}",
                new LichessExternalEngine(engineId, "Cloud", 2, 64, "cs"));
            jobId = (await scope.ServiceProvider.GetRequiredService<AnalysisJobService>().CreateAsync(user.Id,
                new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = 12, MultiPv = 2 }, remember: false)).Id;
            var job = await db.AnalysisJobs.SingleAsync(j => j.Id == jobId);
            job.FruitlessAttempts = fruitless;
            await db.SaveChangesAsync();
        }
        _broker.Answers[engineId] = (400, "");
        return jobId;
    }

    [Fact]
    public async Task LichessBroker_400_CountsAsFruitlessAttempt_AndBacksOff()
    {
        var jobId = await LichessJobAsync(fruitless: 0);

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Failed or AnalysisJobStatus.Paused);
        Assert.Equal(AnalysisJobStatus.Paused, job.Status);      // ein einzelner 400 ist noch kein Urteil
        Assert.Equal(1, job.FruitlessAttempts);
        Assert.Equal("Broker antwortete 400", job.LastError);
        Assert.NotNull(job.NextAttemptAt);
    }

    [Fact]
    public async Task LichessBroker_400_FailsAfterMaxFruitlessAttempts_InsteadOfLoopingForever()
    {
        var jobId = await LichessJobAsync(fruitless: AnalysisJob.MaxFruitlessAttempts - 1);

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Failed or AnalysisJobStatus.Paused);
        Assert.Equal(AnalysisJobStatus.Failed, job.Status);
        Assert.Equal($"Broker antwortete 400 in {AnalysisJob.MaxFruitlessAttempts} Läufen", job.LastError);
        Assert.Single(_broker.Calls);
    }

    // ── Haus-Engine (A4-004): fremde Auftraege rechnen nur, solange die Freigabe gilt ──────────────────────────────

    /// <summary>Der Admin aus <see cref="SetupAsync"/> teilt seine Engine als Haus-Engine; ein Gast reiht einen Auftrag
    /// darauf ein. Danach setzt <paramref name="afterwards"/> die Freigabe (oder nimmt sie zurück).</summary>
    private async Task<int> GuestJobOnHouseEngineAsync(Action<AppUser, LichessEngineCredential> afterwards)
    {
        var (adminId, _) = await SetupAsync();
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.AppUsers.SingleAsync(u => u.Id == adminId);
        var cred = await db.LichessEngineCredentials.SingleAsync(c => c.UserId == adminId);
        admin.IsAdmin = true;
        cred.ShareAsHouseEngine = true;
        var guest = new AppUser { Username = "gast", PasswordHash = "x" };
        db.AppUsers.Add(guest);
        await db.SaveChangesAsync();
        var dto = await scope.ServiceProvider.GetRequiredService<AnalysisJobService>().CreateAsync(guest.Id,
            new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = 12, MultiPv = 2 }, remember: false, engineOwnerUserId: adminId);
        afterwards(admin, cred);
        await db.SaveChangesAsync();
        return dto.Id;
    }

    [Theory]
    [InlineData(false, true)]    // Haekchen weg
    [InlineData(true, false)]    // Admin-Rolle weg, Haekchen steht noch
    public async Task HouseEngineWithdrawn_QueuedGuestJobFailsWithoutReachingTheBroker(bool share, bool isAdmin)
    {
        var jobId = await GuestJobOnHouseEngineAsync((admin, cred) => { admin.IsAdmin = isAdmin; cred.ShareAsHouseEngine = share; });

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Failed or AnalysisJobStatus.Done);
        Assert.Equal(AnalysisJobStatus.Failed, job.Status);
        Assert.Equal(EngineOwnerResolver.HouseEngineWithdrawnError, job.LastError);
        Assert.Empty(_broker.Calls);
    }

    [Fact]
    public async Task HouseEngineStillShared_GuestJobRunsOnIt()
    {
        var jobId = await GuestJobOnHouseEngineAsync((_, _) => { });
        string engineId;
        using (var scope = _sp.CreateScope())
            engineId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AnalysisJobs.SingleAsync(j => j.Id == jobId)).EngineId;
        _broker.Answers[engineId] = (200,
            "{\"time\":9,\"depth\":12,\"nodes\":90,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":-25,\"depth\":12}],\"bestmove\":\"e7e5\"}\n");

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Failed or AnalysisJobStatus.Done);
        Assert.Equal(AnalysisJobStatus.Done, job.Status);
    }

    [Fact]
    public async Task UnknownLocalEngine_FailsWithASourceNeutralMessage()
    {
        var (userId, engines) = await SetupAsync();
        int jobId;
        using (var scope = _sp.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AnalysisJobService>();
            // Eine unbekannte Kennung weist schon das Anlegen ab (A4-003) — der Container reicht die Registry durch.
            await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(userId,
                new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = 5, MultiPv = 1, EngineId = "rhe_gone00000000" }, remember: false));
            // Der Fall des Workers: die Registrierung verschwindet NACH dem Anlegen (Provider-Konto gelöscht).
            var dto = await svc.CreateAsync(userId,
                new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = 5, MultiPv = 1, EngineId = engines[0] }, remember: false);
            jobId = dto.Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ExternalEngineRegistrations.Remove(await db.ExternalEngineRegistrations.SingleAsync(r => r.Id == engines[0]));
            await db.SaveChangesAsync();
        }
        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status == AnalysisJobStatus.Failed);
        Assert.Equal("Engine nicht (mehr) registriert", job.LastError);
        Assert.Empty(_broker.Calls);
    }
}
