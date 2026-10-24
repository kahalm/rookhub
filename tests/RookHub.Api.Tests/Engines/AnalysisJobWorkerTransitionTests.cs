using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Accounts;
using RookHub.Api.DTOs.Engines;

namespace RookHub.Api.Tests.Engines;

/// <summary>
/// Charakterisierung der Statusübergänge eines Worker-Laufs (Zerlegung Z11, vor dem Herausziehen von
/// <c>ClassifyRunEnd</c>/<c>BrokerFailurePolicy</c>): jeder Übergang, der sich ohne Wettlauf und ohne Minuten-Fristen
/// von außen auslösen lässt, einmal echt durch den Worker. Die übrigen (Live-Vorrang beim Start, Kopfzeilen-Frist,
/// keine erste Zeile, Stillstand) hält der Tabellen-Test der reinen Regeln fest.
/// Schon anderswo abgedeckt: Haus-Engine zurückgenommen, Engine unbekannt, 400 eigener Broker, 400 Lichess (zählt/
/// scheitert), 503 mit Wechsel, fertig (Tiefe/Knoten), Verdrängung (<see cref="AnalysisJobWorkerBrokerTests"/>).
/// </summary>
public class AnalysisJobWorkerTransitionTests : IAsyncDisposable
{
    private const string Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
    private readonly ServiceProvider _sp;
    private readonly ScriptedBroker _broker = new();
    private readonly AnalysisJobWorker _worker;

    public AnalysisJobWorkerTransitionTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
            ["AnalysisJobs:TickSeconds"] = "1",
            ["AnalysisJobs:IdleGraceSeconds"] = "0",
            ["AnalysisJobs:FruitlessMinRuntimeSeconds"] = "5",   // kleinster erlaubter Wert
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

    /// <summary>Antwortet mit festem Status/Zeilen, wirft auf Wunsch, oder lässt den Stream erst nach einer Pause enden.</summary>
    private sealed class ScriptedBroker : IEngineBroker
    {
        public int Status = 200;
        public string Lines = "";
        public bool Throw;
        public TimeSpan SilentFor = TimeSpan.Zero;
        public int Calls;

        public Task<EngineAnalysisSession> AnalyseAsync(EngineRef engine, EngineWork work, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (Throw) throw new HttpRequestException("broker down");
            if (Status != 200) return Task.FromResult(EngineAnalysisSession.Rejected(Status, "nope"));
            Stream body = SilentFor > TimeSpan.Zero ? new SilentStream(SilentFor) : new MemoryStream(Encoding.UTF8.GetBytes(Lines));
            return Task.FromResult(new EngineAnalysisSession(200, body));
        }
    }

    /// <summary>Schweigt eine Weile und endet dann ohne eine Zeile — ein gekappter langer Lauf.</summary>
    private sealed class SilentStream(TimeSpan silence) : Stream
    {
        private bool _done;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_done) return 0;
            await Task.Delay(silence, ct);
            _done = true;
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

    private static string Line(int depth) =>
        $"{{\"time\":9,\"depth\":{depth},\"nodes\":{depth * 100},\"pvs\":[{{\"moves\":[\"e7e5\"],\"cp\":-25,\"depth\":{depth}}}]}}\n";

    /// <summary>Ein Auftrag auf einer direkt angebundenen Engine (eine einzige: kein Wechsel möglich), Tiefe 12.</summary>
    private async Task<int> LocalJobAsync(int reachedDepth = 0, int fruitless = 0)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new AppUser { Username = "kahalm", PasswordHash = "x" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var reg = new ExternalEngineRegistration
        {
            Id = ProviderSecrets.NewEngineId(), UserId = user.Id, Name = "E", ClientSecret = "cs",
            ProviderSelector = ProviderSecrets.Selector("secret-0-0123456789"), MaxThreads = 2, MaxHash = 64,
        };
        db.ExternalEngineRegistrations.Add(reg);
        var cred = new LichessEngineCredential { UserId = user.Id, EncryptedToken = "" };
        cred.SetBackgroundEngines([reg.Id]);
        db.LichessEngineCredentials.Add(cred);
        await db.SaveChangesAsync();
        var id = (await scope.ServiceProvider.GetRequiredService<AnalysisJobService>().CreateAsync(user.Id,
            new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = 12, MultiPv = 2 }, remember: false)).Id;
        var job = await db.AnalysisJobs.SingleAsync(j => j.Id == id);
        job.ReachedDepth = reachedDepth;
        job.FruitlessAttempts = fruitless;
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Ein Auftrag auf einer Lichess-Engine; danach wird der Token oder der Engine-Cache weggenommen.</summary>
    private async Task<int> LichessJobAsync(bool dropToken, bool dropCache)
    {
        const string engineId = "eei_cloud";
        using var scope = _sp.CreateScope();
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
        var cacheKey = $"lichess-engine:{user.Id}:{engineId}";
        var cache = _sp.GetRequiredService<IMemoryCache>();
        cache.Set(cacheKey, new LichessExternalEngine(engineId, "Cloud", 2, 64, "cs"));
        var id = (await scope.ServiceProvider.GetRequiredService<AnalysisJobService>().CreateAsync(user.Id,
            new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = 12, MultiPv = 2 }, remember: false)).Id;
        if (dropCache) cache.Remove(cacheKey);
        if (dropToken) { cred.EncryptedToken = ""; await db.SaveChangesAsync(); }
        return id;
    }

    private async Task<AnalysisJob> RunAsync(int jobId)
    {
        await _worker.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _sp.CreateScope();
            var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AnalysisJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
            if (job.Status is AnalysisJobStatus.Done or AnalysisJobStatus.Failed or AnalysisJobStatus.Paused) return job;
            await Task.Delay(100);
        }
        throw new TimeoutException("Auftrag kam nicht an");
    }

    private static void AssertBackoff(AnalysisJob job, int seconds)
    {
        Assert.NotNull(job.NextAttemptAt);
        var left = (job.NextAttemptAt!.Value - job.UpdatedAt).TotalSeconds;
        Assert.InRange(left, seconds - 2, seconds + 1);
    }

    [Fact]
    public async Task LichessUnreachableWhileResolving_BacksOff60s()
    {
        var jobId = await LichessJobAsync(dropToken: false, dropCache: true);
        var job = await RunAsync(jobId);
        Assert.Equal(AnalysisJobStatus.Paused, job.Status);
        Assert.Equal("Lichess nicht erreichbar", job.LastError);
        AssertBackoff(job, 60);
        Assert.Equal(0, _broker.Calls);
    }

    [Fact]
    public async Task NoLichessToken_Fails()
    {
        var jobId = await LichessJobAsync(dropToken: true, dropCache: false);
        var job = await RunAsync(jobId);
        Assert.Equal(AnalysisJobStatus.Failed, job.Status);
        Assert.Equal("Kein Lichess-Token hinterlegt", job.LastError);
        Assert.Equal(0, _broker.Calls);
    }

    [Fact]
    public async Task BrokerUnreachable_BacksOff60s()
    {
        _broker.Throw = true;
        var job = await RunAsync(await LocalJobAsync());
        Assert.Equal(AnalysisJobStatus.Paused, job.Status);
        Assert.Equal("Broker nicht erreichbar", job.LastError);
        AssertBackoff(job, 60);
    }

    [Fact]
    public async Task Broker503_WithoutAnotherEngine_BacksOff120s()
    {
        _broker.Status = 503;
        var job = await RunAsync(await LocalJobAsync());
        Assert.Equal(AnalysisJobStatus.Paused, job.Status);
        Assert.Equal("Broker antwortete 503", job.LastError);
        AssertBackoff(job, 120);
        Assert.Equal(0, job.FruitlessAttempts);
    }

    [Fact]
    public async Task StreamEndsBelowTheTarget_WithProgress_PausesAndResetsTheCounter()
    {
        _broker.Lines = Line(5) + Line(8);
        var job = await RunAsync(await LocalJobAsync(fruitless: 2));
        Assert.Equal(AnalysisJobStatus.Paused, job.Status);
        Assert.Equal("Stream vor der Zieltiefe beendet", job.LastError);
        AssertBackoff(job, 30);
        Assert.Equal(0, job.FruitlessAttempts);
        Assert.Equal(8, job.ReachedDepth);
    }

    [Fact]
    public async Task StreamEndsAtOnce_WithoutProgress_CountsAFruitlessAttempt()
    {
        _broker.Lines = Line(5);   // flacher als das gespeicherte Ergebnis: kein Fortschritt
        var job = await RunAsync(await LocalJobAsync(reachedDepth: 10, fruitless: 0));
        Assert.Equal(AnalysisJobStatus.Paused, job.Status);
        Assert.Equal("Stream vor der Zieltiefe beendet", job.LastError);
        AssertBackoff(job, 30);
        Assert.Equal(1, job.FruitlessAttempts);
        Assert.Equal(10, job.ReachedDepth);
    }

    [Fact]
    public async Task StreamEndsAtOnce_WithoutProgress_FailsAfterMaxFruitlessAttempts()
    {
        _broker.Lines = "";
        var job = await RunAsync(await LocalJobAsync(reachedDepth: 10, fruitless: AnalysisJob.MaxFruitlessAttempts - 1));
        Assert.Equal(AnalysisJobStatus.Failed, job.Status);
        Assert.Equal($"Engine lieferte in {AnalysisJob.MaxFruitlessAttempts} Läufen sofort keine Bewertung", job.LastError);
        Assert.Equal(AnalysisJob.MaxFruitlessAttempts, job.FruitlessAttempts);
    }

    [Fact]
    public async Task LongRunWithoutProgress_IsACutConnection_NotAFruitlessAttempt()
    {
        _broker.SilentFor = TimeSpan.FromSeconds(5.5);
        var job = await RunAsync(await LocalJobAsync(reachedDepth: 10, fruitless: 1));
        Assert.Equal(AnalysisJobStatus.Paused, job.Status);
        Assert.Equal("Stream vorzeitig beendet — wird fortgesetzt", job.LastError);
        AssertBackoff(job, 30);
        Assert.Equal(1, job.FruitlessAttempts);
    }
}
