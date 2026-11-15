namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Schreibt die im Speicher gesammelte Besucher-Herkunft (<see cref="VisitorOriginRecorder"/>) einmal je Minute
/// gebündelt in <c>VisitorDays</c> — kein Datenbank-Schreibzugriff je Anfrage. Scheitert das Schreiben, gehen die
/// Zähler zurück in den Sammler. Pausierbar („Hintergrundjobs pausieren“): solange sammelt der Speicher weiter
/// (gedeckelt). Beim Herunterfahren wird ein letztes Mal geschrieben.
/// </summary>
public class VisitorOriginFlushWorker : PeriodicWorker
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VisitorOriginRecorder _recorder;
    private readonly ILogger<VisitorOriginFlushWorker> _logger;

    public VisitorOriginFlushWorker(IServiceScopeFactory scopeFactory, VisitorOriginRecorder recorder,
        ILogger<VisitorOriginFlushWorker> logger) : base(scopeFactory, logger)
    {
        _scopeFactory = scopeFactory;
        _recorder = recorder;
        _logger = logger;
    }

    protected override WorkerSchedule Schedule { get; } = WorkerSchedule.Every(Interval);
    protected override bool Enabled => _recorder.Enabled;
    protected override WorkerStart Start => WorkerStart.After(Interval);
    protected override void LogFailure(Exception ex) => _logger.LogError(ex, "Besucher-Herkunft: Wegschreiben fehlgeschlagen");

    protected override Task StepAsync(IServiceProvider services, CancellationToken ct) => FlushAsync(services, ct);

    /// <summary>Ein Schub: Sammler leeren und wegschreiben; bei einem Fehler die Zähler zurücklegen und weiterwerfen.</summary>
    internal async Task<int> FlushAsync(IServiceProvider services, CancellationToken ct)
    {
        var deltas = _recorder.Drain();
        if (deltas.Count == 0) return 0;
        try
        {
            return await services.GetRequiredService<VisitorOriginService>().FlushAsync(deltas, ct);
        }
        catch
        {
            _recorder.Restore(deltas);
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            using var limit = new CancellationTokenSource(ShutdownBudget);
            using var scope = _scopeFactory.CreateScope();
            await FlushAsync(scope.ServiceProvider, limit.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Besucher-Herkunft: letztes Wegschreiben beim Herunterfahren fehlgeschlagen");
        }
    }
}
