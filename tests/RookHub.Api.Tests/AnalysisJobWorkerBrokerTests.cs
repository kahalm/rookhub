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

    private async Task<int> JobAsync(int userId, int depth = 12, bool background = false, long? nodes = null)
    {
        using var scope = _sp.CreateScope();
        var dto = await scope.ServiceProvider.GetRequiredService<AnalysisJobService>()
            .CreateAsync(userId, new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = depth, MultiPv = 2, TargetNodes = nodes }, remember: false,
                background: background);
        return dto.Id;
    }

    // ── Knotenziel (Phase 1 der Lc0-Zweitprüfung): `nodes` statt `depth`, fertig bei erreichtem Ziel ──────────────

    private static string Line(int depth, long nodes, bool best = false) =>
        $"{{\"time\":9,\"depth\":{depth},\"nodes\":{nodes},\"pvs\":[{{\"moves\":[\"e7e5\"],\"cp\":-25,\"depth\":{depth}}}]"
        + (best ? ",\"bestmove\":\"e7e5\"" : "") + "}\n";

    [Fact]
    public async Task NodeJob_sendsNodesInsteadOfDepth_andIsDoneWhenTheGoalIsReached()
    {
        var (userId, engines) = await SetupAsync();
        _broker.Answers[engines[0]] = (200, Line(6, 2_000) + Line(9, 50_000, best: true));
        var jobId = await JobAsync(userId, depth: 30, nodes: 50_000);

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Done or AnalysisJobStatus.Failed or AnalysisJobStatus.Paused);

        Assert.Equal(AnalysisJobStatus.Done, job.Status);       // Tiefe 9 < 30, und trotzdem fertig: das Knotenziel zählt
        Assert.Equal(9, job.ReachedDepth);
        Assert.Contains("\"nodes\":50000", job.ResultJson);
        var (_, work) = Assert.Single(_broker.Calls);
        Assert.Equal(50_000, work.Nodes);
        Assert.Null(work.Depth);                                 // genau EIN Limit (WorkSanitizer)
    }

    [Fact]
    public async Task NodeJob_stopsItselfAtTheGoal_evenIfTheEngineKeepsGoing()
    {
        var (userId, engines) = await SetupAsync();
        _broker.Answers[engines[0]] = (200, Line(6, 2_000) + Line(8, 6_000) + Line(10, 9_000, best: true));
        var jobId = await JobAsync(userId, nodes: 5_000);

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Done or AnalysisJobStatus.Failed or AnalysisJobStatus.Paused);

        Assert.Equal(AnalysisJobStatus.Done, job.Status);
        Assert.True(job.ReachedDepth >= 8);
    }

    [Fact]
    public async Task NodeJob_streamEndsJustBelowTheGoal_countsAsDone_butFarBelowWithoutBestmoveIsNot()
    {
        var (userId, engines) = await SetupAsync(engines: 2);
        _broker.Answers[engines[0]] = (200, Line(9, 98_000, best: true));   // 98 % — Lc0 hört gern ein paar Knoten früher auf
        // 40 % OHNE bestmove: die Suche wurde abgeschnitten, nicht beendet — kein Ergebnis. (MIT bestmove hätte die
        // Engine selbst aufgehört, siehe NodeJob_directEngineStopsEarlyOnItsOwn_isDoneAfterOneRun.)
        _broker.Answers[engines[1]] = (200, Line(4, 40_000));
        var near = await JobAsync(userId, nodes: 100_000);
        var far = await JobAsync(userId, nodes: 100_000);
        // Beide Aufträge auf je ihre Engine festlegen (die automatische Wahl verteilt sie sonst nach Schlangenlänge).
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AnalysisJobs.SingleAsync(j => j.Id == near)).EngineId = engines[0];
            (await db.AnalysisJobs.SingleAsync(j => j.Id == far)).EngineId = engines[1];
            await db.SaveChangesAsync();
        }

        await _worker.StartAsync(CancellationToken.None);
        Assert.Equal(AnalysisJobStatus.Done, (await WaitForAsync(near, j => j.Status != AnalysisJobStatus.Queued && j.Status != AnalysisJobStatus.Running)).Status);
        Assert.Equal(AnalysisJobStatus.Paused, (await WaitForAsync(far, j => j.Status != AnalysisJobStatus.Queued && j.Status != AnalysisJobStatus.Running)).Status);
    }

    // 2026-10-06: Lc0 beendet eine Suche mit Knotenlimit vor dem Ziel, sobald der beste Zug feststeht (Smart Pruning) —
    // 39 000 von 50 000. Vorher lief so ein Auftrag alle 30 s neu und wurde nie fertig. Bei einer direkt angebundenen
    // Engine reicht EIN Lauf: jeden Stopp von aussen saehe RookHub selbst (eigener Abbruch oder Live-Analyse).
    [Fact]
    public async Task NodeJob_directEngineStopsEarlyOnItsOwn_isDoneAfterOneRun()
    {
        var (userId, engines) = await SetupAsync();
        _broker.Answers[engines[0]] = (200, Line(6, 4_000) + Line(15, 39_367, best: true));
        var jobId = await JobAsync(userId, depth: 30, nodes: 50_000);

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Done or AnalysisJobStatus.Paused or AnalysisJobStatus.Failed);

        Assert.Equal(AnalysisJobStatus.Done, job.Status);
        Assert.Equal(15, job.ReachedDepth);
        Assert.Contains("\"nodes\":39367", job.ResultJson);
        Assert.Single(_broker.Calls);                         // nichts doppelt gerechnet
    }

    [Theory]
    [InlineData(null, 39_000L, false)]      // erster Lauf: kein Vergleich
    [InlineData(39_367L, 39_000L, true)]    // gleiche Stelle
    [InlineData(39_000L, 42_900L, true)]    // bis 10 % mehr gilt als dieselbe Stelle
    [InlineData(20_000L, 39_000L, false)]   // deutlich weiter: der vorige Lauf war gestoppt
    [InlineData(39_000L, 0L, false)]        // keine Knoten gemeldet
    public void StoppedEarlyAgain_vergleichtMitDemVorigenLauf(long? previous, long nodes, bool expected)
        => Assert.Equal(expected, AnalysisJobStream.StoppedEarlyAgain(previous, nodes));

    [Theory]
    //          direkt  bestmove abbruch live   vorher    jetzt    erwartet
    [InlineData(true,  true,  false, false, null,    39_000L, true)]    // direkt: ein sauberes Ende genuegt
    [InlineData(true,  false, false, false, null,    39_000L, false)]   // ohne bestmove: abgeschnitten
    [InlineData(true,  true,  true,  false, null,    39_000L, false)]   // wir selbst haben abgebrochen
    [InlineData(true,  true,  false, true,  null,    39_000L, false)]   // Live-Analyse auf der Engine
    [InlineData(false, true,  false, false, null,    39_000L, false)]   // Lichess, erster Lauf: koennte gestoppt sein
    [InlineData(false, true,  false, false, 39_367L, 39_000L, true)]    // Lichess, zweimal gleich: eigenes Ende
    [InlineData(false, true,  false, false, 20_000L, 39_000L, false)]   // Lichess, deutlich weiter: vorher gestoppt
    public void EngineEndedOwnSearch_entscheidetNachAnbindung(bool direct, bool best, bool cancelled, bool live,
        long? previous, long nodes, bool expected)
        => Assert.Equal(expected, AnalysisJobStream.EngineEndedOwnSearch(direct, best, cancelled, live, previous, nodes));

    [Fact]
    public void HasBestMove_nurMitBestmoveFeld()
    {
        Assert.True(AnalysisJobStream.HasBestMove(Line(9, 10, best: true).Trim()));
        Assert.False(AnalysisJobStream.HasBestMove(Line(9, 10).Trim()));
        Assert.False(AnalysisJobStream.HasBestMove("kein json"));
    }

    [Fact]
    public async Task NodeJob_recordsStepsPerThreshold_missingStepsAreNotInvented()
    {
        var (userId, engines) = await SetupAsync();
        // Schweigen zwischen 31k und 50k ist kein Problem (40k hat die Zeile bei 31k), zwischen 12k und 31k fehlt 30k:
        // die Zeile bei 12k läge 18k darunter — mehr als eine Schrittweite.
        _broker.Answers[engines[0]] = (200, Line(5, 4_000) + Line(7, 12_000) + Line(8, 31_000) + Line(9, 50_000, best: true));
        var jobId = await JobAsync(userId, nodes: 50_000);

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Done or AnalysisJobStatus.Failed or AnalysisJobStatus.Paused);

        Assert.Equal(AnalysisJobStatus.Done, job.Status);
        var steps = NodeSteps.Parse(job.NodeStepsJson);
        Assert.Equal(new[] { 10_000L, 20_000L, 40_000L, 50_000L }, steps.Select(s => s.Threshold));
        Assert.Equal(new[] { 4_000L, 12_000L, 31_000L, 50_000L }, steps.Select(s => s.Nodes));
        Assert.All(steps, s => { Assert.Equal("e7e5", s.Uci); Assert.Equal(25, s.Cp); });     // Schwarz am Zug: -25 (Weiß) → +25
    }

    [Fact]
    public async Task DepthJob_hasNoSteps()
    {
        var (userId, engines) = await SetupAsync();
        _broker.Answers[engines[0]] = (200, Line(8, 10_000) + Line(12, 90_000, best: true));
        var jobId = await JobAsync(userId, depth: 12);

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Done or AnalysisJobStatus.Failed);

        Assert.Equal(AnalysisJobStatus.Done, job.Status);
        Assert.Null(job.NodeStepsJson);
    }

    [Fact]
    public async Task DepthJob_staysUnchanged_depthGoesToTheEngine_noNodes()
    {
        var (userId, engines) = await SetupAsync();
        _broker.Answers[engines[0]] = (200, Line(8, 10) + Line(12, 90, best: true));
        var jobId = await JobAsync(userId, depth: 12);

        await _worker.StartAsync(CancellationToken.None);
        var job = await WaitForAsync(jobId, j => j.Status is AnalysisJobStatus.Done or AnalysisJobStatus.Failed);

        Assert.Equal(AnalysisJobStatus.Done, job.Status);
        Assert.Null(job.TargetNodes);
        var (_, work) = Assert.Single(_broker.Calls);
        Assert.Equal(12, work.Depth);
        Assert.Null(work.Nodes);
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
