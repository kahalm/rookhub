using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Tests.Engines;

/// <summary>R4-018: der lokale Explorer bekommt prozessweit höchstens <see cref="LocalExplorerClient.MaxConcurrent"/>
/// Abfragen zugleich, und die lokale Quelle hat einen eigenen Deckel je Konto.</summary>
public class LocalExplorerConcurrencyTests
{
    private sealed class SlowHandler : HttpMessageHandler
    {
        private int _inFlight;
        public int MaxSeen;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _inFlight);
            lock (this) MaxSeen = Math.Max(MaxSeen, now);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref _inFlight);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"white\":1,\"draws\":0,\"black\":0,\"moves\":[]}"),
            };
        }
    }

    [Fact]
    public async Task VieleParalleleAbfragen_hoechstensMaxConcurrentZugleich()
    {
        var handler = new SlowHandler();
        var client = new LocalExplorerClient(new HttpClient(handler) { BaseAddress = new Uri("http://local-explorer.test") },
            NullLogger<LocalExplorerClient>.Instance);
        const string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => client.FetchAsync(fen, ExplorerQuery.Create("lichess", [1800], ["blitz"]), default)));

        Assert.InRange(handler.MaxSeen, 1, LocalExplorerClient.MaxConcurrent);
    }

    [Fact]
    public void LokaleQuelle_hatEigenenDeckelJeKonto()
    {
        var quota = new ExplorerOnlineQuota(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Explorer:OnlinePerAccountPerMinute"] = "1",
            ["Explorer:LocalPerAccountPerMinute"] = "2",
        }).Build());

        Assert.True(quota.TryAcquire(7, "local", out _));
        Assert.True(quota.TryAcquire(7, "local", out _));
        Assert.False(quota.TryAcquire(7, "local", out var retryAfter));
        Assert.NotNull(retryAfter);
        Assert.True(quota.TryAcquire(7, "online", out _));   // eigener Topf
        Assert.True(quota.TryAcquire(8, "local", out _));    // anderes Konto
    }
}
