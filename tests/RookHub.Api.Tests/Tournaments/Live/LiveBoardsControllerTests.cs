using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Tournaments;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Tournaments;
using RookHub.Api.Models;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>Zugriff: Gäste dürfen lesen und zuschauen, aber nur Runden einer VERKNÜPFTEN Übertragung; Verknüpfen braucht ein Konto.</summary>
public class LiveBoardsControllerTests : IDisposable
{
    private const string Linked = "9904438";
    private const string Unlinked = "9904439";
    private const string RoundId = "L9900001";

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeLichessLiveSource _src = new();
    private readonly FakeData _data = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public LiveBoardsControllerTests()
    {
        _db.TournamentBroadcastLinks.Add(new TournamentBroadcastLink { CrawlerTournamentId = Linked, TourId = "IjDgZVUR", TourName = "BL" });
        _db.SaveChanges();
        _src.Tours["IjDgZVUR"] = $$"""{"tour":{"id":"IjDgZVUR","name":"1. Bundesliga Austria"},"rounds":[{"id":"{{RoundId}}","name":"Round 1","ongoing":true}]}""";
        _src.RoundPgn[RoundId] = LivePgn.Game("g1", "Aditya Mittal", "Hagner, Bennet", "1. d4 { [%clk 1:30:59] }",
            whiteFide: "35042025", blackFide: "16224477", whiteTeam: "Pinggau-Friedberg", blackTeam: "Schachklub Schwaz");
        _src.Top = """{"active":[]}""";
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private sealed class FakeData : ILiveTournamentData
    {
        public Task<CrTournament?> GetTournamentAsync(string id, CancellationToken ct) =>
            Task.FromResult<CrTournament?>(id is Linked or Unlinked ? new CrTournament(id, "Turnier", "01.01.2020", null, 9) : null);
        public Task<IReadOnlyList<CrPlayer>> GetPlayersAsync(string id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CrPlayer>>([new CrPlayer("Aditya, Mittal", "35042025", 2616, "Pinggau-Friedberg", 2),
                new CrPlayer("Hagner, Bennet", "16224477", 2462, "SK Schwaz", 1)]);
        public async Task<CrRound> GetRoundAsync(string id, int round, CancellationToken ct) =>
            new(true, await GetPlayersAsync(id, ct), [], [new CrMatch(1, "Pinggau-Friedberg", "SK Schwaz", null, null)]);
    }

    private TournamentLiveService Service() =>
        new(_db, _data, new BroadcastFinder(_src, _cache, NullLogger<BroadcastFinder>.Instance) { Pause = TimeSpan.Zero }, _src, _cache,
            NullLogger<TournamentLiveService>.Instance);

    private LiveBoardsController Controller(HttpContext? ctx = null) =>
        new(Service()) { ControllerContext = new ControllerContext { HttpContext = ctx ?? new DefaultHttpContext() } };

    [Fact]
    public async Task Status_Guest_LinkedTournament_RoundsMapped()
    {
        var ok = Assert.IsType<OkObjectResult>(await Controller().Status(Linked));
        var s = Assert.IsType<LiveStatus>(ok.Value);
        Assert.True(s.Linked);
        Assert.Equal((1, RoundId), (s.Rounds.Single().Round, s.Rounds.Single().LichessRoundId));
        Assert.Equal(1, s.CurrentRound);
    }

    [Fact]
    public async Task Status_NotLinked_OldTournament_NotLinkedWithoutSearch()
    {
        var s = Assert.IsType<LiveStatus>(Assert.IsType<OkObjectResult>(await Controller().Status(Unlinked)).Value);
        Assert.False(s.Linked);
        Assert.Equal(0, _src.SearchCalls);
        Assert.IsType<NotFoundObjectResult>(await Controller().Status("9900000"));
        Assert.IsType<BadRequestObjectResult>(await Controller().Status("../x"));
    }

    [Fact]
    public async Task Stream_UnlinkedTournament_404_NoLichessStream()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        await Controller(ctx).Stream(Unlinked, 1, false, hub, new SseConnectionLimiter(), _data);
        Assert.Equal(404, ctx.Response.StatusCode);
        Assert.Equal(0, hub.OpenFeeds);
        Assert.Equal(0, _src.RoundPgnCalls);
    }

    [Fact]
    public async Task Stream_Linked_SendsSnapshotWithBoards_ThenPing()
    {
        var body = new RecordingStream();
        using var abort = new CancellationTokenSource();
        var ctx = new DefaultHttpContext { RequestAborted = abort.Token };
        ctx.Response.Body = body;
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        var run = Controller(ctx).Stream(Linked, 1, false, hub, new SseConnectionLimiter(), _data);
        for (var i = 0; i < 250 && !body.Text.Contains("event: snapshot"); i++) await Task.Delay(20);
        abort.Cancel();
        await run;
        Assert.StartsWith("text/event-stream", ctx.Response.ContentType);
        Assert.Equal("no", ctx.Response.Headers["X-Accel-Buffering"]);
        Assert.Contains("event: snapshot", body.Text);
        Assert.Contains("\"gameId\":\"g1\"", body.Text);
        Assert.Contains("\"homeIsWhite\":true", body.Text);
    }

    [Fact]
    public async Task Stream_TooManyPerAddress_429()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var limiter = new SseConnectionLimiter(maxPerKey: 1);
        using var held = limiter.TryAcquire("unknown");
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        await Controller(ctx).Stream(Linked, 1, false, hub, limiter, _data);
        Assert.Equal(429, ctx.Response.StatusCode);
        Assert.Equal(0, hub.OpenFeeds);
    }

    [Fact]
    public async Task Link_InvalidUrl_400_ValidRoundLink_SavesManual()
    {
        Assert.IsType<BadRequestObjectResult>(await Controller().Link(Unlinked, new LiveBoardLinkRequest { Url = "https://example.com/x" }));
        _src.ToursOfRound[RoundId] = _src.Tours["IjDgZVUR"];
        var ok = Assert.IsType<OkObjectResult>(await Controller().Link(Unlinked,
            new LiveBoardLinkRequest { Url = $"https://lichess.org/broadcast/1-bundesliga-austria-20262027/round-1/{RoundId}" }));
        Assert.True(Assert.IsType<LiveStatus>(ok.Value).Manual);
        Assert.True((await _db.TournamentBroadcastLinks.SingleAsync(l => l.CrawlerTournamentId == Unlinked)).Manual);
    }

    [Fact]
    public void Attributes_ReadAnonymous_LinkNeedsAccount()
    {
        static bool Anon(string m) => typeof(LiveBoardsController).GetMethod(m)!.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
        Assert.True(Anon(nameof(LiveBoardsController.Status)));
        Assert.True(Anon(nameof(LiveBoardsController.Stream)));
        Assert.False(Anon(nameof(LiveBoardsController.Link)));
        Assert.False(Anon(nameof(LiveBoardsController.Unlink)));
        Assert.NotNull(typeof(LiveBoardsController).GetCustomAttribute<AuthorizeAttribute>());
    }

    /// <summary>Hält mit, was geschrieben wurde (die SSE-Schleife schreibt aus einem anderen Thread).</summary>
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
