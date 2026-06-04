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

    private static TrainingGoalInputDto Input(int puzzle = 0, int book = 0, int play = 0, int weekly = 0)
        => new() { PuzzleMinutes = puzzle, BookMinutes = book, PlayMinutes = play, WeeklyDaysTarget = weekly };

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
        Assert.Equal(20, goal.PlayMinutes);
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

    [Fact]
    public async Task Tracker_CourseTimeCountsAsBookCategory()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(book: 10)); // 600 s
        var now = DateTime.UtcNow;

        _db.CoursePuzzleResults.Add(new CoursePuzzleResult { UserId = u.Id, BookId = 1, BookPuzzleId = 1, SolvedAt = now, TimeSeconds = 700 });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal(700, day.BookSeconds);
        Assert.Equal("full", day.Status);
    }

    [Fact]
    public async Task Tracker_PlayTimeCountsAsPlayCategory()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(play: 15)); // 900 s
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        _db.PlayTimeDailies.Add(new PlayTimeDaily { UserId = u.Id, Date = today, Platform = "lichess", Seconds = 600 });
        _db.PlayTimeDailies.Add(new PlayTimeDaily { UserId = u.Id, Date = today, Platform = "chesscom", Seconds = 600 });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal(1200, day.PlaySeconds); // beide Plattformen summiert
        Assert.Equal("full", day.Status);
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
        Assert.Equal(20, got.PlayMinutes);

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
}
