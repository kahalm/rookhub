using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>
/// Engine-Balken auf den HINTERGRUND-Engines (1.027.0, Wunsch 2026-11-26: „nur die Bretter rechnen, die jemand ansieht“,
/// „in die Hintergrundengines werfen“, „nur bis Tiefe 30“): parallel auf allen freien Engines, nie auf einer mit laufendem
/// Auftrag, neuer Zug bricht die alte Rechnung ab, Zwischenstände laufend, Cloud ≥ 30 genügt, nur sichtbare Bretter.
/// </summary>
public class LiveEngineEvalTests
{
    private const string Round = "R9900002";
    private const string WhiteFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string BlackFen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

    private static EngineRef E(string id, bool online = true) => new(id, id, 4, 256, EngineSource.Local, Online: online);
    private static readonly LiveEnginePool Pool = new(5, [E("rhe_bg0001"), E("rhe_bg0002"), E("rhe_bg0003")]);

    /// <summary>Rechnet „auf Zuruf": jede Bewertung meldet Tiefe 10, 20 und wartet dann aufs Tor; zählt Engines und Gleichzeitigkeit.</summary>
    private sealed class GatedEvaluator : ILiveEngineEvaluator
    {
        public readonly List<(string Engine, string Fen)> Calls = [];
        public readonly List<string> Cancelled = [];
        public int Running, MaxRunning;
        public bool Gate = true;
        public int FinalDepth = 30;
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _open.TrySetResult();

        public async Task<LiveEval?> EvaluateAsync(EngineRef engine, int owner, string fen, Action<LiveEval> progress, CancellationToken ct)
        {
            lock (Calls) Calls.Add((engine.Id, fen));
            var now = Interlocked.Increment(ref Running);
            lock (Calls) MaxRunning = Math.Max(MaxRunning, now);
            try
            {
                progress(new LiveEval(10, null, "engine", 10));
                progress(new LiveEval(15, null, "engine", 20));
                if (Gate) await _open.Task.WaitAsync(ct);
                return new LiveEval(42, null, "engine", FinalDepth);
            }
            catch (OperationCanceledException)
            {
                lock (Calls) Cancelled.Add(fen);
                throw;
            }
            finally { Interlocked.Decrement(ref Running); }
        }
    }

    private static async Task Until(Func<bool> cond)
    {
        for (var i = 0; i < 250 && !cond(); i++) await Task.Delay(20);
        Assert.True(cond());
    }

    private static LiveEngineQueue Queue(ILiveEngineEvaluator ev, LiveEngineLane? lane = null, LiveEnginePool? pool = null,
        EngineActivityTracker? activity = null, int maxParallel = 0) =>
        new(ev, new LiveEngineOptions { MaxParallel = maxParallel, MinShowDepth = 1 }, new MemoryCache(new MemoryCacheOptions()), NullLogger<LiveEngineQueue>.Instance,
            lane, activity, _ => Task.FromResult(pool ?? Pool)) { Poll = TimeSpan.FromMilliseconds(30), ProgressSpacing = TimeSpan.Zero };

    // ── Warteschlange ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Queue_ParallelOnAllFreeEngines_SkipsEngineWithJob_ClaimsWhileWaiting()
    {
        var ev = new GatedEvaluator();
        var lane = new LiveEngineLane();
        lane.AttachWorker(e => e == "rhe_bg0002");                         // dort rechnet ein Hintergrund-Auftrag
        await using var q = Queue(ev, lane);
        foreach (var k in new[] { "A", "B", "C" }) q.Enqueue(k, "fen-" + k, _ => true, (_, _) => { });

        await Until(() => ev.Running == 2);
        lock (ev.Calls) Assert.Equal(["rhe_bg0001", "rhe_bg0003"], ev.Calls.Select(c => c.Engine).Order());
        Assert.Equal(1, q.Pending);
        Assert.True(lane.IsClaimed("rhe_bg0002"));                         // C wartet → der Worker startet dort nichts Neues
        lane.AttachWorker(_ => false);                                     // Auftrag zu Ende
        lane.NotifyJobEnded();
        await Until(() => ev.Running == 3);
        Assert.False(lane.IsClaimed("rhe_bg0002"));                        // nichts wartet mehr
        ev.Open();
        await Until(() => q.RunningCount == 0);
        Assert.Equal(3, ev.MaxRunning);
    }

    [Fact]
    public async Task Queue_NewMove_CancelsRunningOldPosition_NewestOnly()
    {
        var ev = new GatedEvaluator();
        await using var q = Queue(ev, pool: new LiveEnginePool(5, [E("rhe_bg0001")]));
        var applied = new List<(string Fen, int? Depth)>();
        void Add(string fen) => q.Enqueue("A", fen, _ => true, (f, e) => { lock (applied) applied.Add((f, e.Depth)); });

        Add("a1");
        await Until(() => ev.Running == 1);
        Add("a2");                                                         // neuer Zug: a1 bricht ab
        Add("a3");                                                         // ersetzt a2 noch in der Schlange
        await Until(() => { lock (ev.Calls) return ev.Calls.Count == 2; });
        ev.Open();
        await Until(() => q.RunningCount == 0 && q.Pending == 0);
        lock (ev.Calls) Assert.Equal(["a1", "a3"], ev.Calls.Select(c => c.Fen));
        Assert.Contains("a1", ev.Cancelled);
        lock (applied)
        {
            Assert.Contains(("a3", 30), applied);                          // Ergebnis
            Assert.Contains(("a3", 10), applied);                          // Zwischenstände
            Assert.DoesNotContain(applied, a => a.Fen == "a1" && a.Depth == 30);
        }
        Assert.True(q.IsSettled("a3"));
        Assert.False(q.IsSettled("a1"));
    }

    [Fact]
    public async Task Queue_NotWanted_NotComputed_AndUnwantedDuringRunCancels()
    {
        var ev = new GatedEvaluator();
        await using var q = Queue(ev);
        q.Enqueue("A", "x", _ => false, (_, _) => Assert.Fail("übernommen"));
        await Until(() => q.Pending == 0);
        await Task.Delay(80);
        Assert.Empty(ev.Calls);

        var wanted = true;
        q.Enqueue("B", "y", _ => wanted, (_, _) => wanted = false);       // nach dem ersten Zwischenstand sieht niemand mehr hin
        await Until(() => ev.Cancelled.Contains("y"));
        Assert.False(q.IsSettled("y"));
    }

    [Fact]
    public async Task Queue_SettledPosition_FromCache_NoEngine()
    {
        var ev = new GatedEvaluator { Gate = false };
        await using var q = Queue(ev);
        q.Enqueue("A", "z", _ => true, (_, _) => { });
        await Until(() => q.IsSettled("z"));
        LiveEval? got = null;
        q.Enqueue("B", "z", _ => true, (_, e) => got = e);
        Assert.Equal(30, got!.Depth);
        Assert.Single(ev.Calls);
    }

    [Fact]
    public async Task Queue_ManualLiveAnalysisOnEngine_LeavesItAlone_MaxParallel()
    {
        var ev = new GatedEvaluator();
        var tracker = new EngineActivityTracker();
        tracker.Begin(5, "rhe_bg0001");                                    // jemand analysiert dort selbst
        await using var q = Queue(ev, activity: tracker, maxParallel: 1);
        q.Enqueue("A", "a", _ => true, (_, _) => { });
        q.Enqueue("B", "b", _ => true, (_, _) => { });
        await Until(() => ev.Running == 1);
        await Task.Delay(100);
        lock (ev.Calls) Assert.Equal(["rhe_bg0002"], ev.Calls.Select(c => c.Engine));
        ev.Open();
    }

    // ── Hub: Rückfallkette, Zieltiefe, sichtbare Bretter ────────────────────────────────────────────────────────────

    private static BroadcastLiveHub Hub(FakeLichessLiveSource src, LiveEngineQueue q) =>
        new(src, new LichessCloudEval(src, new MemoryCache(new MemoryCacheOptions()), NullLogger<LichessCloudEval>.Instance) { Spacing = TimeSpan.Zero },
            NullLogger<BroadcastLiveHub>.Instance, new BroadcastLiveHub.HubOptions { IdleClose = TimeSpan.FromMilliseconds(50) }, q);

    private static async Task<Dictionary<string, LiveEval>> Evals(BroadcastLiveHub.RoundSubscription sub, Func<Dictionary<string, LiveEval>, bool> done)
    {
        var got = new Dictionary<string, LiveEval>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!done(got))
            if (await sub.Events.ReadAsync(cts.Token) is LiveGameChanged { Game.Eval: { } e } c) got[c.Game.Id] = e;
        return got;
    }

    [Fact]
    public async Task Hub_BroadcastEval_Kept_DeepCloudSuffices_ShallowCloudThenEngineToDepth30()
    {
        var src = new FakeLichessLiveSource();
        var withEval = LivePgn.Game("g1", "A", "B", "1. e4 { [%eval 0.3] }");
        var deepCloud = LivePgn.Game("g2", "C", "D", "1. d4");
        var shallowCloud = LivePgn.Game("g3", "E", "F", "1. c4");
        src.RoundPgn[Round] = withEval + deepCloud + shallowCloud;
        src.CloudEvals[LiveGameParser.Parse(deepCloud)!.Fen] = """{"depth":40,"pvs":[{"cp":20}]}""";
        src.CloudEvals[LiveGameParser.Parse(shallowCloud)!.Fen] = """{"depth":22,"pvs":[{"cp":5}]}""";
        var ev = new GatedEvaluator { Gate = false };
        await using var q = Queue(ev);
        await using var hub = Hub(src, q);

        var sub = hub.Subscribe(Round, false, wantsEval: true);
        var got = await Evals(sub, g => g.Count >= 2 && g.GetValueOrDefault("g3")?.Depth == 30);
        Assert.Equal(("cloud", 40), (got["g2"].Source, got["g2"].Depth));
        Assert.Equal(("engine", 30), (got["g3"].Source, got["g3"].Depth));   // Cloud 22 < 30 → Engine bis 30
        lock (ev.Calls)
        {
            Assert.DoesNotContain(ev.Calls, c => c.Fen == LiveGameParser.Parse(withEval)!.Fen);   // [%eval] genügt
            Assert.DoesNotContain(ev.Calls, c => c.Fen == LiveGameParser.Parse(deepCloud)!.Fen);  // Cloud 40 genügt
        }
        await sub.DisposeAsync();
    }

    [Fact]
    public async Task Hub_OnlyVisibleBoardsAreEvaluated()
    {
        var src = new FakeLichessLiveSource();
        src.RoundPgn[Round] = LivePgn.Game("g1", "A", "B", "1. c4") + LivePgn.Game("g2", "C", "D", "1. Nf3");
        var ev = new GatedEvaluator { Gate = false };
        await using var q = Queue(ev);
        await using var hub = Hub(src, q);
        var sub = hub.Subscribe(Round, false, wantsEval: true, visible: new HashSet<string>());   // Filter, Zuordnung noch offen
        await Task.Delay(150);
        Assert.Empty(ev.Calls);
        Assert.Equal(0, src.CloudEvalCalls);
        sub.SetVisible(new HashSet<string> { "g2" });
        await Evals(sub, g => g.ContainsKey("g2"));
        await Task.Delay(100);
        lock (ev.Calls) Assert.Equal([LiveGameParser.Parse(LivePgn.Game("g2", "C", "D", "1. Nf3"))!.Fen], ev.Calls.Select(c => c.Fen));
        await sub.DisposeAsync();
    }

    [Fact]
    public async Task Hub_NoViewerWithBar_NoEngine()
    {
        var src = new FakeLichessLiveSource();
        src.RoundPgn[Round] = LivePgn.Game("g1", "A", "B", "1. c4");
        var ev = new GatedEvaluator { Gate = false };
        await using var q = Queue(ev);
        await using var hub = Hub(src, q);
        var sub = hub.Subscribe(Round, false, wantsEval: false);
        await src.StreamOf(Round).Writer.WriteAsync(LivePgn.Game("g1", "A", "B", "1. c4 e5"));
        await Task.Delay(200);
        Assert.Empty(ev.Calls);
        Assert.Equal(0, src.CloudEvalCalls);
        await sub.DisposeAsync();
    }

    [Fact]
    public void LiveView_TeamPlayerAndAddedBoards()
    {
        var team = new LiveRoundLayout(true,
        [
            new LiveMatchSlot(1, "Pinggau", "Schwaz", null, null, 0, 0, [new LiveBoardSlot(1, "a", "b", null, "Game0001", true), new LiveBoardSlot(2, "c", "d", null, "Game0002", false)]),
            new LiveMatchSlot(2, "Absam", "Wörgl", null, null, 0, 0, [new LiveBoardSlot(1, "e", "f", null, "Game0003", true), new LiveBoardSlot(2, "g", "h", null, "Game0004", true)]),
        ], [], ["Unassig1"]);
        Assert.Null(LiveView.Visible(team, null, null, ["2.1"]));                      // ohne Filter: alles
        Assert.Equal(["Game0001", "Game0002", "Game0004", "Unassig1"],
            LiveView.Visible(team, "Schwaz", null, LiveView.ParseAdd("2.2,Unassig1,9.9,bad"))!.Order());
        var single = new LiveRoundLayout(false, [], [new LiveBoardSlot(1, "Mittal, Aditya", "X", null, "Game0001"), new LiveBoardSlot(2, "Y", "Z", null, "Game0002")], []);
        Assert.Equal(["Game0001"], LiveView.Visible(single, null, "Aditya Mittal", []));
        Assert.Equal(["Game0001", "Game0002"], LiveView.Visible(single, null, "Aditya Mittal", ["2"])!.Order());
    }

    // ── Bewerter über den Broker ────────────────────────────────────────────────────────────────────────────────────

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

    [Fact]
    public async Task Evaluator_Depth30_ProgressPerDeeperLine_WhitePov_NoGraceForWorker()
    {
        const string lines = "{\"keepalive\":true}\n{\"time\":10,\"depth\":8,\"nodes\":1,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":10,\"depth\":8}]}\n"
                             + "{\"time\":11,\"depth\":8,\"nodes\":2,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":12,\"depth\":8}]}\n"
                             + "{\"time\":1500,\"depth\":30,\"nodes\":9,\"pvs\":[{\"moves\":[\"c7c5\"],\"cp\":-35,\"depth\":30}]}\n";
        var broker = new FakeBroker(lines);
        var sp = new ServiceCollection().AddTransient<IEngineBroker>(_ => broker).BuildServiceProvider();
        var tracker = new EngineActivityTracker();
        var eval = new BrokerLiveEngineEvaluator(sp.GetRequiredService<IServiceScopeFactory>(), new LiveEngineOptions(),
            NullLogger<BrokerLiveEngineEvaluator>.Instance, tracker);
        var progress = new List<LiveEval>();
        var ev = await eval.EvaluateAsync(E("rhe_bg0001"), 5, BlackFen, progress.Add, default);
        Assert.Equal(new LiveEval(-35, null, "engine", 30, "c7c5"), ev);     // Broker liefert schon Weiß-Sicht: nicht drehen (1.027.2); bester Zug mit (1.030.0)
        Assert.Equal([8, 30], progress.Select(p => p.Depth));                // gleiche Tiefe zählt nicht als Fortschritt
        Assert.Equal((30, 1, 4, 256), (broker.Last!.Depth, broker.Last.MultiPv, broker.Last.Threads, broker.Last.Hash));
        Assert.Null(broker.Last.Movetime);
        Assert.False(tracker.IsEngineBusy("rhe_bg0001"));
        Assert.Equal(TimeSpan.MaxValue, tracker.EngineIdleFor("rhe_bg0001"));   // keine Ruhephase: der Worker darf sofort wieder
    }

    [Fact]
    public void Pick_BackgroundListInOrder_OnlineOnly_NoExplicitOnly_ConfiguredWins()
    {
        var engines = new[] { E("rhe_bg2"), E("rhe_off", online: false), E("rhe_bg1"), E("rhe_lc0"), E("rhe_live"),
            new EngineRef("eei_lichess", "L", 1, 16, EngineSource.Lichess) };
        var background = new[] { "rhe_bg1", "rhe_bg2", "rhe_off", "rhe_lc0" };
        Assert.Equal(["rhe_bg1", "rhe_bg2"], RegistryLiveEnginePoolSource.Pick(engines, background, [], new HashSet<string> { "rhe_lc0" }).Select(e => e.Id));
        Assert.Equal(["rhe_live"], RegistryLiveEnginePoolSource.Pick(engines, background, ["rhe_live", "rhe_off", "eei_lichess"], new HashSet<string>()).Select(e => e.Id));
    }

    [Fact]
    public async Task Owner_HouseEngineOwner_NotSmallestAdminId()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AppUsers.AddRange(new AppUser { Id = 990002, Username = "Taulajoe", PasswordHash = "x", IsAdmin = true },
            new AppUser { Id = 990005, Username = "kahalm", PasswordHash = "x", IsAdmin = true });
        var cred = new LichessEngineCredential { UserId = 990005, EncryptedToken = "" };
        cred.SetBackgroundEngines(["rhe_bg1"]);
        db.LichessEngineCredentials.Add(cred);
        await db.SaveChangesAsync();
        Assert.Equal(990005, await RegistryLiveEnginePoolSource.OwnerAsync(db, new LiveEngineOptions(), default));   // ohne Freigabe: Admin MIT Engines
        cred.ShareAsHouseEngine = true;
        await db.SaveChangesAsync();
        Assert.Equal(990005, await RegistryLiveEnginePoolSource.OwnerAsync(db, new LiveEngineOptions(), default));   // Haus-Engine
        Assert.Equal(990002, await RegistryLiveEnginePoolSource.OwnerAsync(db, new LiveEngineOptions { OwnerUserName = "Taulajoe" }, default));
    }

    [Fact]
    public void Options_DefaultDepth30_ListOfEngines()
    {
        var c = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LiveBoards:Engine:EngineIds:0"] = "rhe_a", ["LiveBoards:Engine:EngineId"] = "rhe_b",
        }).Build();
        var o = LiveEngineOptions.From(c);
        Assert.Equal(30, o.Depth);
        Assert.Equal(["rhe_a", "rhe_b"], o.EngineIds);
        Assert.Equal(0, o.MaxParallel);
        Assert.Equal(30, LiveEngineOptions.From(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()).Depth);
        _ = WhiteFen;
    }
}
