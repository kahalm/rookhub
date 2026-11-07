using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Social;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Dauerhafter Speicher des lokalen Explorers (0.884.0) gegen ECHTES MariaDB: die Migration legt <c>LocalExplorerPositions</c> an, das
/// gebündelte <c>INSERT … ON DUPLICATE KEY UPDATE</c> (handgebaut, InMemory prüft es nicht) schreibt und erneuert, das Lesen per
/// <c>IN</c> in Portionen findet alles, und das Aufräumen (<c>ExecuteDelete</c>) löscht nur Abgelaufenes.
/// </summary>
public class LocalExplorerStoreSqlTests(LocalExplorerStoreSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<LocalExplorerStoreSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly ExplorerQuery Query = ExplorerQuery.Create("lichess", [2000, 2200], ["blitz", "rapid", "classical"]);

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static RepertoireExplorerService Service(AppDbContext db, TimeProvider time)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" }).Build();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var notifications = new NotificationService(db);
        var reps = new RepertoireService(db, new RepertoireAnalyzeService(db, cache), new FriendService(db, notifications), notifications);
        var gate = new LichessExplorerGate(time, refillInterval: TimeSpan.Zero);
        var online = new LichessExplorerClient(new HttpClient { BaseAddress = new Uri(LichessExplorerClient.BaseUrl) }, gate,
            NullLogger<LichessExplorerClient>.Instance);
        var local = new LocalExplorerClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9/") }, NullLogger<LocalExplorerClient>.Instance);
        return new RepertoireExplorerService(db, reps, online, local, cache, gate, new EncryptionService(config), config,
            NullLogger<RepertoireExplorerService>.Instance, time);
    }

    private static ExplorerPositionStats Stats(long total) =>
        new(total, [new ExplorerMoveStat("e2e4", "e4", total, "King's Pawn „ü“", "B00", 1, 2, 3, 1900)], 1, 2, 3);

    [MySqlFact]
    public async Task Upsert_InBundles_ReadBackInChunks_Renew_AndPurge()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 11, 7, 3, 30, 0, TimeSpan.Zero));
        var keys = Enumerable.Range(0, 1234).Select(i => $"pos{i}").ToList();
        await using (var db = fixture.Schema.NewContext())
        {
            var svc = Service(db, time);
            await svc.StoreLocalAsync(Query, keys.Select(k => (k, Stats(10))).ToList(), default);   // 7 Anweisungen à ≤ 200
            Assert.Equal(1234, svc.StoreWrites);
            Assert.Equal(1234, await db.LocalExplorerPositions.CountAsync());
        }

        time.Now = time.Now.AddDays(10);
        await using (var db = fixture.Schema.NewContext())
        {
            var svc = Service(db, time);
            var loaded = await svc.LoadLocalAsync(Query, keys, default);
            Assert.Equal(1234, loaded.Count);
            Assert.Equal("King's Pawn „ü“", loaded["pos7"].Moves[0].Opening);
            // dieselben Schlüssel erneut: erneuert, nicht verdoppelt
            await svc.StoreLocalAsync(Query, keys.Take(5).Select(k => (k, Stats(99))).ToList(), default);
            Assert.Equal(1234, await db.LocalExplorerPositions.CountAsync());
            Assert.Equal(99, (await svc.LoadLocalAsync(Query, ["pos3"], default))["pos3"].Total);
            Assert.Equal(5, (await svc.StoredLocalKeysAsync(Query, keys.Take(5).Append("fehlt").ToList(), default)).Count);
        }

        time.Now = time.Now.AddDays(40);   // die ersten 1229 sind 50 Tage alt, die 5 erneuerten 40
        await using (var db = fixture.Schema.NewContext())
        {
            var svc = Service(db, time);
            Assert.Equal(5, (await svc.LoadLocalAsync(Query, keys, default)).Count);
            Assert.Equal(1229, await svc.PurgeLocalAsync(default));
            Assert.Equal(5, await db.LocalExplorerPositions.CountAsync());
        }
    }
}

public sealed class LocalExplorerStoreSqlFixture() : MariaDbClassFixture("lexp", withApp: false);
