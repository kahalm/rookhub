using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Social;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Tests.Social;

/// <summary>
/// Charakterisierung der Tages-/Wochenrechnung von <see cref="TrainingGoalService"/> vor der Zerlegung (N9-008):
/// Tageswechsel (UTC-Kalendertag, keine Umrechnung der <see cref="DateTimeKind"/>), Wochenanfang Montag,
/// Fenstergrenzen, Deckel je Quelle und die Aufteilung der Kurszeit. Feste Daten relativ zu „heute UTC".
/// </summary>
public class TrainingGoalAggregationCharacterizationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TrainingGoalService _service;
    private readonly DateTime _today = DateTime.UtcNow.Date;

    public TrainingGoalAggregationCharacterizationTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = new TrainingGoalService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> UserAsync(int dailyMinutes = 10, int playGames = 0)
    {
        var u = new AppUser { Username = "u", Email = "u@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(u);
        _db.Puzzles.Add(new Puzzle { Id = 1, LichessId = "p1", Fen = "x", Moves = "x", Rating = 1500 });
        await _db.SaveChangesAsync();
        await _service.SetPersonalGoalAsync(u.Id, new TrainingGoalInputDto { DailyMinutes = dailyMinutes, PlayGames = playGames });
        return u.Id;
    }

    private void Puzzle(int userId, DateTime at, int seconds) =>
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = userId, PuzzleId = 1, Solved = true, TimeSpentSeconds = seconds, AttemptedAt = at });

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd");

    [Fact]
    public async Task DayChange_IsTheUtcCalendarDay_LastSecondBeforeMidnightBelongsToYesterday()
    {
        var u = await UserAsync();
        Puzzle(u, _today.AddSeconds(-1), 100);   // gestern 23:59:59
        Puzzle(u, _today, 200);                  // heute 00:00:00
        await _db.SaveChangesAsync();

        var days = (await _service.GetTrackerAsync(u, 1)).Days;
        Assert.Equal([Key(_today.AddDays(-1)), Key(_today)], days.Select(d => d.Date));
        Assert.Equal([100, 200], days.Select(d => d.TotalSeconds));

        var today = await _service.GetTodayAsync(u);
        Assert.Equal(200, today.Daily.DoneSeconds);
    }

    [Fact]
    public async Task TimeZone_StoredTimesAreNotConverted_LocalAndUnspecifiedCountOnTheirOwnDate()
    {
        var u = await UserAsync();
        // 23:30 ohne/mit lokaler Kennung: gezählt wird am Kalendertag des gespeicherten Werts, ohne Umrechnung.
        Puzzle(u, DateTime.SpecifyKind(_today.AddDays(-2).AddHours(23).AddMinutes(30), DateTimeKind.Unspecified), 60);
        Puzzle(u, DateTime.SpecifyKind(_today.AddDays(-1).AddHours(23).AddMinutes(30), DateTimeKind.Local), 70);
        await _db.SaveChangesAsync();

        var days = (await _service.GetTrackerAsync(u, 1)).Days;
        Assert.Equal([Key(_today.AddDays(-2)), Key(_today.AddDays(-1))], days.Select(d => d.Date));
        Assert.Equal([60, 70], days.Select(d => d.TotalSeconds));
    }

    [Fact]
    public async Task Week_StartsOnMonday_PlayGamesAndFullDaysCountFromMondayOnly()
    {
        var u = await UserAsync(dailyMinutes: 1, playGames: 10);
        var monday = _today.AddDays(-(((int)_today.DayOfWeek + 6) % 7));
        var sundayBefore = monday.AddDays(-1);

        _db.PlayTimeDailies.Add(new PlayTimeDaily { UserId = u, Date = DateOnly.FromDateTime(sundayBefore), Platform = "lichess", Games = 4 });
        _db.PlayTimeDailies.Add(new PlayTimeDaily { UserId = u, Date = DateOnly.FromDateTime(monday), Platform = "lichess", Games = 3 });
        Puzzle(u, sundayBefore.AddHours(12), 120);   // voller Tag, aber Vorwoche
        Puzzle(u, monday.AddHours(12), 120);         // voller Tag dieser Woche
        await _db.SaveChangesAsync();

        var today = await _service.GetTodayAsync(u);
        Assert.Equal(3, today.Play.DoneGames);
        Assert.False(today.Play.Met);
        Assert.Equal(1, today.WeekDaysMet);
        Assert.Equal(monday == _today ? 120 : 0, today.Daily.DoneSeconds);
    }

    [Fact]
    public async Task TrackerWindow_StartsAtMidnight_WeeksTimesSevenMinusOneDaysBack()
    {
        var u = await UserAsync();
        var windowStart = _today.AddDays(-6);   // weeks = 1
        Puzzle(u, windowStart, 50);              // erste Sekunde des Fensters
        Puzzle(u, windowStart.AddSeconds(-1), 40);
        _db.ManualActivities.Add(new ManualActivity { UserId = u, Date = DateOnly.FromDateTime(windowStart), Kind = ManualActivityKind.OfflineStudy, Amount = 2, CreatedAt = DateTime.UtcNow });
        _db.ManualActivities.Add(new ManualActivity { UserId = u, Date = DateOnly.FromDateTime(windowStart.AddDays(-1)), Kind = ManualActivityKind.OfflineStudy, Amount = 3, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var day = Assert.Single((await _service.GetTrackerAsync(u, 1)).Days);
        Assert.Equal(Key(windowStart), day.Date);
        Assert.Equal(50 + 120, day.TotalSeconds);
        Assert.True(day.HasManual);

        var weeks0 = await _service.GetTrackerAsync(u, 0);   // auf 1 Woche geklemmt
        Assert.Single(weeks0.Days);
    }

    [Fact]
    public async Task Caps_PerSource_AndStatusBoundaryAtExactlyTheGoal()
    {
        var u = await UserAsync(dailyMinutes: 300);   // 18 000 s
        var noon = _today.AddHours(12);
        Puzzle(u, noon, 5000);                                                               // → 1 800
        _db.EndlessSessions.Add(new EndlessSession { UserId = u, DurationSeconds = 20000, CreatedAt = noon, Timestamp = 0 });       // → 14 400
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u, TimeSeconds = 5000, AttemptedAt = noon });                // → 3 600
        _db.ManualActivities.Add(new ManualActivity { UserId = u, Date = DateOnly.FromDateTime(_today), Kind = ManualActivityKind.Coaching, Amount = 700, CreatedAt = DateTime.UtcNow }); // → 36 000
        _db.ManualActivities.Add(new ManualActivity { UserId = u, Date = DateOnly.FromDateTime(_today), Kind = ManualActivityKind.OtbGame, Amount = 80, CreatedAt = DateTime.UtcNow });   // → 50 Partien
        await _db.SaveChangesAsync();

        var day = Assert.Single((await _service.GetTrackerAsync(u, 1)).Days);
        Assert.Equal(1800 + 14400 + 3600 + 36000, day.TotalSeconds);
        Assert.Equal(1800 + 14400, day.BySource.RandomPuzzleSeconds);
        Assert.Equal(36000, day.BySource.CourseBookSeconds);
        Assert.Equal(3600, day.BySource.ChessableSeconds);
        Assert.Equal(36000 + 3600, day.ByTheme.OtherSeconds);   // Coaching ohne Thema → other; Chessable ohne Kind → other
        Assert.Equal(1800 + 14400, day.ByTheme.TacticsSeconds); // Puzzle ohne Phase + Endlos → Taktik
        Assert.Equal(50, day.PlayGames);

        Assert.Equal("none", TrainingGoalService.DayStatus(0, 10));
        Assert.Equal("none", TrainingGoalService.DayStatus(100, 0));
        Assert.Equal("partial", TrainingGoalService.DayStatus(599, 10));
        Assert.Equal("full", TrainingGoalService.DayStatus(600, 10));
    }

    [Fact]
    public async Task CourseTime_IsCappedFirst_ThenSplitEvenly_RemainderToTheFirstTheme()
    {
        var u = await UserAsync();
        var book = new Book { Id = 7, FileName = "b7.pgn", DisplayName = "B7", Kind = BookKind.Study, Themes = "endgame,opening,tactics", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
        _db.Books.Add(book);
        _db.BookPuzzles.Add(new BookPuzzle { Id = 7, LineId = "l7", BookFileName = "b7.pgn", Fen = "x", Moves = "x", BookId = 7 });
        var noon = _today.AddHours(12);
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u, BookId = 7, BookPuzzleId = 7, Solved = true, TimeSeconds = 100, AttemptedAt = noon });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u, BookId = 7, BookPuzzleId = 7, Solved = false, TimeSeconds = 9999, AttemptedAt = noon });   // → 1 800
        await _db.SaveChangesAsync();

        var day = Assert.Single((await _service.GetTrackerAsync(u, 1)).Days);
        Assert.Equal(1900, day.TotalSeconds);
        Assert.Equal(34 + 600, day.ByTheme.EndgameSeconds);   // 100 = 34 + 33 + 33
        Assert.Equal(33 + 600, day.ByTheme.OpeningSeconds);
        Assert.Equal(33 + 600, day.ByTheme.TacticsSeconds);
        Assert.Equal(1900, day.BySource.CourseBookSeconds);

        var tracker = await _service.GetTrackerAsync(u, 1);
        Assert.Equal(1900, tracker.BreakdownBySource.CourseBookSeconds);
        Assert.Equal(634, tracker.BreakdownByTheme.EndgameSeconds);
    }

    [Fact]
    public async Task DailySeries_AndTracker_MapTheSameDay()
    {
        var u = await UserAsync(dailyMinutes: 1);
        Puzzle(u, _today.AddHours(1), 90);
        await _db.SaveChangesAsync();

        var t = Assert.Single((await _service.GetTrackerAsync(u, 1)).Days);
        var s = Assert.Single((await _service.GetDailySeriesAsync(u)).Days);
        Assert.Equal((t.Date, t.TotalSeconds, t.Status, t.BySource.RandomPuzzleSeconds, t.ByTheme.TacticsSeconds, t.HasManual),
                     (s.Date, s.TotalSeconds, s.Status, s.BySource.RandomPuzzleSeconds, s.ByTheme.TacticsSeconds, s.HasManual));
        Assert.Equal("full", s.Status);
    }
}
