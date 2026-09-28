using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using System.Security.Claims;

namespace RookHub.Api.Tests;

/// <summary>Sperrzeiten der Spark (0.546.0): die Angabe, die Randfälle der Fenster (Wien, mit Sommerzeit), und wie
/// Texte zur Partie sie einhalten — automatische zurückgestellt, Knöpfe sagen ab.</summary>
public class QuietHoursTests
{
    /// <summary>Eine Uhr, die man stellt.</summary>
    internal sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    /// <summary>Ortszeit Wien → Zeitpunkt.</summary>
    private static DateTimeOffset At(int year, int month, int day, int hour, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0);
        return new DateTimeOffset(local, Vienna.GetUtcOffset(local));
    }

    private static QuietHours Default(ManualTime? time = null) => new(QuietHours.DefaultSpec, "Europe/Vienna", time);

    [Theory]
    // Donnerstag 24.09.2026 (Sommerzeit), Freitag 25.09., Samstag 26.09., Montag 28.09.
    [InlineData(2026, 9, 24, 16, 59, true)]
    [InlineData(2026, 9, 24, 17, 0, false)]    // das Ende ist frei
    [InlineData(2026, 9, 24, 7, 59, false)]
    [InlineData(2026, 9, 24, 8, 0, true)]
    [InlineData(2026, 9, 25, 13, 59, true)]
    [InlineData(2026, 9, 25, 14, 0, false)]    // Freitag endet früher
    [InlineData(2026, 9, 26, 10, 0, false)]    // Samstag
    [InlineData(2026, 9, 27, 10, 0, false)]    // Sonntag
    [InlineData(2026, 9, 28, 8, 30, true)]     // Montag
    // Nach dem Wechsel auf Winterzeit (25.10.): dieselbe Ortszeit, eine Stunde andere UTC-Zeit.
    [InlineData(2026, 10, 29, 16, 59, true)]
    [InlineData(2026, 10, 29, 17, 0, false)]
    public void IsQuiet_TheWindowsInViennaTime(int y, int m, int d, int h, int min, bool quiet)
        => Assert.Equal(quiet, Default().IsQuiet(At(y, m, d, h, min)));

    [Fact]
    public void IsQuiet_IsLocalTime_NotUtc_AcrossTheSummerTimeChange()
    {
        var q = Default();
        // Sommerzeit (UTC+2): 15:00 UTC = 17:00 Wien → frei. Winterzeit (UTC+1): 15:00 UTC = 16:00 Wien → gesperrt.
        Assert.False(q.IsQuiet(new DateTimeOffset(2026, 9, 24, 15, 0, 0, TimeSpan.Zero)));
        Assert.True(q.IsQuiet(new DateTimeOffset(2026, 10, 29, 15, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void EndOf_TheEndOfTheWindow_OrTheSameInstantWhenFree()
    {
        var q = Default();
        Assert.Equal(At(2026, 9, 24, 17, 0), q.EndOf(At(2026, 9, 24, 9, 15)));
        Assert.Equal(At(2026, 9, 25, 14, 0), q.EndOf(At(2026, 9, 25, 13, 59)));
        Assert.Equal(At(2026, 10, 26, 17, 0), q.EndOf(At(2026, 10, 26, 9, 0)));   // Montag nach dem Wechsel
        var free = At(2026, 9, 26, 10, 0);
        Assert.Equal(free, q.EndOf(free));
        // Fenster, die lückenlos anschließen, sind eines.
        var chain = new QuietHours("Mon 08:00-12:00; Mon 12:00-13:30", "Europe/Vienna");
        Assert.Equal(At(2026, 9, 28, 13, 30), chain.EndOf(At(2026, 9, 28, 9, 0)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptySpec_NeverQuiet(string? spec)
    {
        var q = new QuietHours(spec, "Europe/Vienna");
        Assert.False(q.Enabled);
        Assert.False(q.IsQuiet(At(2026, 9, 24, 10, 0)));
    }

    [Fact]
    public void Parse_RangesListsAndAcrossTheWeekend()
    {
        Assert.Equal(5, QuietHours.Parse(QuietHours.DefaultSpec).Count);
        Assert.Equal([DayOfWeek.Saturday, DayOfWeek.Sunday], QuietHours.Parse("Sat,Sun 10:00-12:00").Select(w => w.Day));
        Assert.Equal([DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday],
            QuietHours.Parse("fri-MON 08:00-09:00").Select(w => w.Day));
    }

    [Theory]
    [InlineData("Mon 17:00-08:00")]       // über Mitternacht
    [InlineData("Mo 08:00-09:00")]        // kein Wochentag
    [InlineData("Mon 8-9")]               // keine Uhrzeit
    [InlineData("08:00-17:00")]           // ohne Tage
    [InlineData("Mon 08:00-25:00")]
    public void Parse_Unclear_IsAConfigurationError(string spec)
        => Assert.Throws<FormatException>(() => QuietHours.Parse(spec));

    [Fact]
    public void QuietUntil_FollowsTheClock()
    {
        var time = new ManualTime { Now = At(2026, 9, 25, 11, 0) };
        var q = Default(time);
        Assert.Equal(At(2026, 9, 25, 14, 0), q.QuietUntil());
        time.Now = At(2026, 9, 25, 14, 0);
        Assert.Null(q.QuietUntil());
    }

    // ── Texte zur Partie ────────────────────────────────────────────────────────────────────────

    private sealed class RecordingScheduler(QuietHours quiet) : GameReviewTextScheduler(
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<GameReviewTextScheduler>.Instance, quiet)
    {
        public List<(int, bool)> Runs { get; } = new();
        public List<int> Recaps { get; } = new();
        internal override void Execute(int analysisId, bool refined) { lock (Runs) Runs.Add((analysisId, refined)); }
        internal override void ExecuteRecap(int savedGameId) => Recaps.Add(savedGameId);
    }

    [Fact]
    public void Scheduler_DefersTheAutomaticRunDuringTheWindow_MergesRefined_ButTheRecapOnOpeningRunsAtOnce()
    {
        var time = new ManualTime { Now = At(2026, 9, 24, 10, 0) };
        var scheduler = new RecordingScheduler(Default(time));

        scheduler.Schedule(7, refined: false);
        scheduler.Schedule(7, refined: true);    // Vertiefung während der Sperre: nach dem Fenster wird mehr neu geschrieben
        scheduler.Schedule(8, refined: false);
        scheduler.ScheduleRecap(3);               // beim Öffnen: auf Zuruf, läuft sofort (0.585.0)

        Assert.Empty(scheduler.Runs);
        Assert.Equal([3], scheduler.Recaps);
        Assert.Equal(2, scheduler.DeferredCount);

        time.Now = At(2026, 9, 24, 17, 0);
        scheduler.ReleaseDeferred();

        Assert.Equal([(7, true), (8, false)], scheduler.Runs.OrderBy(r => r.Item1));
        Assert.Equal(0, scheduler.DeferredCount);
    }

    private sealed class FakeLlm : IClaudeJsonClient
    {
        public bool IsConfigured => true;
        public bool IsLocal => true;
        public string TranslationModel => "fake-local";
        public int Calls;
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult<string?>("{\"roast\":\"Mutig.\"}");
        }
    }

    [Fact]
    public async Task Buttons_WorkDuringTheWindow_OnlyTheBackgroundRunsWait()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Username = "u", PasswordHash = "x" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var analysis = new GameAnalysis
        {
            UserId = user.Id, Pgn = "1. e4 *", StartFen = "startpos", Status = GameAnalysisStatus.Done, PlyCount = 1,
            Origin = GameAnalysisOrigin.SavedGame,
        };
        analysis.Positions.Add(new GameAnalysisPosition
        {
            Ply = 0, Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", GameMoveUci = "e2e4", GameMoveSan = "e4",
            CandidatesJson = """[{"uci":"e2e4","cp":30}]""", Depth = 20,
        });
        db.GameAnalyses.Add(analysis);
        await db.SaveChangesAsync();
        var game = new SavedGame { UserId = user.Id, Source = "lichess", Pgn = "1. e4 *", ShareToken = "tok", GameAnalysisId = analysis.Id };
        db.SavedGames.Add(game);
        await db.SaveChangesAsync();

        // Donnerstag 10:00 in Wien — mitten in der Sperrzeit. Die gilt nur für die Hintergrundläufe (Scheduler, Kurs-
        // Übersetzung, Bibliothekslauf); „Fehler erklären lassen" und „Roast my game" sind Aufträge auf Zuruf (0.585.0).
        Assert.True(Default(new ManualTime { Now = At(2026, 9, 24, 10, 0) }).IsQuietNow());
        var llm = new FakeLlm();
        var explanations = new GameMoveExplanationService(db, llm, new GameExplanationJobs(),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<GameMoveExplanationService>.Instance);
        var roasts = new GameRoastService(db, llm, TestServices.SavedGames(db), NullLogger<GameRoastService>.Instance);

        var state = await explanations.GetAsync(await explanations.OwnGameAsync(user.Id, game.Id), "de", owner: true);
        Assert.True(state.CanGenerate);

        Assert.Null((await roasts.RoastAsync(user.Id, game.Id, "friendly", "de")).Reason);
        Assert.Equal(1, llm.Calls);

        var controller = new GameRoastController(roasts)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "t")),
                },
            },
        };
        Assert.IsType<OkObjectResult>((await controller.Roast(game.Id, "cheeky", "de", default)).Result);
        Assert.Equal(2, llm.Calls);
    }

    [Fact]
    public async Task RecapOnOpening_IsStartedDuringTheWindow_TooOnDemand()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Username = "u", PasswordHash = "x" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var analysis = new GameAnalysis { UserId = user.Id, Pgn = "1. e4 *", StartFen = "startpos", Status = GameAnalysisStatus.Done, PlyCount = 1 };
        db.GameAnalyses.Add(analysis);
        await db.SaveChangesAsync();
        var game = new SavedGame { UserId = user.Id, Source = "lichess", Pgn = "1. e4 *", ShareToken = "tok", GameAnalysisId = analysis.Id };
        db.SavedGames.Add(game);
        await db.SaveChangesAsync();

        var time = new ManualTime { Now = At(2026, 9, 25, 9, 0) };
        var quiet = Default(time);
        var scheduler = new RecordingScheduler(quiet);
        var recaps = new GameRecapService(db, new FakeLlm(), TestServices.SavedGames(db), NullLogger<GameRecapService>.Instance);
        var controller = new GameRecapController(recaps, scheduler)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "t")),
                },
            },
        };

        var dto = Assert.IsType<GameRecapDto>(Assert.IsType<OkObjectResult>((await controller.Get(game.Id, default)).Result).Value);
        Assert.True(quiet.IsQuietNow());
        Assert.True(dto.Pending);
        Assert.Equal([game.Id], scheduler.Recaps);
    }
}
