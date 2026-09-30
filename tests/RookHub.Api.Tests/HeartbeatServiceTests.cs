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

    /// <summary>
    /// I2-012: Vertrag mit dem log-watcher (config.py HEARTBEAT_CHECKS). Er zählt die Zeilen mit
    /// <c>labels.HeartbeatService == "rookhub-api"</c> (Form <c>rookhub-api=rookhub-logs-*</c>), die Altform der
    /// Prod-Konfig sucht per match_phrase den gerenderten Satz <c>"Heartbeat: rookhub-api"</c>. Der Test oben prüft nur
    /// Contains("Heartbeat") + Contains(ServiceName) und bliebe bei einer Umformulierung („Lebenszeichen: …") grün —
    /// der Wächter meldete dann für rookhub-prod und -dev bei jedem Zyklus heartbeat_missing HIGH. Deshalb hier die
    /// LITERALEN Werte, nicht die Konstanten aus dem Dienst.
    /// </summary>
    [Fact]
    public async Task EmitAsync_WritesTheExactSentenceAndLabelTheLogWatcherLooksFor()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("hb-" + Guid.NewGuid()));
        using var provider = services.BuildServiceProvider();
        var logger = new CapturingLogger<HeartbeatService>();
        var config = new ConfigurationBuilder().Build();

        await new HeartbeatService(provider.GetRequiredService<IServiceScopeFactory>(), logger, config).EmitAsync();

        var line = Assert.Single(logger.Events);
        Assert.StartsWith("Heartbeat: rookhub-api ", line.Message);            // match_phrase der Altform
        Assert.Equal("rookhub-api", line.State["HeartbeatService"]);            // → labels.HeartbeatService
        Assert.Equal(
            "Heartbeat: {HeartbeatService} {HeartbeatStatus} db={HeartbeatDbOk} uptime={HeartbeatUptimeSeconds}s",
            line.State["{OriginalFormat}"]);
    }
}
