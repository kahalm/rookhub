using System.Text;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AnalysisJobStreamTests
{
    [Fact]
    public void DepthOf_ParsesDepth_IgnoresHeartbeatsAndGarbage()
    {
        Assert.Equal(27, AnalysisJobStream.DepthOf("{\"time\":5,\"depth\":27,\"nodes\":10,\"pvs\":[]}"));
        Assert.Null(AnalysisJobStream.DepthOf(""));
        Assert.Null(AnalysisJobStream.DepthOf("   "));          // Heartbeat-Leerzeile des Proxys
        Assert.Null(AnalysisJobStream.DepthOf("{\"pvs\":[]}"));   // ohne depth
        Assert.Null(AnalysisJobStream.DepthOf("{kaputt"));
        Assert.Null(AnalysisJobStream.DepthOf("[1,2]"));
    }

    [Fact]
    public void ShouldPersist_OnlyAtOrBeyondReachedDepth()
    {
        // Fortsetzung/Neustart liefert die flachen Iterationen erneut — die dürfen das Ergebnis nicht zurückwerfen.
        Assert.False(AnalysisJobStream.ShouldPersist(5, 27));
        Assert.True(AnalysisJobStream.ShouldPersist(27, 27));
        Assert.True(AnalysisJobStream.ShouldPersist(28, 27));
        Assert.True(AnalysisJobStream.ShouldPersist(1, 0));
    }

    [Fact]
    public async Task ConsumeAsync_DeliversOnlyLinesWithDepth_InOrder()
    {
        var ndjson = "{\"depth\":1,\"pvs\":[]}\n\n\n{\"depth\":2,\"pvs\":[]}\nkaputt\n{\"depth\":3,\"pvs\":[]}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ndjson));
        var seen = new List<int>();
        await AnalysisJobStream.ConsumeAsync(stream, (_, d) => { seen.Add(d); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal([1, 2, 3], seen);
    }

    [Fact]
    public async Task ConsumeAsync_Tally_SeparatesDataLinesHeartbeatsAndRest()
    {
        // Genau die Frage, die beim Abriss zählt: kam Verkehr — und welcher Art?
        var ndjson = "{\"depth\":1,\"pvs\":[]}\n\n\n{\"depth\":2,\"pvs\":[]}\nkaputt\n{\"ok\":true}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ndjson));
        var tally = new StreamTally();
        await AnalysisJobStream.ConsumeAsync(stream, (_, _) => Task.CompletedTask, CancellationToken.None, tally);

        Assert.Equal(2, tally.DataLines);
        Assert.Equal(2, tally.Heartbeats);     // die beiden Leerzeilen
        Assert.Equal(2, tally.OtherLines);     // „kaputt" und JSON ohne depth
        Assert.NotNull(tally.LastDataUtc);
    }

    [Fact]
    public void Tally_WithoutAnyLine_ReportsNoGaps()
    {
        var tally = new StreamTally();
        Assert.Null(tally.DataGapSeconds(DateTime.UtcNow));
        Assert.Null(tally.AnyGapSeconds(DateTime.UtcNow));
    }

    [Fact]
    public void Tally_HeartbeatKeepsAnyGapSmall_ButDataGapKeepsGrowing()
    {
        // Der Unterschied, an dem sich die Ursachen scheiden: die Leitung ist NICHT stumm (AnyGap klein),
        // obwohl die Engine seit Minuten keine Bewertung geliefert hat (DataGap gross).
        var t0 = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var tally = new StreamTally();
        tally.Note("{\"depth\":20,\"pvs\":[]}", t0);
        tally.Note("", t0.AddSeconds(300));

        Assert.Equal(305, tally.DataGapSeconds(t0.AddSeconds(305)));
        Assert.Equal(5, tally.AnyGapSeconds(t0.AddSeconds(305)));
    }

    [Fact]
    public async Task ConsumeAsync_StopsOnCancellation()
    {
        // Endloser Stream (Pipe ohne Schreiber) — der Abbruch muss ihn verlassen.
        var pipe = new System.IO.Pipelines.Pipe();
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AnalysisJobStream.ConsumeAsync(pipe.Reader.AsStream(), (_, _) => Task.CompletedTask, cts.Token));
    }

    [Fact]
    public void Tally_CountsRepeatedDataLineAsHeartbeat()
    {
        // Der Provider wiederholt seine letzte info-Zeile, wenn nach oben nichts mehr geht
        // (engine-provider/patch_provider.py). Zeichengleich = Lebenszeichen, nicht Fortschritt:
        // eine rechnende Engine kann sich nicht wiederholen, time und nodes wandern immer mit.
        var t0 = new DateTime(2026, 9, 12, 1, 51, 0, DateTimeKind.Utc);
        var line = "{\"time\":27,\"depth\":11,\"nodes\":24006,\"pvs\":[]}";
        var tally = new StreamTally();
        tally.Note(line, t0);
        tally.Note(line, t0.AddSeconds(15));
        tally.Note(line, t0.AddSeconds(30));

        Assert.Equal(1, tally.DataLines);
        Assert.Equal(2, tally.Heartbeats);
        // Die Luecke zaehlt ab der EINEN echten Zeile — genau daran haengt der Stillstands-Waechter.
        Assert.Equal(30, tally.DataGapSeconds(t0.AddSeconds(30)));
        Assert.Equal(0, tally.AnyGapSeconds(t0.AddSeconds(30)));
    }

    [Fact]
    public void Tally_CountsChangedDataLineAsProgress()
    {
        var t0 = new DateTime(2026, 9, 12, 1, 51, 0, DateTimeKind.Utc);
        var tally = new StreamTally();
        tally.Note("{\"time\":27,\"depth\":11,\"nodes\":24006,\"pvs\":[]}", t0);
        tally.Note("{\"time\":92,\"depth\":12,\"nodes\":81233,\"pvs\":[]}", t0.AddSeconds(15));

        Assert.Equal(2, tally.DataLines);
        Assert.Equal(0, tally.Heartbeats);
    }

    [Fact]
    public void IsRepeat_OnlyForIdenticalPredecessor()
    {
        Assert.False(StreamTally.IsRepeat("a", null));
        Assert.False(StreamTally.IsRepeat("a", "b"));
        Assert.True(StreamTally.IsRepeat("a", "a"));
    }

    [Theory]
    // Reihum, damit der Auftrag der haengenden Engine nicht sofort wieder in die Arme laeuft:
    // sie hat gerade nichts zu tun und gewaenne jeden Vergleich der Schlangenlaenge.
    [InlineData("b", "a,b,c", "c")]
    [InlineData("c", "a,b,c", "a")]
    // Nur eine hinterlegt: es gibt nichts zu wechseln.
    [InlineData("a", "a", null)]
    // Von Hand gewaehlte Engine (steht nicht in der Hintergrund-Liste): bleibt die Entscheidung des Nutzers.
    [InlineData("x", "a,b,c", null)]
    public void NextEngineAfter_RotatesWithinTheConfiguredList(string current, string list, string? expected)
    {
        var engines = list.Split(',');
        Assert.Equal(expected, AnalysisJobWorker.NextEngineAfter(engines, current));
    }
}
