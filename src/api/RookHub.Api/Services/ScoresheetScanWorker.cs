namespace RookHub.Api.Services;

/// <summary>Weckt den <see cref="ScoresheetScanWorker"/>, sobald ein Foto hochgeladen wurde (sonst schaut er
/// nur alle <see cref="ScoresheetScanWorker.IdlePoll"/> nach).</summary>
public sealed class ScoresheetScanSignal
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Wake()
    {
        // Mehr als ein wartendes Signal braucht es nicht: der Worker arbeitet ohnehin alles ab, was wartet.
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Liest hochgeladene Partieformulare nacheinander (<see cref="ScoresheetScanService.ProcessAsync"/>). Der
/// Zustand liegt in der Datenbank: was beim Herunterfahren mitten im Lesen war, kommt beim Start zurück in die
/// Schlange — die Warteschlange im Arbeitsspeicher (<see cref="IBackgroundTaskQueue"/>) verlöre es beim
/// nächtlichen Watchtower-Neustart, und sie teilt sich die Plätze mit dem minutenlangen Chessable-Import.
/// </summary>
public class ScoresheetScanWorker : BackgroundService
{
    /// <summary>So oft schaut der Worker ohne Weckruf nach.</summary>
    public static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(30);

    /// <summary>Deckel für EINE Einlesung (Modell-Aufrufe inklusive Nachfragen).</summary>
    public static readonly TimeSpan MaxRuntime = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly ScoresheetScanSignal _signal;
    private readonly ILogger<ScoresheetScanWorker> _logger;

    public ScoresheetScanWorker(IServiceScopeFactory scopes, ScoresheetScanSignal signal, ILogger<ScoresheetScanWorker> logger)
    {
        _scopes = scopes;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var requeued = await scope.ServiceProvider.GetRequiredService<ScoresheetScanService>()
                .RequeueInterruptedAsync(stoppingToken);
            if (requeued > 0) _logger.LogInformation("{Count} unterbrochene Formular-Einlesung(en) wieder eingereiht", requeued);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Formular-Einlesungen: Aufräumen beim Start fehlgeschlagen");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            int? id = null;
            try
            {
                using var scope = _scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ScoresheetScanService>();
                id = await service.ClaimNextAsync(stoppingToken);
                if (id is int scanId)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(MaxRuntime);
                    await service.ProcessAsync(scanId, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // die Einlesung steht auf Running und kommt beim nächsten Start zurück
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Formular-Einlesung {ScanId} abgebrochen", id);
                if (id is int failed) await MarkFailedAsync(failed);
            }

            if (id == null)
            {
                try { await _signal.WaitAsync(IdlePoll, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>In einem EIGENEN Scope: der Kontext des abgebrochenen Laufs kann in beliebigem Zustand sein.</summary>
    private async Task MarkFailedAsync(int id)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
            var scan = await db.ScoresheetScans.FindAsync(id);
            if (scan == null || scan.Status != Models.ScoresheetScanStatus.Running) return;
            scan.Status = Models.ScoresheetScanStatus.Failed;
            scan.Error = "failed";
            scan.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Formular-Einlesung {ScanId}: Status konnte nicht gesetzt werden", id);
        }
    }
}
