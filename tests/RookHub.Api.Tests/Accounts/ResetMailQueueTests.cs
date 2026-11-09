using RookHub.Api.Controllers.Accounts;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Tests.Accounts;

/// <summary>R1-002: die Mails von „Passwort vergessen“ laufen in einer eigenen kleinen Queue, nicht mehr in der Bot-Webhook-Queue,
/// und eine volle Queue verwirft, statt die Anfrage warten zu lassen.</summary>
public class ResetMailQueueTests
{
    [Fact]
    public void AuthController_bekommtDieResetMailQueue_nichtDieWebhookQueue()
    {
        var parameters = typeof(AuthController).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Contains(typeof(IResetMailQueue), parameters);
        Assert.DoesNotContain(typeof(IWebhookTaskQueue), parameters);
    }

    [Fact]
    public void VolleQueue_verwirft_stattZuWarten()
    {
        var queue = new ResetMailQueue();
        for (var i = 0; i < ResetMailQueue.Capacity; i++)
            Assert.True(queue.TryEnqueue((_, _) => Task.CompletedTask));
        Assert.False(queue.TryEnqueue((_, _) => Task.CompletedTask));
        Assert.Equal(ResetMailQueue.Capacity, queue.PendingCount);
    }
}
