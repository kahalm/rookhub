using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>Hub-Lebenszyklus: EIN Strom je Runde, Vollstand zuerst, Deltas nur bei Änderung, Schließen nach dem letzten
/// Zuschauer, fertige Runden ohne Strom, Cloud-Eval nur bei eingeschaltetem Balken.</summary>
public class BroadcastLiveHubTests
{
    private const string Round = "R9900001";
    private static readonly string Game1 = LivePgn.Game("g1", "A", "B", "1. e4 { [%clk 1:30:00] }");

    private static BroadcastLiveHub Hub(FakeLichessLiveSource src, LichessCloudEval? cloud = null) =>
        new(src, cloud, NullLogger<BroadcastLiveHub>.Instance, new BroadcastLiveHub.HubOptions
        {
            IdleClose = TimeSpan.FromMilliseconds(80), ReconnectMin = TimeSpan.FromMilliseconds(20), ReconnectMax = TimeSpan.FromMilliseconds(40),
        });

    private static async Task<T> Next<T>(BroadcastLiveHub.RoundSubscription sub) where T : LiveEvent
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var e = await sub.Events.ReadAsync(cts.Token);
            if (e is T t) return t;
        }
    }

    private static async Task Until(Func<bool> cond)
    {
        for (var i = 0; i < 250 && !cond(); i++) await Task.Delay(20);
        Assert.True(cond());
    }

    [Fact]
    public async Task TwoViewers_OneStream_SnapshotThenDeltas_ClosesAfterLastViewer()
    {
        var src = new FakeLichessLiveSource();
        src.RoundPgn[Round] = Game1;
        await using var hub = Hub(src);

        var a = hub.Subscribe(Round, finished: false, wantsEval: false);
        var snapA = await Next<LiveSnapshot>(a);
        Assert.Equal("g1", Assert.Single(snapA.Games).Id);
        var b = hub.Subscribe(Round, finished: false, wantsEval: false);
        Assert.Single((await Next<LiveSnapshot>(b)).Games);
        await Until(() => src.StreamsOpened == 1);

        // Wiederholung nach dem Verbinden: unverändert → kein Ereignis; dann ein neuer Zug → beide bekommen ihn.
        var ch = src.StreamOf(Round);
        await ch.Writer.WriteAsync(Game1);
        await ch.Writer.WriteAsync(LivePgn.Game("g1", "A", "B", "1. e4 { [%clk 1:30:00] } 1... c5 { [%clk 1:29:50] }"));
        var da = await Next<LiveGameChanged>(a);
        var db = await Next<LiveGameChanged>(b);
        Assert.Equal(2, da.Game.Plies);
        Assert.Equal(da.Game.Hash, db.Game.Hash);
        Assert.Equal(1, src.StreamsOpened);
        Assert.Equal(1, src.RoundPgnCalls);

        await a.DisposeAsync();
        await Task.Delay(150);
        Assert.Equal(1, hub.OpenFeeds);                      // b schaut noch
        await b.DisposeAsync();
        await Until(() => hub.OpenFeeds == 0 && src.StreamsClosed == 1);
    }

    [Fact]
    public async Task ViewerReturnsWithinIdle_StreamStaysOpen()
    {
        var src = new FakeLichessLiveSource();
        src.RoundPgn[Round] = Game1;
        await using var hub = new BroadcastLiveHub(src, null, NullLogger<BroadcastLiveHub>.Instance,
            new BroadcastLiveHub.HubOptions { IdleClose = TimeSpan.FromSeconds(5) });
        var a = hub.Subscribe(Round, false, false);
        await Next<LiveSnapshot>(a);
        await Until(() => src.StreamsOpened == 1);
        await a.DisposeAsync();
        var b = hub.Subscribe(Round, false, false);           // Seite neu geladen
        await Next<LiveSnapshot>(b);
        await Task.Delay(100);
        Assert.Equal((1, 0), (src.StreamsOpened, src.StreamsClosed));
        await b.DisposeAsync();
    }

    /// <summary>
    /// Das Fenster zwischen „Leerlauf-Timer abgelaufen“ und „Feed entfernt“: früher landete ein Zuschauer darin am sterbenden
    /// Feed (neuer Strom, tote Bewertungs-Schlange), und der nächste legte einen ZWEITEN Feed mit zweitem Lichess-Strom an.
    /// Der Haken hält den Hub genau dort an; der Zuschauer, der jetzt kommt, rettet den Feed.
    /// </summary>
    [Fact]
    public async Task ViewerArrivesWhileIdleCloseFires_KeepsSameFeed_OneStream_EvalAlive()
    {
        var src = new FakeLichessLiveSource();
        src.RoundPgn[Round] = Game1;
        var fen = LiveGameParser.Parse(Game1)!.Fen;
        src.CloudEvals[fen] = """{"depth":30,"pvs":[{"cp":25}]}""";
        var cloud = new LichessCloudEval(src, new MemoryCache(new MemoryCacheOptions()), NullLogger<LichessCloudEval>.Instance) { Spacing = TimeSpan.Zero };
        await using var hub = Hub(src, cloud);
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.BeforeIdleClose = async () => { fired.TrySetResult(); await release.Task; };

        var a = hub.Subscribe(Round, false, false);
        await Next<LiveSnapshot>(a);
        await Until(() => src.StreamsOpened == 1);
        await a.DisposeAsync();
        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5));          // Timer abgelaufen, Schließen steht bevor

        var b = hub.Subscribe(Round, false, wantsEval: false);        // genau ins Fenster
        await Next<LiveSnapshot>(b);
        hub.BeforeIdleClose = null;
        release.SetResult();
        await Task.Delay(150);                                         // Schließversuch ist durch

        Assert.Equal(1, hub.OpenFeeds);
        Assert.Equal(1, hub.Viewers(Round));
        var c = hub.Subscribe(Round, false, false);                    // nächster Zuschauer: derselbe Feed
        await Next<LiveSnapshot>(c);
        Assert.Equal(2, hub.Viewers(Round));
        Assert.Equal(1, src.StreamsOpened);                            // kein zweiter Lichess-Strom
        Assert.Equal(0, src.StreamsClosed);

        await src.StreamOf(Round).Writer.WriteAsync(LivePgn.Game("g1", "A", "B", "1. e4 { [%clk 1:30:00] } 1... c5"));
        Assert.Equal(2, (await Next<LiveGameChanged>(b)).Game.Plies); // der Strom lebt
        src.CloudEvals[LiveGameParser.Parse(LivePgn.Game("g1", "A", "B", "1. e4 { [%clk 1:30:00] } 1... c5"))!.Fen] = """{"depth":30,"pvs":[{"cp":25}]}""";
        b.SetEval(true);
        Assert.NotNull((await Next<LiveGameChanged>(b)).Game.Eval);    // die Bewertungs-Schlange lebt
        await b.DisposeAsync();
        await c.DisposeAsync();
    }

    [Fact]
    public async Task IdleCloseCompleted_NextViewerGetsFreshFeed()
    {
        var src = new FakeLichessLiveSource();
        src.RoundPgn[Round] = Game1;
        await using var hub = Hub(src);
        var a = hub.Subscribe(Round, false, false);
        await Next<LiveSnapshot>(a);
        await Until(() => src.StreamsOpened == 1);
        await a.DisposeAsync();
        await Until(() => hub.OpenFeeds == 0 && src.StreamsClosed == 1);
        var b = hub.Subscribe(Round, false, false);
        await Next<LiveSnapshot>(b);
        await Until(() => src.StreamsOpened == 2);
        Assert.Equal(1, hub.OpenFeeds);
        await b.DisposeAsync();
    }

    [Fact]
    public async Task FinishedRound_NoStream_KeptForNextViewer()
    {
        var src = new FakeLichessLiveSource();
        src.RoundPgn[Round] = LivePgn.Game("g1", "A", "B", "1. e4 e5", "1-0");
        await using var hub = Hub(src);
        var a = hub.Subscribe(Round, finished: true, wantsEval: false);
        Assert.Equal("finished", (await Next<LiveSnapshot>(a)).State);
        await a.DisposeAsync();
        await Until(() => hub.OpenFeeds == 0);
        var b = hub.Subscribe(Round, finished: true, wantsEval: false);
        var snap = await Next<LiveSnapshot>(b);
        Assert.Equal("1-0", Assert.Single(snap.Games).Result);
        Assert.Equal(0, src.StreamsOpened);
        Assert.Equal(1, src.RoundPgnCalls);                  // zweiter Zuschauer: aus dem Speicher
        await b.DisposeAsync();
    }

    [Fact]
    public async Task StreamEnds_Reconnects()
    {
        var src = new FakeLichessLiveSource();
        src.RoundPgn[Round] = Game1;
        await using var hub = Hub(src);
        var a = hub.Subscribe(Round, false, false);
        await Next<LiveSnapshot>(a);
        await Until(() => src.StreamsOpened == 1);
        src.StreamOf(Round).Writer.Complete();               // Lichess schließt
        await Until(() => src.StreamsOpened >= 2);
        await a.DisposeAsync();
    }

    [Fact]
    public async Task CloudEval_OnlyWhenAViewerWantsIt()
    {
        var src = new FakeLichessLiveSource();
        src.RoundPgn[Round] = Game1;
        var fen = LiveGameParser.Parse(Game1)!.Fen;
        src.CloudEvals[fen] = """{"fen":"x","depth":30,"pvs":[{"moves":"c7c5","cp":25}]}""";
        var cloud = new LichessCloudEval(src, new MemoryCache(new MemoryCacheOptions()), NullLogger<LichessCloudEval>.Instance) { Spacing = TimeSpan.Zero };
        await using var hub = Hub(src, cloud);

        var plain = hub.Subscribe(Round, false, wantsEval: false);
        await Next<LiveSnapshot>(plain);
        await Task.Delay(100);
        Assert.Equal(0, src.CloudEvalCalls);

        plain.SetEval(true);
        var withEval = await Next<LiveGameChanged>(plain);
        Assert.Equal(new LiveEval(25, null, "cloud", 30, "c7c5"), withEval.Game.Eval);   // bester Zug mit (1.030.0)
        Assert.Equal(1, src.CloudEvalCalls);
        await plain.DisposeAsync();
    }
}
