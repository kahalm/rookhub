using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Tournaments;

namespace RookHub.Api.Tests.Tournaments;

/// <summary>
/// Charakterisierung der Hintergrundschleife der Turnier-Abos (Zerlegung Z10, A8-013): erster Lauf nach der
/// Startverzögerung, dann Abo-Prüfung aller Nutzer und Refresh-Crawls, alle 24 h; ein Fehler in einer der beiden Phasen
/// wird mit eigener Zeile geloggt, die andere läuft trotzdem, und die Schleife lebt weiter.
/// </summary>
public class AutoSubscriptionLoopTests
{
    private sealed class PathHandler(string playerTournamentsJson) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            lock (Paths) Paths.Add(path);
            var body = path.StartsWith("/api/players/tournaments", StringComparison.Ordinal) ? playerTournamentsJson : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static ServiceProvider Provider(string dbName, PathHandler handler, bool withDb = true)
    {
        var services = new ServiceCollection();
        if (withDb)
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped(_ => new CrawlerProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://crawler:8080") }));
        return services.BuildServiceProvider();
    }

    /// <summary>Die Schleife mit sofortigem ersten Lauf (statt 60 s).</summary>
    private static BackgroundService Loop(IServiceProvider sp, ILogger<AutoSubscriptionService> log) =>
        new AutoSubscriptionService(sp.GetRequiredService<IServiceScopeFactory>(), log) { InitialDelay = TimeSpan.Zero };

    private static async Task WaitUntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(25);
        Assert.True(done(), "Schleife hat den erwarteten Stand nicht erreicht");
    }

    [Fact]
    public void Timing_FirstRunAfterOneMinute()
    {
        var service = new AutoSubscriptionService(null!, new CapturingLogger<AutoSubscriptionService>());
        Assert.Equal(TimeSpan.FromSeconds(60), service.InitialDelay);
    }

    [Fact]
    public async Task FirstTick_ChecksUsers_AndLogsStart()
    {
        var dbName = Guid.NewGuid().ToString();
        var handler = new PathHandler("""[{"tournamentId":"T1","tournamentName":"Open","endDate":"25.12.2099"}]""");
        await using var sp = Provider(dbName, handler);
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AppUsers.Add(new AppUser
            {
                Username = "u", Email = "u@example.com", PasswordHash = "x",
                Profile = new UserProfile { LastName = "Ober", FirstName = "Pat", ChessResultsId = "1" },
            });
            await db.SaveChangesAsync();
        }
        var log = new CapturingLogger<AutoSubscriptionService>();
        var loop = Loop(sp, log);

        await loop.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() =>
        {
            using var scope = sp.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().TournamentSubscriptions.Any();
        });
        await loop.StopAsync(CancellationToken.None);

        Assert.Contains(log.Events, e => e.Message == "AutoSubscriptionService started" && e.Level == LogLevel.Information);
        lock (handler.Paths) Assert.Contains(handler.Paths, p => p.StartsWith("/api/players/tournaments?lastName=Ober", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Events, e => e.Level == LogLevel.Error);
        Assert.False(loop.ExecuteTask?.IsFaulted ?? false);
    }

    [Fact]
    public async Task FailingPhases_AreLoggedSeparately_AndTheLoopSurvives()
    {
        // Ohne DbContext scheitern beide Phasen schon beim Auflösen — jede mit ihrer eigenen Zeile.
        await using var sp = Provider("egal", new PathHandler("[]"), withDb: false);
        var log = new CapturingLogger<AutoSubscriptionService>();
        var loop = Loop(sp, log);

        await loop.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => log.Events.Count(e => e.Level == LogLevel.Error) >= 2);
        await loop.StopAsync(CancellationToken.None);

        Assert.Contains(log.Events, e => e.Level == LogLevel.Error && e.Message == "Unexpected error in AutoSubscriptionService loop");
        Assert.Contains(log.Events, e => e.Level == LogLevel.Error && e.Message == "Unexpected error in AutoSubscriptionService refresh");
        Assert.False(loop.ExecuteTask?.IsFaulted ?? false);
    }
}
