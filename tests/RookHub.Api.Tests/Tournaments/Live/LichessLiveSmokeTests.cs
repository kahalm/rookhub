using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;
using RookHub.Api.Services.Tournaments.Live;
using Xunit.Abstractions;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>
/// Echte Prüfung gegen Lichess — läuft NUR mit <c>ROOKHUB_LIVE_LICHESS=1</c> (sonst sofort grün, die CI fragt Lichess nie):
/// findet die Übertragung der 1. Bundesliga 2026/27 über <c>/api/broadcast/top</c> (tnr1404438), liest den Strom der
/// laufenden (sonst letzten) Runde ein paar Sekunden und meldet, ob <c>%clk</c>/<c>%eval</c> drin sind.
/// </summary>
public class LichessLiveSmokeTests(ITestOutputHelper output)
{
    private sealed class Factory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub/1.0 (+https://rookhub.oberschmid.homes)");
            return c;
        }
    }

    [Fact]
    public async Task RealLichess_BundesligaStream()
    {
        if (Environment.GetEnvironmentVariable("ROOKHUB_LIVE_LICHESS") != "1") return;
        var src = new HttpLichessLiveSource(new Factory());
        var finder = new BroadcastFinder(src, new MemoryCache(new MemoryCacheOptions()), NullLogger<BroadcastFinder>.Instance);
        var tour = await finder.FindAsync(new CrTournament("1404438", "1. BUNDESLIGA AUT 2026/2027", null, DateTime.UtcNow, 11), [], DateTime.UtcNow, default);
        Assert.NotNull(tour);
        output.WriteLine($"Übertragung: {tour!.Id} {tour.Name}");
        var detail = LichessBroadcasts.ParseTourDetail((await src.GetTourAsync(tour.Id, default))!)!;
        var round = detail.Rounds.LastOrDefault(r => r.Ongoing) ?? detail.Rounds.Last(r => r.Finished);
        output.WriteLine($"Runde: {round.Name} ({round.Id}) ongoing={round.Ongoing} finished={round.Finished}");

        var splitter = new BroadcastPgnSplitter();
        var games = new List<LiveGame>();
        var raw = new System.Text.StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await foreach (var chunk in src.StreamRoundAsync(round.Id, cts.Token))
            {
                raw.Append(chunk);
                games.AddRange(splitter.Feed(chunk).Select(LiveGameParser.Parse).OfType<LiveGame>());
            }
        }
        catch (OperationCanceledException) { }
        output.WriteLine($"Partien im Strom: {games.Count}, %clk: {raw.ToString().Contains("%clk")}, %eval: {raw.ToString().Contains("%eval")}");
        foreach (var g in games.Take(6))
            output.WriteLine($"  {g.BoardTag} {g.White.Name} ({g.White.Team}) – {g.Black.Name} ({g.Black.Team}) {g.Result} " +
                             $"Züge {g.Plies} Uhr {g.White.ClockSeconds}/{g.Black.ClockSeconds} {g.LastMove}");
        Assert.NotEmpty(games);
    }
}
