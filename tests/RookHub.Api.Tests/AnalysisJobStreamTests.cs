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
    public async Task ConsumeAsync_StopsOnCancellation()
    {
        // Endloser Stream (Pipe ohne Schreiber) — der Abbruch muss ihn verlassen.
        var pipe = new System.IO.Pipelines.Pipe();
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AnalysisJobStream.ConsumeAsync(pipe.Reader.AsStream(), (_, _) => Task.CompletedTask, cts.Token));
    }
}
