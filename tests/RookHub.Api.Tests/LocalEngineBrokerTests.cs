using System.IO.Pipelines;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.DTOs;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// Anfragender ↔ Hub ↔ Upload, ohne HTTP: der Anfragende bekommt die Zeilen WÄHREND des Uploads, das
/// Lebenszeichen unverändert, ein Abbruch beendet den Upload sofort, ohne Provider gibt es nach der Frist
/// 503 (daran hängt der Engine-Wechsel des Workers).
/// </summary>
public class LocalEngineBrokerTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private readonly LocalBrokerOptions _options = new() { ProviderTimeout = TimeSpan.FromMilliseconds(300) };
    private readonly EngineHub _hub;
    private readonly LocalEngineBroker _broker;

    public LocalEngineBrokerTests()
    {
        _hub = new EngineHub(_options, () => DateTime.UtcNow, startSweeper: false);
        _broker = new LocalEngineBroker(_hub, _options, NullLogger<LocalEngineBroker>.Instance);
    }

    private static EngineRef Engine(string selector = "sel") => new(
        "rhe_aaaaaaaaaaaa", "Heim-PC", 8, 1024, EngineSource.Local,
        Local: new LocalEngineTarget(selector, new ExternalEngineRegistrationDto
        {
            Id = "rhe_aaaaaaaaaaaa", Name = "Heim-PC", ClientSecret = "cs", UserId = "kahalm",
            MaxThreads = 8, MaxHash = 1024, Variants = ["chess"],
        }));

    private static EngineWork Work(int multiPv = 1, params string[] moves) =>
        new("sess", 64, 65536, multiPv, Start, moves, Depth: 20);

    /// <summary>Holt den Auftrag wie ein Provider ab und startet den Upload über eine Pipe.</summary>
    private async Task<(PendingJob Job, PipeWriter Body, Task<UploadResult> Upload)> ProviderTakesAsync(string selector = "sel")
    {
        var polled = await _hub.AcquireAsync(selector, TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.NotNull(polled);
        var job = _hub.TakeOngoing(polled!.Id!)!;
        var pipe = new Pipe();
        var upload = EngineUploadPump.RunAsync(job, pipe.Reader.AsStream(), CancellationToken.None, NullLogger.Instance);
        return (job, pipe.Writer, upload);
    }

    private static async Task WriteLineAsync(PipeWriter w, string line)
    {
        await w.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
        await w.FlushAsync();
    }

    private static async Task<string?> NextLineAsync(StreamReader r) =>
        await r.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Lines_ArriveWhileTheUploadRuns_KeepaliveVerbatim_BestmoveEnds()
    {
        var sessionTask = _broker.AnalyseAsync(Engine(), Work(), CancellationToken.None);
        var (job, body, upload) = await ProviderTakesAsync();
        await using var session = await sessionTask;
        Assert.Equal(200, session.StatusCode);
        Assert.Equal(8, job.Work.Threads);                 // auf die Engine-Maxima geklemmt
        Assert.Equal(1024, job.Work.Hash);
        using var reader = new StreamReader(session.Ndjson!);

        await WriteLineAsync(body, "info depth 1 multipv 1 score cp 20 nodes 10 time 1 pv e2e4");
        Assert.Equal("""{"time":1,"depth":1,"nodes":10,"pvs":[{"moves":["e2e4"],"cp":20,"depth":1}]}""", await NextLineAsync(reader));

        await WriteLineAsync(body, """{"keepalive":true}""");
        Assert.Equal("""{"keepalive":true}""", await NextLineAsync(reader));

        await WriteLineAsync(body, "bestmove e2e4 ponder e7e5");
        Assert.Equal("""{"time":1,"depth":1,"nodes":10,"pvs":[{"moves":["e2e4"],"cp":20,"depth":1}],"bestmove":"e2e4","ponder":"e7e5"}""",
            await NextLineAsync(reader));
        Assert.Null(await NextLineAsync(reader));          // Strom zu Ende
        Assert.Equal(UploadOutcome.Completed, (await upload).Outcome);
    }

    [Fact]
    public async Task RequesterLeaves_TheUploadEndsImmediately_EvenWhileTheEngineIsSilent()
    {
        var sessionTask = _broker.AnalyseAsync(Engine(), Work(), CancellationToken.None);
        var (_, body, upload) = await ProviderTakesAsync();
        var session = await sessionTask;
        await WriteLineAsync(body, "info depth 1 multipv 1 score cp 20 nodes 10 time 1 pv e2e4");

        await session.DisposeAsync();                       // Browser wechselt die Stellung / Worker pausiert
        var result = await upload.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(UploadOutcome.RequesterGone, result.Outcome);
    }

    [Fact]
    public async Task NoProvider_Is503_AfterTheProviderTimeout_AndTheJobIsDropped()
    {
        var started = DateTime.UtcNow;
        await using var session = await _broker.AnalyseAsync(Engine(), Work(), CancellationToken.None);
        Assert.Equal(503, session.StatusCode);
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(250));
        Assert.Equal(0, _hub.QueuedCount("sel"));          // ein später Provider bekommt ihn nicht mehr
        Assert.Equal(1, _hub.Stats.For("rhe_aaaaaaaaaaaa")!.ProviderTimeouts);
    }

    [Fact]
    public async Task InvalidWork_Is400_WithoutQueueing()
    {
        await using var session = await _broker.AnalyseAsync(Engine(), Work(1, "e2e5"), CancellationToken.None);
        Assert.Equal(400, session.StatusCode);
        Assert.Contains("illegal uci move", session.Error);
        Assert.Equal(0, _hub.QueuedCount("sel"));
    }

    [Fact]
    public async Task FullQueue_Is503()
    {
        var options = new LocalBrokerOptions { ProviderTimeout = TimeSpan.FromSeconds(5), MaxQueuedPerEngine = 1 };
        var hub = new EngineHub(options, () => DateTime.UtcNow, startSweeper: false);
        var broker = new LocalEngineBroker(hub, options, NullLogger<LocalEngineBroker>.Instance);
        using var cts = new CancellationTokenSource();
        var waiting = broker.AnalyseAsync(Engine(), Work(), cts.Token);
        await using var second = await broker.AnalyseAsync(Engine(), Work(), CancellationToken.None);
        Assert.Equal(503, second.StatusCode);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task UploadWithoutBestmove_EndsTheStreamRegularly_WithTheLastState()
    {
        var sessionTask = _broker.AnalyseAsync(Engine(), Work(), CancellationToken.None);
        var (_, body, upload) = await ProviderTakesAsync();
        await using var session = await sessionTask;
        using var reader = new StreamReader(session.Ndjson!);
        await WriteLineAsync(body, "info depth 3 multipv 1 score cp 5 nodes 30 time 3 pv d2d4");
        await NextLineAsync(reader);
        await body.CompleteAsync();                         // Provider schließt ohne bestmove

        Assert.Equal("""{"time":3,"depth":3,"nodes":30,"pvs":[{"moves":["d2d4"],"cp":5,"depth":3}]}""", await NextLineAsync(reader));
        Assert.Null(await NextLineAsync(reader));
        Assert.Equal(UploadOutcome.WithoutBestmove, (await upload).Outcome);
    }

    [Fact]
    public async Task BrokenLines_AreSkipped_NotFatal()
    {
        var sessionTask = _broker.AnalyseAsync(Engine(), Work(), CancellationToken.None);
        var (_, body, upload) = await ProviderTakesAsync();
        await using var session = await sessionTask;
        using var reader = new StreamReader(session.Ndjson!);

        await WriteLineAsync(body, "info depth 1 score cp 1 wdl 1 2 3 pv e2e4");   // lila-engine: 400 + Abbruch
        await WriteLineAsync(body, """{"hello":"world"}""");                        // unbekannte Steuerzeile
        await WriteLineAsync(body, "info string " + new string('x', 20_000));      // über 16 KiB
        await WriteLineAsync(body, "info depth 2 multipv 1 score cp 7 nodes 5 time 2 pv g1f3");
        Assert.Equal("""{"time":2,"depth":2,"nodes":5,"pvs":[{"moves":["g1f3"],"cp":7,"depth":2}]}""", await NextLineAsync(reader));
        await WriteLineAsync(body, "bestmove g1f3");
        await NextLineAsync(reader);
        var result = await upload;
        Assert.Equal(UploadOutcome.Completed, result.Outcome);
        Assert.Equal(3, result.SkippedLines);
    }

    [Fact]
    public async Task CrLfAndAFinalLineWithoutNewline_AreRead()
    {
        var sessionTask = _broker.AnalyseAsync(Engine(), Work(), CancellationToken.None);
        var (_, body, upload) = await ProviderTakesAsync();
        await using var session = await sessionTask;
        using var reader = new StreamReader(session.Ndjson!);
        await body.WriteAsync(Encoding.UTF8.GetBytes("info depth 1 multipv 1 score cp 1 nodes 1 time 1 pv e2e4\r\nbestmove e2e4"));
        await body.CompleteAsync();
        Assert.Contains("\"cp\":1", await NextLineAsync(reader));
        Assert.Contains("\"bestmove\":\"e2e4\"", await NextLineAsync(reader));
        Assert.Equal(UploadOutcome.Completed, (await upload).Outcome);
    }

    [Fact]
    public async Task ProviderConnectionBreaks_RequesterGetsTheLastStateAndAnEnd()
    {
        var sessionTask = _broker.AnalyseAsync(Engine(), Work(), CancellationToken.None);
        var (_, body, upload) = await ProviderTakesAsync();
        await using var session = await sessionTask;
        using var reader = new StreamReader(session.Ndjson!);
        await WriteLineAsync(body, "info depth 4 multipv 1 score cp 9 nodes 40 time 4 pv c2c4");
        await NextLineAsync(reader);
        await body.CompleteAsync(new IOException("connection reset"));

        Assert.Equal("""{"time":4,"depth":4,"nodes":40,"pvs":[{"moves":["c2c4"],"cp":9,"depth":4}]}""", await NextLineAsync(reader));
        Assert.Null(await NextLineAsync(reader));
        Assert.Equal(UploadOutcome.ProviderGone, (await upload).Outcome);
    }

    [Fact]
    public async Task AcquireResponse_EngineObject_HasTheLichessShape()
    {
        var sessionTask = _broker.AnalyseAsync(Engine(), Work(3, "e2e4"), CancellationToken.None);
        var (job, body, upload) = await ProviderTakesAsync();
        Assert.Equal(
            """{"id":"rhe_aaaaaaaaaaaa","name":"Heim-PC","clientSecret":"cs","userId":"kahalm","maxThreads":8,"maxHash":1024,"variants":["chess"],"providerData":null}""",
            job.EngineJson.ToJsonString());
        Assert.Equal(
            """{"sessionId":"sess","threads":8,"hash":1024,"depth":20,"multiPv":3,"variant":"chess","initialFen":"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1","moves":["e2e4"]}""",
            job.Work.ToJson().ToJsonString());
        await WriteLineAsync(body, "bestmove e7e5");
        await (await sessionTask).DisposeAsync();
        await upload;
    }
}
