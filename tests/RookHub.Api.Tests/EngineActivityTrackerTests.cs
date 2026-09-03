using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class EngineActivityTrackerTests
{
    private const string A = "eei_a";
    private const string B = "eei_b";

    [Fact]
    public void CountsPerUserForTheCap_AndPerEngineForPriority()
    {
        var t = new EngineActivityTracker();
        Assert.Equal(1, t.Begin(7, A));
        Assert.Equal(2, t.Begin(7, B));      // zwei Tabs desselben Users auf verschiedenen Engines
        Assert.Equal(2, t.ActiveCount(7));
        Assert.True(t.IsEngineBusy(A));
        Assert.True(t.IsEngineBusy(B));

        t.End(7, A);
        Assert.False(t.IsEngineBusy(A));     // Engine A ist wieder frei …
        Assert.True(t.IsEngineBusy(B));      // … B rechnet weiter
        Assert.Equal(1, t.ActiveCount(7));
        t.End(7, B);
        Assert.Equal(0, t.ActiveCount(7));
    }

    [Fact]
    public void SameEngineFromTwoUsers_StaysBusyUntilBothEnd()
    {
        var t = new EngineActivityTracker();
        t.Begin(1, A);
        t.Begin(2, A);
        t.End(1, A);
        Assert.True(t.IsEngineBusy(A));      // der zweite rechnet noch — die Engine gehört den Aufträgen nicht
        t.End(2, A);
        Assert.False(t.IsEngineBusy(A));
    }

    [Fact]
    public void LiveStarted_FiresPerEngine_OnlyOnTransitionFromZero()
    {
        var t = new EngineActivityTracker();
        var fired = new List<string>();
        t.LiveStarted += fired.Add;
        t.Begin(1, A); t.Begin(2, A); t.Begin(1, B);
        Assert.Equal([A, B], fired);         // A nur einmal, obwohl zwei Streams
        t.End(1, A); t.End(2, A);
        t.Begin(1, A);                       // wieder von 0 → feuert erneut
        Assert.Equal([A, B, A], fired);
    }

    [Fact]
    public void Begin_KeepsCountingWhenAListenerThrows()
    {
        // Regression: warf der LiveStarted-Abnehmer (Worker.PauseEngine auf einer bereits disposed CTS),
        // lief der Aufrufer nie in sein End() — die Engine galt für immer als belegt.
        var t = new EngineActivityTracker();
        Exception? reported = null;
        t.OnNotifyFailed += ex => reported = ex;
        t.LiveStarted += _ => throw new ObjectDisposedException("cts");

        Assert.Equal(1, t.Begin(4, A));
        Assert.IsType<ObjectDisposedException>(reported);
        t.End(4, A);
        Assert.False(t.IsEngineBusy(A));
        Assert.Equal(0, t.ActiveCount(4));
    }

    [Fact]
    public void EngineIdleFor_ZeroWhileBusy_MeasuresSinceLastEnd_MaxWhenNever()
    {
        var now = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var t = new EngineActivityTracker(() => now);
        Assert.Equal(TimeSpan.MaxValue, t.EngineIdleFor(A));   // dort lief nie etwas → sofort frei
        t.Begin(3, A);
        Assert.Equal(TimeSpan.Zero, t.EngineIdleFor(A));
        t.End(3, A);
        now = now.AddSeconds(25);
        Assert.Equal(TimeSpan.FromSeconds(25), t.EngineIdleFor(A));
        Assert.Equal(TimeSpan.MaxValue, t.EngineIdleFor(B));   // andere Engine unberührt
    }

    [Fact]
    public async Task ParallelStreamsOnOneEngine_AreNeverInvisibleWhileRunning()
    {
        // Regression: `End` entfernte den Zähler-Eintrag UNBEDINGT, sobald es selbst auf 0 herunterzählte.
        // Läuft dazwischen ein `Begin` derselben Engine (das Analysebrett bricht bei jedem Tiefen-/
        // Linienwechsel Stream A ab und öffnet sofort B), löschte das den Eintrag des NEUEN Streams —
        // die Engine sah frei aus, der Worker startete einen Auftrag daneben und Stockfish bekam zwei
        // Suchen. Jeder Thread prüft direkt nach seinem eigenen Begin, ob die Engine als belegt gilt.
        var t = new EngineActivityTracker();
        var hidden = 0;
        var churn = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                t.Begin(worker, A);
                if (!t.IsEngineBusy(A)) Interlocked.Increment(ref hidden);
                t.End(worker, A);
            }
        }));
        await Task.WhenAll(churn);

        Assert.Equal(0, hidden);
        Assert.False(t.IsEngineBusy(A));                 // am Ende ist alles abgerechnet
        for (var w = 0; w < 8; w++) Assert.Equal(0, t.ActiveCount(w));
    }

}
