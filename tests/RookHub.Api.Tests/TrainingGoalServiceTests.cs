using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

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

    private static TrainingGoalInputDto Input(int daily = 0, int play = 0, int weekly = 0)
        => new() { DailyMinutes = daily, PlayGames = play, WeeklyDaysTarget = weekly };

    private async Task<Book> CreateBookAsync(BookKind kind, int id = 1)
    {
        var b = new Book { Id = id, FileName = $"b{id}.pgn", DisplayName = $"Book {id}", Kind = kind, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(b);
        await _db.SaveChangesAsync();
        return b;
    }

    private async Task<Puzzle> CreatePuzzleAsync(int id, string? themes = null)
    {
        var p = new Puzzle { Id = id, LichessId = $"p{id}", Fen = "x", Moves = "x", Rating = 1500, Themes = themes };
        _db.Puzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private async Task<BookPuzzle> CreateBookPuzzleAsync(int id, string? tags = null, string? chapter = null, int? bookId = null)
    {
        var bp = new BookPuzzle { Id = id, LineId = $"l{id}", BookFileName = "b.pgn", Fen = "x", Moves = "x", Tags = tags, Chapter = chapter, BookId = bookId };
        _db.BookPuzzles.Add(bp);
        await _db.SaveChangesAsync();
        return bp;
    }

    // ---- Effektives Ziel --------------------------------------------------

    [Fact]
    public async Task GetEffectiveGoal_NoGoal_ReturnsNone()
    {
        var u = await CreateUserAsync();
        var goal = await _service.GetEffectiveGoalAsync(u.Id);
        Assert.Equal("none", goal.Source);
        Assert.Equal(0, goal.DailyMinutes);
    }

    [Fact]
    public async Task GetEffectiveGoal_GroupTemplateOnly_ReturnsGroup()
    {
        var u = await CreateUserAsync();
        var g = await CreateGroupAsync("A");
        await AddToGroupAsync(u.Id, g.Id);
        await _service.SetGroupGoalAsync(g.Id, Input(daily: 25, weekly: 5));

        var goal = await _service.GetEffectiveGoalAsync(u.Id);
        Assert.Equal("group", goal.Source);
        Assert.Equal("A", goal.GroupName);
        Assert.Equal(25, goal.DailyMinutes);
        Assert.Equal(5, goal.WeeklyDaysTarget);
    }

    [Fact]
    public async Task GetEffectiveGoal_PersonalOverridesGroup()
    {
        var u = await CreateUserAsync();
        var g = await CreateGroupAsync("A");
        await AddToGroupAsync(u.Id, g.Id);
        await _service.SetGroupGoalAsync(g.Id, Input(daily: 15));
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 30, play: 20));

        var goal = await _service.GetEffectiveGoalAsync(u.Id);
        Assert.Equal("personal", goal.Source);
        Assert.Equal(30, goal.DailyMinutes);
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

        _db.GroupTrainingGoals.Add(new GroupTrainingGoal { GroupId = older.Id, DailyMinutes = 10, UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        _db.GroupTrainingGoals.Add(new GroupTrainingGoal { GroupId = newer.Id, DailyMinutes = 25, UpdatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) });
        await _db.SaveChangesAsync();

        var goal = await _service.GetEffectiveGoalAsync(u.Id);
        Assert.Equal("group", goal.Source);
        Assert.Equal("Newer", goal.GroupName);
        Assert.Equal(25, goal.DailyMinutes);
    }

    [Fact]
    public async Task DeletePersonalGoal_FallsBackToGroupTemplate()
    {
        var u = await CreateUserAsync();
        var g = await CreateGroupAsync("A");
        await AddToGroupAsync(u.Id, g.Id);
        await _service.SetGroupGoalAsync(g.Id, Input(daily: 15));
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 30));

        var after = await _service.DeletePersonalGoalAsync(u.Id);
        Assert.Equal("group", after.Source);
        Assert.Equal(15, after.DailyMinutes);
        Assert.False(await _db.UserTrainingGoals.AnyAsync(x => x.UserId == u.Id));
    }

    // ---- Tracker / Aggregation: ein gemeinsamer Zeit-Topf -----------------

    [Fact]
    public async Task Tracker_AllSourcesSumIntoTotal_AndFeedSourceBuckets()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 15)); // 900 s
        var now = DateTime.UtcNow;

        await CreatePuzzleAsync(1);
        await CreateBookPuzzleAsync(1);
        var puzzleBook = await CreateBookAsync(BookKind.Puzzle, 1);

        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 200, AttemptedAt = now });
        _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt { BookPuzzleId = 1, UserId = u.Id, Solved = true, TimeSeconds = 100, AttemptedAt = now });
        _db.EndlessSessions.Add(new EndlessSession { UserId = u.Id, DurationSeconds = 200, CreatedAt = now, Timestamp = 0 });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = puzzleBook.Id, BookPuzzleId = 1, Solved = true, TimeSeconds = 300, AttemptedAt = now });
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 200, MovesTrained = 5, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        Assert.Equal(1000, day.TotalSeconds);                       // 200+100+200+300+200
        Assert.Equal(500, day.BySource.RandomPuzzleSeconds);        // 200 std + 100 book-puzzle + 200 endless
        Assert.Equal(300, day.BySource.CourseBookSeconds);          // 300 course
        Assert.Equal(200, day.BySource.ChessableSeconds);
        Assert.Equal("full", day.Status);                          // 1000 >= 900
    }

    [Fact]
    public async Task DailySeries_IncludesDaysBeyondTrackerWindow_WithSourceBuckets()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 5)); // 300 s
        await CreatePuzzleAsync(1);
        var now = DateTime.UtcNow;
        var old = now.AddDays(-600); // weit jenseits des 53-Wochen-Tracker-Fensters

        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 120, AttemptedAt = old });
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 240, AttemptedAt = now });
        await _db.SaveChangesAsync();

        // Tracker (gedeckelt) sieht nur den jüngsten Tag …
        Assert.Single((await _service.GetTrackerAsync(u.Id, 53)).Days);

        // … die Tagesreihe liefert die ganze Historie, aufsteigend, je Tag aufgeschlüsselt.
        var series = await _service.GetDailySeriesAsync(u.Id);
        Assert.Equal(2, series.Days.Count);
        Assert.Equal(old.Date.ToString("yyyy-MM-dd"), series.Days[0].Date); // älteste zuerst
        Assert.Equal(120, series.Days[0].BySource.RandomPuzzleSeconds);
        Assert.Equal(240, series.Days[1].BySource.RandomPuzzleSeconds);
    }

    [Fact]
    public async Task Tracker_PartialAndNoneStatus_AgainstSingleDailyGoal()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 15)); // 900 s
        var now = DateTime.UtcNow;
        await CreatePuzzleAsync(1);
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 400, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        Assert.Equal("partial", Assert.Single(res.Days).Status);    // 400 < 900 aber > 0
    }

    [Fact]
    public async Task Tracker_NoGoalSet_StatusNone()
    {
        var u = await CreateUserAsync();
        var now = DateTime.UtcNow;
        await CreatePuzzleAsync(1);
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 400, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var res = await _service.GetTrackerAsync(u.Id, 1);
        Assert.Equal("none", Assert.Single(res.Days).Status);       // kein Tageszeit-Ziel
    }

    [Fact]
    public async Task Tracker_WeeklyPostAttempts_CountAsRandomPuzzleTactics()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 10)); // 600 s
        var now = DateTime.UtcNow;
        var post = new WeeklyPost { Title = "wp1", FileName = "wp1.pgn", PgnContent = "", ScheduledAt = now };
        _db.WeeklyPosts.Add(post);
        await _db.SaveChangesAsync();

        _db.WeeklyPostAttempts.Add(new WeeklyPostAttempt {
            WeeklyPostId = post.Id, UserId = u.Id, PuzzleIndex = 0,
            Solved = true, TimeSeconds = 400, AttemptedAt = now,
        });
        _db.WeeklyPostAttempts.Add(new WeeklyPostAttempt {
            WeeklyPostId = post.Id, UserId = u.Id, PuzzleIndex = 1,
            Solved = false, TimeSeconds = 250, AttemptedAt = now,
        });
        await _db.SaveChangesAsync();

        var day = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(650, day.TotalSeconds);                         // 400 + 250
        Assert.Equal(650, day.BySource.RandomPuzzleSeconds);         // Wochenpost fließt in Puzzles
        Assert.Equal(650, day.ByTheme.TacticsSeconds);               // fest als Taktik
        Assert.Equal("full", day.Status);                            // 650 >= 600
    }

    [Fact]
    public async Task Tracker_CourseTime_BothBookKinds_CountAsCourseBookSource()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 10)); // 600 s
        var study = await CreateBookAsync(BookKind.Study, 1);
        var puzzle = await CreateBookAsync(BookKind.Puzzle, 2);
        await CreateBookPuzzleAsync(1, bookId: study.Id);
        await CreateBookPuzzleAsync(2, bookId: puzzle.Id);
        var now = DateTime.UtcNow;

        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = study.Id, BookPuzzleId = 1, Solved = true, TimeSeconds = 300, AttemptedAt = now });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = puzzle.Id, BookPuzzleId = 2, Solved = true, TimeSeconds = 400, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var day = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(700, day.BySource.CourseBookSeconds);          // beide Buch-Arten → courseBook
        Assert.Equal(0, day.BySource.RandomPuzzleSeconds);
        Assert.Equal("full", day.Status);
    }

    [Fact]
    public async Task Tracker_CourseAttempts_AccumulateSolvedAndFailed()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 10)); // 600 s
        var book = await CreateBookAsync(BookKind.Puzzle);
        await CreateBookPuzzleAsync(1, bookId: book.Id);
        var now = DateTime.UtcNow;

        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = book.Id, BookPuzzleId = 1, Solved = false, TimeSeconds = 200, AttemptedAt = now });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = book.Id, BookPuzzleId = 1, Solved = true, TimeSeconds = 250, AttemptedAt = now });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = book.Id, BookPuzzleId = 1, Solved = true, TimeSeconds = 300, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var day = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(750, day.BySource.CourseBookSeconds);          // 200 + 250 + 300
        Assert.Equal(750, day.TotalSeconds);
    }

    [Fact]
    public async Task Tracker_ClampsInflatedSinglePuzzleTime()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 40)); // 2400 s
        var now = DateTime.UtcNow;
        await CreatePuzzleAsync(1);
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 99999, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var day = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(1800, day.TotalSeconds);   // auf PerPuzzleCap gedeckelt
        Assert.Equal("partial", day.Status);     // 1800 < 2400, aber > 0
    }

    // ---- Theme-Klassifikation ---------------------------------------------

    [Fact]
    public async Task Theme_StandardPuzzle_PhaseTagWins_ElseTactics()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 10));
        var now = DateTime.UtcNow;
        await CreatePuzzleAsync(1, themes: "fork rookEndgame");     // → Endgame (Phase schlägt Motiv)
        await CreatePuzzleAsync(2, themes: "fork pin");            // → Tactics (kein Phasen-Tag)
        await CreatePuzzleAsync(3, themes: "opening advantage");    // → Opening
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 100, AttemptedAt = now });
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 2, Solved = true, TimeSpentSeconds = 200, AttemptedAt = now });
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 3, Solved = true, TimeSpentSeconds = 50, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var day = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(100, day.ByTheme.EndgameSeconds);
        Assert.Equal(200, day.ByTheme.TacticsSeconds);
        Assert.Equal(50, day.ByTheme.OpeningSeconds);
    }

    [Fact]
    public async Task Theme_Chessable_FromCourseKind_NoneIsOther()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 10));
        var now = DateTime.UtcNow;
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 120, CourseKind = RepertoireKind.Opening, AttemptedAt = now });
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 60, CourseKind = RepertoireKind.Endgame, AttemptedAt = now });
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 30, CourseKind = null, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var day = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(120, day.ByTheme.OpeningSeconds);
        Assert.Equal(60, day.ByTheme.EndgameSeconds);
        Assert.Equal(30, day.ByTheme.OtherSeconds);     // CourseKind null → Sonstiges
    }

    [Fact]
    public async Task Theme_CourseBook_FromBookThemes_DefaultTactics_MultiSplitEvenly()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 10));
        var def = await CreateBookAsync(BookKind.Study, 1);                 // Themes unset → Default Taktik
        var endgame = await CreateBookAsync(BookKind.Study, 2);
        endgame.Themes = "endgame";
        var split = await CreateBookAsync(BookKind.Study, 3);
        split.Themes = "tactics,endgame";                                  // 50/50-Aufteilung
        await CreateBookPuzzleAsync(1, bookId: def.Id);
        await CreateBookPuzzleAsync(2, bookId: endgame.Id);
        await CreateBookPuzzleAsync(3, bookId: split.Id);
        await _db.SaveChangesAsync();
        var now = DateTime.UtcNow;

        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = def.Id, BookPuzzleId = 1, Solved = true, TimeSeconds = 100, AttemptedAt = now });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = endgame.Id, BookPuzzleId = 2, Solved = true, TimeSeconds = 60, AttemptedAt = now });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = split.Id, BookPuzzleId = 3, Solved = true, TimeSeconds = 200, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var day = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(200, day.ByTheme.TacticsSeconds);  // 100 (Default) + 100 (Split-Hälfte)
        Assert.Equal(160, day.ByTheme.EndgameSeconds);  // 60 (Endspiel-Buch) + 100 (Split-Hälfte)
        Assert.Equal(0, day.ByTheme.OtherSeconds);
        Assert.Equal(360, day.TotalSeconds);            // Gesamtzeit bleibt korrekt (kein Doppelzählen)
    }

    [Fact]
    public async Task Breakdown_SourceSum_EqualsThemeSum_EqualsTotal()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 30));
        var now = DateTime.UtcNow;
        await CreatePuzzleAsync(1, themes: "fork");
        var book = await CreateBookAsync(BookKind.Study, 1);
        await CreateBookPuzzleAsync(1, bookId: book.Id);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 300, AttemptedAt = now });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = u.Id, BookId = book.Id, BookPuzzleId = 1, Solved = true, TimeSeconds = 240, AttemptedAt = now });
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 180, CourseKind = RepertoireKind.Middlegame, AttemptedAt = now });
        await _db.SaveChangesAsync();
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflinePuzzle, 5, date: today)); // 300 s tactics

        var res = await _service.GetTrackerAsync(u.Id, 1);
        var day = Assert.Single(res.Days);
        int srcSum = day.BySource.RandomPuzzleSeconds + day.BySource.CourseBookSeconds + day.BySource.ChessableSeconds;
        int themeSum = day.ByTheme.OpeningSeconds + day.ByTheme.MiddlegameSeconds + day.ByTheme.EndgameSeconds
                       + day.ByTheme.TacticsSeconds + day.ByTheme.OtherSeconds;
        Assert.Equal(day.TotalSeconds, srcSum);
        Assert.Equal(day.TotalSeconds, themeSum);
        Assert.Equal(1020, day.TotalSeconds);   // 300 + 240 + 180 + 300

        // Perioden-Breakdown == Tages-Breakdown (nur ein Tag).
        Assert.Equal(day.BySource.ChessableSeconds, res.BreakdownBySource.ChessableSeconds);
        Assert.Equal(day.ByTheme.MiddlegameSeconds, res.BreakdownByTheme.MiddlegameSeconds);
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
        Assert.Equal(3600, rows[1].TimeSeconds);   // auf PerChessableFlushCapSeconds gedeckelt
    }

    [Fact]
    public async Task RecordChessableActivity_PersistsCourseIdAndName_Trimmed()
    {
        var u = await CreateUserAsync();
        await _service.RecordChessableActivityAsync(u.Id, new ChessableActivityInputDto
        { SecondsActive = 60, CourseId = "  12345 ", CourseName = "  Caro-Kann  " });

        var row = await _db.ChessableActivities.SingleAsync(a => a.UserId == u.Id);
        Assert.Equal("12345", row.CourseId);
        Assert.Equal("Caro-Kann", row.CourseName);
    }

    // ---- Chessable-Kurs-History + manuelle Themen-Zuordnung --------------

    [Fact]
    public async Task GetChessableCourses_GroupsByCourse_ResolvesAutoAndManualTheme()
    {
        var u = await CreateUserAsync();
        var now = DateTime.UtcNow;
        // Kurs A: nur Auto-Thema aus CourseKind (Opening).
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 100, MovesTrained = 2, CourseId = "111", CourseName = "Course A", CourseKind = RepertoireKind.Opening, AttemptedAt = now.AddMinutes(-10) });
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 50, MovesTrained = 1, CourseId = "111", CourseName = "Course A", AttemptedAt = now });
        // Kurs B: kein Thema → unzugeordnet.
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 70, CourseId = "222", CourseName = "Course B", AttemptedAt = now });
        // Aktivität ohne Kurs-ID → taucht NICHT in der History auf.
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 999, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var all = await _service.GetChessableCoursesAsync(u.Id);
        Assert.Equal(2, all.Count);
        var a = all.Single(c => c.CourseId == "111");
        Assert.Equal("Course A", a.CourseName);
        Assert.Equal(150, a.TotalSeconds);
        Assert.Equal(2, a.ActivityCount);
        Assert.Equal("opening", a.AutoTheme);
        Assert.Null(a.AssignedTheme);
        Assert.True(a.IsAssigned);          // Auto-Thema zählt als zugeordnet
        var b = all.Single(c => c.CourseId == "222");
        Assert.False(b.IsAssigned);

        // Filter „nur unzugeordnet" liefert nur Kurs B.
        var unassigned = await _service.GetChessableCoursesAsync(u.Id, unassignedOnly: true);
        Assert.Equal("222", Assert.Single(unassigned).CourseId);
    }

    [Fact]
    public async Task SetChessableCourseTheme_Upserts_TakesNameFromActivity_AndDrivesTracker()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 10));
        var now = DateTime.UtcNow;
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 120, CourseId = "333", CourseName = "Endgame Course", AttemptedAt = now });
        await _db.SaveChangesAsync();

        // Vor Zuordnung: kein CourseKind → other.
        var before = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(120, before.ByTheme.OtherSeconds);

        Assert.True(await _service.SetChessableCourseThemeAsync(u.Id, "333", ChessableTheme.Endgame));
        var assignment = await _db.ChessableCourseThemes.SingleAsync(t => t.UserId == u.Id && t.CourseId == "333");
        Assert.Equal("Endgame Course", assignment.CourseName);  // Name aus Aktivität übernommen

        // Nach Zuordnung: Zeit zählt rückwirkend als Endspiel.
        var after = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(120, after.ByTheme.EndgameSeconds);
        Assert.Equal(0, after.ByTheme.OtherSeconds);

        // Manuelle Zuordnung hat Vorrang vor CourseKind: Upsert auf Tactics.
        Assert.True(await _service.SetChessableCourseThemeAsync(u.Id, "333", ChessableTheme.Tactics));
        Assert.Equal(1, await _db.ChessableCourseThemes.CountAsync(t => t.UserId == u.Id)); // kein Duplikat
        var summary = (await _service.GetChessableCoursesAsync(u.Id)).Single();
        Assert.Equal("tactics", summary.AssignedTheme);
    }

    [Fact]
    public async Task ManualTheme_OverridesAutoCourseKind_InTracker()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 10));
        var now = DateTime.UtcNow;
        // Auto-Thema wäre Opening, manuelle Zuordnung sagt Tactics → Tactics gewinnt.
        _db.ChessableActivities.Add(new ChessableActivity { UserId = u.Id, TimeSeconds = 90, CourseId = "444", CourseKind = RepertoireKind.Opening, AttemptedAt = now });
        await _db.SaveChangesAsync();
        await _service.SetChessableCourseThemeAsync(u.Id, "444", ChessableTheme.Tactics);

        var day = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(90, day.ByTheme.TacticsSeconds);
        Assert.Equal(0, day.ByTheme.OpeningSeconds);
    }

    [Fact]
    public async Task ClearChessableCourseTheme_RemovesAssignment()
    {
        var u = await CreateUserAsync();
        await _service.SetChessableCourseThemeAsync(u.Id, "555", ChessableTheme.Middlegame);
        Assert.True(await _service.ClearChessableCourseThemeAsync(u.Id, "555"));
        Assert.False(await _service.ClearChessableCourseThemeAsync(u.Id, "555")); // schon weg → 404-Pfad
        Assert.Empty(await _db.ChessableCourseThemes.Where(t => t.UserId == u.Id).ToListAsync());
    }

    // ---- Heute / Wochenstand ----------------------------------------------

    [Fact]
    public async Task Today_DailyProgress_AndBreakdown()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 20, weekly: 3)); // 1200 s
        var now = DateTime.UtcNow;
        await CreatePuzzleAsync(1, themes: "endgame");
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = u.Id, PuzzleId = 1, Solved = true, TimeSpentSeconds = 1200, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var today = await _service.GetTodayAsync(u.Id);
        Assert.Equal(20, today.Goal.DailyMinutes);
        Assert.Equal(20, today.Daily.TargetMinutes);
        Assert.Equal(1200, today.Daily.DoneSeconds);
        Assert.True(today.Daily.Met);
        Assert.Equal("full", today.Status);
        Assert.Equal(1200, today.BySource.RandomPuzzleSeconds);
        Assert.Equal(1200, today.ByTheme.EndgameSeconds);
        Assert.Equal(1, today.WeekDaysMet);
        Assert.Equal(3, today.WeeklyDaysTarget);
    }

    [Fact]
    public async Task Today_PlayGamesCountWeekly_AcrossPlatforms_AndDayStatusIgnoresPlay()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(play: 5)); // Wochenziel: 5 Partien, kein Zeitziel
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        _db.PlayTimeDailies.Add(new PlayTimeDaily { UserId = u.Id, Date = today, Platform = "lichess", Games = 3 });
        _db.PlayTimeDailies.Add(new PlayTimeDaily { UserId = u.Id, Date = today, Platform = "chesscom", Games = 2 });
        await _db.SaveChangesAsync();

        var todayProgress = await _service.GetTodayAsync(u.Id);
        Assert.Equal(5, todayProgress.Play.TargetGames);
        Assert.Equal(5, todayProgress.Play.DoneGames);
        Assert.True(todayProgress.Play.Met);
        Assert.Equal("none", todayProgress.Status);     // Spielen füttert den Zeit-Topf nicht

        var day = Assert.Single((await _service.GetTrackerAsync(u.Id, 1)).Days);
        Assert.Equal(5, day.PlayGames);                 // informativ je Tag
        Assert.Equal(0, day.TotalSeconds);
        Assert.Equal("none", day.Status);
    }

    [Fact]
    public async Task GroupGoal_SetGetDelete_RoundTrips()
    {
        var g = await CreateGroupAsync("A");
        await _service.SetGroupGoalAsync(g.Id, Input(daily: 25, play: 20, weekly: 4));

        var got = await _service.GetGroupGoalAsync(g.Id);
        Assert.Equal("group", got.Source);
        Assert.Equal(25, got.DailyMinutes);
        Assert.Equal(20, got.PlayGames);

        await _service.DeleteGroupGoalAsync(g.Id);
        Assert.Equal("none", (await _service.GetGroupGoalAsync(g.Id)).Source);
    }

    // ---- Controller (Admin-Vorlage) --------------------------------------

    [Fact]
    public async Task GroupController_TrainingGoalEndpoints_Work()
    {
        var g = await CreateGroupAsync("A");
        var controller = new GroupController(_db, _service);

        var setResult = await controller.SetTrainingGoal(g.Id, Input(daily: 25)) as OkObjectResult;
        var dto = Assert.IsType<TrainingGoalDto>(setResult!.Value);
        Assert.Equal(25, dto.DailyMinutes);

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
        Assert.Equal("Vereinsabend", dto.Note);
        Assert.Single(await _db.ManualActivities.Where(m => m.UserId == u.Id).ToListAsync());
    }

    [Fact]
    public async Task AddManual_ClampsGamesAndMinutes()
    {
        var u = await CreateUserAsync();
        var game = await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OtbGame, 999));
        var study = await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflineStudy, 999));

        Assert.Equal(50, game.Amount);
        Assert.Equal(600, study.Amount);
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
        Assert.Equal("2026-06-10", list[0].Date);
    }

    [Fact]
    public async Task Today_ManualEntries_FeedSourcesAndPlay()
    {
        var u = await CreateUserAsync();
        await _service.SetPersonalGoalAsync(u.Id, Input(daily: 20, play: 3));
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflinePuzzle, 15, date: today)); // → randomPuzzle/tactics
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflineStudy, 5, date: today));   // → courseBook/other
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.Coaching, 5, date: today));       // → courseBook/other
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OtbGame, 2, date: today));        // → Spielen

        var t = await _service.GetTodayAsync(u.Id);
        Assert.Equal(15 * 60, t.BySource.RandomPuzzleSeconds);
        Assert.Equal(10 * 60, t.BySource.CourseBookSeconds);    // 5 + 5 min
        Assert.Equal(15 * 60, t.ByTheme.TacticsSeconds);
        Assert.Equal(10 * 60, t.ByTheme.OtherSeconds);
        Assert.Equal(25 * 60, t.Daily.DoneSeconds);
        Assert.Equal(2, t.Play.DoneGames);
        Assert.True(t.Daily.Met);                               // 1500 >= 1200
    }

    [Fact]
    public async Task Tracker_MarksDaysWithManualActivity()
    {
        var u = await CreateUserAsync();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await _service.AddManualAsync(u.Id, ManualInput(ManualActivityKind.OfflineStudy, 30, date: today));

        var day = Assert.Single((await _service.GetTrackerAsync(u.Id, 4)).Days);
        Assert.True(day.HasManual);
        Assert.Equal(30 * 60, day.BySource.CourseBookSeconds);
    }

    // ============ Activity Presets + Timer =============================

    [Fact]
    public async Task AddPreset_CreatesPreset_RejectsOtbGameOrEmptyLabel()
    {
        var u = await CreateUserAsync();
        var ok = await _service.AddPresetAsync(u.Id, new() { Label = " Coaching Alice ", Kind = ManualActivityKind.Coaching });
        Assert.Equal("Coaching Alice", ok.Label);
        Assert.Equal(ManualActivityKind.Coaching, ok.Kind);
        Assert.True(ok.Id > 0);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AddPresetAsync(u.Id, new() { Label = "OTB", Kind = ManualActivityKind.OtbGame }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AddPresetAsync(u.Id, new() { Label = "   ", Kind = ManualActivityKind.OfflineStudy }));
    }

    [Fact]
    public async Task ListPresets_ReturnsOnlyOwnPresets()
    {
        var a = await CreateUserAsync("a");
        var b = await CreateUserAsync("b");
        await _service.AddPresetAsync(a.Id, new() { Label = "A1", Kind = ManualActivityKind.OfflinePuzzle });
        await _service.AddPresetAsync(a.Id, new() { Label = "A2", Kind = ManualActivityKind.OfflineStudy });
        await _service.AddPresetAsync(b.Id, new() { Label = "B1", Kind = ManualActivityKind.Coaching });

        var listA = await _service.ListPresetsAsync(a.Id);
        Assert.Equal(new[] { "A1", "A2" }, listA.Select(p => p.Label).ToArray());
    }

    [Fact]
    public async Task UpdatePreset_ChangesLabelAndKind_404ForForeign()
    {
        var a = await CreateUserAsync("a");
        var b = await CreateUserAsync("b");
        var p = await _service.AddPresetAsync(a.Id, new() { Label = "old", Kind = ManualActivityKind.OfflinePuzzle });

        var updated = await _service.UpdatePresetAsync(a.Id, p.Id, new() { Label = "new", Kind = ManualActivityKind.Coaching });
        Assert.NotNull(updated);
        Assert.Equal("new", updated!.Label);
        Assert.Equal(ManualActivityKind.Coaching, updated.Kind);

        Assert.Null(await _service.UpdatePresetAsync(b.Id, p.Id, new() { Label = "hijack", Kind = ManualActivityKind.Coaching }));
    }

    [Fact]
    public async Task DeletePreset_RemovesOwn_404ForForeign()
    {
        var a = await CreateUserAsync("a");
        var b = await CreateUserAsync("b");
        var p = await _service.AddPresetAsync(a.Id, new() { Label = "x", Kind = ManualActivityKind.OfflineStudy });

        Assert.False(await _service.DeletePresetAsync(b.Id, p.Id));
        Assert.True(await _service.DeletePresetAsync(a.Id, p.Id));
        Assert.Empty(await _service.ListPresetsAsync(a.Id));
    }

    [Fact]
    public async Task StartTimer_FromPreset_SetsLabelAndKind_AndReplacesExisting()
    {
        var u = await CreateUserAsync();
        var preset = await _service.AddPresetAsync(u.Id, new() { Label = "Coaching Alice", Kind = ManualActivityKind.Coaching });

        var t1 = await _service.StartTimerAsync(u.Id, new() { PresetId = preset.Id });
        Assert.Equal("Coaching Alice", t1.Label);
        Assert.Equal(ManualActivityKind.Coaching, t1.Kind);

        // Zweites Start ersetzt still (kein Fehler, keine ManualActivity aus dem alten Timer).
        var t2 = await _service.StartTimerAsync(u.Id, new() { Label = "Book", Kind = ManualActivityKind.OfflineStudy });
        Assert.Equal("Book", t2.Label);
        Assert.Equal(1, _db.ActivityTimers.Count(x => x.UserId == u.Id));
        Assert.Empty(_db.ManualActivities.Where(m => m.UserId == u.Id));  // Ersetzung schreibt NICHTS
    }

    [Fact]
    public async Task StartTimer_Adhoc_RejectsOtbKindAndMissingLabel()
    {
        var u = await CreateUserAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StartTimerAsync(u.Id, new() { Label = "OTB", Kind = ManualActivityKind.OtbGame }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StartTimerAsync(u.Id, new() { Kind = ManualActivityKind.Coaching })); // kein Label
    }

    [Fact]
    public async Task StartTimer_UnknownPreset_Throws()
    {
        var u = await CreateUserAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StartTimerAsync(u.Id, new() { PresetId = 999 }));
    }

    [Fact]
    public async Task StopTimer_WritesManualActivity_WithRoundedMinutes_AndRemovesTimer()
    {
        var u = await CreateUserAsync();
        var start = DateTime.UtcNow.AddMinutes(-25);
        _db.ActivityTimers.Add(new ActivityTimer { UserId = u.Id, Label = "Book", Kind = ManualActivityKind.OfflineStudy, StartedAt = start });
        await _db.SaveChangesAsync();

        var saved = await _service.StopTimerAsync(u.Id, new());
        Assert.NotNull(saved);
        Assert.Equal(ManualActivityKind.OfflineStudy, saved!.Kind);
        Assert.InRange(saved.Amount, 24, 26); // ~25 min, ±1 wg. Rundung
        Assert.Contains("Book", saved.Note);
        Assert.Empty(_db.ActivityTimers.Where(t => t.UserId == u.Id));
        Assert.Single(_db.ManualActivities.Where(m => m.UserId == u.Id));
    }

    [Fact]
    public async Task StopTimer_HonoursBackdatedEndedAt()
    {
        var u = await CreateUserAsync();
        var start = DateTime.UtcNow.AddHours(-3);
        _db.ActivityTimers.Add(new ActivityTimer { UserId = u.Id, Label = "Coaching", Kind = ManualActivityKind.Coaching, StartedAt = start });
        await _db.SaveChangesAsync();

        // Backdate: als hätte der Timer nur 45 Minuten gelaufen.
        var ended = start.AddMinutes(45);
        var saved = await _service.StopTimerAsync(u.Id, new() { EndedAt = ended.ToString("o") });

        Assert.NotNull(saved);
        Assert.Equal(45, saved!.Amount);
    }

    [Fact]
    public async Task StopTimer_EndedAtBeforeStart_Throws()
    {
        var u = await CreateUserAsync();
        var start = DateTime.UtcNow;
        _db.ActivityTimers.Add(new ActivityTimer { UserId = u.Id, Label = "Book", Kind = ManualActivityKind.OfflineStudy, StartedAt = start });
        await _db.SaveChangesAsync();

        var ended = start.AddMinutes(-10);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StopTimerAsync(u.Id, new() { EndedAt = ended.ToString("o") }));
    }

    [Fact]
    public async Task StopTimer_EndedAtInFuture_ClampsToNow()
    {
        var u = await CreateUserAsync();
        var start = DateTime.UtcNow.AddMinutes(-5);
        _db.ActivityTimers.Add(new ActivityTimer { UserId = u.Id, Label = "Book", Kind = ManualActivityKind.OfflineStudy, StartedAt = start });
        await _db.SaveChangesAsync();

        var future = DateTime.UtcNow.AddHours(1);
        var saved = await _service.StopTimerAsync(u.Id, new() { EndedAt = future.ToString("o") });
        // ~5 Minuten (Now), NICHT 65 Minuten.
        Assert.InRange(saved!.Amount, 4, 6);
    }

    [Fact]
    public async Task StopTimer_NoRunningTimer_ReturnsNull()
    {
        var u = await CreateUserAsync();
        Assert.Null(await _service.StopTimerAsync(u.Id, new()));
    }

    [Fact]
    public async Task StopTimer_HonoursOverriddenStartedAt()
    {
        var u = await CreateUserAsync();
        var originalStart = DateTime.UtcNow.AddHours(-5);
        _db.ActivityTimers.Add(new ActivityTimer { UserId = u.Id, Label = "Coaching", Kind = ManualActivityKind.Coaching, StartedAt = originalStart });
        await _db.SaveChangesAsync();

        // Client hat Start nach vorn geschoben (Duration-Feld angepasst) — Server nimmt den neuen Start.
        var newStart = DateTime.UtcNow.AddMinutes(-30);
        var end = DateTime.UtcNow;
        var saved = await _service.StopTimerAsync(u.Id, new() { StartedAt = newStart.ToString("o"), EndedAt = end.ToString("o") });

        Assert.NotNull(saved);
        Assert.InRange(saved!.Amount, 29, 31);  // ~30 Minuten Dauer, nicht 5 h
    }

    [Fact]
    public async Task StopTimer_PersistsThemeOverride()
    {
        var u = await CreateUserAsync();
        _db.ActivityTimers.Add(new ActivityTimer {
            UserId = u.Id, Label = "Study", Kind = ManualActivityKind.OfflineStudy,
            Theme = ChessableTheme.Middlegame, StartedAt = DateTime.UtcNow.AddMinutes(-10),
        });
        await _db.SaveChangesAsync();

        var saved = await _service.StopTimerAsync(u.Id, new() { Theme = ChessableTheme.Endgame });
        Assert.NotNull(saved);
        Assert.Equal(ChessableTheme.Endgame, saved!.Theme);
    }

    [Fact]
    public async Task AddManual_PersistsTheme_And_ThemeDrivesBreakdown()
    {
        var u = await CreateUserAsync();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        // Coaching = default „Other"; User setzt Thema explizit auf Endgame.
        await _service.AddManualAsync(u.Id, new() {
            Date = today, Kind = ManualActivityKind.Coaching, Amount = 45, Theme = ChessableTheme.Endgame,
        });

        var list = await _service.ListManualAsync(u.Id);
        Assert.Equal(ChessableTheme.Endgame, list.Single().Theme);

        var tracker = await _service.GetTrackerAsync(u.Id, 4);
        var day = Assert.Single(tracker.Days);
        Assert.Equal(45 * 60, day.ByTheme.EndgameSeconds);
        Assert.Equal(0, day.ByTheme.OtherSeconds);  // Default „Other" wurde überschrieben
    }

    [Fact]
    public async Task DiscardTimer_RemovesWithoutCreatingManualActivity()
    {
        var u = await CreateUserAsync();
        _db.ActivityTimers.Add(new ActivityTimer { UserId = u.Id, Label = "Book", Kind = ManualActivityKind.OfflineStudy, StartedAt = DateTime.UtcNow.AddMinutes(-10) });
        await _db.SaveChangesAsync();

        Assert.True(await _service.DiscardTimerAsync(u.Id));
        Assert.Empty(_db.ActivityTimers.Where(t => t.UserId == u.Id));
        Assert.Empty(_db.ManualActivities.Where(m => m.UserId == u.Id));
        Assert.False(await _service.DiscardTimerAsync(u.Id));
    }
}
