using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace RookHub.Api.Tests;

public class TrainingGoalServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TrainingGoalService _service;

    public TrainingGoalServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new TrainingGoalService(_db);
    }

    public void Dispose() => _db.Dispose();

    // ---- Helpers ----------------------------------------------------------

    private async Task<AppUser> CreateUserAsync(string username = "u")
    {
        var u = new AppUser { Username = username, Email = $"{username}@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    private async Task<Group> CreateGroupAsync(string name)
    {
        var g = new Group { Name = name, CreatedAt = DateTime.UtcNow };
        _db.Groups.Add(g);
        await _db.SaveChangesAsync();
        return g;
    }

    private async Task AddToGroupAsync(int userId, int groupId)
    {
        _db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = groupId });
        await _db.SaveChangesAsync();
    }

    private static TrainingGoalInputDto Input(int puzzle = 0, int book = 0, int play = 0, int weekly = 0, int chessable = 0)
        => new() { PuzzleMinutes = puzzle, BookMinutes = book, PlayGames = play, WeeklyDaysTarget = weekly, ChessableMinutes = chessable };

    // ---- Effektives Ziel --------------------------------------------------

    [Fact]
    public async Task GetEffectiveGoal_NoGoal_ReturnsNone()
    {
        var u = await CreateUserAsync();
        var goal = await _service.GetEffectiveGoalAsync(u.Id);
        Assert.Equal("none", goal.Source);
        Assert.Equal(0, goal.PuzzleMinutes);
    }

    [Fact]
    public async Task GetEffectiveGoal_GroupTemplateOnly_ReturnsGroup()
    {
        var u = await CreateUserAsync();
        var g = await CreateGroupAsync("A");
        await AddToGroupAsync(u.Id, g.Id);
        await _service.SetGroupGoalAsync(g.Id, Input(puzzle: 15, book: 10, weekly: 5));

        var goal = await _service.GetEffectiveGoalAsync(u.Id);
        Assert.Equal("group", goal.Source);
        Assert.Equal("A", goal.GroupName);
        Assert.Equal(15, goal.PuzzleMinutes);
        Assert.Equal(5, goal.WeeklyDaysTarget);
    }

    [Fact]
    public async Task GetEffectiveGoal_PersonalOverridesGroup()
    {
        var u = await CreateUserAsync();
        var g = await CreateGroupAsync("A");
        await AddToGroupAsync(u.Id, g.Id);
        await _service.SetGroupGoalAsync(g.Id, Input(puzzle: 15));
        await _service.SetPersonalGoalAsync(u.Id, Input(puzzle: 30, play: 20));

        var goal = await _service.GetEffectiveGoalAsync(u.Id);
        Assert.Equal("personal", goal.Source);
        Assert.Equal(30, goal.PuzzleMinutes);
        Assert.Equal(20, goal.PlayGames);
    }

    [Fact]
    public async Task GetEffectiveGoal_MultipleGroups_MostRecentlyUpdatedWins()
    {
        var u = await CreateUserAsync();
        var older = await CreateGroupAsync("Older");
        var newer = await CreateGroupAsync("Newer");
        await AddToGroupAsync(u.Id, older.Id);
        await AddToGroupAsync(u.Id, newer.Id);

        // Explizite UpdatedAt-Werte für deterministische Reihenfolge.
        _db.GroupTrainingGoals.Add(new GroupTrainingGoal { GroupId = older.Id, PuzzleMinutes = 10, UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        _db.GroupTrainingGoals.Add(new GroupTrainingGoal { GroupId = newer.Id, PuzzleMinutes = 25, UpdatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) });
        await _db.SaveChangesAsync();

        var goal = await _service.GetEffectiveGoalAsync(u.Id);
        Assert.Equal("group", goal.Source);
        Assert.Equal("Newer", goal.GroupName);
        Assert.Equal(25, goal.PuzzleMinutes);
    }

    [Fact]
    public async Task DeletePersonalGoal_FallsBackToGroupTemplate()
    {
        var u = await CreateUserAsync();
        var g = await CreateGroupAsync("A");
        await AddToGroupAsync(u.Id, g.Id);
        await _service.SetGroupGoalAsync(g.Id, Input(puzzle: 15));
        await _service.SetPersonalGoalAsync(u.Id, Input(puzzle: 30));

        var after = await _service.DeletePersonalGoalAsync(u.Id);
        Assert.Equal("group", after.Source);
        Assert.Equal(15, after.PuzzleMinutes);
        Assert.False(await _db.UserTrainingGoals.AnyAsync(x => x.UserId == u.Id));
    }

    // ---- Tracker / Aggregation -------------------------------------------

    [Fact]
    public async Task Tracker_AggregatesAllPuzzleSourcesIntoPuzzleCategory()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(puzzle: 15)); // 900 s
        var now = DateTime.UtcNow;

        _db.Puzzles.Add(new Puzzle { Id = 1, LichessId = "p1", Fen = "x", Moves = "x", Rating = 1500 });
        await _db.SaveChangesAsync();
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 300, AttemptedAt = now });
        _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt { BookPuzzleId = 1, UserId = u.Id, Solved = true, TimeSeconds = 300, AttemptedAt = now });
        _db.EndlessSessions.Add(new EndlessSession { UserId = u.Id, DurationSeconds = 400, CreatedAt = now, Timestamp = 0 });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal(1000, day.PuzzleSeconds);   // 300 + 300 + 400
        Assert.Equal("full", day.Status);        // 1000 >= 900, einzige Ziel-Kategorie
    }

    [Fact]
    public async Task Tracker_PartialWhenSomeButNotAllCategoriesMet()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(puzzle: 15, book: 15)); // beide 900 s
        var now = DateTime.UtcNow;

        _db.Puzzles.Add(new Puzzle { Id = 1, LichessId = "p1", Fen = "x", Moves = "x", Rating = 1500 });
        await _db.SaveChangesAsync();
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 900, AttemptedAt = now });
        // keine Buch-Zeit → Buch nicht erfüllt
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal("partial", day.Status);
    }

    private async Task<Book> CreateBookAsync(BookKind kind, int id = 1)
    {
        var b = new Book { Id = id, FileName = $"b{id}.pgn", DisplayName = $"Book {id}", Kind = kind, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(b);
        await _db.SaveChangesAsync();
        return b;
    }

    [Fact]
    public async Task Tracker_CourseTime_StudyBook_CountsAsBookCategory()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(book: 10)); // 600 s
        var book = await CreateBookAsync(BookKind.Study);
        var now = DateTime.UtcNow;

        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = book.Id, BookPuzzleId = 1, Solved = true, TimeSeconds = 700, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal(700, day.BookSeconds);
        Assert.Equal(0, day.PuzzleSeconds);
        Assert.Equal("full", day.Status);
    }

    [Fact]
    public async Task Tracker_CourseTime_PuzzleBook_CountsAsPuzzleCategory()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(puzzle: 10)); // 600 s
        var book = await CreateBookAsync(BookKind.Puzzle);
        var now = DateTime.UtcNow;

        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = book.Id, BookPuzzleId = 1, Solved = true, TimeSeconds = 700, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal(700, day.PuzzleSeconds);
        Assert.Equal(0, day.BookSeconds);
        Assert.Equal("full", day.Status);
    }

    [Fact]
    public async Task Tracker_CourseAttempts_AccumulateSolvedAndFailed()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(puzzle: 10)); // 600 s
        var book = await CreateBookAsync(BookKind.Puzzle);
        var now = DateTime.UtcNow;

        // Mehrere Versuche am selben Puzzle (gelöst + fehlgeschlagen + Wiederholung) summieren sich.
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = book.Id, BookPuzzleId = 1, Solved = false, TimeSeconds = 200, AttemptedAt = now });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = book.Id, BookPuzzleId = 1, Solved = true, TimeSeconds = 250, AttemptedAt = now });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = book.Id, BookPuzzleId = 1, Solved = true, TimeSeconds = 300, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal(750, day.PuzzleSeconds);   // 200 + 250 + 300, kein Cap (je < 1800)
        Assert.Equal("full", day.Status);
    }

    // ---- Chessable-Aktivität ---------------------------------------------

    [Fact]
    public async Task RecordChessableActivity_PersistsChunk_AndClampsToFlushCap()
    {
        var u = await CreateUserAsync();

        await _service.RecordChessableActivityAsync(u.Id, new ChessableActivityInputDto { SecondsActive = 90, MovesTrained = 4 });
        await _service.RecordChessableActivityAsync(u.Id, new ChessableActivityInputDto { SecondsActive = 99999, MovesTrained = 1 });

        var rows = await _db.ChessableActivities.Where(a => a.UserId == u.Id).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(90, rows[0].TimeSeconds);
        Assert.Equal(4, rows[0].MovesTrained);
        Assert.Equal(3600, rows[1].TimeSeconds);   // auf PerChessableFlushCapSeconds gedeckelt
    }

    [Fact]
    public async Task Tracker_ChessableActivity_CountsAsChessableCategory_AndAccumulates()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(chessable: 10)); // 600 s
        var now = DateTime.UtcNow;

        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 300, MovesTrained = 10, AttemptedAt = now });
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 400, MovesTrained = 12, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal(700, day.ChessableSeconds);   // 300 + 400 akkumuliert
        Assert.Equal(0, day.PuzzleSeconds);
        Assert.Equal(0, day.BookSeconds);
        Assert.Equal("full", day.Status);          // Chessable ist ein Tagesziel
    }

    [Fact]
    public async Task Today_ChessableCategory_ReflectsGoalAndDoneSeconds()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(chessable: 20)); // 1200 s
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 600, MovesTrained = 5, AttemptedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var today = await _service.GetTodayAsync(u.Id);
        Assert.Equal(20, today.Goal.ChessableMinutes);
        Assert.Equal(20, today.Chessable.TargetMinutes);
        Assert.Equal(600, today.Chessable.DoneSeconds);
        Assert.False(today.Chessable.Met);          // 600 < 1200
        // Einzige gesetzte Kategorie nicht erreicht → "none" (partial erst, wenn ≥1 Kategorie voll erfüllt ist).
        Assert.Equal("none", today.Status);
    }

    [Fact]
    public async Task Today_ChessablePartialWithOtherCategory_IsPartial()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(puzzle: 10, chessable: 10)); // je 600 s
        var now = DateTime.UtcNow;

        _db.Puzzles.Add(new Puzzle { Id = 1, LichessId = "p1", Fen = "x", Moves = "x", Rating = 1500 });
        await _db.SaveChangesAsync();
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 600, AttemptedAt = now });
        // Chessable-Ziel gesetzt, aber keine Chessable-Zeit → nicht erfüllt.
        await _db.SaveChangesAsync();

        var today = await _service.GetTodayAsync(u.Id);
        Assert.Equal("partial", today.Status);
    }

    [Fact]
    public async Task Today_PlayGamesCountWeekly_AcrossPlatforms_AndDayStatusIgnoresPlay()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(play: 5)); // Wochenziel: 5 Partien
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Heute 3 (lichess) + 2 (chesscom) = 5 Partien.
        _db.PlayTimeDailies.Add(new PlayTimeDaily { UserId = u.Id, Date = today, Platform = "lichess", Games = 3 });
        _db.PlayTimeDailies.Add(new PlayTimeDaily { UserId = u.Id, Date = today, Platform = "chesscom", Games = 2 });
        await _db.SaveChangesAsync();

        var todayProgress = await _service.GetTodayAsync(u.Id);
        Assert.Equal(5, todayProgress.Play.TargetGames);
        Assert.Equal(5, todayProgress.Play.DoneGames);   // beide Plattformen, ganze Woche summiert
        Assert.True(todayProgress.Play.Met);
        // Spielen ist Wochenziel → kein Tagesziel → Tagesstatus bleibt "none".
        Assert.Equal("none", todayProgress.Status);

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal(5, day.PlayGames);                  // informativ je Tag
        Assert.Equal("none", day.Status);                // Tagesstatus nutzt nur Puzzles/Buch
    }

    [Fact]
    public async Task Tracker_ClampsInflatedSinglePuzzleTime()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(puzzle: 40)); // 2400 s
        var now = DateTime.UtcNow;

        _db.Puzzles.Add(new Puzzle { Id = 1, LichessId = "p1", Fen = "x", Moves = "x", Rating = 1500 });
        await _db.SaveChangesAsync();
        // Ein einzelner Versuch mit absurd hoher Zeit (Tab offen gelassen) → auf 1800 s gedeckelt.
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 99999, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal(1800, day.PuzzleSeconds); // gedeckelt
        Assert.Equal("none", day.Status);      // 1800 < 2400 → Ziel nicht erreicht
    }

    [Fact]
    public async Task Today_ReportsPerCategoryProgressAndWeekDaysMet()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(puzzle: 15, weekly: 3)); // 900 s
        var now = DateTime.UtcNow;

        _db.Puzzles.Add(new Puzzle { Id = 1, LichessId = "p1", Fen = "x", Moves = "x", Rating = 1500 });
        await _db.SaveChangesAsync();
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 900, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var today = await _service.GetTodayAsync(u.Id);
        Assert.Equal("full", today.Status);
        Assert.True(today.Puzzles.Met);
        Assert.Equal(900, today.Puzzles.DoneSeconds);
        Assert.Equal(1, today.WeekDaysMet);
        Assert.Equal(3, today.WeeklyDaysTarget);
    }

    [Fact]
    public async Task GroupGoal_SetGetDelete_RoundTrips()
    {
        var g = await CreateGroupAsync("A");
        await _service.SetGroupGoalAsync(g.Id, Input(puzzle: 15, book: 10, play: 20, weekly: 4));

        var got = await _service.GetGroupGoalAsync(g.Id);
        Assert.Equal("group", got.Source);
        Assert.Equal(20, got.PlayGames);

        await _service.DeleteGroupGoalAsync(g.Id);
        var after = await _service.GetGroupGoalAsync(g.Id);
        Assert.Equal("none", after.Source);
    }

    // ---- Controller (Admin-Vorlage) --------------------------------------

    [Fact]
    public async Task GroupController_TrainingGoalEndpoints_Work()
    {
        var g = await CreateGroupAsync("A");
        var controller = new GroupController(_db, _service);

        var setResult = await controller.SetTrainingGoal(g.Id, Input(puzzle: 15)) as OkObjectResult;
        var dto = Assert.IsType<TrainingGoalDto>(setResult!.Value);
        Assert.Equal(15, dto.PuzzleMinutes);

        var getResult = await controller.GetTrainingGoal(g.Id) as OkObjectResult;
        Assert.Equal("group", Assert.IsType<TrainingGoalDto>(getResult!.Value).Source);

        var del = await controller.DeleteTrainingGoal(g.Id);
        Assert.IsType<NoContentResult>(del);
        Assert.False(await _db.GroupTrainingGoals.AnyAsync(x => x.GroupId == g.Id));
    }

    [Fact]
    public async Task GroupController_TrainingGoal_UnknownGroup_NotFound()
    {
        var controller = new GroupController(_db, _service);
        var result = await controller.GetTrainingGoal(999);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- Manuelle Offline-Aktivitäten -------------------------------------

    private static ManualActivityInputDto ManualInput(ManualActivityKind kind, int amount, string? date = null, string? note = null)
        => new() { Kind = kind, Amount = amount, Date = date ?? DateTime.UtcNow.ToString("yyyy-MM-dd"), Note = note };

    [Fact]
    public async Task AddManual_PersistsAndReturnsDto()
    {
        var u = await CreateUserAsync();
        var dto = await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OtbGame, 1, note: "  Vereinsabend  "));

        Assert.True(dto.Id > 0);
        Assert.Equal(ManualActivityKind.OtbGame, dto.Kind);
        Assert.Equal("Vereinsabend", dto.Note); // getrimmt
        Assert.Single(await _db.ManualActivities.Where(m => m.UserId == u.Id).ToListAsync());
    }

    [Fact]
    public async Task AddManual_ClampsGamesAndMinutes()
    {
        var u = await CreateUserAsync();
        var game = await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OtbGame, 999));
        var study = await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflineStudy, 999));

        Assert.Equal(50, game.Amount);   // OTB-Cap
        Assert.Equal(600, study.Amount); // Minuten-Cap
    }

    [Fact]
    public async Task AddManual_FutureDate_Throws()
    {
        var u = await CreateUserAsync();
        var future = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd");
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflineStudy, 30, date: future)));
    }

    [Fact]
    public async Task AddManual_BadDate_Throws()
    {
        var u = await CreateUserAsync();
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflineStudy, 30, date: "22.06.2026")));
    }

    [Fact]
    public async Task UpdateManual_OwnEntry_Updates_OtherUser_ReturnsNull()
    {
        var u = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var created = await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflineStudy, 30));

        var updated = await _service.UpdateManualAsync(u.Id, created.Id, ManualInput(ManualActivityKind.Coaching, 45));
        Assert.NotNull(updated);
        Assert.Equal(ManualActivityKind.Coaching, updated!.Kind);
        Assert.Equal(45, updated.Amount);

        Assert.Null(await _service.UpdateManualAsync(other.Id, created.Id, ManualInput(ManualActivityKind.Coaching, 10)));
    }

    [Fact]
    public async Task DeleteManual_OnlyOwn()
    {
        var u = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var created = await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflinePuzzle, 20));

        Assert.False(await _service.DeleteManualAsync(other.Id, created.Id));
        Assert.True(await _service.DeleteManualAsync(u.Id, created.Id));
        Assert.Empty(await _db.ManualActivities.ToListAsync());
    }

    [Fact]
    public async Task ListManual_ReturnsOwnNewestFirst()
    {
        var u = await CreateUserAsync();
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflineStudy, 30, date: "2026-06-01"));
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.Coaching, 60, date: "2026-06-10"));

        var list = await _service.ListManualAsync(u.Id);
        Assert.Equal(2, list.Count);
        Assert.Equal("2026-06-10", list[0].Date); // neuestes zuerst
    }

    [Fact]
    public async Task Today_ManualEntries_FeedExistingCategories()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(puzzle: 10, book: 10, play: 3));
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflinePuzzle, 15, date: today)); // → Puzzles
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflineStudy, 5, date: today));   // → Buch
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.Coaching, 5, date: today));       // → Buch
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OtbGame, 2, date: today));        // → Spielen

        var t = await _service.GetTodayAsync(u.Id);
        Assert.Equal(15 * 60, t.Puzzles.DoneSeconds);
        Assert.Equal(10 * 60, t.Book.DoneSeconds); // 5 + 5 min
        Assert.Equal(2, t.Play.DoneGames);
        Assert.True(t.Puzzles.Met);
        Assert.True(t.Book.Met);
    }

    [Fact]
    public async Task Tracker_MarksDaysWithManualActivity()
    {
        var u = await CreateUserAsync();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflineStudy, 30, date: today));

        var tracker = await _service.GetTrackerAsync(u.Id, 4);
        var day = Assert.Single(tracker.Days);
        Assert.True(day.HasManual);
        Assert.Equal(30 * 60, day.BookSeconds);
    }
}
