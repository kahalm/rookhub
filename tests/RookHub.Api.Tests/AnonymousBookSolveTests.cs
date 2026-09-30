using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// A2-003: der anonyme Tagespuzzle-Solve (<c>POST /api/book-puzzles/{id}/attempt/anonymous</c>) und „Track solves"
/// (<c>POST {id}/track</c>) nahmen ohne eigenes Limit je frischer Session-Id einen Solve an, und jeder anonyme
/// Solve löste eine Löser-Aggregation + einen Webhook aus, auf den hin der Bot jeden Tagespuzzle-Post editiert.
/// Jetzt: Named-Limit wie die übrigen anonymen Puzzle-Senken, Solves nur für anonym lesbare Bücher bzw.
/// Tagespuzzles, und der Webhook anonymer Solves ist je Puzzle entprellt (eine Nachmeldung trägt den Rest).
/// </summary>
public class AnonymousBookSolveTests : IDisposable
{
    private readonly AppDbContext _db;

    public AnonymousBookSolveTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Uhr, die nur auf Zuruf weiterläuft (Timer laufen weiter in Echtzeit).</summary>
    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string Session() => Guid.NewGuid().ToString("N");

    private async Task<BookPuzzle> PuzzleInBookAsync(bool exposed, int? ownerUserId = null)
    {
        var book = new Book
        {
            FileName = $"b-{Guid.NewGuid():N}.pgn", DisplayName = "Buch", OwnerUserId = ownerUserId,
            ForDaily = exposed, Source = new BookSource(),
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var p = new BookPuzzle
        {
            LineId = book.FileName + ":1", BookFileName = book.FileName, BookId = book.Id, Round = "1",
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", Moves = "e7e5 d2d4",
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private BookPuzzleController AnonymousController() => new(
        new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue()),
        new DailyLeaderboardService(_db), HintTestHelper.Build(_db), new NoOpTaskQueue(), _db)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    // --- Named-Limit ------------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(BookPuzzleController.RecordAnonymousAttempt))]
    [InlineData(nameof(BookPuzzleController.Track))]
    public void AnonymousWrites_UseTheAnonymousWriteLimiter(string action)
    {
        var attr = typeof(BookPuzzleController).GetMethod(action)!
            .GetCustomAttributes<EnableRateLimitingAttribute>(inherit: false).SingleOrDefault();
        Assert.NotNull(attr);
        Assert.Equal("anonymous-write", attr!.PolicyName);
    }

    // --- nur lesbare Bücher bzw. Tagespuzzles -----------------------------------------------

    [Fact]
    public async Task AnonymousSolve_OnPrivateBookLine_IsNotFound_AndStoresNothing()
    {
        var p = await PuzzleInBookAsync(exposed: false, ownerUserId: 5);

        var r = await AnonymousController().RecordAnonymousAttempt(p.Id,
            new RecordAnonymousBookAttemptDto { Solved = true, TimeSeconds = 3, SessionId = Session() });

        Assert.IsType<NotFoundObjectResult>(r);
        Assert.Equal(0, await _db.BookPuzzleAttempts.CountAsync());
    }

    [Fact]
    public async Task AnonymousSolve_OnExposedBookOrAssignedDaily_IsRecorded()
    {
        var pool = await PuzzleInBookAsync(exposed: true);
        var formerDaily = await PuzzleInBookAsync(exposed: false);
        _db.DailyPuzzles.Add(new DailyPuzzle
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow), BookPuzzleId = formerDaily.Id, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        foreach (var p in new[] { pool, formerDaily })
            Assert.IsType<OkResult>(await AnonymousController().RecordAnonymousAttempt(p.Id,
                new RecordAnonymousBookAttemptDto { Solved = true, TimeSeconds = 3, SessionId = Session() }));

        Assert.Equal(2, await _db.BookPuzzleAttempts.CountAsync(a => a.AnonymousSessionId != null));
    }

    // --- Webhook entprellt ------------------------------------------------------------------

    [Fact]
    public async Task AnonymousSolves_NotifyOncePerWindow_PlusOneTrailingNotify_LoggedInStaysImmediate()
    {
        var p = await PuzzleInBookAsync(exposed: true);
        var user = new AppUser { Username = "anna", Email = "anna@t.com", PasswordHash = "h", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var queue = new CountingTaskQueue();
        // Eingefrorene Uhr: alle Solves fallen ins selbe Fenster; die Nachmeldung kommt nach 150 ms Echtzeit.
        var throttle = new AnonymousSolveNotifyThrottle(time: new ManualTime(DateTimeOffset.UtcNow),
            window: TimeSpan.FromMilliseconds(150));
        var svc = new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, queue, throttle);

        for (var i = 0; i < 5; i++)
            await svc.RecordAnonymousAttemptAsync(p.Id,
                new RecordAnonymousBookAttemptDto { Solved = true, TimeSeconds = 3, SessionId = Session() });
        Assert.Equal(5, await _db.BookPuzzleAttempts.CountAsync());
        Assert.Equal(1, queue.EnqueuedCount);   // vorher: 5 Webhooks = 5 Aggregationen + 5 × jeder Discord-Post

        // Eingeloggte Versuche melden unverändert sofort (Namen/Erwähnungen).
        await svc.RecordAttemptAsync(p.Id, user.Id, new RecordBookAttemptDto { Solved = true, TimeSeconds = 4 });
        Assert.Equal(2, queue.EnqueuedCount);

        // Genau EINE Nachmeldung am Fensterende trägt die übrigen vier anonymen Solves.
        for (var waited = 0; queue.EnqueuedCount < 3 && waited < 5000; waited += 20)
            await Task.Delay(20);
        Assert.Equal(3, queue.EnqueuedCount);
        await Task.Delay(400);
        Assert.Equal(3, queue.EnqueuedCount);
    }

    [Fact]
    public void Throttle_AllowsAgainAfterTheWindow_AndKeepsPuzzlesApart()
    {
        var clock = new ManualTime(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        var throttle = new AnonymousSolveNotifyThrottle(time: clock);
        Func<ValueTask> never = () => throw new InvalidOperationException("keine Nachmeldung erwartet");

        Assert.True(throttle.TryNotifyNow(1, never));
        Assert.True(throttle.TryNotifyNow(2, never));   // anderes Puzzle, eigenes Fenster
        clock.Now += AnonymousSolveNotifyThrottle.DefaultWindow;
        Assert.True(throttle.TryNotifyNow(1, never));   // Fenster abgelaufen → wieder sofort
    }

    [Fact]
    public void ProgramCs_RegistersTheThrottleAsSingleton()
    {
        // Ohne Registrierung bekäme BookPuzzleService null — und meldete still wieder jeden anonymen Solve.
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains("AddSingleton<AnonymousSolveNotifyThrottle>()", src);
    }

    private static string ProgramCs([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("Program.cs nicht gefunden");
        return "";
    }
}
