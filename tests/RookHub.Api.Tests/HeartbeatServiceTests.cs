using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.Tests;

public class HeartbeatServiceTests
{
    [Fact]
    public async Task EmitAsync_LogsStructuredHealthyHeartbeat_WhenDbReachable()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("hb-" + Guid.NewGuid()));
        using var provider = services.BuildServiceProvider();
        var logger = new TestLogger<HeartbeatService>();
        var config = new ConfigurationBuilder().Build();

        var svc = new HeartbeatService(provider.GetRequiredService<IServiceScopeFactory>(), logger, config);
        await svc.EmitAsync();

        Assert.Single(logger.Messages);
        Assert.Contains("Heartbeat", logger.Messages[0]);
        Assert.Contains(HeartbeatService.ServiceName, logger.Messages[0]);   // rookhub-api
        Assert.Contains("healthy", logger.Messages[0]);                       // InMemory-DB ist erreichbar
    }
}
