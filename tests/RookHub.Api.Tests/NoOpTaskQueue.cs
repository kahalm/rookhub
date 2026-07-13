using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// No-op implementation of IBackgroundTaskQueue for unit tests.
/// Enqueued tasks are silently discarded.
/// </summary>
public class NoOpTaskQueue : IWebhookTaskQueue
{
    public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
        => ValueTask.CompletedTask;

    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException("NoOpTaskQueue does not support dequeue");
}

/// <summary>No-op <see cref="IBackgroundTaskQueue"/> für Tests, die einen Dienst nur konstruieren
/// müssen (enqueuete Work-Items werden verworfen).</summary>
public class NoOpBackgroundTaskQueue : IBackgroundTaskQueue
{
    public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
        => ValueTask.CompletedTask;

    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException("NoOpBackgroundTaskQueue does not support dequeue");
}

/// <summary>
/// Zählt enqueuete Work-Items (führt sie nicht aus) — für Tests, die prüfen, OB ein
/// Hintergrund-Job angestoßen wurde (z.B. Auto-Subscription nur bei Identitätsänderung).
/// </summary>
public class CountingTaskQueue : IWebhookTaskQueue
{
    public int EnqueuedCount { get; private set; }

    public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        EnqueuedCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException("CountingTaskQueue does not support dequeue");
}

/// <summary>
/// Executes work items synchronously for tests that need to verify background logic.
/// </summary>
public class ImmediateTaskQueue : IWebhookTaskQueue
{
    public async ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        await workItem(null!, CancellationToken.None);
    }

    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException("ImmediateTaskQueue does not support dequeue");
}
