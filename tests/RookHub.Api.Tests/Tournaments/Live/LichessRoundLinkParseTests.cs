using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>
/// <c>/api/broadcast/-/-/{roundId}</c> liefert <c>{round, tour, games}</c> ohne <c>rounds</c>-Liste (Form wie am 26.11.2026 bei
/// der WM-Partie fnmEB1kH gelesen). Vorher galt jede fremde Runde per Partie-/Runden-Link als „nicht gefunden“.
/// </summary>
public class LichessRoundLinkParseTests
{
    private const string RoundOfTour =
        """
        {"round":{"id":"fnmEB1kH","name":"Game 3","slug":"game-3","ongoing":true,"startsAt":1795701600000,
          "url":"https://lichess.org/broadcast/fide-world-chess-championship-2026/game-3/fnmEB1kH"},
         "tour":{"id":"qfkdt8mn","name":"FIDE World Chess Championship 2026","slug":"fide-world-chess-championship-2026"},
         "games":[{"id":"RD8sbYw1","name":"A - B"}]}
        """;

    [Fact]
    public void ParseTourDetail_RoundEndpoint_TakesTheNamedRound()
    {
        var d = LichessBroadcasts.ParseTourDetail(RoundOfTour);

        Assert.NotNull(d);
        Assert.Equal("qfkdt8mn", d!.Tour.Id);
        var r = Assert.Single(d.Rounds);
        Assert.Equal("fnmEB1kH", r.Id);
        Assert.Equal("Game 3", r.Name);
        Assert.True(r.Ongoing);
        Assert.NotNull(r.StartsAt);
    }

    [Fact]
    public void ParseTourDetail_RoundsListAndRound_NoDuplicate()
    {
        const string json = """
            {"tour":{"id":"t1","name":"T"},"round":{"id":"r2","name":"Round 2"},
             "rounds":[{"id":"r1","name":"Round 1"},{"id":"r2","name":"Round 2"}]}
            """;
        var d = LichessBroadcasts.ParseTourDetail(json)!;

        Assert.Equal(new[] { "r1", "r2" }, d.Rounds.Select(x => x.Id));
    }
}

/// <summary>Zwischenstände erst ab <c>MinShowDepth</c> an den Balken (Wunsch 2026-11-26: kein kurzer Ausschlag nach dem Zug).</summary>
public class LiveEngineMinShowDepthTests
{
    private static RookHub.Api.Services.Tournaments.Live.LiveEngineQueue Queue(int minShow, int depth = 30) =>
        new(new NeverEvaluator(), new RookHub.Api.Services.Tournaments.Live.LiveEngineOptions { MinShowDepth = minShow, Depth = depth },
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RookHub.Api.Services.Tournaments.Live.LiveEngineQueue>.Instance, null, null);

    private sealed class NeverEvaluator : RookHub.Api.Services.Tournaments.Live.ILiveEngineEvaluator
    {
        public Task<RookHub.Api.Services.Tournaments.Live.LiveEval?> EvaluateAsync(RookHub.Api.Services.EngineBroker.EngineRef engine,
            int ownerUserId, string fen, Action<RookHub.Api.Services.Tournaments.Live.LiveEval> progress, CancellationToken ct) =>
            Task.FromResult<RookHub.Api.Services.Tournaments.Live.LiveEval?>(null);
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(27, true)]
    public void ShowProgress_OnlyFromMinShowDepth(int depth, bool shown) =>
        Assert.Equal(shown, Queue(20).ShowProgress(new RookHub.Api.Services.Tournaments.Live.LiveEval(50, null, "engine", depth)));

    [Fact]
    public void ShowProgress_MinAboveTarget_TargetCounts() =>
        Assert.True(Queue(40, depth: 25).ShowProgress(new RookHub.Api.Services.Tournaments.Live.LiveEval(50, null, "engine", 25)));

    [Fact]
    public void Options_DefaultMinShowDepthIs20() =>
        Assert.Equal(20, RookHub.Api.Services.Tournaments.Live.LiveEngineOptions.From(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()).MinShowDepth);
}

/// <summary>Partie-Link mit Abfrage-Anhang (z. B. <c>?pov=black</c>, kroatische Mannschaftsmeisterschaft 26.11.2026).</summary>
public class LichessBoardLinkQueryTests
{
    [Fact]
    public void BoardLinkOf_WithPovQuery_ReadsRoundAndGame()
    {
        var ids = LichessBroadcasts.BoardLinkOf(
            "https://lichess.org/broadcast/35-ekipno-prvenstvo-hrvatske-2026-croatian-team-championship-1-a-hsl/round-6/Z7694HAY/Z2ULjsmB?pov=black");
        Assert.NotNull(ids);
        Assert.Equal("Z7694HAY", ids!.Value.RoundId);
        Assert.Equal("Z2ULjsmB", ids.Value.GameId);
    }
}
