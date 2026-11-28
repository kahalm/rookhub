using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Tournaments;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Tournaments;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>
/// Geteilte, lebende Live-Ansicht (1.029.0, Wunsch 2026-11-26: „einfach einen Hash — wenn ich Bretter ergänze oder entferne,
/// sollen alle mit dem Link das auch sehen“): anlegen (auch Gast), lesen, nur der Besitzer ändert, offene Ströme folgen ohne
/// zusätzlichen Lichess-Strom, Aufbewahrungsfrist.
/// </summary>
public class SharedLiveViewsTests : IDisposable
{
    private const string Linked = "9904438";
    private const string Unlinked = "9904439";
    private const string RoundId = "L9900001";

    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly AppDbContext _db;
    private readonly FakeLichessLiveSource _src = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly LiveViewChanges _changes = new();

    public SharedLiveViewsTests()
    {
        _db = new AppDbContext(_options);
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

    private readonly FakeData _data = new();

    private SharedLiveViews Views() => new(_db, _changes);

    private LiveBoardsController Controller(HttpContext? ctx = null) =>
        new(new TournamentLiveService(_db, _data, new BroadcastFinder(_src, _cache, NullLogger<BroadcastFinder>.Instance) { Pause = TimeSpan.Zero },
            _src, _cache, NullLogger<TournamentLiveService>.Instance))
        { ControllerContext = new ControllerContext { HttpContext = ctx ?? new DefaultHttpContext() } };

    private static HttpContext WithKey(string? key, int? userId = null)
    {
        var ctx = new DefaultHttpContext();
        if (key is not null) ctx.Request.Headers[LiveBoardsController.ViewKeyHeader] = key;
        if (userId is { } id) ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));
        return ctx;
    }

    private static LiveViewState View(string? team = null, params string[] add) => new() { Round = 1, Team = team, Add = [.. add] };

    private async Task<LiveViewResponse> CreateAsGuest(LiveViewState view)
    {
        var ok = Assert.IsType<OkObjectResult>(await Controller().CreateView(new CreateLiveViewRequest { TournamentId = Linked, View = view }, Views()));
        return Assert.IsType<LiveViewResponse>(ok.Value);
    }

    [Fact]
    public async Task Create_Guest_ShortTokenAndKey_OnlyHashStored()
    {
        var created = await CreateAsGuest(View("SK Schwaz", "2.1", "bad key!", "2.1"));
        Assert.Matches("^[A-Za-z0-9]{10}$", created.Token);
        Assert.Equal(SharedLiveViews.EditKeyLength, created.EditKey!.Length);
        Assert.True(created.CanEdit);
        Assert.Equal(["2.1"], created.View.Add);                                            // geprüft + entdoppelt wie ?add=
        var row = await _db.SharedLiveViews.SingleAsync();
        Assert.Null(row.OwnerUserId);
        Assert.DoesNotContain(created.EditKey, row.EditKeyHash);
        Assert.Equal(64, row.EditKeyHash.Length);
    }

    [Fact]
    public async Task Create_UnlinkedOrInvalidTournament_Rejected()
    {
        Assert.IsType<NotFoundObjectResult>(await Controller().CreateView(new CreateLiveViewRequest { TournamentId = Unlinked, View = View() }, Views()));
        Assert.IsType<BadRequestObjectResult>(await Controller().CreateView(new CreateLiveViewRequest { TournamentId = "../x" }, Views()));
        Assert.Equal(0, await _db.SharedLiveViews.CountAsync());
    }

    [Fact]
    public async Task Get_Anonymous_CanEditOnlyWithKey()
    {
        var created = await CreateAsGuest(View("SK Schwaz"));
        var anon = Assert.IsType<LiveViewResponse>(Assert.IsType<OkObjectResult>(await Controller().GetView(created.Token, Views())).Value);
        Assert.False(anon.CanEdit);
        Assert.Null(anon.EditKey);
        Assert.Equal("SK Schwaz", anon.View.Team);
        var owner = Assert.IsType<LiveViewResponse>(Assert.IsType<OkObjectResult>(
            await Controller(WithKey(created.EditKey)).GetView(created.Token, Views())).Value);
        Assert.True(owner.CanEdit);
        Assert.IsType<NotFoundObjectResult>(await Controller().GetView("Zz3dEf7hJk", Views()));
        Assert.IsType<NotFoundObjectResult>(await Controller().GetView("../../etc", Views()));
    }

    [Fact]
    public async Task Update_OnlyOwner_KeyOrAccount()
    {
        var created = await CreateAsGuest(View("SK Schwaz"));
        var denied = Assert.IsType<ObjectResult>(await Controller().UpdateView(created.Token, new UpdateLiveViewRequest { View = View("X") }, Views()));
        Assert.Equal(403, denied.StatusCode);
        denied = Assert.IsType<ObjectResult>(await Controller(WithKey("falsch")).UpdateView(created.Token, new UpdateLiveViewRequest { View = View("X") }, Views()));
        Assert.Equal(403, denied.StatusCode);
        var ok = Assert.IsType<OkObjectResult>(await Controller(WithKey(created.EditKey))
            .UpdateView(created.Token, new UpdateLiveViewRequest { View = new LiveViewState { Round = 0, Team = "  Pinggau-Friedberg " } }, Views()));
        var v = Assert.IsType<LiveViewResponse>(ok.Value).View;
        Assert.Equal((null, "Pinggau-Friedberg"), (v.Round, v.Team));                       // Runde 0 → laufende Runde

        // Angemeldet angelegt: das Konto darf ohne Schlüssel, ein anderes Konto nicht.
        var mine = Assert.IsType<LiveViewResponse>(Assert.IsType<OkObjectResult>(await Controller(WithKey(null, 7))
            .CreateView(new CreateLiveViewRequest { TournamentId = Linked, View = View() }, Views())).Value);
        Assert.Equal(7, (await _db.SharedLiveViews.SingleAsync(x => x.Token == mine.Token)).OwnerUserId);
        Assert.IsType<OkObjectResult>(await Controller(WithKey(null, 7)).UpdateView(mine.Token, new UpdateLiveViewRequest { View = View("A") }, Views()));
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Controller(WithKey(null, 8))
            .UpdateView(mine.Token, new UpdateLiveViewRequest { View = View("B") }, Views())).StatusCode);
    }

    [Fact]
    public async Task Update_WakesWatchers_UnchangedDoesNot()
    {
        var created = await CreateAsGuest(View("SK Schwaz"));
        using var watch = _changes.Watch(created.Token);
        var first = watch.Current;
        await Views().UpdateAsync(created.Token, View("SK Schwaz"), null, created.EditKey, default);   // gleich → kein Wecken
        Assert.False(first.IsCancellationRequested);
        await Views().UpdateAsync(created.Token, View("SK Schwaz", "1.2"), null, created.EditKey, default);
        Assert.True(first.IsCancellationRequested);
        Assert.False(watch.Current.IsCancellationRequested);                                // neues Zeichen für die nächste Änderung
        watch.Dispose();
        Assert.Equal(0, _changes.Watched);
    }

    [Fact]
    public async Task ViewStream_FollowsOwnerChange_WithoutSecondLichessStream()
    {
        var created = await CreateAsGuest(View("SK Schwaz"));
        var body = new RecordingStream();
        using var abort = new CancellationTokenSource();
        var ctx = new DefaultHttpContext { RequestAborted = abort.Token };
        ctx.Response.Body = body;
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        var run = Controller(ctx).ViewStream(created.Token, Views(), _changes, hub, new SseConnectionLimiter(), _data);
        for (var i = 0; i < 250 && !body.Text.Contains("event: snapshot"); i++) await Task.Delay(20);
        Assert.StartsWith("text/event-stream", ctx.Response.ContentType);
        Assert.Contains("event: view", body.Text);
        Assert.Contains("\"team\":\"SK Schwaz\"", body.Text);

        // Besitzer ändert über seine eigene Anfrage (eigener DbContext): Zuschauer bekommt view + neuen snapshot.
        await using (var other = new AppDbContext(_options))
            await new SharedLiveViews(other, _changes).UpdateAsync(created.Token, View("Pinggau-Friedberg", "1.1"), null, created.EditKey, default);
        for (var i = 0; i < 250 && Count(body.Text, "event: snapshot") < 2; i++) await Task.Delay(20);
        abort.Cancel();
        await run;
        Assert.Equal(2, Count(body.Text, "event: view"));
        Assert.Equal(2, Count(body.Text, "event: snapshot"));
        Assert.Contains("\"team\":\"Pinggau-Friedberg\"", body.Text);
        Assert.Equal(1, Count(body.Text, "retry: 5000"));
        Assert.Equal(1, _src.StreamsOpened);                                               // derselbe Lichess-Strom, nur umgehängt
        Assert.Equal(0, _changes.Watched);
    }

    [Fact]
    public async Task ViewStream_Unknown_404()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        await Controller(ctx).ViewStream("Zz3dEf7hJk", Views(), _changes, hub, new SseConnectionLimiter(), _data);
        Assert.Equal(404, ctx.Response.StatusCode);
        Assert.Equal(0, hub.OpenFeeds);
    }

    [Fact]
    public async Task Retention_UnviewedAfterDays_AndCap()
    {
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
            _db.SharedLiveViews.Add(new SharedLiveView { Token = $"Tok{i}567890", CrawlerTournamentId = Linked, EditKeyHash = "h",
                LastViewedAt = now.AddDays(-i * 10) });        // 0, 10, 20, 30, 40 Tage
        await _db.SaveChangesAsync();
        var removed = await SharedLiveViews.PruneAsync(_db, now, new RetentionOptions { LiveViewDays = 30, LiveViewCap = 2 });
        Assert.Equal(3, removed);                               // 40 Tage (Frist), dann über dem Deckel die älteste (20)
        Assert.Equal(["Tok0567890", "Tok1567890"], await _db.SharedLiveViews.OrderBy(v => v.Token).Select(v => v.Token).ToListAsync());
        Assert.Equal(30, RetentionOptions.From(null).LiveViewDays);
    }

    [Fact]
    public async Task Get_TouchesLastViewedAtAtMostHourly()
    {
        var created = await CreateAsGuest(View());
        var views = Views();
        var t0 = DateTime.UtcNow;
        views.Clock = () => t0.AddMinutes(30);
        await views.GetAsync(created.Token, default);
        var row = await _db.SharedLiveViews.AsNoTracking().SingleAsync();
        Assert.True(row.LastViewedAt < t0.AddMinutes(1));
        views.Clock = () => t0.AddHours(2);
        await views.GetAsync(created.Token, default);
        Assert.Equal(t0.AddHours(2), (await _db.SharedLiveViews.AsNoTracking().SingleAsync()).LastViewedAt);
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
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

    /// <summary>Wunsch 2026-11-28: geteilt ist nur die Brettauswahl — ein <c>eval</c> aus Zeilen vor 1.039.0 wird übergangen,
    /// beim Speichern fällt es weg (jeder Zuschauer schaltet den Balken selbst).</summary>
    [Fact]
    public async Task Parse_OldRowWithEval_IgnoredAndNotStored()
    {
        var v = SharedLiveViews.Parse("{\"round\":2,\"team\":\"SK Schwaz\",\"add\":[\"1.2\"],\"ext\":[],\"eval\":true}");
        Assert.Equal((2, "SK Schwaz", "1.2"), (v.Round, v.Team, Assert.Single(v.Add)));
        var created = await CreateAsGuest(View("SK Schwaz"));
        var row = await _db.SharedLiveViews.SingleAsync(x => x.Token == created.Token);
        Assert.DoesNotContain("eval", row.ViewJson, StringComparison.OrdinalIgnoreCase);
    }
}
