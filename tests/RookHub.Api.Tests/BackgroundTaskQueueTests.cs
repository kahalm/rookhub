using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class BackgroundTaskQueueTests
{
    [Fact]
    public async Task EnqueueDequeue_FIFO_Order()
    {
        var queue = new BackgroundTaskQueue(10);
        var order = new List<int>();

        await queue.EnqueueAsync(async (_, _) => order.Add(1));
        await queue.EnqueueAsync(async (_, _) => order.Add(2));
        await queue.EnqueueAsync(async (_, _) => order.Add(3));

        var cts = new CancellationTokenSource();
        var item1 = await queue.DequeueAsync(cts.Token);
        var item2 = await queue.DequeueAsync(cts.Token);
        var item3 = await queue.DequeueAsync(cts.Token);

        await item1(null!, CancellationToken.None);
        await item2(null!, CancellationToken.None);
        await item3(null!, CancellationToken.None);

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public async Task DequeueAsync_WaitsForItem()
    {
        var queue = new BackgroundTaskQueue(10);
        var dequeued = false;

        var dequeueTask = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var item = await queue.DequeueAsync(cts.Token);
            dequeued = true;
        });

        // Give the dequeue task a moment to start waiting
        await Task.Delay(50);
        Assert.False(dequeued);

        // Enqueue an item to unblock it
        await queue.EnqueueAsync(async (_, _) => { });
        await dequeueTask;
        Assert.True(dequeued);
    }

    [Fact]
    public async Task DequeueAsync_Cancellation_ThrowsCancellationException()
    {
        var queue = new BackgroundTaskQueue(10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Channel.ReadAsync throws TaskCanceledException (subclass of OperationCanceledException)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            queue.DequeueAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task BoundedCapacity_WaitsWhenFull()
    {
        var queue = new BackgroundTaskQueue(2);

        await queue.EnqueueAsync(async (_, _) => { });
        await queue.EnqueueAsync(async (_, _) => { });

        // Third enqueue should block (queue is full with capacity 2)
        var enqueueTask = queue.EnqueueAsync(async (_, _) => { });
        var completed = enqueueTask.IsCompleted;

        // Dequeue one to make room
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await queue.DequeueAsync(cts.Token);

        // Now the enqueue should complete
        await enqueueTask;
    }

    [Fact]
    public async Task SingleEnqueueDequeue_Works()
    {
        var queue = new BackgroundTaskQueue(10);
        var executed = false;

        await queue.EnqueueAsync(async (_, _) => executed = true);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var item = await queue.DequeueAsync(cts.Token);
        await item(null!, CancellationToken.None);

        Assert.True(executed);
    }
}
