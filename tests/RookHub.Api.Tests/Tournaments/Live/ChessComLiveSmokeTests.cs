using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services.Tournaments.Live;
using Xunit.Abstractions;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>
/// Echte Prüfung gegen chess.com — läuft NUR mit <c>ROOKHUB_LIVE_CHESSCOM=1</c> (sonst sofort grün, die CI fragt chess.com nie).
/// <c>ROOKHUB_LIVE_CHESSCOM_URL</c> = Event-/Partie-Link (Vorgabe: WM 2026). Liest die laufende (sonst letzte) Runde über die
/// echte Quelle und pollt sie zwei Takte lang.
/// </summary>
public class ChessComLiveSmokeTests(ITestOutputHelper output)
{
    private sealed class Factory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var c = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub/1.0 (+https://rookhub.oberschmid.homes)");
            return c;
        }
    }

    [Fact]
    public async Task RealChessCom_EventRoundPoll()
    {
        if (Environment.GetEnvironmentVariable("ROOKHUB_LIVE_CHESSCOM") != "1") return;
        var url = Environment.GetEnvironmentVariable("ROOKHUB_LIVE_CHESSCOM_URL") ?? "https://www.chess.com/events/2026-fide-chess-world-championship";
        var link = ChessComEvents.LinkOf(url);
        Assert.NotNull(link);
        var src = new ChessComLiveSource(new HttpChessComEventsApi(new Factory()), NullLogger<ChessComLiveSource>.Instance);
        var room = (await src.RoomAsync(link!.Room!, TimeSpan.Zero, default))!;
        var now = DateTime.UtcNow;
        var round = link.Round is { } rs ? room.RoundBySlug(rs)! : room.Rounds.LastOrDefault(r => ChessComEvents.IsOngoing(room, r, now))
            ?? room.Rounds.Last(r => ChessComEvents.IsFinished(room, r));
        var roundId = ChessComEvents.RoundId(room.Slug, round.Slug);
        output.WriteLine($"{room.Name} · {round.Name} ({roundId}), {room.GamesOf(round).Count()} Partien");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var n = 0;
        try
        {
            await foreach (var chunk in src.StreamRoundAsync(roundId, cts.Token))
            {
                var splitter = new BroadcastPgnSplitter();
                foreach (var g in splitter.Feed(chunk).Concat(splitter.Flush()).Select(LiveGameParser.Parse).OfType<LiveGame>().Take(4))
                    output.WriteLine($"  [{DateTime.UtcNow:HH:mm:ss}] {g.BoardTag} {g.White.Name} – {g.Black.Name} {g.Result} Züge {g.Plies} " +
                                     $"Uhr {g.White.ClockSeconds}/{g.Black.ClockSeconds} {g.LastMove}");
                if (++n >= 3) break;
            }
        }
        catch (OperationCanceledException) { }
        output.WriteLine($"Partie-Abrufe: {src.GameFetches}");
        Assert.True(n >= 2);
    }
}
