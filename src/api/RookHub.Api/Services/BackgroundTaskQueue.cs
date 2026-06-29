using System.Threading.Channels;

namespace RookHub.Api.Services;

public interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem);
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
}

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;
    private readonly ILogger? _logger;
    private readonly string _name;

    public BackgroundTaskQueue(int capacity = 100, ILogger<BackgroundTaskQueue>? logger = null)
        : this(capacity, logger, "Background") { }

    protected BackgroundTaskQueue(int capacity, ILogger? logger, string name)
    {
        _logger = logger;
        _name = name;
        _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    public async ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        if (!_queue.Writer.TryWrite(workItem))
        {
            _logger?.LogWarning("{Name} task queue is full, dropping oldest item", _name);
            await _queue.Writer.WriteAsync(workItem);
        }
    }

    public async ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
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
}
