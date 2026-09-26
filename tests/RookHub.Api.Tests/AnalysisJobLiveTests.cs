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

    [Fact]
    public void Summary_CountsOwnRunsAndSumsTheirSpeed()
    {
        var live = new AnalysisJobLive();
        live.Start(1, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Start(2, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Start(3, userId: 5, secondsBase: 0, startedUtc: Start);   // noch keine Zeile mit Tempo
        live.Start(4, userId: 9, secondsBase: 0, startedUtc: Start);   // fremder Auftrag
        live.Update(1, depth: 20, nps: 3_000_000);
        live.Update(2, depth: 22, nps: 2_500_000);
        live.Update(4, depth: 25, nps: 9_000_000);

        var (runs, nps) = live.Summary(5);

        Assert.Equal(3, runs);
        Assert.Equal(5_500_000, nps);
    }

    // ===== Spitze der letzten 24 Stunden (0.543.0) ===========================

    [Fact]
    public void Peak_RemembersTheHighestConcurrencyAndSpeed_AfterRunsEnded()
    {
        // Anlass: „engines: 1 · 1 164 kN/s" am Schwanz einer Partie sah wie ein Ausfall aus — erst der
        // Vergleich mit der Spitze sagt, ob gerade alle Engines rechnen.
        var live = new AnalysisJobLive();
        live.Start(1, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Start(2, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Start(3, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Update(1, depth: 20, nps: 1_000_000, nowUtc: Start);
        live.Update(2, depth: 20, nps: 1_200_000, nowUtc: Start);
        live.Update(3, depth: 20, nps: 900_000, nowUtc: Start);
        live.Stop(2);
        live.Stop(3);

        Assert.Equal((1, 1_000_000L), live.Summary(5));                       // jetzt: eine Engine
        Assert.Equal((3, 3_100_000L), live.Peak(5, Start.AddMinutes(30)));    // Spitze: drei, zusammen 3,1 MN/s
    }

    [Fact]
    public void Peak_IsPerUser()
    {
        var live = new AnalysisJobLive();
        live.Start(1, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Start(2, userId: 9, secondsBase: 0, startedUtc: Start);
        live.Update(2, depth: 20, nps: 9_000_000, nowUtc: Start);

        Assert.Equal((1, 0L), live.Peak(5, Start));
        Assert.Equal((1, 9_000_000L), live.Peak(9, Start));
        Assert.Equal((0, 0L), live.Peak(7, Start));   // nie gerechnet
    }

    [Fact]
    public void Peak_ForgetsWhatIsOlderThanTheWindow()
    {
        var live = new AnalysisJobLive();
        live.Start(1, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Start(2, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Update(1, depth: 20, nps: 5_000_000, nowUtc: Start);
        live.Stop(1); live.Stop(2);

        // Innerhalb des Fensters sichtbar, danach weg — auch ohne dass inzwischen etwas lief.
        Assert.Equal((2, 5_000_000L), live.Peak(5, Start.AddHours(AnalysisJobLive.PeakHours - 1)));
        Assert.Equal((0, 0L), live.Peak(5, Start.AddHours(AnalysisJobLive.PeakHours + 2)));

        // Ein neuer Lauf viel später räumt die alten Körbe auch aus dem Speicher; die Spitze ist dann die neue.
        var later = Start.AddHours(AnalysisJobLive.PeakHours + 2);
        live.Start(3, userId: 5, secondsBase: 0, startedUtc: later);
        live.Update(3, depth: 20, nps: 700_000, nowUtc: later);
        Assert.Equal((1, 700_000L), live.Peak(5, later));
    }

    [Fact]
    public void Peak_KeepsTheHighestValueWithinAnHour_NotTheLatest()
    {
        var live = new AnalysisJobLive();
        live.Start(1, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Update(1, depth: 20, nps: 4_000_000, nowUtc: Start);
        live.Update(1, depth: 25, nps: 2_000_000, nowUtc: Start.AddMinutes(5));   // wird langsamer (tiefer)

        Assert.Equal((1, 4_000_000L), live.Peak(5, Start.AddMinutes(10)));
    }

    [Fact]
    public void Summary_DropsStoppedRuns()
    {
        var live = new AnalysisJobLive();
        live.Start(1, userId: 5, secondsBase: 0, startedUtc: Start);
        live.Update(1, depth: 20, nps: 3_000_000);
        live.Stop(1);

        Assert.Equal((0, 0L), live.Summary(5));
    }
}
