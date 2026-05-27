using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// No-op implementation of IBackgroundTaskQueue for unit tests.
/// Enqueued tasks are silently discarded.
/// </summary>
public class NoOpTaskQueue : IBackgroundTaskQueue
{
    public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
        => ValueTask.CompletedTask;

    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException("NoOpTaskQueue does not support dequeue");
}

/// <summary>
/// Executes work items synchronously for tests that need to verify background logic.
/// </summary>
public class ImmediateTaskQueue : IBackgroundTaskQueue
{
    public async ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        await workItem(null!, CancellationToken.None);
    }

    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException("ImmediateTaskQueue does not support dequeue");
}
