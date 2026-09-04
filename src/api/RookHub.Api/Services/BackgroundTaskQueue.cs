using System.Threading.Channels;

namespace RookHub.Api.Services;

public interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem);
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>Wartende, noch nicht abgeholte Arbeiten. Wird beim Herunterfahren protokolliert: die
    /// Queue ist reiner Arbeitsspeicher, ein Neustart (nächtlich per Watchtower!) verwirft ihren Inhalt —
    /// vorher lautlos. Wer den Verlust nicht verkraftet, braucht einen DB-gestützten Zustand wie die
    /// Chessable-Importe (Status + Watchdog) statt dieser Queue.</summary>
    /// Standard-Implementierung „leer": die Test-Doubles (No-Op/Counting/Immediate) halten gar keine
    /// Queue und sollen nicht jedes Mal mitwachsen, wenn hier ein Diagnose-Glied dazukommt.</summary>
    int PendingCount => 0;

    /// <summary>Eine wartende Arbeit sofort abholen, ohne zu blocken (Rest-Drain beim Herunterfahren);
    /// <c>false</c>, wenn nichts wartet. Standard-Implementierung wie bei <see cref="PendingCount"/>.</summary>
    bool TryDequeue(out Func<IServiceProvider, CancellationToken, Task>? workItem)
    {
        workItem = null;
        return false;
    }
}

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;
    private readonly ILogger? _logger;
    private readonly string _name;

    // FRÜHER: capacity 100 + DropOldest. Unter DropOldest gelingt TryWrite IMMER (der älteste
    // wartende Eintrag wird still verdrängt) — der Warn-Zweig unten war toter Code und Verluste
    // unsichtbar. Verdrängt wurden dabei auch Arbeiten OHNE Watchdog/Re-Drive (Hint-Generierung,
    // Auto-Subscription-Checks, Tag-Backfills) → permanent und lautlos verloren (die Chessable-
    // Importe selbst überleben Drops über ihren DB-Status + Watchdog/Resume). JETZT: FullMode.Wait
    // (kein Verlust; ist die Queue voll, wartet der Enqueuer auf einen freien Slot — echte
    // Backpressure) + Kapazität 2048 als reine Runaway-Schranke: selbst ein Resume-Sturm mit
    // hunderten Import-Tickets bleibt weit darunter, ein Warten tritt praktisch nie ein.
    public BackgroundTaskQueue(int capacity = 2048, ILogger<BackgroundTaskQueue>? logger = null)
        : this(capacity, logger, "Background") { }

    protected BackgroundTaskQueue(int capacity, ILogger? logger, string name)
    {
        _logger = logger;
        _name = name;
        _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait });
    }

    public async ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        if (!_queue.Writer.TryWrite(workItem))
        {
            _logger?.LogWarning("{Name} task queue is full — enqueue waits for a free slot (no items are dropped)", _name);
            await _queue.Writer.WriteAsync(workItem);
        }
    }

    public async ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }

    public int PendingCount => _queue.Reader.Count;

    public bool TryDequeue(out Func<IServiceProvider, CancellationToken, Task>? workItem)
        => _queue.Reader.TryRead(out workItem);
}

/// <summary>
/// Eigene Queue NUR für die (kurzen, latenz-sensiblen) schach-bot-Webhook-Pushes (Solver-Updates
/// Tagespuzzle/Wochenpost). Bewusst GETRENNT von der allgemeinen <see cref="IBackgroundTaskQueue"/>:
/// die teilt sich der Chessable-Import, und ein großer Import-Schwung (ResumeService re-enqueued
/// dutzende minutenlange Jobs in die bounded/DropOldest-Queue) verdrängte sonst das Webhook-Ticket,
/// bevor es lief → Daily-Solver erschien nicht in Discord. Mit eigener Queue + eigenem Consumer
/// feuert der Webhook unabhängig von der Import-Last.
/// </summary>
public interface IWebhookTaskQueue : IBackgroundTaskQueue { }

public sealed class WebhookTaskQueue : BackgroundTaskQueue, IWebhookTaskQueue
{
    public WebhookTaskQueue(ILogger<WebhookTaskQueue>? logger = null)
        : base(capacity: 256, logger: logger, name: "Webhook") { }
}

/// <summary>Eigener Consumer für die <see cref="IWebhookTaskQueue"/> (analog
/// <see cref="BackgroundTaskWorker"/>, aber unabhängige Drain-Schleife).</summary>
public sealed class WebhookTaskWorker : BackgroundService
{
    private readonly IWebhookTaskQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookTaskWorker> _logger;

    public WebhookTaskWorker(IWebhookTaskQueue queue, IServiceScopeFactory scopeFactory, ILogger<WebhookTaskWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await _queue.DequeueAsync(stoppingToken);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook task failed");
            }
        }
    }

    /// <summary>Beim Herunterfahren: kurz nachdrainen, was schon in der Queue liegt, und den Rest
    /// SICHTBAR machen. Die Queue lebt nur im Arbeitsspeicher — ohne diese Zeilen verschwanden
    /// Tag-Backfills, Tipp-Generierung und Auto-Subscription-Tickets beim (nächtlichen) Neustart
    /// ohne eine einzige Logzeile. Das Zeitbudget ist knapp, weil Docker nach ~10 s hart abschießt.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        var pending = _queue.PendingCount;
        if (pending == 0) return;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        var done = 0;
        while (!budget.IsCancellationRequested && _queue.TryDequeue(out var item) && item is not null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await item(scope.ServiceProvider, budget.Token);
                done++;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Rest-Arbeit beim Herunterfahren fehlgeschlagen"); }
        }
        var lost = _queue.PendingCount;
        if (lost > 0)
            _logger.LogWarning("Herunterfahren: {Done} von {Pending} wartenden Arbeiten noch erledigt, {Lost} verworfen "
                + "(die Queue ist nicht persistent — betroffene Arbeiten müssen erneut angestoßen werden)", done, pending, lost);
        else
            _logger.LogInformation("Herunterfahren: {Done} wartende Arbeiten noch erledigt", done);
    }
}

public class BackgroundTaskWorker : BackgroundService
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundTaskWorker> _logger;

    public BackgroundTaskWorker(IBackgroundTaskQueue queue, IServiceScopeFactory scopeFactory, ILogger<BackgroundTaskWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await _queue.DequeueAsync(stoppingToken);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // App faehrt herunter — abgebrochene Work-Items sind kein Fehler, nicht als Error loggen.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background task failed");
            }
        }
    }

    /// <summary>Beim Herunterfahren: kurz nachdrainen, was schon in der Queue liegt, und den Rest
    /// SICHTBAR machen. Die Queue lebt nur im Arbeitsspeicher — ohne diese Zeilen verschwanden
    /// Tag-Backfills, Tipp-Generierung und Auto-Subscription-Tickets beim (nächtlichen) Neustart
    /// ohne eine einzige Logzeile. Das Zeitbudget ist knapp, weil Docker nach ~10 s hart abschießt.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        var pending = _queue.PendingCount;
        if (pending == 0) return;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        var done = 0;
        while (!budget.IsCancellationRequested && _queue.TryDequeue(out var item) && item is not null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await item(scope.ServiceProvider, budget.Token);
                done++;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Rest-Arbeit beim Herunterfahren fehlgeschlagen"); }
        }
        var lost = _queue.PendingCount;
        if (lost > 0)
            _logger.LogWarning("Herunterfahren: {Done} von {Pending} wartenden Arbeiten noch erledigt, {Lost} verworfen "
                + "(die Queue ist nicht persistent — betroffene Arbeiten müssen erneut angestoßen werden)", done, pending, lost);
        else
            _logger.LogInformation("Herunterfahren: {Done} wartende Arbeiten noch erledigt", done);
    }
}
