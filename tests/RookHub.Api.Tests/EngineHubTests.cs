using System.Text.Json.Nodes;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Vermittlung des eigenen Brokers (lila-engine <c>hub.rs</c>/<c>ongoing.rs</c>): Long-Poll wartet und
/// liefert, übersprungen wird, wessen Anfragender weg ist, abgeholte Aufträge verfallen ohne Upload, die
/// Schlange je Selector ist gedeckelt.
/// </summary>
public class EngineHubTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private DateTime _now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    private EngineHub Hub(int maxQueued = 64) =>
        new(new LocalBrokerOptions { MaxQueuedPerEngine = maxQueued, OngoingExpiry = TimeSpan.FromSeconds(30) }, () => _now, startSweeper: false);

    private static PendingJob Job(string selector = "sel-a", string engineId = "rhe_aaaaaaaaaaaa") =>
        new(selector, engineId, new JsonObject { ["id"] = engineId },
            new EngineWork("s", 1, 16, 1, Start, [], Depth: 10), Start);

    [Fact]
    public async Task Acquire_ReturnsTheOldestQueuedJob_WithAnId()
    {
        var hub = Hub();
        var first = Job();
        var second = Job();
        Assert.True(hub.Submit(first));
        Assert.True(hub.Submit(second));

        var got = await hub.AcquireAsync("sel-a", TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Same(first, got);
        Assert.Matches("^[A-Za-z0-9]{16}$", got!.Id);
        Assert.Equal(1, hub.OngoingCount);
        Assert.Same(first, hub.TakeOngoing(got.Id!));
        Assert.Null(hub.TakeOngoing(got.Id!));          // genau einmal einlösbar
    }

    [Fact]
    public async Task Acquire_Waits_AndGetsAJobSubmittedLater()
    {
        var hub = Hub();
        var poll = hub.AcquireAsync("sel-a", TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50);
        Assert.False(poll.IsCompleted);
        var job = Job();
        Assert.True(hub.Submit(job));
        Assert.Same(job, await poll.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, hub.QueuedCount("sel-a"));      // direkt übergeben, nicht über die Schlange
    }

    [Fact]
    public async Task Acquire_TimesOut_WithNull()
    {
        var hub = Hub();
        var started = DateTime.UtcNow;
        Assert.Null(await hub.AcquireAsync("sel-a", TimeSpan.FromMilliseconds(100), CancellationToken.None));
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(90));
    }

    [Fact]
    public async Task Acquire_SkipsJobsWhoseRequesterIsGone()
    {
        var hub = Hub();
        var gone = Job();
        var alive = Job();
        hub.Submit(gone);
        hub.Submit(alive);
        gone.CancelRequester();
        Assert.Same(alive, await hub.AcquireAsync("sel-a", TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public async Task Selectors_AreSeparateQueues()
    {
        var hub = Hub();
        var a = Job("sel-a");
        hub.Submit(a);
        Assert.Null(await hub.AcquireAsync("sel-b", TimeSpan.FromMilliseconds(50), CancellationToken.None));
        Assert.Same(a, await hub.AcquireAsync("sel-a", TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public void QueueCap_PerSelector_CountsOnlyValidJobs()
    {
        var hub = Hub(maxQueued: 2);
        var j1 = Job();
        Assert.True(hub.Submit(j1));
        Assert.True(hub.Submit(Job()));
        Assert.False(hub.Submit(Job()));                 // → 503 beim Anfragenden
        Assert.True(hub.Submit(Job("sel-b")));           // anderer Selector, eigener Deckel
        j1.CancelRequester();
        Assert.True(hub.Submit(Job()));                  // ein weggegangener zählt nicht mehr
    }

    [Fact]
    public async Task Ongoing_ExpiresWithoutUpload()
    {
        var hub = Hub();
        var job = Job();
        hub.Submit(job);
        var got = await hub.AcquireAsync("sel-a", TimeSpan.FromSeconds(1), CancellationToken.None);

        _now = _now.AddSeconds(29);
        hub.Sweep();
        Assert.Equal(1, hub.OngoingCount);

        _now = _now.AddSeconds(2);
        hub.Sweep();
        Assert.Equal(0, hub.OngoingCount);
        Assert.Null(hub.TakeOngoing(got!.Id!));
        Assert.False(job.IsValid);                       // der Anfragende bekommt kein ewiges Warten
        Assert.Equal(1, hub.Stats.For(job.EngineId)!.OngoingExpired);
    }

    [Fact]
    public async Task ProviderHangsUp_WhileWaiting_NothingIsLost()
    {
        var hub = Hub();
        using var cts = new CancellationTokenSource();
        var poll = hub.AcquireAsync("sel-a", TimeSpan.FromSeconds(10), cts.Token);
        await Task.Delay(20);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll);

        var job = Job();
        Assert.True(hub.Submit(job));                    // landet in der Schlange, nicht bei einem toten Wartenden
        Assert.Equal(1, hub.QueuedCount("sel-a"));
        Assert.Same(job, await hub.AcquireAsync("sel-a", TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public async Task ManyPollsAndJobs_EveryJobIsDeliveredExactlyOnce()
    {
        var hub = Hub(maxQueued: 1024);
        const int n = 200;
        var jobs = Enumerable.Range(0, n).Select(_ => Job()).ToList();
        var delivered = new System.Collections.Concurrent.ConcurrentBag<PendingJob>();
        var pollers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            while (delivered.Count < n)
            {
                var j = await hub.AcquireAsync("sel-a", TimeSpan.FromMilliseconds(30), CancellationToken.None);
                if (j is not null) delivered.Add(j);
            }
        })).ToList();
        foreach (var j in jobs) { Assert.True(hub.Submit(j)); if (Random.Shared.Next(4) == 0) await Task.Yield(); }
        await Task.WhenAll(pollers).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(n, delivered.Count);
        Assert.Equal(n, delivered.Distinct().Count());
    }
}
