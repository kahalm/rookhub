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

        public Task<EngineAnalysisSession> AnalyseAsync(EngineRef engine, EngineWork work, CancellationToken ct)
        {
            lock (Calls) Calls.Add((engine.Id, work));
            var (status, lines) = Answers[engine.Id];
            return Task.FromResult(status == 200
                ? new EngineAnalysisSession(200, new MemoryStream(Encoding.UTF8.GetBytes(lines)))
                : EngineAnalysisSession.Rejected(status, "invalid work: illegal initial position: opposite check"));
        }
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

    private async Task<int> JobAsync(int userId, int depth = 12)
    {
        using var scope = _sp.CreateScope();
        var dto = await scope.ServiceProvider.GetRequiredService<AnalysisJobService>()
            .CreateAsync(userId, new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = depth, MultiPv = 2 }, remember: false);
        return dto.Id;
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

    [Fact]
    public async Task UnknownLocalEngine_FailsWithASourceNeutralMessage()
    {
        var (userId, _) = await SetupAsync();
        int jobId;
        using (var scope = _sp.CreateScope())
        {
            var dto = await scope.ServiceProvider.GetRequiredService<AnalysisJobService>().CreateAsync(userId,
                new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = 5, MultiPv = 1, EngineId = "rhe_gone00000000" }, remember: false);
            jobId = dto.Id;
        }
        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status == AnalysisJobStatus.Failed);
        Assert.Equal("Engine nicht (mehr) registriert", job.LastError);
        Assert.Empty(_broker.Calls);
    }
}
