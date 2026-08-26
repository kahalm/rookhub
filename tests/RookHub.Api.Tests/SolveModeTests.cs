using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Spielweise („training" = Brett eingefroren/höhere Visualisierungsstufe, „easy" = Figuren ziehbar)
/// je Lösungsversuch — in ALLEN Solver-Bereichen. Für Kurse, Buch-/Tagespuzzle, Endless und
/// Wochenpost ist der Modus eine eigene Spalte: er wird gespeichert, ein fehlender/unbekannter Wert
/// fällt auf „training" zurück, und Altbestand (Zeilen ohne Modus) zählt als Training.
/// <para>
/// Standard-Puzzles sind die AUSNAHME: dort gibt es keine Modus-Spalte, weil
/// <c>PuzzleAttempt.VisualizationLevel</c> die Spielweise schon vollständig bestimmt
/// (0 = „easy", &gt; 0 = „training"). Geprüft wird deshalb die Ableitung.
/// </para>
/// </summary>
public class SolveModeTests : IDisposable
{
    private readonly AppDbContext _db;

    public SolveModeTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // --- Gemeinsamer Helfer -------------------------------------------------------------

    [Theory]
    [InlineData("easy", SolveMode.Easy)]
    [InlineData("EASY", SolveMode.Easy)]
    [InlineData("  easy  ", SolveMode.Easy)]
    [InlineData("training", SolveMode.Training)]
    [InlineData("hyperspeed", SolveMode.Training)]
    [InlineData("", SolveMode.Training)]
    [InlineData(null, SolveMode.Training)]
    public void Normalize_UnknownOrMissing_FallsBackToTraining(string? input, string expected)
        => Assert.Equal(expected, SolveMode.Normalize(input));

    [Fact]
    public void WeeklyPostAttempt_ConstantsAndNormalize_DelegateToSolveMode()
    {
        // Die öffentlichen Namen der Wochenpost bleiben erhalten, zeigen aber auf SolveMode.
        Assert.Equal(SolveMode.Training, WeeklyPostAttempt.ModeTraining);
        Assert.Equal(SolveMode.Easy, WeeklyPostAttempt.ModeEasy);
        Assert.Equal(SolveMode.Easy, WeeklyPostAttempt.NormalizeMode("easy"));
        Assert.Equal(SolveMode.Training, WeeklyPostAttempt.NormalizeMode("was auch immer"));
    }

    [Theory]
    [InlineData(5, 2, 3, 2)]
    [InlineData(5, 0, 5, 0)]   // nichts „easy" → alles Training (Altbestand)
    [InlineData(0, 0, 0, 0)]
    [InlineData(3, 9, 0, 3)]   // Ausreißer werden geklemmt, nie negativ
    public void Split_CountsEverythingNotEasyAsTraining(int total, int easyCount, int expTraining, int expEasy)
    {
        var (training, easy) = SolveMode.Split(total, easyCount);
        Assert.Equal(expTraining, training);
        Assert.Equal(expEasy, easy);
    }

    // --- Standard-Puzzles ---------------------------------------------------------------

    private (PuzzleService svc, PuzzleStatsService stats) BuildPuzzleServices()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tagging = new PuzzleTaggingService(_db, NullLogger<PuzzleTaggingService>.Instance);
        return (new PuzzleService(_db, cache, NullLogger<PuzzleService>.Instance, tagging),
                new PuzzleStatsService(_db, new MemoryCache(new MemoryCacheOptions())));
    }

    private async Task<int> CreateUserAsync(string username = "solver")
    {
        var user = new AppUser { Username = username, Email = $"{username}@example.com", PasswordHash = "hash", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Puzzle> CreatePuzzleAsync()
    {
        var puzzle = new Puzzle
        {
            LichessId = Guid.NewGuid().ToString()[..8],
            Fen = "r1bqkbnr/pppppppp/2n5/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 2",
            Moves = "e2e4 e7e5",
            Rating = 1500,
            Themes = "fork",
        };
        _db.Puzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        return puzzle;
    }

    // Standard-Puzzles führen KEINE Modus-Spalte: die Spielweise folgt vollständig aus
    // VisualizationLevel (0 = „easy"/Drag & Drop, > 0 = „training"/Blindstufen). Geprüft wird
    // deshalb hier die ABLEITUNG — inklusive des Altbestands, der genau deshalb nicht pauschal
    // als „training" gelten darf.

    [Fact]
    public async Task PuzzleAttempt_VisualizationLevelZero_DerivesEasy()
    {
        var (svc, _) = BuildPuzzleServices();
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync();

        var dto = await svc.RecordAttemptAsync(userId, puzzle.Id,
            new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 5, VisualizationLevel = 0 });

        Assert.Equal(SolveMode.Easy, dto.Mode);
        Assert.Equal(0, (await _db.PuzzleAttempts.SingleAsync()).VisualizationLevel);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task PuzzleAttempt_HigherVisualizationLevel_DerivesTraining(int level)
    {
        var (svc, _) = BuildPuzzleServices();
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync();

        var dto = await svc.RecordAttemptAsync(userId, puzzle.Id,
            new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 5, VisualizationLevel = level });

        Assert.Equal(SolveMode.Training, dto.Mode);
    }

    [Fact]
    public async Task PuzzleAnonymousAttempt_DerivesModeFromVisualizationLevel()
    {
        var (svc, stats) = BuildPuzzleServices();
        var puzzle = await CreatePuzzleAsync();

        var dto = await svc.RecordAnonymousAttemptAsync("sess-1", puzzle.Id,
            new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 3, VisualizationLevel = 0 });

        Assert.Equal(SolveMode.Easy, dto.Mode);
        var anon = await stats.GetAnonymousStatsAsync("sess-1");
        Assert.Equal(0, anon.TrainingCount);
        Assert.Equal(1, anon.EasyCount);
    }

    [Fact]
    public async Task PuzzleStats_CountsPerMode_DerivedFromVisualizationLevel()
    {
        var (svc, stats) = BuildPuzzleServices();
        var userId = await CreateUserAsync();
        var p1 = await CreatePuzzleAsync();
        var p2 = await CreatePuzzleAsync();
        var p3 = await CreatePuzzleAsync();

        await svc.RecordAttemptAsync(userId, p1.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 5, VisualizationLevel = 0 });
        await svc.RecordAttemptAsync(userId, p2.Id, new RecordPuzzleAttemptDto { Solved = false, TimeSpentSeconds = 5, VisualizationLevel = 2 });
        await svc.RecordAttemptAsync(userId, p3.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 4, VisualizationLevel = 4 });

        var s = await stats.GetStatsAsync(userId);
        Assert.Equal(3, s.TotalAttempts);
        Assert.Equal(1, s.EasyCount);
        Assert.Equal(2, s.TrainingCount);
    }

    [Fact]
    public async Task PuzzleStats_LegacyAttemptWithLevelZero_CountsAsEasy_NotTraining()
    {
        // GENAU der Grund gegen eine eigene Modus-Spalte: eine per `defaultValue: "training"`
        // migrierte Spalte hätte den kompletten Altbestand als „training" ausgewiesen, obwohl er
        // überwiegend auf Stufe 0 — also einfach — gelöst wurde. Abgeleitet stimmt er rückwirkend.
        var (_, stats) = BuildPuzzleServices();
        var userId = await CreateUserAsync();
        var p1 = await CreatePuzzleAsync();
        var p2 = await CreatePuzzleAsync();

        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = userId, PuzzleId = p1.Id, Solved = true, TimeSpentSeconds = 4, VisualizationLevel = 0 });
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = userId, PuzzleId = p2.Id, Solved = true, TimeSpentSeconds = 9, VisualizationLevel = 3 });
        await _db.SaveChangesAsync();

        var s = await stats.GetStatsAsync(userId);
        Assert.Equal(1, s.EasyCount);
        Assert.Equal(1, s.TrainingCount);

        // Auch die History-Projektion (SQL-seitig) leitet den Modus ab.
        var history = await stats.GetHistoryAsync(userId, page: 1, pageSize: 10);
        Assert.Equal(SolveMode.Easy, history.Single(h => h.VisualizationLevel == 0).Mode);
        Assert.Equal(SolveMode.Training, history.Single(h => h.VisualizationLevel == 3).Mode);
    }

    [Fact]
    public void PuzzleAttempt_HasNoModeColumn()
    {
        // Absicherung gegen Rückfall: doppelt geführter Zustand (Spalte + abgeleiteter Wert)
        // könnte auseinanderlaufen — eine Zeile mit Stufe 0 und „training" wäre unauflösbar.
        Assert.Null(typeof(PuzzleAttempt).GetProperty("Mode"));
        Assert.Null(_db.Model.FindEntityType(typeof(PuzzleAttempt))!.FindProperty("Mode"));
    }

    // --- Kurse / Buchlinien -------------------------------------------------------------

    private CourseService BuildCourseService() =>
        TestServices.Course(_db);

    private async Task<(Book book, BookPuzzle p1, BookPuzzle p2, BookPuzzle p3)> SeedCourseAsync(int ownerUserId)
    {
        var book = new Book { FileName = "b.pgn", DisplayName = "B", OwnerUserId = ownerUserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var puzzles = new List<BookPuzzle>();
        foreach (var round in new[] { "001", "002", "003" })
        {
            var p = new BookPuzzle
            {
                LineId = $"p{round}",
                BookFileName = book.FileName,
                BookId = book.Id,
                Round = round,
                Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                Moves = "e2e4",
            };
            _db.BookPuzzles.Add(p);
            puzzles.Add(p);
        }
        await _db.SaveChangesAsync();
        return (book, puzzles[0], puzzles[1], puzzles[2]);
    }

    [Fact]
    public async Task CourseAttempt_StoresSolveMode_AndDoesNotCollideWithSequentialRandomMode()
    {
        var svc = BuildCourseService();
        var userId = await CreateUserAsync();
        var (book, p1, _, _) = await SeedCourseAsync(userId);

        // Mode = Durchlaufart (sequential/random), SolveMode = Spielweise — zwei verschiedene Dinge.
        await svc.RecordResultAsync(userId, book.Id,
            new RecordCourseResultDto { BookPuzzleId = p1.Id, Solved = true, TimeSeconds = 7, Mode = "sequential", SolveMode = "easy" },
            isAdmin: false);

        var stored = await _db.CourseAttempts.SingleAsync();
        Assert.Equal(SolveMode.Easy, stored.Mode);
    }

    [Fact]
    public async Task CourseAttempt_MissingOrUnknownSolveMode_FallsBackToTraining()
    {
        var svc = BuildCourseService();
        var userId = await CreateUserAsync();
        var (book, p1, p2, _) = await SeedCourseAsync(userId);

        await svc.RecordResultAsync(userId, book.Id, new RecordCourseResultDto { BookPuzzleId = p1.Id, Solved = true, TimeSeconds = 3 }, isAdmin: false);
        await svc.RecordResultAsync(userId, book.Id, new RecordCourseResultDto { BookPuzzleId = p2.Id, Solved = false, TimeSeconds = 3, SolveMode = "hyperspeed" }, isAdmin: false);

        var modes = await _db.CourseAttempts.Select(a => a.Mode).ToListAsync();
        Assert.Equal(new[] { SolveMode.Training, SolveMode.Training }, modes);
    }

    [Fact]
    public async Task CourseStats_CountsPerMode_LegacyRowsCountAsTraining()
    {
        var svc = BuildCourseService();
        var stats = new CourseStatsService(_db);
        var userId = await CreateUserAsync();
        var (book, p1, p2, p3) = await SeedCourseAsync(userId);

        await svc.RecordResultAsync(userId, book.Id, new RecordCourseResultDto { BookPuzzleId = p1.Id, Solved = true, TimeSeconds = 3, SolveMode = "easy" }, isAdmin: false);
        await svc.RecordResultAsync(userId, book.Id, new RecordCourseResultDto { BookPuzzleId = p2.Id, Solved = true, TimeSeconds = 3, SolveMode = "training" }, isAdmin: false);
        // Altbestand ohne Modus.
        _db.CourseAttempts.Add(new CourseAttempt { UserId = userId, BookId = book.Id, BookPuzzleId = p3.Id, Solved = false, TimeSeconds = 2, Mode = "" });
        await _db.SaveChangesAsync();

        var s = await stats.GetStatsAsync(userId);
        Assert.Equal(3, s.TotalAttempts);
        Assert.Equal(1, s.EasyCount);
        Assert.Equal(2, s.TrainingCount);
    }

    // --- Buch-/Tagespuzzle --------------------------------------------------------------

    private BookPuzzleService BuildBookPuzzleService() =>
        new(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue());

    private async Task<BookPuzzle> CreateBookPuzzleAsync(string lineId = "b.pgn:001")
    {
        var p = new BookPuzzle
        {
            LineId = lineId,
            BookFileName = "b.pgn",
            Round = "001",
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
            Moves = "e7e5 d2d4",
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    [Fact]
    public async Task BookPuzzleAttempt_StoresMode()
    {
        var svc = BuildBookPuzzleService();
        var userId = await CreateUserAsync();
        var puzzle = await CreateBookPuzzleAsync();

        await svc.RecordAttemptAsync(puzzle.Id, userId, new RecordBookAttemptDto { Solved = true, TimeSeconds = 6, Mode = "easy" });

        Assert.Equal(SolveMode.Easy, (await _db.BookPuzzleAttempts.SingleAsync()).Mode);
    }

    [Fact]
    public async Task BookPuzzleAttempt_MissingOrUnknownMode_FallsBackToTraining()
    {
        var svc = BuildBookPuzzleService();
        var userId = await CreateUserAsync();
        var puzzle = await CreateBookPuzzleAsync();

        await svc.RecordAttemptAsync(puzzle.Id, userId, new RecordBookAttemptDto { Solved = false, TimeSeconds = 6 });
        await svc.RecordAttemptAsync(puzzle.Id, userId, new RecordBookAttemptDto { Solved = false, TimeSeconds = 6, Mode = "hyperspeed" });

        var modes = await _db.BookPuzzleAttempts.Select(a => a.Mode).ToListAsync();
        Assert.Equal(new[] { SolveMode.Training, SolveMode.Training }, modes);
    }

    [Fact]
    public async Task BookPuzzleAnonymousAttempt_StoresMode()
    {
        var svc = BuildBookPuzzleService();
        var puzzle = await CreateBookPuzzleAsync();

        await svc.RecordAnonymousAttemptAsync(puzzle.Id, new RecordAnonymousBookAttemptDto
        {
            Solved = true,
            TimeSeconds = 4,
            Mode = "easy",
            SessionId = Guid.NewGuid().ToString(),
        });

        Assert.Equal(SolveMode.Easy, (await _db.BookPuzzleAttempts.SingleAsync()).Mode);
    }

    [Fact]
    public async Task BookPuzzleResults_CountSolversPerMode_LegacyRowsCountAsTraining()
    {
        var svc = BuildBookPuzzleService();
        var easyUser = await CreateUserAsync("easyuser");
        var trainUser = await CreateUserAsync("trainuser");
        var legacyUser = await CreateUserAsync("legacyuser");
        var puzzle = await CreateBookPuzzleAsync();

        await svc.RecordAttemptAsync(puzzle.Id, easyUser, new RecordBookAttemptDto { Solved = true, TimeSeconds = 5, Mode = "easy" });
        await svc.RecordAttemptAsync(puzzle.Id, trainUser, new RecordBookAttemptDto { Solved = true, TimeSeconds = 5, Mode = "training" });
        // Altbestand ohne Modus.
        _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt
        {
            BookPuzzleId = puzzle.Id, UserId = legacyUser, Solved = true, TimeSeconds = 5, AttemptedAt = DateTime.UtcNow, Mode = "",
        });
        await _db.SaveChangesAsync();

        var results = await svc.GetResultsAsync(puzzle.Id, since: null);
        Assert.Equal(3, results.SolvedCount);
        Assert.Equal(1, results.EasyCount);
        Assert.Equal(2, results.TrainingCount);
        Assert.Equal(SolveMode.Easy, results.Solvers.Single(s => s.Name == "easyuser").Mode);
        Assert.Equal(SolveMode.Training, results.Solvers.Single(s => s.Name == "legacyuser").Mode);
    }

    // --- Endless ------------------------------------------------------------------------

    private EndlessProgressService BuildEndlessService() =>
        new(_db, NullLogger<EndlessProgressService>.Instance);

    private static RecordEndlessSessionDto EndlessDto(long timestamp, string? mode) => new()
    {
        Timestamp = timestamp,
        TotalSolved = 4,
        MaxRating = 1300,
        DurationSeconds = 120,
        ConfigJson = "{}",
        MistakeAtRatings = "",
        Mode = mode,
    };

    [Fact]
    public async Task EndlessSession_StoresMode()
    {
        var svc = BuildEndlessService();
        var userId = await CreateUserAsync();

        var dto = await svc.RecordSessionAsync(userId, EndlessDto(1000, "easy"));

        Assert.Equal(SolveMode.Easy, dto.Mode);
        Assert.Equal(SolveMode.Easy, (await _db.EndlessSessions.SingleAsync()).Mode);
    }

    [Fact]
    public async Task EndlessSession_MissingOrUnknownMode_FallsBackToTraining()
    {
        var svc = BuildEndlessService();
        var userId = await CreateUserAsync();

        await svc.RecordSessionAsync(userId, EndlessDto(1000, null));
        await svc.RecordSessionAsync(userId, EndlessDto(2000, "hyperspeed"));
        await svc.RecordAnonymousSessionAsync(Guid.NewGuid().ToString(), EndlessDto(3000, null));

        var modes = await _db.EndlessSessions.Select(s => s.Mode).ToListAsync();
        Assert.All(modes, m => Assert.Equal(SolveMode.Training, m));
    }

    [Fact]
    public async Task EndlessBulkImport_StoresMode()
    {
        var svc = BuildEndlessService();
        var userId = await CreateUserAsync();

        await svc.BulkImportSessionsAsync(userId, new List<RecordEndlessSessionDto> { EndlessDto(1000, "easy"), EndlessDto(2000, null) });

        var modes = await _db.EndlessSessions.OrderBy(s => s.Timestamp).Select(s => s.Mode).ToListAsync();
        Assert.Equal(new[] { SolveMode.Easy, SolveMode.Training }, modes);
    }

    [Fact]
    public async Task EndlessHistory_CountsRunsPerMode_LegacyRowsCountAsTraining()
    {
        var svc = BuildEndlessService();
        var userId = await CreateUserAsync();

        await svc.RecordSessionAsync(userId, EndlessDto(1000, "easy"));
        await svc.RecordSessionAsync(userId, EndlessDto(2000, "training"));
        // Altbestand ohne Modus.
        _db.EndlessSessions.Add(new EndlessSession { UserId = userId, Timestamp = 3000, ConfigJson = "{}", Mode = "" });
        await _db.SaveChangesAsync();

        var history = await svc.GetSessionHistoryAsync(userId, page: 1, pageSize: 2);
        Assert.Equal(3, history.TotalCount);
        Assert.Equal(2, history.Items.Count);     // Zähler gelten für den GANZEN Bestand, nicht nur die Seite
        Assert.Equal(1, history.EasyCount);
        Assert.Equal(2, history.TrainingCount);
    }
}
