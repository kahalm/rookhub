using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// S4-001: die vier anonym erreichbaren Ergebnis-Endpunkte (Buch-Puzzle-Löser, Tages-Ladder, Hall of Fame,
/// Wochenpost-Ergebnisse) geben die Discord-Verknüpfung (DiscordId/-Username) nur noch an den per
/// Pfad-Signatur ausgewiesenen Bot und an eingeloggte Nutzer heraus. Vertrag mit dem Bot
/// (schach-bot <c>puzzle/rookhub.py</c> <c>_bot_auth_headers</c>): <c>X-Bot-Timestamp</c> = Unix-Sekunden,
/// <c>X-Bot-Signature</c> = <c>"sha256=" + hex(HMAC_SHA256(secret, "&lt;ts&gt;.&lt;path&gt;"))</c>, path ohne Query.
/// </summary>
public class ResultsDiscordAccessTests : IDisposable
{
    private const string Secret = "shared_bot_stats_secret_value";
    private readonly AppDbContext _db;

    public ResultsDiscordAccessTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // --- Aufbau ---------------------------------------------------------------------------

    private static IConfiguration Config(string? secret) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["SchachBot:StatsSecret"] = secret })
        .Build();

    /// <summary>Signatur wie der Bot sie baut — bewusst direkt mit HMACSHA256, nicht über den Server-Helfer.</summary>
    private static string BotSignature(string secret, string ts, string path)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{path}"));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>HttpContext für einen Aufruf auf <paramref name="path"/>: anonym, eingeloggt oder mit Bot-Headern.</summary>
    private static DefaultHttpContext Context(string path, int? userId = null, string? signature = null, string? timestamp = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.User = userId is int uid
            ? new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, uid.ToString()) }, "Test"))
            : new ClaimsPrincipal();
        if (signature != null) ctx.Request.Headers["X-Bot-Signature"] = signature;
        if (timestamp != null) ctx.Request.Headers["X-Bot-Timestamp"] = timestamp;
        return ctx;
    }

    private static DefaultHttpContext Signed(string path, string secret = Secret, string? signedPath = null, string? ts = null)
    {
        var stamp = ts ?? Now();
        return Context(path, signature: BotSignature(secret, stamp, signedPath ?? path), timestamp: stamp);
    }

    private BookPuzzleController BookController(HttpContext ctx, string? secret = Secret) => new(
        new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue()),
        new DailyLeaderboardService(_db), HintTestHelper.Build(_db), new NoOpTaskQueue(), _db,
        config: Config(secret))
    {
        ControllerContext = new ControllerContext { HttpContext = ctx },
    };

    private WeeklyPostController WeeklyController(HttpContext ctx, string? secret = Secret) =>
        new(_db, new WeeklyPostService(_db, NullLogger<WeeklyPostService>.Instance), Config(secret))
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };

    private async Task<AppUser> LinkedUserAsync(string username = "anna", string discordId = "111")
    {
        var u = new AppUser
        {
            Username = username,
            Email = $"{username}@t.com",
            PasswordHash = "h",
            Profile = new UserProfile { DisplayName = username, DiscordId = discordId, DiscordUsername = username + "#disc" },
        };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    /// <summary>Ein gelöstes Buch-Puzzle, das zugleich Tagespuzzle war → füllt Löser, Ladder und Hall of Fame.</summary>
    private async Task<BookPuzzle> SolvedDailyAsync(AppUser user)
    {
        var p = new BookPuzzle
        {
            LineId = "daily.pgn:1", BookFileName = "daily.pgn", Round = "1",
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", Moves = "e7e5 d2d4",
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        var today = DateTime.UtcNow.Date;
        _db.DailyPuzzles.Add(new DailyPuzzle { Date = DateOnly.FromDateTime(today), BookPuzzleId = p.Id, CreatedAt = today });
        _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt
        {
            BookPuzzleId = p.Id, UserId = user.Id, Solved = true, TimeSeconds = 12, AttemptedAt = today.AddHours(1),
        });
        await _db.SaveChangesAsync();
        return p;
    }

    private async Task<int> WeeklyWithAttemptAsync(AppUser user)
    {
        var w = new WeeklyPost
        {
            Title = "Woche", FileName = "w.pgn", PgnContent = "[Event \"x\"]\n\n1. e4 *", PuzzleCount = 1,
            ScheduledAt = DateTime.UtcNow.AddDays(-1),
        };
        _db.WeeklyPosts.Add(w);
        await _db.SaveChangesAsync();
        _db.WeeklyPostAttempts.Add(new WeeklyPostAttempt
        {
            WeeklyPostId = w.Id, UserId = user.Id, PuzzleIndex = 0, Solved = true, TimeSeconds = 30,
        });
        await _db.SaveChangesAsync();
        return w.Id;
    }

    private static T Ok<T>(ActionResult<T> r) where T : class =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(r.Result).Value);

    private static T Ok<T>(IActionResult r) where T : class =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(r).Value);

    // --- Buch-Puzzle-Löser ----------------------------------------------------------------

    [Fact]
    public async Task BookResults_Anonymous_OmitsDiscordButKeepsSolver()
    {
        var anna = await LinkedUserAsync();
        var p = await SolvedDailyAsync(anna);
        var path = $"/api/book-puzzles/{p.Id}/results";

        var res = Ok(await BookController(Context(path)).GetResults(p.Id));

        var solver = Assert.Single(res.Solvers);
        Assert.Equal("anna", solver.Name);
        Assert.Equal(12, solver.TimeSeconds);
        Assert.Null(solver.DiscordId);
        Assert.Null(solver.DiscordUsername);
        Assert.Equal(1, res.SolvedCount);
    }

    [Fact]
    public async Task BookResults_SignedBot_IncludesDiscord()
    {
        var anna = await LinkedUserAsync();
        var p = await SolvedDailyAsync(anna);
        var path = $"/api/book-puzzles/{p.Id}/results";

        var res = Ok(await BookController(Signed(path)).GetResults(p.Id));

        var solver = Assert.Single(res.Solvers);
        Assert.Equal("111", solver.DiscordId);
        Assert.Equal("anna#disc", solver.DiscordUsername);
    }

    [Fact]
    public async Task BookResults_LoggedInUser_IncludesDiscord()
    {
        var anna = await LinkedUserAsync();
        var p = await SolvedDailyAsync(anna);

        var res = Ok(await BookController(Context($"/api/book-puzzles/{p.Id}/results", userId: 99)).GetResults(p.Id));

        Assert.Equal("111", Assert.Single(res.Solvers).DiscordId);
    }

    [Fact]
    public async Task BookResults_SignatureForOtherPuzzle_IsRejected()
    {
        // Die Signatur hängt am Pfad — eine abgefangene Signatur taugt nicht für eine andere Puzzle-ID.
        var anna = await LinkedUserAsync();
        var p = await SolvedDailyAsync(anna);
        var ctx = Signed($"/api/book-puzzles/{p.Id}/results", signedPath: $"/api/book-puzzles/{p.Id + 1}/results");

        var r = await BookController(ctx).GetResults(p.Id);

        Assert.IsType<UnauthorizedResult>(r.Result);
    }

    [Fact]
    public async Task BookResults_WrongSecret_ExpiredTimestamp_OrNoServerSecret_AreRejected()
    {
        var anna = await LinkedUserAsync();
        var p = await SolvedDailyAsync(anna);
        var path = $"/api/book-puzzles/{p.Id}/results";
        var old = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        Assert.IsType<UnauthorizedResult>((await BookController(Signed(path, secret: "falsch")).GetResults(p.Id)).Result);
        Assert.IsType<UnauthorizedResult>((await BookController(Signed(path, ts: old)).GetResults(p.Id)).Result);
        Assert.IsType<UnauthorizedResult>((await BookController(Signed(path), secret: null).GetResults(p.Id)).Result);
    }

    // --- Tages-Ladder + Hall of Fame -------------------------------------------------------

    [Fact]
    public async Task DailyLeaderboard_Anonymous_Redacted_SignedBot_Full()
    {
        var anna = await LinkedUserAsync();
        await SolvedDailyAsync(anna);
        const string path = "/api/book-puzzles/daily/leaderboard";

        var anon = Ok<DailyLadderDto>(await BookController(Context(path)).GetDailyLeaderboard());
        var entry = Assert.Single(anon.Entries);
        Assert.Equal("anna", entry.Name);
        Assert.Null(entry.DiscordId);
        Assert.Null(entry.DiscordUsername);

        var bot = Ok<DailyLadderDto>(await BookController(Signed(path)).GetDailyLeaderboard());
        Assert.Equal("111", Assert.Single(bot.Entries).DiscordId);
    }

    [Fact]
    public async Task DailyLeaderboard_InvalidSignature_IsRejected()
    {
        const string path = "/api/book-puzzles/daily/leaderboard";
        Assert.IsType<UnauthorizedResult>(await BookController(Signed(path, secret: "falsch")).GetDailyLeaderboard());
    }

    [Fact]
    public async Task DailyHallOfFame_Anonymous_RedactsAllLists_SignedBot_Full()
    {
        var anna = await LinkedUserAsync();
        await SolvedDailyAsync(anna);
        const string path = "/api/book-puzzles/daily/hall-of-fame";

        var anon = Ok<DailyHallOfFameDto>(await BookController(Context(path)).GetDailyHallOfFame());
        Assert.All(anon.MostSolved.Concat(anon.MostGolds), e => { Assert.Null(e.DiscordId); Assert.Null(e.DiscordUsername); });
        Assert.NotEmpty(anon.MostSolved);
        Assert.NotNull(anon.Fastest);
        Assert.Null(anon.Fastest!.DiscordId);
        Assert.Null(anon.Fastest.DiscordUsername);

        var bot = Ok<DailyHallOfFameDto>(await BookController(Signed(path)).GetDailyHallOfFame());
        Assert.Equal("111", bot.MostSolved[0].DiscordId);
        Assert.Equal("111", bot.Fastest!.DiscordId);
    }

    // --- Wochenpost-Ergebnisse -------------------------------------------------------------

    [Fact]
    public async Task WeeklyResults_Anonymous_Redacted_SignedBotAndLoggedIn_Full()
    {
        var anna = await LinkedUserAsync();
        var id = await WeeklyWithAttemptAsync(anna);
        var path = $"/api/weekly-posts/{id}/results";

        var anon = Ok(await WeeklyController(Context(path)).GetResults(id));
        var player = Assert.Single(anon.Players);
        Assert.Equal("anna", player.Name);
        Assert.Null(player.DiscordId);
        Assert.Null(player.DiscordUsername);

        Assert.Equal("111", Assert.Single(Ok(await WeeklyController(Signed(path)).GetResults(id)).Players).DiscordId);
        // Die Wochenpost-Bestenliste der App zeigt discordUsername || name — eingeloggt bleibt sie unverändert.
        Assert.Equal("anna#disc",
            Assert.Single(Ok(await WeeklyController(Context(path, userId: 5)).GetResults(id)).Players).DiscordUsername);
    }

    [Fact]
    public async Task WeeklyResults_InvalidSignature_IsRejected()
    {
        var anna = await LinkedUserAsync();
        var id = await WeeklyWithAttemptAsync(anna);

        var r = await WeeklyController(Signed($"/api/weekly-posts/{id}/results", secret: "falsch")).GetResults(id);

        Assert.IsType<UnauthorizedResult>(r.Result);
    }

    // --- Helfer --------------------------------------------------------------------------

    [Fact]
    public void CheckPath_AbsentValidInvalid()
    {
        const string path = "/api/book-puzzles/7/results";
        Assert.Equal(BotSignatureCheck.Absent, BotRequestSignature.CheckPath(Context(path).Request, Secret));
        Assert.Equal(BotSignatureCheck.Absent, BotRequestSignature.CheckPath(null, Secret));
        Assert.Equal(BotSignatureCheck.Valid, BotRequestSignature.CheckPath(Signed(path).Request, Secret));
        // Signatur ohne Timestamp ist wertlos (ewig replaybar).
        Assert.Equal(BotSignatureCheck.Invalid,
            BotRequestSignature.CheckPath(Context(path, signature: BotSignature(Secret, Now(), path)).Request, Secret));
        Assert.Equal(BotSignatureCheck.Invalid, BotRequestSignature.CheckPath(Signed(path).Request, ""));
    }
}
