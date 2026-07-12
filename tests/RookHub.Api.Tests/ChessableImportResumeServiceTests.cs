using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>ChessableImportResumeService (IHostedService): beim Start werden durch Crash/Deploy
/// unterbrochene „running"-Importe auf Phase „queued" gesetzt und je einer wieder in die Queue gelegt —
/// das Prod-Stall-Recovery. War nur beiläufig referenziert, nie ausgeführt.</summary>
public class ChessableImportResumeServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ServiceProvider _provider;
    public ChessableImportResumeServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(opts);
        var services = new ServiceCollection();
        services.AddSingleton(_db);            // der Scope liefert genau diesen InMemory-Context
        _provider = services.BuildServiceProvider();
    }
    public void Dispose() { _provider.Dispose(); _db.Dispose(); }

    private sealed class CountingBgQueue : IBackgroundTaskQueue
    {
        public int Count;
        public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem) { Count++; return ValueTask.CompletedTask; }
        public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct) => throw new NotImplementedException();
    }

    private ChessableImportResumeService Service(CountingBgQueue queue)
        => new(_provider.GetRequiredService<IServiceScopeFactory>(), queue, NullLogger<ChessableImportResumeService>.Instance);

    [Fact]
    public async Task StartAsync_RequeuesRunningImports_AndFlipsPhaseToQueued()
    {
        _db.ChessableImports.AddRange(
            new ChessableImport { UserId = 1, Status = "running", Phase = "fetching" },
            new ChessableImport { UserId = 2, Status = "running", Phase = "importing" },
            new ChessableImport { UserId = 3, Status = "completed", Phase = "done" });   // nicht betroffen
        await _db.SaveChangesAsync();

        var queue = new CountingBgQueue();
        await Service(queue).StartAsync(CancellationToken.None);

        Assert.Equal(2, queue.Count);   // ein Ticket je unterbrochenem Import
        var running = await _db.ChessableImports.Where(i => i.Status == "running").ToListAsync();
        Assert.All(running, i => Assert.Equal("queued", i.Phase));
        var completed = await _db.ChessableImports.SingleAsync(i => i.Status == "completed");
        Assert.Equal("done", completed.Phase);   // unangetastet
    }

    [Fact]
    public async Task StartAsync_NoRunningImports_EnqueuesNothing()
    {
        _db.ChessableImports.Add(new ChessableImport { UserId = 1, Status = "completed", Phase = "done" });
        await _db.SaveChangesAsync();
        var queue = new CountingBgQueue();
        await Service(queue).StartAsync(CancellationToken.None);
        Assert.Equal(0, queue.Count);
    }
}
