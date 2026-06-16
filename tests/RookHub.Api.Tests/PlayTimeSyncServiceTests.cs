using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class PlayTimeSyncServiceTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<TrainingGoalService>();
        services.AddHttpClient<PlayTimeService>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunOnceAsync_ShutdownCancellation_PropagatesAndDoesNotLogError()
    {
        // Regression: bei Deploy/Restart mitten im Sync warf ein ct-gebundener Await eine
        // OperationCanceledException, die als Error "Durchlauf fehlgeschlagen" geloggt wurde
        // (Fehlalarm im log-watcher). Erwartung: sauber propagieren, NICHT als Error loggen.
        using var sp = BuildProvider();
        var config = new ConfigurationBuilder().Build();
        var logger = new CapturingLogger<PlayTimeSyncService>();
        var svc = new PlayTimeSyncService(sp.GetRequiredService<IServiceScopeFactory>(), config, logger);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.RunOnceAsync(cts.Token));
        Assert.DoesNotContain(logger.Events, e => e.Message.Contains("fehlgeschlagen"));
    }
}
