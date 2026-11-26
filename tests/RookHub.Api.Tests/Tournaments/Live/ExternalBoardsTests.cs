using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Tournaments;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>
/// Zusätzliche Bretter aus FREMDEN Übertragungen (1.025.0, Wunsch 2026-11-26: Schwaz-Kampf + WM-Partie auf einer Seite):
/// Link-Prüfung, Grenzen, Lichess-Prüfung der Runde (gecacht), der gebündelte Strom (EINE Verbindung, nur die gewählten
/// Partien, Bewertung nur für sie) und der Auswahl-Endpunkt.
/// </summary>
public class ExternalBoardsTests : IDisposable
{
    private const string Linked = "9904438";
    private const string OwnRound = "L9900001";
    private const string WmRound = "fnmEB1kH";
    private const string WmGame = "RD8sbYw1";
    private const string WmLink = "https://lichess.org/broadcast/fide-world-chess-championship-2026/game-3/fnmEB1kH/RD8sbYw1";

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeLichessLiveSource _src = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public ExternalBoardsTests()
    {
        _db.TournamentBroadcastLinks.Add(new TournamentBroadcastLink { CrawlerTournamentId = Linked, TourId = "IjDgZVUR", TourName = "BL" });
        _db.SaveChanges();
        _src.Tours["IjDgZVUR"] = $$"""{"tour":{"id":"IjDgZVUR","name":"1. Bundesliga Austria"},"rounds":[{"id":"{{OwnRound}}","name":"Round 1","ongoing":true}]}""";
        _src.RoundPgn[OwnRound] = LivePgn.Game("g1", "Aditya Mittal", "Hagner, Bennet", "1. d4 { [%clk 1:30:59] }",
            whiteTeam: "Pinggau-Friedberg", blackTeam: "SK Schwaz");
        var wmTour = $$"""{"tour":{"id":"WmTour01","name":"FIDE World Championship 2026"},"rounds":[{"id":"{{WmRound}}","name":"Game 3","ongoing":true}]}""";
        _src.ToursOfRound[WmRound] = wmTour;
        _src.Tours["WmTour01"] = wmTour;
        _src.RoundPgn[WmRound] = LivePgn.Game(WmGame, "Gukesh D", "Carlsen, Magnus", "1. e4 { [%clk 1:59:00] }")
                                 + LivePgn.Game("Other001", "X", "Y", "1. d4");
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private ExternalRoundRegistry Registry(int max = 10) => new(_src, _cache, NullLogger<ExternalRoundRegistry>.Instance, max);

    private sealed class FakeData : ILiveTournamentData
    {
        public Task<CrTournament?> GetTournamentAsync(string id, CancellationToken ct) =>
            Task.FromResult<CrTournament?>(new CrTournament(id, "Turnier", "01.01.2020", null, 9));
        public Task<IReadOnlyList<CrPlayer>> GetPlayersAsync(string id, CancellationToken ct) => Task.FromResult<IReadOnlyList<CrPlayer>>([]);
        public Task<CrRound> GetRoundAsync(string id, int round, CancellationToken ct) =>
            Task.FromResult(new CrRound(true, [], [], [new CrMatch(1, "Pinggau-Friedberg", "SK Schwaz", null, null)]));
    }

    private LiveBoardsController Controller(HttpContext? ctx = null) =>
        new(new TournamentLiveService(_db, new FakeData(), new BroadcastFinder(_src, _cache, NullLogger<BroadcastFinder>.Instance), _src, _cache,
            NullLogger<TournamentLiveService>.Instance)) { ControllerContext = new ControllerContext { HttpContext = ctx ?? new DefaultHttpContext() } };

    // ── Link-Prüfung ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(WmLink, null, WmRound, WmGame)]
    [InlineData("https://lichess.org/broadcast/fide-world-chess-championship-2026/game-3/fnmEB1kH", null, WmRound, null)]
    [InlineData("lichess.org/broadcast/fide-world-chess-championship-2026/WmTour01", "WmTour01", null, null)]
    [InlineData("https://www.lichess.org/broadcast/x/game-3/fnmEB1kH/RD8sbYw1?embed=1#last", null, WmRound, WmGame)]
    public void BoardLinkOf_ValidLinks(string url, string? tour, string? round, string? game) =>
        Assert.Equal((tour, round, game), LichessBroadcasts.BoardLinkOf(url));

    [Theory]
    [InlineData("fnmEB1kH")]                                                                  // nackte Kennung: kein Link
    [InlineData("https://lichess.org/broadcast/x/game-3/fnmEB1k/RD8sbYw1")]                    // Runde 7 Zeichen
    [InlineData("https://lichess.org/broadcast/x/game-3/fnmEB1kH/RD8sb-w1")]                   // Partie mit Strich
    [InlineData("https://lichess.org/study/fnmEB1kH/RD8sbYw1")]                                // keine Übertragung
    [InlineData("https://evil.example/?u=lichess.org/broadcast/x/game-3/fnmEB1kH/RD8sbYw1")]    // fremder Wirt
    [InlineData("")]
    [InlineData(null)]
    public void BoardLinkOf_Rejects(string? url) => Assert.Null(LichessBroadcasts.BoardLinkOf(url));

    [Fact]
    public void ParseRefs_ValidDistinct_CappedRoundsAndBoards()
    {
        Assert.Equal([new ExternalBoardRef(WmRound, WmGame)],
            ExternalRoundRegistry.ParseRefs($"{WmRound}.{WmGame}, {WmRound}.{WmGame},bad,{WmRound}.short,../x"));
        // Fünf Runden → nur die ersten vier
        var five = string.Join(',', Enumerable.Range(1, 5).Select(i => $"Round00{i}.Game0001"));
        Assert.Equal(ExternalRoundRegistry.MaxRoundsPerStream, ExternalRoundRegistry.ParseRefs(five).Select(r => r.RoundId).Distinct().Count());
        var many = string.Join(',', Enumerable.Range(10, 20).Select(i => $"{WmRound}.Game00{i}"));
        Assert.Equal(ExternalRoundRegistry.MaxBoardsPerStream, ExternalRoundRegistry.ParseRefs(many).Count);
    }

    // ── Grenzen + Lichess-Prüfung ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_KnownRound_Info_UnknownCached_OneLichessCall()
    {
        var reg = Registry();
        var info = await reg.ResolveAsync(WmRound, default);
        Assert.Equal(("FIDE World Championship 2026", "Game 3", true), (info!.TourName, info.RoundName, info.Ongoing));
        Assert.Null(await reg.ResolveAsync("Unknown1", default));
        Assert.Null(await reg.ResolveAsync("Unknown1", default));
        Assert.Null(await reg.ResolveAsync("../etc", default));
        Assert.Equal(2, _src.TourOfRoundCalls);                       // je Kennung höchstens ein Abruf, die ungültige gar keinen
    }

    [Fact]
    public void TryWatch_ServerwideCapOnDistinctRounds_SameRoundShares()
    {
        var reg = Registry(max: 2);
        using var a = reg.TryWatch("RoundAAA");
        using var a2 = reg.TryWatch("RoundAAA");                     // dieselbe Runde kostet keinen Platz
        var b = reg.TryWatch("RoundBBB");
        Assert.NotNull(a2);
        Assert.NotNull(b);
        Assert.Null(reg.TryWatch("RoundCCC"));
        b!.Dispose();
        using var c = reg.TryWatch("RoundCCC");
        Assert.NotNull(c);
        Assert.Equal(2, reg.WatchedRounds);
    }

    // ── Hub-Filter ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hub_FilteredSubscription_OnlyWantedGames_EvalOnlyForThem()
    {
        var fen = LiveGameParser.Parse(LivePgn.Game(WmGame, "Gukesh D", "Carlsen, Magnus", "1. e4 { [%clk 1:59:00] }"))!.Fen;
        _src.CloudEvals[fen] = """{"depth":30,"pvs":[{"cp":25}]}""";
        _src.RoundPgn[WmRound] = LivePgn.Game(WmGame, "Gukesh D", "Carlsen, Magnus", "1. e4 { [%clk 1:59:00] }")
                                 + LivePgn.Game("Other001", "X", "Y", "1. d4 { [%clk 1:59:00] }");
        var cloud = new LichessCloudEval(_src, new MemoryCache(new MemoryCacheOptions()), NullLogger<LichessCloudEval>.Instance) { Spacing = TimeSpan.Zero };
        await using var hub = new BroadcastLiveHub(_src, cloud, NullLogger<BroadcastLiveHub>.Instance);
        await using var sub = hub.Subscribe(WmRound, false, wantsEval: true, only: new HashSet<string> { WmGame });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        LiveSnapshot? snap = null;
        LiveGameChanged? evald = null;
        while (snap is null || evald is null)
        {
            switch (await sub.Events.ReadAsync(cts.Token))
            {
                case LiveSnapshot s: snap = s; break;
                case LiveGameChanged c: Assert.Equal(WmGame, c.Game.Id); if (c.Game.Eval is not null) evald = c; break;
            }
        }
        Assert.Equal(WmGame, Assert.Single(snap.Games).Id);
        await _src.StreamOf(WmRound).Writer.WriteAsync(LivePgn.Game("Other001", "X", "Y", "1. d4 { [%clk 1:59:00] } 1... d5"));
        await Task.Delay(150);
        Assert.False(sub.Events.TryRead(out var stray) && stray is LiveGameChanged { Game.Id: "Other001" });
        Assert.Equal(1, _src.CloudEvalCalls);                          // nur die gewählte Partie wurde bewertet
    }

    // ── Strom ───────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stream_WithExtra_OneConnection_ExtSnapshotOnlyWantedGame_UnknownRoundError()
    {
        var body = new RecordingStream();
        using var abort = new CancellationTokenSource();
        var ctx = new DefaultHttpContext { RequestAborted = abort.Token };
        ctx.Response.Body = body;
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        var reg = Registry();
        var run = Controller(ctx).Stream(Linked, 1, false, hub, new SseConnectionLimiter(), new FakeData(), reg,
            $"{WmRound}.{WmGame},Unknown1.Game0001");
        for (var i = 0; i < 250 && !(body.Text.Contains("event: snapshot") && body.Text.Contains("event: ext-snapshot")); i++) await Task.Delay(20);
        Assert.Equal(1, reg.WatchedRounds);
        abort.Cancel();
        await run;

        Assert.Contains("event: snapshot", body.Text);
        Assert.Contains("event: ext-error\ndata: {\"round\":\"Unknown1\",\"code\":\"live_ext_not_found\"}", body.Text);
        var ext = body.Text[body.Text.IndexOf("event: ext-snapshot", StringComparison.Ordinal)..];
        ext = ext[..ext.IndexOf("\n\n", StringComparison.Ordinal)];
        Assert.Contains($"\"round\":\"{WmRound}\"", ext);
        Assert.Contains("\"tourName\":\"FIDE World Championship 2026\"", ext);
        Assert.Contains($"\"id\":\"{WmGame}\"", ext);
        Assert.DoesNotContain("Other001", ext);
        Assert.Equal(0, reg.WatchedRounds);                            // Verbindung zu → Platz frei
        Assert.Equal(0, hub.Viewers(WmRound));
    }

    [Fact]
    public async Task Stream_ServerwideLimitReached_ExtErrorLimit_OwnRoundStillStreams()
    {
        var body = new RecordingStream();
        using var abort = new CancellationTokenSource();
        var ctx = new DefaultHttpContext { RequestAborted = abort.Token };
        ctx.Response.Body = body;
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        var reg = Registry(max: 0);
        var run = Controller(ctx).Stream(Linked, 1, false, hub, new SseConnectionLimiter(), new FakeData(), reg, $"{WmRound}.{WmGame}");
        for (var i = 0; i < 250 && !body.Text.Contains("event: snapshot"); i++) await Task.Delay(20);
        abort.Cancel();
        await run;
        Assert.Contains("\"code\":\"live_ext_limit\"", body.Text);
        Assert.DoesNotContain("ext-snapshot", body.Text);
        Assert.DoesNotContain(WmRound, _src.StreamedRounds);
    }

    // ── Auswahl-Endpunkt ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task External_GameLink_RoundWithGames_Anonymous()
    {
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        var ok = Assert.IsType<OkObjectResult>(await Controller().External(WmLink, Registry(), hub));
        var v = Assert.IsType<ExternalRoundView>(ok.Value);
        Assert.Equal((WmRound, WmGame, "Game 3"), (v.RoundId, v.GameId, v.RoundName));
        Assert.Equal([WmGame, "Other001"], v.Games.Select(g => g.Id));
        Assert.NotNull(typeof(LiveBoardsController).GetMethod(nameof(LiveBoardsController.External))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), false).SingleOrDefault());
        Assert.Equal(0, hub.OpenFeeds);                                // die Auswahl öffnet keinen Strom
    }

    [Fact]
    public async Task External_TourLink_TakesOngoingRound()
    {
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        var ok = Assert.IsType<OkObjectResult>(await Controller().External("https://lichess.org/broadcast/wm/WmTour01", Registry(), hub));
        Assert.Equal(WmRound, Assert.IsType<ExternalRoundView>(ok.Value).RoundId);
    }

    [Fact]
    public async Task External_Errors_InvalidUrl400_UnknownRound404_UnknownGame404()
    {
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        Assert.IsType<BadRequestObjectResult>(await Controller().External("https://example.com/broadcast/x/y/fnmEB1kH", Registry(), hub));
        Assert.IsType<NotFoundObjectResult>(await Controller().External("https://lichess.org/broadcast/x/y/Unknown1", Registry(), hub));
        var nf = Assert.IsType<NotFoundObjectResult>(await Controller().External(
            "https://lichess.org/broadcast/x/game-3/fnmEB1kH/Missing1", Registry(), hub));
        Assert.Contains("live_ext_game_not_found", System.Text.Json.JsonSerializer.Serialize(nf.Value));
    }

    private sealed class RecordingStream : MemoryStream
    {
        private readonly StringBuilder _sb = new();
        public string Text { get { lock (_sb) return _sb.ToString(); } }
        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_sb) _sb.Append(Encoding.UTF8.GetString(buffer, offset, count));
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            lock (_sb) _sb.Append(Encoding.UTF8.GetString(buffer.Span));
            return ValueTask.CompletedTask;
        }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
