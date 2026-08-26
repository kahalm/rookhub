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
