using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Der laufende Stand ist die Grundlage der sekündlichen Anzeige — er muss auch dann weiterlaufen,
/// wenn die Engine gerade schweigt, und darf keine fremden Aufträge zeigen.</summary>
public class AnalysisJobLiveTests
{
    private static readonly DateTime Start = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ForUser_RunningJob_CountsSecondsFromStartEvenWithoutLines()
    {
        var live = new AnalysisJobLive();
        live.Start(jobId: 7, userId: 1, secondsBase: 100, startedUtc: Start);

        // Keine einzige Zeile empfangen (Engine rechnet sich nach einer Fortsetzung erst wieder hoch)
        var rows = live.ForUser(1, Start.AddSeconds(42));

        var row = Assert.Single(rows);
        Assert.Equal(7, row.Id);
        Assert.Equal(142, row.Seconds);
        Assert.Equal(0, row.Depth);
        Assert.Equal(0, row.Nps);
    }

    [Fact]
    public void Update_KeepsLastKnownValues_WhenLineCarriesNone()
    {
        var live = new AnalysisJobLive();
        live.Start(1, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Update(1, depth: 18, nps: 4_000_000);
        live.Update(1, depth: 0, nps: 0);   // erste Zeilen tragen oft time=0 ⇒ kein Tempo ableitbar

        var row = Assert.Single(live.ForUser(5, Start));
        Assert.Equal(18, row.Depth);
        Assert.Equal(4_000_000, row.Nps);
    }

    [Fact]
    public void ForUser_ReturnsOnlyOwnJobs()
    {
        var live = new AnalysisJobLive();
        live.Start(1, userId: 1, secondsBase: 0, startedUtc: Start);
        live.Start(2, userId: 2, secondsBase: 0, startedUtc: Start);

        Assert.Equal(new[] { 1 }, live.ForUser(1, Start).Select(r => r.Id));
        Assert.Equal(new[] { 2 }, live.ForUser(2, Start).Select(r => r.Id));
    }

    [Fact]
    public void Stop_RemovesJob_AndUpdateAfterwardsIsIgnored()
    {
        var live = new AnalysisJobLive();
        live.Start(3, userId: 1, secondsBase: 0, startedUtc: Start);
        live.Stop(3);
        live.Update(3, depth: 20, nps: 1);   // Nachzügler eines beendeten Laufs darf nichts wiederbeleben

        Assert.Empty(live.ForUser(1, Start));
    }

    [Fact]
    public void ForUser_ClockJumpBackwards_DoesNotShrinkBelowBase()
    {
        var live = new AnalysisJobLive();
        live.Start(4, userId: 1, secondsBase: 30, startedUtc: Start);

        var row = Assert.Single(live.ForUser(1, Start.AddSeconds(-10)));
        Assert.Equal(30, row.Seconds);
    }
}
