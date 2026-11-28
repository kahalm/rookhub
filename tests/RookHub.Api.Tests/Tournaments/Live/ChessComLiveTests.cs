using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Tournaments;
using RookHub.Api.Data;
using RookHub.Api.Services;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>chess.com ohne Netz: Antwort je Pfad, zählt die Abrufe, kann ausfallen.</summary>
internal sealed class FakeChessComApi : IChessComEventsApi
{
    public Dictionary<string, string?> Responses { get; } = new(StringComparer.Ordinal);
    public List<string> Calls { get; } = [];
    /// <summary>So viele der nächsten Abrufe werfen <see cref="ChessComUnavailableException"/>.</summary>
    public int FailNext;

    public Task<string?> PostAsync(string path, CancellationToken ct)
    {
        lock (Calls) Calls.Add(path);
        if (FailNext > 0) { FailNext--; throw new ChessComUnavailableException("chess.com: 429"); }
        return Task.FromResult(Responses.GetValueOrDefault(path));
    }

    public int CountOf(string prefix) { lock (Calls) return Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal)); }
}

/// <summary>
/// chess.com-Übertragungen in den Live-Brettern (1.040.0): Link-Erkennung, Lesen der ECHTEN Antworten (gespeichert 28.11.2026 in
/// <c>Fixtures/ChessCom</c>: WM 2026 Gukesh–Sindarov, London Chess Classic Swiss Runde 2 live), PGN für den Übertragungs-Leser,
/// Abfrage im Takt mit Deckel und Vorrang der gewählten Bretter, Ausfall/Backoff, Router, Auswahl-Endpunkt und Verknüpfen.
/// </summary>
public class ChessComLiveTests : IDisposable
{
    private const string Wc = "2026-fide-chess-world-championship";
    private const string Lcc = "2026-xtx-markets-london-chess-classic-lcc-swiss";
    private const string WcRound3 = "cc~" + Wc + "~03";
    private const string LccRound2 = "cc~" + Lcc + "~02";
    private const string WcGame3 = "Sindarov_Javokhir-Gukesh_D";
    private const string LccLiveGame = "Verma_Aditya-Zwirs_Nico";

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ChessCom", name));

    private readonly FakeChessComApi _api = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public ChessComLiveTests()
    {
        _api.Responses["room/" + Wc] = Fixture("room-wc-2026.json");
        _api.Responses["room/" + Lcc] = Fixture("room-lcc-swiss-live.json");
        _api.Responses[$"game/{Wc}/03/{WcGame3}"] = Fixture("game-wc-03-finished.json");
        _api.Responses[$"game/{Lcc}/02/{LccLiveGame}"] = Fixture("game-lcc-live.json");
        _api.Responses["index/2026-xtx-markets-london-chess-classic"] = Fixture("index-lcc.json");
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    private ChessComLiveSource Source(int perTick = 6, int perSnapshot = 12) => new(_api, NullLogger<ChessComLiveSource>.Instance,
        new ChessComLiveSource.Options
        {
            Poll = TimeSpan.FromMilliseconds(20), IdlePoll = TimeSpan.FromMilliseconds(20), MaxBackoff = TimeSpan.FromMilliseconds(80),
            GameFetchesPerTick = perTick, GameFetchesPerSnapshot = perSnapshot,
        });

    // ── Link-Erkennung ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.chess.com/events/2026-fide-chess-world-championship", Wc, null, null)]
    [InlineData("chess.com/events/2026-fide-chess-world-championship/games", Wc, null, null)]
    [InlineData("https://www.chess.com/events/2026-fide-chess-world-championship/results?x=1#y", Wc, null, null)]
    [InlineData("https://www.chess.com/events/2026-fide-chess-world-championship/03/Sindarov_Javokhir-Gukesh_D", Wc, "03", WcGame3)]
    [InlineData("https://www.chess.com/de/events/2026-fide-chess-world-championship/03/Sindarov_Javokhir-Gukesh_D", Wc, "03", WcGame3)]
    [InlineData("https://www.chess.com/events/2026-fide-chess-world-championship/dashboard/03/Sindarov_Javokhir-Gukesh_D", Wc, "03", WcGame3)]
    [InlineData("https://www.chess.com/events/2026-fide-chess-world-championship/04", Wc, "04", null)]
    [InlineData("http://schach.chess.com/events/2026-fide-chess-world-championship", Wc, null, null)]
    public void LinkOf_EventRoundGameLinks(string url, string room, string? round, string? game) =>
        Assert.Equal(new ChessComEvents.Link(room, round, game), ChessComEvents.LinkOf(url));

    [Fact]
    public void LinkOf_IndexLink() =>
        Assert.Equal(new ChessComEvents.Link(null, null, null, "2026-xtx-markets-london-chess-classic"),
            ChessComEvents.LinkOf("https://www.chess.com/events/info/2026-xtx-markets-london-chess-classic"));

    [Theory]
    [InlineData("https://evil.com/events/2026-fide-chess-world-championship")]                  // falscher Wirt
    [InlineData("https://chess.com.evil.com/events/x")]                                         // nur *.chess.com
    [InlineData("https://www.chess.com/game/live/123456")]                                     // kein Event
    [InlineData("https://www.chess.com/events")]                                               // Übersicht
    [InlineData("https://www.chess.com/events/current")]                                       // Liste
    [InlineData("https://www.chess.com/events/x/03/Name.With.Dots")]                            // Slug mit Punkt
    [InlineData("ftp://www.chess.com/events/x")]
    [InlineData("https://lichess.org/broadcast/x/y/fnmEB1kH")]
    [InlineData("")]
    public void LinkOf_RejectsOthers(string url) => Assert.Null(ChessComEvents.LinkOf(url));

    [Fact]
    public void Ids_RoundTripAndValidation()
    {
        Assert.Equal(WcRound3, ChessComEvents.RoundId(Wc, "03"));
        Assert.Equal((Wc, "03"), ChessComEvents.SplitRound(WcRound3));
        Assert.Equal(Wc, ChessComEvents.RoomOfTour("cc~" + Wc));
        Assert.True(ExternalRoundRegistry.IsRoundId(WcRound3));
        Assert.True(ExternalRoundRegistry.IsRoundId("fnmEB1kH"));
        Assert.False(ExternalRoundRegistry.IsRoundId("cc~bad slug~03"));
        Assert.True(ExternalRoundRegistry.IsGameIdFor(WcRound3, WcGame3));
        Assert.False(ExternalRoundRegistry.IsGameIdFor("fnmEB1kH", WcGame3));            // Lichess-Runde, chess.com-Partie
        Assert.Equal("https://www.chess.com/events/" + Wc + "/games", ExternalRoundRegistry.RoundUrl(WcRound3));
        Assert.Equal("https://www.chess.com/events/" + Wc, TournamentLiveService.TourUrl("cc~" + Wc));
        Assert.Equal("https://lichess.org/broadcast/-/IjDgZVUR", TournamentLiveService.TourUrl("IjDgZVUR"));
    }

    [Fact]
    public void ParseRefs_MixesLichessAndChessCom_DropsMismatched() =>
        Assert.Equal(["fnmEB1kH.RD8sbYw1", $"{WcRound3}.{WcGame3}"],
            ExternalRoundRegistry.ParseRefs($"fnmEB1kH.RD8sbYw1,{WcRound3}.{WcGame3},fnmEB1kH.{WcGame3},{WcRound3}.a.b").Select(r => r.ToString()));

    // ── echte Antworten lesen ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseRoom_Wc_RoundsGamesPlayers()
    {
        var room = ChessComEvents.ParseRoom(Fixture("room-wc-2026.json"))!;
        Assert.Equal((Wc, "Salesforce FIDE World Championship 2026", 14), (room.Slug, room.Name, room.Rounds.Count));
        var r3 = room.RoundBySlug("03")!;
        Assert.Equal("Game 3", r3.Name);
        var g = Assert.Single(room.GamesOf(r3));
        Assert.Equal((WcGame3, "1/2-1/2", 847000L, 1003000L), (g.Slug, g.Result, g.WhiteClockMs, g.BlackClockMs));
        Assert.Equal(("Sindarov, Javokhir", "GM", 2782, "14205483"), (g.White.Name, g.White.Title, g.White.Elo, g.White.FideId));
        Assert.Null(g.White.Team);                                                  // „UZB“ ist die Föderation, keine Mannschaft
        Assert.True(ChessComEvents.IsFinished(room, r3));
        Assert.False(ChessComEvents.IsFinished(room, room.RoundBySlug("05")!));
    }

    [Fact]
    public void ParseRoom_LccLive_RoundWithoutName_GetsNumber()
    {
        var room = ChessComEvents.ParseRoom(Fixture("room-lcc-swiss-live.json"))!;
        var r2 = room.RoundBySlug("02")!;
        Assert.Equal("Round 2", r2.Name);
        Assert.Equal(6, room.GamesOf(r2).Count());
        Assert.True(ChessComEvents.IsOngoing(room, r2, new DateTime(2026, 11, 28, 13, 0, 0, DateTimeKind.Utc)));
        var live = room.GamesOf(r2).Single(g => g.Slug == LccLiveGame);
        Assert.Equal(("*", 34), (live.Result, live.Board));
        Assert.NotNull(live.Fen);
    }

    [Fact]
    public void TourJson_ReadByLichessParser_RoundIdsAndState()
    {
        var room = ChessComEvents.ParseRoom(Fixture("room-wc-2026.json"))!;
        var d = LichessBroadcasts.ParseTourDetail(ChessComEvents.ToTourJson(room, new DateTime(2026, 11, 28, 15, 0, 0, DateTimeKind.Utc)))!;
        Assert.Equal("cc~" + Wc, d.Tour.Id);
        Assert.Equal(14, d.Rounds.Count);
        Assert.Equal((WcRound3, "Game 3", true, false), (d.Rounds[2].Id, d.Rounds[2].Name, d.Rounds[2].Finished, d.Rounds[2].Ongoing));
        Assert.Equal((false, true), (d.Rounds[3].Finished, d.Rounds[3].Ongoing));     // Game 4 hat am 28.11. um 14:00 UTC begonnen
        // chess-results-Runde 3 → „Game 3“ (Zahl im Namen)
        Assert.Equal(WcRound3, TournamentLiveService.RoundsOf(d).Single(r => r.Round == 3).LichessRoundId);
    }

    [Fact]
    public void GameDetail_ToPgn_ParsedLikeABroadcast_FinalPositionAndClocks()
    {
        var room = ChessComEvents.ParseRoom(Fixture("room-wc-2026.json"))!;
        var (game, moves) = ChessComEvents.ParseGameDetail(Fixture("game-wc-03-finished.json"))!.Value;
        Assert.Equal(120, moves.Count);
        Assert.Equal(("e4", 7189000L), (moves[0].San, moves[0].ClockMs));
        var pgn = ChessComEvents.ToPgn(room, room.RoundBySlug("03")!, game, moves);
        Assert.Contains("[GameURL \"https://www.chess.com/events/" + Wc + "/03/" + WcGame3 + "\"]", pgn);
        Assert.Contains("1. e4 { [%clk 1:59:49] } e5", pgn);
        var g = LiveGameParser.Parse(pgn)!;
        Assert.Equal((WcGame3, "3.1", 120, "1/2-1/2"), (g.Id, g.BoardTag, g.Plies, g.Result));
        Assert.StartsWith("8/8/b7/2p4p/P6P/2Kp1k2/3B4/8 w", g.Fen);                 // currentFEN der chess.com-Antwort
        Assert.Equal((847, 1003), (g.White.ClockSeconds, g.Black.ClockSeconds));
        Assert.Equal(("14205483", 2782), (g.White.FideId, g.White.Elo));
    }

    [Fact]
    public void ToPgn_WithoutMoves_ShowsCurrentPositionFromRoom()
    {
        var room = ChessComEvents.ParseRoom(Fixture("room-lcc-swiss-live.json"))!;
        var r2 = room.RoundBySlug("02")!;
        var live = room.GamesOf(r2).Single(x => x.Slug == LccLiveGame);
        var g = LiveGameParser.Parse(ChessComEvents.ToPgn(room, r2, live, null))!;
        Assert.Equal(live.Fen!.Split(' ')[0], g.Fen.Split(' ')[0]);
        Assert.Equal((LccLiveGame, "2.34", "*"), (g.Id, g.BoardTag, g.Result));
    }

    [Fact]
    public void PickRoom_Index_LatestUpdatedStartedRoom()
    {
        var rooms = ChessComEvents.ParseIndex(Fixture("index-lcc.json"));
        Assert.True(rooms.Count > 3);
        Assert.Equal(Lcc, ChessComEvents.PickRoom(rooms, new DateTime(2026, 11, 28, 13, 0, 0, DateTimeKind.Utc))!.Slug);
    }

    // ── Quelle: Abfrage im Takt ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundPgn_OneRoomCall_FetchesStartedGamesOnly_Cached()
    {
        var src = Source();
        var pgn = await src.GetRoundPgnAsync(WcRound3, default);
        Assert.Contains("1. e4", pgn);
        var again = await src.GetRoundPgnAsync(WcRound3, default);
        Assert.Equal(pgn, again);
        Assert.Equal(1, _api.CountOf("room/"));                                   // Raum im Takt gecacht
        Assert.Equal(1, _api.CountOf("game/"));                                   // Züge nur einmal (gleiche Revision)
        // Runde 5 hat noch nicht begonnen: kein Partie-Abruf, Grundstellung.
        var r5 = await src.GetRoundPgnAsync("cc~" + Wc + "~05", default);
        Assert.Equal(1, _api.CountOf("game/"));
        Assert.Equal(0, LiveGameParser.Parse(r5!)!.Plies);
        Assert.Null(await src.GetRoundPgnAsync("cc~" + Wc + "~99", default));     // unbekannte Runde
    }

    [Fact]
    public async Task RoundPgn_BudgetPerCall_WantedGamesFirst()
    {
        var src = Source(perTick: 1, perSnapshot: 1);
        src.WantedGames = r => r == LccRound2 ? new HashSet<string> { LccLiveGame } : null;
        await src.GetRoundPgnAsync(LccRound2, default);
        Assert.Equal([$"game/{Lcc}/02/{LccLiveGame}"], _api.Calls.Where(c => c.StartsWith("game/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Stream_SendsChangedGamesOnly_EndsWhenRoundFinishedAndComplete()
    {
        var src = Source();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var chunks = new List<string>();
        await foreach (var c in src.StreamRoundAsync(WcRound3, cts.Token)) chunks.Add(c);   // fertige Runde: endet selbst
        Assert.Equal("", chunks[0]);                                              // verbunden
        Assert.Contains("[%clk", chunks[1]);
        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public async Task Stream_LiveRound_RepeatsOnlyHeartbeatWithoutChange()
    {
        var src = Source();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var chunks = new List<string>();
        await foreach (var c in src.StreamRoundAsync(LccRound2, cts.Token))
        {
            chunks.Add(c);
            if (chunks.Count == 4) break;
        }
        Assert.Contains(LccLiveGame, chunks[1]);
        Assert.Equal(["", ""], chunks.Skip(2));                                  // nichts geändert → nur Lebenszeichen
        Assert.True(_api.CountOf("game/") <= 6 + 1);                              // gedeckelt; bekannte Revision wird nicht neu geholt
    }

    [Fact]
    public async Task Stream_TwoFailuresBackOffAndRecover_ThirdInARowGivesUp()
    {
        var src = Source();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _api.FailNext = 2;
        var chunks = new List<string>();
        await foreach (var c in src.StreamRoundAsync(WcRound3, cts.Token)) chunks.Add(c);
        Assert.Contains("[%clk", chunks[^1]);                                     // nach zwei Fehlern weiter

        var src2 = Source();
        _api.FailNext = 3;
        await Assert.ThrowsAsync<ChessComUnavailableException>(async () =>
        {
            await foreach (var _ in src2.StreamRoundAsync(WcRound3, cts.Token)) { }
        });
    }

    [Fact]
    public async Task Router_ChessComIdsToChessCom_RestToLichess()
    {
        var lichess = new FakeLichessLiveSource();
        lichess.RoundPgn["fnmEB1kH"] = "[White \"A\"]\n\n*";
        var router = new LiveSourceRouter(lichess, Source());
        Assert.Contains("chess.com", await router.GetRoundPgnAsync(WcRound3, default));
        Assert.Equal("[White \"A\"]\n\n*", await router.GetRoundPgnAsync("fnmEB1kH", default));
        Assert.Contains("\"cc~" + Wc + "\"", await router.GetTourAsync("cc~" + Wc, default));
        Assert.Equal(1, lichess.RoundPgnCalls);
    }

    [Fact]
    public async Task Hub_WantedGames_UnionOfFilters_NullWhenSomeoneSeesAll()
    {
        var router = new LiveSourceRouter(new FakeLichessLiveSource(), Source());
        await using var hub = new BroadcastLiveHub(router, null, NullLogger<BroadcastLiveHub>.Instance);
        Assert.Null(hub.WantedGames(WcRound3));
        await using var a = hub.Subscribe(WcRound3, finished: true, wantsEval: false, only: new HashSet<string> { WcGame3 });
        Assert.Equal([WcGame3], hub.WantedGames(WcRound3)!);
        await using var b = hub.Subscribe(WcRound3, finished: true, wantsEval: false);
        Assert.Null(hub.WantedGames(WcRound3));
    }

    [Fact]
    public async Task Hub_ChessComRound_SnapshotWithMovesAndClocks()
    {
        var router = new LiveSourceRouter(new FakeLichessLiveSource(), Source());
        await using var hub = new BroadcastLiveHub(router, null, NullLogger<BroadcastLiveHub>.Instance);
        await using var sub = hub.Subscribe(WcRound3, finished: false, wantsEval: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        LiveSnapshot? snap = null;
        while (snap is null) if (await sub.Events.ReadAsync(cts.Token) is LiveSnapshot s) snap = s;
        var g = Assert.Single(snap.Games);
        Assert.Equal((WcGame3, 120, 847), (g.Id, g.Plies, g.White.ClockSeconds));
        Assert.Equal("finished", snap.State);
        Assert.Contains("[%clk", hub.PeekPgn(WcRound3, WcGame3));
    }

    // ── Auswahl-Endpunkt, PGN, Verknüpfen ───────────────────────────────────────────────────────────────────────────

    private ExternalRoundRegistry Registry(ChessComLiveSource src) =>
        new(new LiveSourceRouter(new FakeLichessLiveSource(), src), _cache, NullLogger<ExternalRoundRegistry>.Instance, 10, src);

    private sealed class FakeData : ILiveTournamentData
    {
        public Task<CrTournament?> GetTournamentAsync(string id, CancellationToken ct) =>
            Task.FromResult<CrTournament?>(new CrTournament(id, "WM", "24.11.2026", null, 14));
        public Task<IReadOnlyList<CrPlayer>> GetPlayersAsync(string id, CancellationToken ct) => Task.FromResult<IReadOnlyList<CrPlayer>>([]);
        public Task<CrRound> GetRoundAsync(string id, int round, CancellationToken ct) => Task.FromResult(new CrRound(true, [], [], []));
    }

    private LiveBoardsController Controller(ChessComLiveSource src)
    {
        var router = new LiveSourceRouter(new FakeLichessLiveSource(), src);
        return new LiveBoardsController(new TournamentLiveService(_db, new FakeData(),
            new BroadcastFinder(router, _cache, NullLogger<BroadcastFinder>.Instance), router, _cache,
            NullLogger<TournamentLiveService>.Instance, src)) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    [Fact]
    public async Task External_ChessComGameLink_RoundFromRoom_NoMoveFetches()
    {
        var src = Source();
        await using var hub = new BroadcastLiveHub(new LiveSourceRouter(new FakeLichessLiveSource(), src), null, NullLogger<BroadcastLiveHub>.Instance);
        var ok = Assert.IsType<OkObjectResult>(await Controller(src).External(
            $"https://www.chess.com/events/{Lcc}/02/{LccLiveGame}", Registry(src), hub));
        var v = Assert.IsType<ExternalRoundView>(ok.Value);
        Assert.Equal((LccRound2, LccLiveGame, "Round 2", "LCC Swiss"), (v.RoundId, v.GameId, v.RoundName, v.TourName));
        Assert.Equal("https://www.chess.com/events/" + Lcc + "/games", v.Url);
        Assert.Equal(6, v.Games.Count);
        Assert.Equal(0, _api.CountOf("game/"));
    }

    [Fact]
    public async Task External_ChessComEventAndIndexLinks_TakeOngoingRound()
    {
        var src = Source();
        await using var hub = new BroadcastLiveHub(new LiveSourceRouter(new FakeLichessLiveSource(), src), null, NullLogger<BroadcastLiveHub>.Instance);
        var ok = Assert.IsType<OkObjectResult>(await Controller(src).External("https://www.chess.com/events/info/2026-xtx-markets-london-chess-classic",
            Registry(src), hub));
        Assert.Equal(LccRound2, Assert.IsType<ExternalRoundView>(ok.Value).RoundId);
    }

    [Fact]
    public async Task External_ChessComErrors_NotFound_GameNotFound_Unavailable503()
    {
        var src = Source();
        await using var hub = new BroadcastLiveHub(new LiveSourceRouter(new FakeLichessLiveSource(), src), null, NullLogger<BroadcastLiveHub>.Instance);
        Assert.IsType<NotFoundObjectResult>(await Controller(src).External("https://www.chess.com/events/does-not-exist", Registry(src), hub));
        var nf = Assert.IsType<NotFoundObjectResult>(await Controller(src).External($"https://www.chess.com/events/{Wc}/03/Nobody-Here", Registry(src), hub));
        Assert.Contains("live_ext_game_not_found", JsonSerializer.Serialize(nf.Value));
        _api.FailNext = 1;
        var down = Assert.IsType<ObjectResult>(await Controller(Source()).External($"https://www.chess.com/events/{Wc}", Registry(Source()), hub));
        Assert.Equal(503, down.StatusCode);
        Assert.Contains("live_chesscom_unavailable", JsonSerializer.Serialize(down.Value));
    }

    [Fact]
    public async Task GamePgn_ChessCom_FetchesMovesEvenWithoutFeed()
    {
        var src = Source();
        await using var hub = new BroadcastLiveHub(new LiveSourceRouter(new FakeLichessLiveSource(), src), null, NullLogger<BroadcastLiveHub>.Instance);
        var c = Controller(src);
        var res = Assert.IsType<ContentResult>(await c.GamePgn(WcRound3, WcGame3, Registry(src), hub));
        Assert.Contains("60. ", res.Content);
        Assert.IsType<NotFoundObjectResult>(await c.GamePgn(WcRound3, "Nobody-Here", Registry(src), hub));
        Assert.IsType<BadRequestObjectResult>(await c.GamePgn(WcRound3, "a.b", Registry(src), hub));
    }

    [Fact]
    public async Task Link_OwnTournamentToChessComEvent_StatusWithRoundsAndChessComUrl()
    {
        var src = Source();
        var ok = Assert.IsType<OkObjectResult>(await Controller(src).Link("9912345",
            new RookHub.Api.DTOs.Tournaments.LiveBoardLinkRequest { Url = $"https://www.chess.com/events/{Wc}/games" }));
        var st = Assert.IsType<LiveStatus>(ok.Value);
        Assert.Equal(("cc~" + Wc, "https://www.chess.com/events/" + Wc, 14), (st.TourId, st.Url, st.Rounds.Count));
        Assert.Equal("cc~" + Wc, _db.TournamentBroadcastLinks.Single().TourId);
        Assert.IsType<BadRequestObjectResult>(await Controller(src).Link("9912345",
            new RookHub.Api.DTOs.Tournaments.LiveBoardLinkRequest { Url = "https://www.chess.com/play/online" }));
    }
}
