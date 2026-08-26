using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AnalysisJobWorkerTests
{
    [Fact]
    public void TryCancel_CancelsNormally_AndSurvivesADisposedSource()
    {
        // Zwischen dem Griff ins Dictionary und dem Cancel kann der Lauf selbst geendet und die CTS disposed
        // haben. Cancel() würde dann werfen — im LiveStarted-Handler bis in den Request-Thread des Live-Streams.
        var live = new CancellationTokenSource();
        AnalysisJobWorker.TryCancel(live);
        Assert.True(live.IsCancellationRequested);

        var gone = new CancellationTokenSource();
        gone.Dispose();
        AnalysisJobWorker.TryCancel(gone);   // darf NICHT werfen

        var linkedParent = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(linkedParent.Token);
        linked.Dispose();
        AnalysisJobWorker.TryCancel(linked); // auch die verkettete Variante (so legt der Worker sie an)
    }
}

/// <summary>Die Regeln, nach denen ein beendeter Stream bewertet wird (Spiegel der Worker-Entscheidung —
/// der Worker selbst braucht Broker + DB und ist hier nicht instanziierbar).</summary>
public class AnalysisJobOutcomeTests
{
    private static string Outcome(int reached, int target, int atStart, bool cancelledByUs, bool liveActive)
    {
        if (reached >= target) return "done";
        if (cancelledByUs) return "paused";                 // Live-Vorrang/gelöscht/Shutdown — kein Fehlversuch
        if (reached > atStart) return "paused-progress";    // Broker weg, aber es ging voran
        if (liveActive) return "paused-preempted";          // geteilte Engine: Live hat verdrängt
        return "fruitless";                                 // wirklich nichts erreicht → zählt
    }

    [Fact]
    public void ReachingTheTarget_IsDone()
        => Assert.Equal("done", Outcome(reached: 40, target: 40, atStart: 21, cancelledByUs: false, liveActive: false));

    [Fact]
    public void OurOwnCancel_NeverCountsAsFailure()
        => Assert.Equal("paused", Outcome(reached: 25, target: 40, atStart: 21, cancelledByUs: true, liveActive: true));

    [Fact]
    public void ProgressWithoutReachingTheTarget_ResetsTheCounter()
        => Assert.Equal("paused-progress", Outcome(reached: 30, target: 40, atStart: 21, cancelledByUs: false, liveActive: false));

    [Fact]
    public void PreemptedByLiveOnAsharedEngine_IsNotAFailedAttempt()
        // Regression: eine einzige registrierte Engine für Live UND Hintergrund — jede Live-Anfrage verdrängt
        // den Auftrag, der Stream endet von selbst. Ohne diese Regel galt er nach 3 Runden als gescheitert.
        => Assert.Equal("paused-preempted", Outcome(reached: 38, target: 40, atStart: 38, cancelledByUs: false, liveActive: true));

    [Fact]
    public void NoProgressAndNoLive_CountsTowardsFailure()
        => Assert.Equal("fruitless", Outcome(reached: 38, target: 40, atStart: 38, cancelledByUs: false, liveActive: false));
}
