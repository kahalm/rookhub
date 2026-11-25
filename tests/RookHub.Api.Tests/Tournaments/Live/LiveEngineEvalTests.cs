using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>Engine-Balken mit eigener Engine (1.015.0): Rückfallkette, nur neueste Stellung, eine Rechnung gleichzeitig,
/// nichts ohne Balken-Zuschauer, keine Engine → leer. Broker und Auswahl sind gefälscht.</summary>
public class LiveEngineEvalTests
{
    private const string Round = "R9900002";
    private const string WhiteFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string BlackFen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

    /// <summary>Rechnet „auf Zuruf": jede Bewertung wartet, bis der Test sie freigibt; zählt Aufrufe und Gleichzeitigkeit.</summary>
    private sealed class GatedEvaluator : ILiveEngineEvaluator
    {
        public readonly List<string> Calls = [];
        public int Running, MaxRunning;
        public bool Gate = true;
        private TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource FirstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() { Gate = false; _open.TrySetResult(); }

        public async Task<LiveEval?> EvaluateAsync(string fen, CancellationToken ct)
        {
            lock (Calls) Calls.Add(fen);
            var now = Interlocked.Increment(ref Running);
            lock (Calls) MaxRunning = Math.Max(MaxRunning, now);
            FirstStarted.TrySetResult();
            try
            {
                if (Gate) await _open.Task.WaitAsync(ct);
                await Task.Delay(5, ct);
                return new LiveEval(42, null, "engine", 18);
            }
            finally { Interlocked.Decrement(ref Running); }
        }
    }

    private static async Task Until(Func<bool> cond)
    {
        for (var i = 0; i < 250 && !cond(); i++) await Task.Delay(20);
        Assert.True(cond());
    }

    [Fact]
    public async Task Queue_NewestPositionOnly_RoundRobin_OneAtATime()
    {
        var ev = new GatedEvaluator();
        await using var q = new LiveEngineQueue(ev, NullLogger<LiveEngineQueue>.Instance);
        var applied = new List<(string Key, string Fen)>();
        void Add(string key, string fen) => q.Enqueue(key, fen, _ => true, (f, _) => { lock (applied) applied.Add((key, f)); });

        Add("A", "a1");                                         // läuft sofort an, hängt am Tor
        await ev.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Add("B", "b1");
        Add("A", "a2");                                         // A ist wieder dran — hinter B
        Add("A", "a3");                                         // ersetzt a2 an seinem Platz
        Add("C", "c1");
        Add("B", "b2");                                         // ersetzt b1, B bleibt vorn
        ev.Open();
        await Until(() => { lock (applied) return applied.Count == 4; });
        Assert.Equal(["a1", "b2", "a3", "c1"], ev.Calls);
        Assert.Equal(1, ev.MaxRunning);
        Assert.Equal(0, q.Pending);
    }

    [Fact]
    public async Task Queue_NotWanted_NotComputed()
    {
        var ev = new GatedEvaluator { Gate = false };
        await using var q = new LiveEngineQueue(ev, NullLogger<LiveEngineQueue>.Instance);
        q.Enqueue("A", "x", _ => false, (_, _) => Assert.Fail("übernommen"));
        await Until(() => q.Pending == 0);
        await Task.Delay(50);
        Assert.Empty(ev.Calls);
    }

    private static BroadcastLiveHub Hub(FakeLichessLiveSource src, LiveEngineQueue q) =>
        new(src, new LichessCloudEval(src, new MemoryCache(new MemoryCacheOptions()), NullLogger<LichessCloudEval>.Instance) { Spacing = TimeSpan.Zero },
            NullLogger<BroadcastLiveHub>.Instance, new BroadcastLiveHub.HubOptions { IdleClose = TimeSpan.FromMilliseconds(50) }, q);

    private static async Task<LiveGameChanged> NextChange(BroadcastLiveHub.RoundSubscription sub)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
            if (await sub.Events.ReadAsync(cts.Token) is LiveGameChanged c) return c;
    }

    [Fact]
    public async Task Hub_FallbackChain_BroadcastThenCloudThenEngine()
    {
        var src = new FakeLichessLiveSource();
        var withEval = LivePgn.Game("g1", "A", "B", "1. e4 { [%eval 0.3] }");
        var cloudHit = LivePgn.Game("g2", "C", "D", "1. d4");
        var engineOnly = LivePgn.Game("g3", "E", "F", "1. c4");
        src.RoundPgn[Round] = withEval + cloudHit + engineOnly;
        src.CloudEvals[LiveGameParser.Parse(cloudHit)!.Fen] = """{"depth":40,"pvs":[{"cp":20}]}""";
        var ev = new GatedEvaluator { Gate = false };
        await using var q = new LiveEngineQueue(ev, NullLogger<LiveEngineQueue>.Instance);
        await using var hub = Hub(src, q);

        var sub = hub.Subscribe(Round, false, wantsEval: true);
        var got = new Dictionary<string, LiveEval>();
        while (got.Count < 2)
        {
            var c = await NextChange(sub);
            if (c.Game.Eval is { } e) got[c.Game.Id] = e;
        }
        Assert.Equal("cloud", got["g2"].Source);
        Assert.Equal(("engine", 18), (got["g3"].Source, got["g3"].Depth));
        Assert.DoesNotContain(LiveGameParser.Parse(withEval)!.Fen, ev.Calls);   // [%eval] aus dem Strom genügt
        Assert.DoesNotContain(LiveGameParser.Parse(cloudHit)!.Fen, ev.Calls);   // Cloud hatte sie
        Assert.Equal(2, src.CloudEvalCalls);                                    // g1 nicht gefragt
        await sub.DisposeAsync();
    }

    [Fact]
    public async Task Hub_NoViewerWithBar_NoEngine()
    {
        var src = new FakeLichessLiveSource();
        src.RoundPgn[Round] = LivePgn.Game("g1", "A", "B", "1. c4");
        var ev = new GatedEvaluator { Gate = false };
        await using var q = new LiveEngineQueue(ev, NullLogger<LiveEngineQueue>.Instance);
        await using var hub = Hub(src, q);
        var sub = hub.Subscribe(Round, false, wantsEval: false);
        await src.StreamOf(Round).Writer.WriteAsync(LivePgn.Game("g1", "A", "B", "1. c4 e5"));
        await NextChange(sub);
        await Task.Delay(150);
        Assert.Empty(ev.Calls);
        Assert.Equal(0, src.CloudEvalCalls);
        await sub.DisposeAsync();
    }

    // ── Bewerter über den Broker ────────────────────────────────────────────────────────────────

    private sealed class FakeSelector(LiveEngineChoice? choice) : ILiveEngineSelector
    {
        public Task<LiveEngineChoice?> SelectAsync(CancellationToken ct) => Task.FromResult(choice);
    }

    private sealed class FakeBroker(string ndjson) : IEngineBroker
    {
        public int Calls;
        public EngineWork? Last;
        public Task<EngineAnalysisSession> AnalyseAsync(EngineRef engine, EngineWork work, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Last = work;
            return Task.FromResult(new EngineAnalysisSession(200, new MemoryStream(Encoding.UTF8.GetBytes(ndjson))));
        }
    }

    private static (BrokerLiveEngineEvaluator Eval, FakeBroker Broker) Evaluator(LiveEngineChoice? choice, string ndjson)
    {
        var broker = new FakeBroker(ndjson);
        var sp = new ServiceCollection()
            .AddScoped<ILiveEngineSelector>(_ => new FakeSelector(choice))
            .AddTransient<IEngineBroker>(_ => broker)
            .BuildServiceProvider();
        return (new BrokerLiveEngineEvaluator(sp.GetRequiredService<IServiceScopeFactory>(), new MemoryCache(new MemoryCacheOptions()),
            new LiveEngineOptions(), NullLogger<BrokerLiveEngineEvaluator>.Instance), broker);
    }

    private static readonly LiveEngineChoice Choice = new(new EngineRef("rhe_990001", "Live", 8, 1024, EngineSource.Local, Online: true), 990001);

    [Fact]
    public async Task Evaluator_NoEngine_Null_NoBrokerCall()
    {
        var (eval, broker) = Evaluator(null, "");
        Assert.Null(await eval.EvaluateAsync(WhiteFen, default));
        Assert.Equal(0, broker.Calls);
    }

    [Fact]
    public async Task Evaluator_LastLine_WhitePov_ShortSearch_Cached()
    {
        const string lines = "{\"keepalive\":true}\n{\"time\":10,\"depth\":8,\"nodes\":1,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":10,\"depth\":8}]}\n"
                             + "{\"time\":1500,\"depth\":18,\"nodes\":9,\"pvs\":[{\"moves\":[\"c7c5\"],\"cp\":-35,\"depth\":18}]}\n";
        var (eval, broker) = Evaluator(Choice, lines);
        var ev = await eval.EvaluateAsync(BlackFen, default);
        Assert.Equal(new LiveEval(35, null, "engine", 18), ev);      // Schwarz am Zug: −35 aus seiner Sicht = +35 für Weiß
        Assert.Equal((1500, 1, 2, 64), (broker.Last!.Movetime, broker.Last.MultiPv, broker.Last.Threads, broker.Last.Hash));
        Assert.Equal(ev, await eval.EvaluateAsync(BlackFen, default));
        Assert.Equal(1, broker.Calls);                               // gecacht je Stellung
    }

    [Fact]
    public void Pick_LiveEngineNotBackground_OnlineOnly_ConfiguredWins()
    {
        EngineRef E(string id, bool online, EngineSource src = EngineSource.Local) => new(id, id, 1, 16, src, Online: online);
        var engines = new[] { E("rhe_bg1", true), E("rhe_off", false), E("rhe_live", true), E("eei_lichess", false, EngineSource.Lichess) };
        Assert.Equal("rhe_live", RegistryLiveEngineSelector.Pick(engines, ["rhe_bg1"], null)!.Id);
        Assert.Null(RegistryLiveEngineSelector.Pick(engines, ["rhe_bg1", "rhe_live"], null));
        Assert.Equal("eei_lichess", RegistryLiveEngineSelector.Pick(engines, [], "eei_lichess")!.Id);
        Assert.Null(RegistryLiveEngineSelector.Pick(engines, [], "rhe_off"));
    }
}
