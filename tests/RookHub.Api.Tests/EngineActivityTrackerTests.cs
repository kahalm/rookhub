using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class EngineActivityTrackerTests
{
    [Fact]
    public void Begin_End_CountsPerUser_AndRemovesEmptyEntries()
    {
        var t = new EngineActivityTracker();
        Assert.Equal(1, t.Begin(7));
        Assert.Equal(2, t.Begin(7));
        Assert.Equal(1, t.Begin(8));
        Assert.True(t.IsLiveActive(7));
        t.End(7);
        Assert.Equal(1, t.ActiveCount(7));
        t.End(7);
        Assert.False(t.IsLiveActive(7));
        Assert.Equal(0, t.ActiveCount(7));
        Assert.True(t.IsLiveActive(8));
    }

    [Fact]
    public void LiveStarted_FiresOnlyOnTransitionFromZero()
    {
        var t = new EngineActivityTracker();
        var fired = new List<int>();
        t.LiveStarted += fired.Add;
        t.Begin(1); t.Begin(1); t.Begin(2);
        Assert.Equal([1, 2], fired);
        t.End(1); t.End(1);
        t.Begin(1);                       // wieder von 0 → feuert erneut
        Assert.Equal([1, 2, 1], fired);
    }

    [Fact]
    public void IdleFor_ZeroWhileActive_MeasuresSinceLastEnd_MaxWhenNever()
    {
        var now = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var t = new EngineActivityTracker(() => now);
        Assert.Equal(TimeSpan.MaxValue, t.IdleFor(3));   // nie live gewesen → sofort frei
        t.Begin(3);
        Assert.Equal(TimeSpan.Zero, t.IdleFor(3));
        t.End(3);
        now = now.AddSeconds(25);
        Assert.Equal(TimeSpan.FromSeconds(25), t.IdleFor(3));
    }
}
