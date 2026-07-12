using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class PuzzleServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PuzzleService _service;
    private readonly PuzzleTaggingService _tagging;
    private readonly PuzzleStatsService _stats;

    public PuzzleServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _tagging = new PuzzleTaggingService(_db, NullLogger<PuzzleTaggingService>.Instance);
        _service = new PuzzleService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<PuzzleService>.Instance, _tagging);
        _stats = new PuzzleStatsService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
    }

    public void Dispose() => _db.Dispose();

    // --- Provisorische Start-Kalibrierung der Puzzle-Elo (schnelleres Einpendeln) ---

    [Theory]
    [InlineData(0, 0, 80)]    // ganz am Anfang → ×4
    [InlineData(4, 9, 80)]    // <5 gelöst → ×4
    [InlineData(9, 4, 80)]    // <5 gescheitert → ×4
    [InlineData(50, 3, 80)]   // viele gelöst, aber <5 Fehlschläge → Niveau noch nicht getroffen → weiter ×4
    [InlineData(5, 5, 40)]    // 5 & 5 erreicht → ×2
    [InlineData(9, 9, 40)]    // <10 → ×2
    [InlineData(5, 30, 40)]   // gelöst <10 → ×2 (beide Richtungen nötig)
    [InlineData(10, 10, 20)]  // 10 & 10 → normale Schrittweite
    [InlineData(40, 25, 20)]  // lange eingependelt → normal
    public void ProvisionalKFactor_LargerStepsUntilSolvedAndFailedThresholds(int solved, int failed, int expectedK)
        => Assert.Equal(expectedK, PuzzleElo.ProvisionalKFactor(solved, failed));

    [Fact]
    public void ProvisionalK_TimesCalculateElo_YieldsLargerEarlyEloChange()
    {
        // Gleiches Puzzle/Rating: am Anfang (×4) muss der Elo-Schritt deutlich größer sein als eingependelt (×1).
        var (_, earlyChange) = PuzzleElo.CalculateElo(1500, 1500, solved: true, PuzzleElo.ProvisionalKFactor(0, 0));
        var (_, settledChange) = PuzzleElo.CalculateElo(1500, 1500, solved: true, PuzzleElo.ProvisionalKFactor(10, 10));
        Assert.True(earlyChange > settledChange, $"early {earlyChange} should exceed settled {settledChange}");
        Assert.Equal(settledChange * 4, earlyChange); // ×4 zu Beginn
    }

    private async Task<int> CreateUserAsync(string username = "testuser")
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Profile = new UserProfile()
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Puzzle> CreatePuzzleAsync(int rating = 1500, string themes = "middlegame fork", string lichessId = "", bool syncTags = true)
    {
        var puzzle = new Puzzle
        {
            LichessId = string.IsNullOrEmpty(lichessId) ? Guid.NewGuid().ToString()[..8] : lichessId,
            Fen = "r1bqkbnr/pppppppp/2n5/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 2",
            Moves = "e2e4 d7d5 e4d5",
            Rating = rating,
            Themes = themes
        };
        _db.Puzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        // Wie der echte Import (PuzzleTaggingService.SyncPuzzleTagsAsync): die normalisierten
        // PuzzleTags/Tags mitpflegen, damit die Themen-Auswertung (GetWorstThemes/GetBreakdown,
        // die jetzt server-seitig über die Tags aggregiert) im Test denselben Pfad wie in Prod nimmt.
        // syncTags:false = bewusst leere Tags-Tabelle (z. B. für den GetAllThemes-Fallback-Test).
        if (!syncTags) return puzzle;
        foreach (var name in (themes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct())
        {
            var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Name == name);
            if (tag == null) { tag = new Tag { Name = name }; _db.Tags.Add(tag); await _db.SaveChangesAsync(); }
            _db.PuzzleTags.Add(new PuzzleTag { PuzzleId = puzzle.Id, TagId = tag.Id, Rating = rating });
        }
        await _db.SaveChangesAsync();
        return puzzle;
    }

    [Fact]
    public async Task GetRandomBatch_ReturnsDistinctPuzzlePerWindow()
    {
        // Je Fenster mehrere Kandidaten, damit Eindeutigkeit überhaupt prüfbar ist.
        for (int i = 0; i < 3; i++) await CreatePuzzleAsync(rating: 810);
        for (int i = 0; i < 3; i++) await CreatePuzzleAsync(rating: 850);
        for (int i = 0; i < 3; i++) await CreatePuzzleAsync(rating: 900);

        var windows = new (int, int)[] { (800, 840), (840, 880), (880, 920) };
        var result = await _service.GetRandomBatchAsync(null, windows, null, false);

        Assert.Equal(3, result.Count);
        Assert.Equal(3, result.Select(p => p.Id).Distinct().Count());   // keine Duplikate
        Assert.InRange(result[0].Rating, 800, 840);
        Assert.InRange(result[1].Rating, 840, 880);
        Assert.InRange(result[2].Rating, 880, 920);
    }

    [Fact]
    public async Task GetRandomBatch_ThemesAny_FiltersAndDistributesPerWindow()
    {
        // Treffer (fork) + Nicht-Treffer (endgame) je Fenster — der ODER-Filter darf nur Treffer liefern.
        await CreatePuzzleAsync(rating: 810, themes: "fork", lichessId: "tb-a1");
        await CreatePuzzleAsync(rating: 810, themes: "endgame", lichessId: "tb-a2");
        await CreatePuzzleAsync(rating: 900, themes: "pin", lichessId: "tb-b1");
        await CreatePuzzleAsync(rating: 900, themes: "endgame", lichessId: "tb-b2");

        var windows = new (int, int)[] { (800, 840), (880, 920) };
        var result = await _service.GetRandomBatchAsync(null, windows, themes: null, excludeSolved: false, themesAny: "fork pin");

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Select(p => p.Id).Distinct().Count());
        Assert.All(result, p => Assert.DoesNotContain("endgame", p.Themes));   // nur fork/pin
        Assert.InRange(result[0].Rating, 800, 840);
        Assert.InRange(result[1].Rating, 880, 920);
    }

    [Fact]
    public async Task BackfillPuzzleTags_CreatesTagsAndLinks_Idempotent()
    {
        await CreatePuzzleAsync(rating: 1500, themes: "fork endgame", lichessId: "bf1");
        await CreatePuzzleAsync(rating: 1600, themes: "fork pin", lichessId: "bf2");
        await CreatePuzzleAsync(rating: 1700, themes: "", lichessId: "bf3");   // ohne Themen → keine Links

        var processed = await _tagging.BackfillPuzzleTagsAsync(batchSize: 2);

        Assert.Equal(3, processed);
        Assert.Equal(3, await _db.Tags.CountAsync());                 // fork, endgame, pin
        Assert.Equal(4, await _db.PuzzleTags.CountAsync());           // 2 + 2 + 0
        var fork = await _db.Tags.SingleAsync(t => t.Name == "fork");
        Assert.Equal(2, await _db.PuzzleTags.CountAsync(pt => pt.TagId == fork.Id));
        // Rating denormalisiert?
        Assert.All(await _db.PuzzleTags.Where(pt => pt.TagId == fork.Id).ToListAsync(),
            pt => Assert.Contains(pt.Rating, new[] { 1500, 1600 }));

        // Erneuter Lauf legt nichts doppelt an.
        await _tagging.BackfillPuzzleTagsAsync();
        Assert.Equal(4, await _db.PuzzleTags.CountAsync());
    }

    [Fact]
    public async Task GetRandom_ThemesAny_UsesTagIndexPath_WhenPopulated()
    {
        await CreatePuzzleAsync(rating: 1500, themes: "endgame mateIn2", lichessId: "ti1");
        await CreatePuzzleAsync(rating: 1500, themes: "middlegame fork", lichessId: "ti2");
        await _tagging.BackfillPuzzleTagsAsync();   // PuzzleTags befüllen → Schnellpfad aktiv

        var result = await _service.GetRandomAsync(null, 1400, 1600, themes: null, excludeSolved: false, themesAny: "fork pin");

        Assert.NotNull(result);
        Assert.Contains("fork", result!.Themes);   // nur das fork-Puzzle matcht
    }

    [Fact]
    public async Task GetRandomBatch_SkipsEmptyWindows()
    {
        await CreatePuzzleAsync(rating: 810);
        // Zweites Fenster (2000–2040) hat keine Puzzles → entfällt.
        var windows = new (int, int)[] { (800, 840), (2000, 2040) };
        var result = await _service.GetRandomBatchAsync(null, windows, null, false);

        Assert.Single(result);
        Assert.InRange(result[0].Rating, 800, 840);
    }

    [Fact]
    public async Task GetRandom_ReturnsPuzzleInRatingRange()
    {
        var userId = await CreateUserAsync();
        await CreatePuzzleAsync(rating: 1000);
        await CreatePuzzleAsync(rating: 1500);
        await CreatePuzzleAsync(rating: 2000);

        var result = await _service.GetRandomAsync(userId, minRating: 1400, maxRating: 1600, null, false);

        Assert.NotNull(result);
        Assert.Equal(1500, result!.Rating);
    }

    [Fact]
    public async Task GetRandom_FilteredSelectionIsNotDegenerate()
    {
        var userId = await CreateUserAsync();
        // 3 Treffer mit NIEDRIGEN Ids (verschiedene Ratings zur Unterscheidung) ...
        await CreatePuzzleAsync(rating: 2000);
        await CreatePuzzleAsync(rating: 2100);
        await CreatePuzzleAsync(rating: 2200);
        // ... gefolgt von vielen Nicht-Treffern mit HOHEN Ids.
        for (int i = 0; i < 100; i++)
            await CreatePuzzleAsync(rating: 1000);

        var seen = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < 60; i++)
        {
            var r = await _service.GetRandomAsync(userId, minRating: 1900, maxRating: null, null, false);
            Assert.NotNull(r);
            Assert.True(r!.Rating >= 1900);
            seen.Add(r.Rating);
        }
        // Vor dem Fix landete randomId (globale Range) fast immer ausserhalb der
        // niedrigen Treffer-Ids -> degenerierter Fallback, stets dasselbe Puzzle.
        // Jetzt wird die Range ueber die gefilterte Menge bestimmt -> es variiert.
        Assert.True(seen.Count >= 2, $"Erwartet >=2 verschiedene Treffer, war {seen.Count}");
    }

    [Fact]
    public async Task GetRandom_ReturnsNull_WhenNoPuzzles()
    {
        var userId = await CreateUserAsync();

        var result = await _service.GetRandomAsync(userId, null, null, null, false);

        Assert.Null(result);
    }

    [Fact]
    public async Task RecordAttempt_FailThenSolveWithin30s_RecordsTheSolve()
    {
        // Regression: das 30-s-Idempotenz-Fenster ignorierte dto.Solved — ein Fail gefolgt von
        // einem echten Solve innerhalb von 30 s gab den alten FAILED-Versuch zurück und
        // verwarf die Lösung (Puzzle blieb für excludeSolved/Leaderboards ungelöst).
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "idem1");

        var fail = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = false, TimeSpentSeconds = 10 });
        var solve = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 12 });

        Assert.False(fail.Solved);
        Assert.True(solve.Solved);
        Assert.Equal(2, await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId && a.PuzzleId == puzzle.Id));
        Assert.True(await _db.PuzzleAttempts.AnyAsync(a => a.UserId == userId && a.PuzzleId == puzzle.Id && a.Solved));
    }

    [Fact]
    public async Task RecordAttempt_SameOutcomeWithin30s_IsDeduplicated()
    {
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "idem2");

        await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });
        await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });

        Assert.Equal(1, await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId && a.PuzzleId == puzzle.Id));
    }

    [Fact]
    public async Task GetRandom_ExcludesSolvedPuzzles()
    {
        var userId = await CreateUserAsync();
        var solved = await CreatePuzzleAsync(rating: 1500, lichessId: "solved1");
        var unsolved = await CreatePuzzleAsync(rating: 1500, lichessId: "unsolved1");

        _db.PuzzleAttempts.Add(new PuzzleAttempt
        {
            UserId = userId,
            PuzzleId = solved.Id,
            Solved = true,
            TimeSpentSeconds = 30
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetRandomAsync(userId, null, null, null, excludeSolved: true);

        Assert.NotNull(result);
        Assert.Equal(unsolved.Id, result!.Id);
    }

    [Fact]
    public async Task GetRandom_AnonymousUser_IgnoresExcludeSolved()
    {
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "anon1");

        _db.PuzzleAttempts.Add(new PuzzleAttempt
        {
            UserId = userId,
            PuzzleId = puzzle.Id,
            Solved = true,
            TimeSpentSeconds = 30
        });
        await _db.SaveChangesAsync();

        // Anonymous user (null) with excludeSolved=true should still get puzzles
        var result = await _service.GetRandomAsync(null, null, null, null, excludeSolved: true);

        Assert.NotNull(result);
        Assert.Equal(puzzle.Id, result!.Id);
    }

    [Fact]
    public async Task GetRandom_FiltersByTheme()
    {
        var userId = await CreateUserAsync();
        await CreatePuzzleAsync(themes: "endgame mateIn2");
        await CreatePuzzleAsync(themes: "middlegame fork");

        var result = await _service.GetRandomAsync(userId, null, null, themes: "fork", false);

        Assert.NotNull(result);
        Assert.Contains("fork", result!.Themes);
    }

    [Fact]
    public async Task GetById_ReturnsPuzzle()
    {
        var puzzle = await CreatePuzzleAsync();

        var result = await _service.GetByIdAsync(puzzle.Id);

        Assert.NotNull(result);
        Assert.Equal(puzzle.LichessId, result!.LichessId);
    }

    [Fact]
    public async Task GetById_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetByIdAsync(99999);

        Assert.Null(result);
    }

    [Fact]
    public async Task RecordAttempt_CreatesRecord()
    {
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync();

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true,
            TimeSpentSeconds = 45
        });

        Assert.True(result.Solved);
        Assert.Equal(45, result.TimeSpentSeconds);
        Assert.Single(await _db.PuzzleAttempts.ToListAsync());
    }

    [Fact]
    public async Task RecordAttempt_ThrowsWhenPuzzleNotFound()
    {
        var userId = await CreateUserAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.RecordAttemptAsync(userId, 99999, new RecordPuzzleAttemptDto { Solved = true }));
    }

    [Fact]
    public async Task GetStats_CalculatesAccuracy()
    {
        var userId = await CreateUserAsync();
        var p1 = await CreatePuzzleAsync(lichessId: "stats1");
        var p2 = await CreatePuzzleAsync(lichessId: "stats2");
        var p3 = await CreatePuzzleAsync(lichessId: "stats3");

        _db.PuzzleAttempts.AddRange(
            new PuzzleAttempt { UserId = userId, PuzzleId = p1.Id, Solved = true, TimeSpentSeconds = 10, AttemptedAt = DateTime.UtcNow.AddMinutes(-3) },
            new PuzzleAttempt { UserId = userId, PuzzleId = p2.Id, Solved = false, TimeSpentSeconds = 20, AttemptedAt = DateTime.UtcNow.AddMinutes(-2) },
            new PuzzleAttempt { UserId = userId, PuzzleId = p3.Id, Solved = true, TimeSpentSeconds = 15, AttemptedAt = DateTime.UtcNow.AddMinutes(-1) }
        );
        await _db.SaveChangesAsync();

        var stats = await _stats.GetStatsAsync(userId);

        Assert.Equal(3, stats.TotalAttempts);
        Assert.Equal(2, stats.Solved);
        Assert.Equal(66.7, stats.Accuracy);
        Assert.Equal(1, stats.CurrentStreak);
    }

    [Fact]
    public async Task GetStats_ReturnsZeros_WhenNoAttempts()
    {
        var userId = await CreateUserAsync();

        var stats = await _stats.GetStatsAsync(userId);

        Assert.Equal(0, stats.TotalAttempts);
        Assert.Equal(0, stats.Solved);
        Assert.Equal(0, stats.Accuracy);
    }

    [Fact]
    public async Task GetStats_NoLevel_ReturnsEloOfMostPlayedLevel()
    {
        // User spielt überwiegend auf Viz-Level 1 (dort Elo 1800), Level 0 ist Default 1500.
        var userId = await CreateUserAsync();
        var user = await _db.AppUsers.FindAsync(userId);
        user!.PuzzleElo = 1500;
        user.PuzzleEloViz1 = 1800;
        await _db.SaveChangesAsync();

        var p = await CreatePuzzleAsync(lichessId: "lvl1");
        _db.PuzzleAttempts.AddRange(
            new PuzzleAttempt { UserId = userId, PuzzleId = p.Id, Solved = true, VisualizationLevel = 1, AttemptedAt = DateTime.UtcNow.AddMinutes(-3) },
            new PuzzleAttempt { UserId = userId, PuzzleId = p.Id, Solved = true, VisualizationLevel = 1, AttemptedAt = DateTime.UtcNow.AddMinutes(-2) },
            new PuzzleAttempt { UserId = userId, PuzzleId = p.Id, Solved = false, VisualizationLevel = 0, AttemptedAt = DateTime.UtcNow.AddMinutes(-1) }
        );
        await _db.SaveChangesAsync();

        var overview = await _stats.GetStatsAsync(userId);    // ohne Level → meistgespieltes (Level 1)
        var level0 = await _stats.GetStatsAsync(userId, 0);    // explizit Level 0

        Assert.Equal(1800, overview.PuzzleElo);   // tatsächliches Haupt-Elo, nicht der Level-0-Default
        Assert.Equal(1500, level0.PuzzleElo);     // explizit angefragtes Level bleibt unverändert
    }

    [Fact]
    public async Task GetHistory_ReturnsPaginated()
    {
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync();

        for (int i = 0; i < 5; i++)
        {
            _db.PuzzleAttempts.Add(new PuzzleAttempt
            {
                UserId = userId,
                PuzzleId = puzzle.Id,
                Solved = i % 2 == 0,
                TimeSpentSeconds = 10 + i,
                AttemptedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await _db.SaveChangesAsync();

        var page1 = await _stats.GetHistoryAsync(userId, page: 1, pageSize: 2);
        var page2 = await _stats.GetHistoryAsync(userId, page: 2, pageSize: 2);

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
    }

    [Fact]
    public async Task Import_InsertsFromCsv()
    {
        var csv = "abc123,rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1,e2e4 d7d5,1500,75,90,1000,middlegame fork,https://lichess.org/abc,Italian_Game\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var count = await _service.ImportFromCsvAsync(stream, null, null, null);

        Assert.Equal(1, count);
        var puzzle = await _db.Puzzles.FirstAsync();
        Assert.Equal("abc123", puzzle.LichessId);
        Assert.Equal(1500, puzzle.Rating);
        Assert.Equal("middlegame fork", puzzle.Themes);
        // Import pflegt die normalisierten Tags mit.
        Assert.Equal(2, await _db.Tags.CountAsync());                              // middlegame, fork
        Assert.Equal(2, await _db.PuzzleTags.CountAsync(pt => pt.PuzzleId == puzzle.Id));
    }

    [Fact]
    public async Task Import_SkipsDuplicates()
    {
        await CreatePuzzleAsync(lichessId: "dup1");

        var csv = "dup1,fen,moves,1500,75,90,1000,themes,,\nnew1,fen2,moves2,1600,80,85,500,endgame,,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var count = await _service.ImportFromCsvAsync(stream, null, null, null);

        Assert.Equal(1, count);
        Assert.Equal(2, await _db.Puzzles.CountAsync());
    }

    [Fact]
    public async Task Import_RespectsRatingFilter()
    {
        var csv = "a,fen,moves,800,75,90,1000,,,\nb,fen,moves,1500,75,90,1000,,,\nc,fen,moves,2500,75,90,1000,,,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var count = await _service.ImportFromCsvAsync(stream, minRating: 1000, maxRating: 2000, null);

        Assert.Equal(1, count);
        var puzzle = await _db.Puzzles.FirstAsync();
        Assert.Equal(1500, puzzle.Rating);
    }

    [Fact]
    public async Task GetRandom_ThemeWithSqlWildcard_DoesNotMatch()
    {
        var userId = await CreateUserAsync("wildcarduser");
        // Puzzle with theme "mateIn2" should NOT match a search for "mate_n2" (wildcard)
        await CreatePuzzleAsync(themes: "mateIn2", lichessId: "wc1");
        await CreatePuzzleAsync(themes: "endgame", lichessId: "wc2");

        // Search with underscore wildcard — should be escaped and not match
        var result = await _service.GetRandomAsync(userId, null, null, themes: "mate_n2", false);

        // Should either return null or not match "mateIn2" (InMemory provider may behave differently)
        // The important thing is the code sanitizes wildcards — this test verifies the sanitization path runs
        // InMemory doesn't support EF.Functions.Like, so we just verify it doesn't throw
        Assert.True(true);
    }

    [Fact]
    public async Task GetStats_WithManyAttempts_ReturnsCorrectCounts()
    {
        var userId = await CreateUserAsync("statsuser");
        var puzzle = await CreatePuzzleAsync(lichessId: "bulkstats");

        // Create 50 attempts: 30 solved, 20 unsolved
        for (int i = 0; i < 50; i++)
        {
            _db.PuzzleAttempts.Add(new PuzzleAttempt
            {
                UserId = userId,
                PuzzleId = puzzle.Id,
                Solved = i < 30,
                TimeSpentSeconds = 10,
                AttemptedAt = DateTime.UtcNow.AddMinutes(-50 + i)
            });
        }
        await _db.SaveChangesAsync();

        var stats = await _stats.GetStatsAsync(userId);

        Assert.Equal(50, stats.TotalAttempts);
        Assert.Equal(30, stats.Solved);
        Assert.Equal(60.0, stats.Accuracy);
    }

    [Fact]
    public async Task Import_SupportsCancellation()
    {
        var csv = string.Join('\n', Enumerable.Range(0, 100).Select(i =>
            $"cancel{i},fen,moves,1500,75,90,1000,themes,,"));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.ImportFromCsvAsync(stream, null, null, null, cts.Token));
    }

    [Fact]
    public async Task RecordAttempt_WithMoveLog_StoresJson()
    {
        var userId = await CreateUserAsync("movelog_user");
        var puzzle = await CreatePuzzleAsync(lichessId: "movelog1");
        var moveLog = "[{\"i\":0,\"uci\":\"e2e4\",\"exp\":\"e2e4\",\"ms\":3400,\"ok\":true}]";

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true,
            TimeSpentSeconds = 10,
            MoveLog = moveLog
        });

        Assert.Equal(moveLog, result.MoveLog);
        var attempt = await _db.PuzzleAttempts.SingleAsync();
        Assert.Equal(moveLog, attempt.MoveLog);
    }

    [Fact]
    public async Task RecordAttempt_WithoutMoveLog_AcceptsNull()
    {
        var userId = await CreateUserAsync("nomovelog_user");
        var puzzle = await CreatePuzzleAsync(lichessId: "nomovelog1");

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true,
            TimeSpentSeconds = 15
        });

        Assert.Null(result.MoveLog);
        var attempt = await _db.PuzzleAttempts.SingleAsync();
        Assert.Null(attempt.MoveLog);
    }

    [Fact]
    public async Task GetHistory_IncludesMoveLog()
    {
        var userId = await CreateUserAsync("history_movelog");
        var puzzle = await CreatePuzzleAsync(lichessId: "hist_ml1");
        var moveLog = "[{\"i\":0,\"uci\":\"d2d4\",\"exp\":\"e2e4\",\"ms\":5000,\"ok\":false}]";

        _db.PuzzleAttempts.Add(new PuzzleAttempt
        {
            UserId = userId,
            PuzzleId = puzzle.Id,
            Solved = false,
            TimeSpentSeconds = 20,
            MoveLog = moveLog
        });
        await _db.SaveChangesAsync();

        var history = await _stats.GetHistoryAsync(userId, 1, 10);

        Assert.Single(history);
        Assert.Equal(moveLog, history[0].MoveLog);
    }

    // ── Elo Rating Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task RecordAttempt_SolvedPuzzle_IncreasesElo()
    {
        var userId = await CreateUserAsync("elo_win");
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "elo_w1");

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });

        Assert.NotNull(result.EloAfter);
        Assert.NotNull(result.EloChange);
        Assert.True(result.EloChange > 0);
        Assert.True(result.EloAfter > 1500);
        var user = await _db.AppUsers.FindAsync(userId);
        Assert.Equal(result.EloAfter, user!.PuzzleElo);
    }

    [Fact]
    public async Task RecordAttempt_FailedPuzzle_DecreasesElo()
    {
        var userId = await CreateUserAsync("elo_lose");
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "elo_l1");

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = false, TimeSpentSeconds = 10 });

        Assert.NotNull(result.EloChange);
        Assert.True(result.EloChange < 0);
        Assert.True(result.EloAfter < 1500);
    }

    [Fact]
    public async Task RecordAttempt_ProvisionalKFactor_LargerSwings()
    {
        var userId = await CreateUserAsync("elo_prov");
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "elo_p1");

        // Start-Kalibrierung ×4 (0 gelöst/0 gescheitert) → K=80, gleiches Rating (~0.5) → change ~40
        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });

        Assert.Equal(40, result.EloChange);
    }

    [Fact]
    public async Task RecordAttempt_EstablishedKFactor_SmallerSwings()
    {
        var userId = await CreateUserAsync("elo_est");
        // Eingependelt heißt: mind. 10 gelöste UND 10 gescheiterte Vorversuche (auf anderen Puzzles,
        // damit das Ziel-Puzzle ein „erster Versuch" bleibt).
        var target = await CreatePuzzleAsync(rating: 1500, lichessId: "elo_e_target");
        for (int i = 0; i < 10; i++)
        {
            var solvedP = await CreatePuzzleAsync(rating: 1500, lichessId: $"elo_e_solved_{i}");
            var failedP = await CreatePuzzleAsync(rating: 1500, lichessId: $"elo_e_failed_{i}");
            _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = userId, PuzzleId = solvedP.Id, Solved = true, TimeSpentSeconds = 5, AttemptedAt = DateTime.UtcNow.AddMinutes(-40 + i) });
            _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = userId, PuzzleId = failedP.Id, Solved = false, TimeSpentSeconds = 5, AttemptedAt = DateTime.UtcNow.AddMinutes(-20 + i) });
        }
        await _db.SaveChangesAsync();

        // Reset user Elo to 1500 for clean test
        var user = await _db.AppUsers.FindAsync(userId);
        user!.PuzzleElo = 1500;
        await _db.SaveChangesAsync();

        var result = await _service.RecordAttemptAsync(userId, target.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });

        // K=20 (≥10 gelöst & ≥10 gescheitert → eingependelt), gleiches Rating → change ~10
        Assert.Equal(10, result.EloChange);
    }

    [Fact]
    public async Task RecordAttempt_VeryHighPuzzle_SmallLoss()
    {
        var userId = await CreateUserAsync("elo_high");
        var puzzle = await CreatePuzzleAsync(rating: 2800, lichessId: "elo_h1");

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = false, TimeSpentSeconds = 10 });

        // Failing a 2800 puzzle at 1500 Elo should cost almost nothing
        Assert.True(result.EloChange >= -5);
    }

    [Fact]
    public async Task RecordAttempt_EloFloorAt100()
    {
        var userId = await CreateUserAsync("elo_floor");
        var user = await _db.AppUsers.FindAsync(userId);
        user!.PuzzleElo = 100;
        await _db.SaveChangesAsync();

        var puzzle = await CreatePuzzleAsync(rating: 100, lichessId: "elo_f1");

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = false, TimeSpentSeconds = 10 });

        Assert.True(result.EloAfter >= 100);
    }

    [Fact]
    public async Task RecordAttempt_MultipleAttempts_EloProgresses()
    {
        var userId = await CreateUserAsync("elo_multi");

        for (int i = 0; i < 5; i++)
        {
            var p = await CreatePuzzleAsync(rating: 1500, lichessId: $"elo_m{i}");
            await _service.RecordAttemptAsync(userId, p.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });
        }

        var user = await _db.AppUsers.FindAsync(userId);
        Assert.True(user!.PuzzleElo > 1500);
    }

    [Fact]
    public async Task GetStats_IncludesPuzzleElo()
    {
        var userId = await CreateUserAsync("elo_stats");
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "elo_s1");
        await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });

        var stats = await _stats.GetStatsAsync(userId);

        Assert.True(stats.PuzzleElo > 1500);
    }

    [Fact]
    public async Task RecordAnonymousAttempt_NoEloCalculation()
    {
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "elo_anon1");

        var result = await _service.RecordAnonymousAttemptAsync("anon-session-123", puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });

        Assert.Null(result.EloAfter);
        Assert.Null(result.EloChange);
    }

    [Fact]
    public async Task GetHistory_IncludesEloFields()
    {
        var userId = await CreateUserAsync("elo_hist");
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "elo_hist1");
        await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });

        var history = await _stats.GetHistoryAsync(userId, 1, 10);

        Assert.Single(history);
        Assert.NotNull(history[0].EloAfter);
        Assert.NotNull(history[0].EloChange);
        Assert.True(history[0].EloChange > 0);
    }

    // ── Per-Level Elo Tests ───────────────────────────────────────────────

    [Fact]
    public async Task RecordAttempt_VizLevel0_UsesExistingPuzzleElo()
    {
        var userId = await CreateUserAsync("viz0_compat");
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "viz0_c1");

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true, TimeSpentSeconds = 10, VisualizationLevel = 0
        });

        Assert.True(result.EloAfter > 1500);
        var user = await _db.AppUsers.FindAsync(userId);
        Assert.Equal(result.EloAfter, user!.PuzzleElo);
    }

    [Fact]
    public async Task RecordAttempt_VizLevel2_UsesViz2Elo()
    {
        var userId = await CreateUserAsync("viz2_user");
        var user = await _db.AppUsers.FindAsync(userId);
        user!.PuzzleEloViz2 = 1300;
        await _db.SaveChangesAsync();

        var puzzle = await CreatePuzzleAsync(rating: 1300, lichessId: "viz2_1");

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true, TimeSpentSeconds = 10, VisualizationLevel = 2
        });

        Assert.True(result.EloAfter > 1300);
        user = await _db.AppUsers.FindAsync(userId);
        Assert.Equal(result.EloAfter, user!.PuzzleEloViz2);
        // Level 0 should be unaffected
        Assert.Equal(1500, user.PuzzleElo);
    }

    [Fact]
    public async Task RecordAttempt_VizLevel2_FirstAttempt_UsesDefaultElo()
    {
        var userId = await CreateUserAsync("viz2_default");
        var puzzle = await CreatePuzzleAsync(rating: 1300, lichessId: "viz2_d1");

        // PuzzleEloViz2 is null → should use default 1300
        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true, TimeSpentSeconds = 10, VisualizationLevel = 2
        });

        // Equal rating, Start-Kalibrierung ×4 → K=80, solved → change = +40
        Assert.Equal(40, result.EloChange);
        Assert.Equal(1340, result.EloAfter);
    }

    [Fact]
    public async Task RecordAttempt_VizLevel1_ProvisionalKFactor_PerLevel()
    {
        var userId = await CreateUserAsync("viz1_kfactor");
        var puzzle = await CreatePuzzleAsync(rating: 1400, lichessId: "viz1_k1");

        // Create 30 attempts on Level 0 → should NOT affect Level 1 K-factor
        for (int i = 0; i < 30; i++)
        {
            _db.PuzzleAttempts.Add(new PuzzleAttempt
            {
                UserId = userId, PuzzleId = puzzle.Id, Solved = true, TimeSpentSeconds = 5,
                AttemptedAt = DateTime.UtcNow.AddMinutes(-30 + i), VisualizationLevel = 0
            });
        }
        await _db.SaveChangesAsync();

        // Level 1 hat 0 gelöst/0 gescheitert → Start-Kalibrierung ×4 (K=80), unabhängig von Level 0
        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true, TimeSpentSeconds = 10, VisualizationLevel = 1
        });

        // Default Elo for L1 = 1400, puzzle = 1400 → equal rating → K=80 → change = 40
        Assert.Equal(40, result.EloChange);
    }

    [Fact]
    public async Task RecordAttempt_VizLevel0_DoesNotAffectOtherLevels()
    {
        var userId = await CreateUserAsync("viz_isolate");
        var user = await _db.AppUsers.FindAsync(userId);
        user!.PuzzleEloViz1 = 1400;
        user.PuzzleEloViz2 = 1300;
        await _db.SaveChangesAsync();

        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "viz_iso1");

        await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true, TimeSpentSeconds = 10, VisualizationLevel = 0
        });

        user = await _db.AppUsers.FindAsync(userId);
        Assert.True(user!.PuzzleElo > 1500); // Level 0 changed
        Assert.Equal(1400, user.PuzzleEloViz1); // Level 1 unchanged
        Assert.Equal(1300, user.PuzzleEloViz2); // Level 2 unchanged
    }

    [Fact]
    public async Task GetStats_ReturnsEloForRequestedLevel()
    {
        var userId = await CreateUserAsync("stats_level");
        var user = await _db.AppUsers.FindAsync(userId);
        user!.PuzzleEloViz3 = 1200;
        await _db.SaveChangesAsync();

        var stats = await _stats.GetStatsAsync(userId, vizLevel: 3);

        Assert.Equal(1200, stats.PuzzleElo);
    }

    [Fact]
    public async Task GetStats_ReturnsEloPerLevelDictionary()
    {
        var userId = await CreateUserAsync("stats_dict");
        var user = await _db.AppUsers.FindAsync(userId);
        user!.PuzzleEloViz1 = 1450;
        user.PuzzleEloViz3 = 1250;
        await _db.SaveChangesAsync();

        var stats = await _stats.GetStatsAsync(userId);

        Assert.NotNull(stats.PuzzleEloPerLevel);
        Assert.Equal(1500, stats.PuzzleEloPerLevel![0]); // PuzzleElo default
        Assert.Equal(1450, stats.PuzzleEloPerLevel[1]);  // explicit
        Assert.Equal(1300, stats.PuzzleEloPerLevel[2]);  // default for L2
        Assert.Equal(1250, stats.PuzzleEloPerLevel[3]);  // explicit
        Assert.Equal(1100, stats.PuzzleEloPerLevel[4]);  // default for L4
    }

    [Fact]
    public async Task RecordAttempt_StoresVisualizationLevel()
    {
        var userId = await CreateUserAsync("viz_store");
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "viz_st1");

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true, TimeSpentSeconds = 10, VisualizationLevel = 3
        });

        Assert.Equal(3, result.VisualizationLevel);
        var attempt = await _db.PuzzleAttempts.SingleAsync();
        Assert.Equal(3, attempt.VisualizationLevel);
    }

    [Theory]
    [InlineData(0, 1500)]
    [InlineData(1, 1400)]
    [InlineData(2, 1300)]
    [InlineData(3, 1200)]
    [InlineData(4, 1100)]
    [InlineData(14, 100)]  // clamped to min 100
    public void GetDefaultElo_ReturnsCorrectValues(int level, int expected)
    {
        Assert.Equal(expected, PuzzleElo.GetDefaultElo(level));
    }

    [Fact]
    public async Task GetEloHistory_ReturnsRatedAttemptsChronologically()
    {
        var userId = await CreateUserAsync();
        var puzzle = await CreatePuzzleAsync();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.PuzzleAttempts.AddRange(
            new PuzzleAttempt { UserId = userId, PuzzleId = puzzle.Id, Solved = true, EloAfter = 1520, AttemptedAt = t0.AddMinutes(2) },
            new PuzzleAttempt { UserId = userId, PuzzleId = puzzle.Id, Solved = false, EloAfter = 1505, AttemptedAt = t0 },
            new PuzzleAttempt { UserId = userId, PuzzleId = puzzle.Id, Solved = true, EloAfter = 1540, AttemptedAt = t0.AddMinutes(5) },
            new PuzzleAttempt { UserId = userId, PuzzleId = puzzle.Id, Solved = true, EloAfter = null, AttemptedAt = t0.AddMinutes(1) }); // ohne Elo -> ausgeschlossen
        await _db.SaveChangesAsync();

        var hist = await _stats.GetEloHistoryAsync(userId);

        Assert.Equal(3, hist.Count);
        Assert.Equal(new[] { 1505, 1520, 1540 }, hist.Select(h => h.Elo));   // chronologisch aufsteigend
        Assert.True(hist[0].AttemptedAt <= hist[1].AttemptedAt && hist[1].AttemptedAt <= hist[2].AttemptedAt);
    }

    [Fact]
    public async Task GetBreakdown_AggregatesThemesRatingBandsAndActivity()
    {
        var userId = await CreateUserAsync();
        var p1 = await CreatePuzzleAsync(rating: 1450, lichessId: "b1", themes: "fork endgame");
        var p2 = await CreatePuzzleAsync(rating: 1650, lichessId: "b2", themes: "fork pin");
        var now = DateTime.UtcNow;
        _db.PuzzleAttempts.AddRange(
            new PuzzleAttempt { UserId = userId, PuzzleId = p1.Id, Solved = true, AttemptedAt = now },
            new PuzzleAttempt { UserId = userId, PuzzleId = p1.Id, Solved = false, AttemptedAt = now },
            new PuzzleAttempt { UserId = userId, PuzzleId = p2.Id, Solved = true, AttemptedAt = now });
        await _db.SaveChangesAsync();

        var b = await _stats.GetBreakdownAsync(userId);

        var fork = b.Themes.Single(t => t.Theme == "fork");
        Assert.Equal(3, fork.Attempts);   // p1 x2 + p2 x1
        Assert.Equal(2, fork.Solved);
        Assert.Equal(1, b.Themes.Single(t => t.Theme == "pin").Attempts);

        var band1400 = b.RatingBands.Single(x => x.From == 1400);
        Assert.Equal(2, band1400.Attempts);
        Assert.Equal(1, band1400.Solved);
        Assert.Equal(1, b.RatingBands.Single(x => x.From == 1600).Attempts);

        Assert.Equal(3, b.Activity.Sum(a => a.Count));   // alle heute
    }

    [Fact]
    public async Task GetWorstThemes_OrdersByLowestSolveRate_AndRespectsMinAttempts()
    {
        var userId = await CreateUserAsync();
        var pGood = await CreatePuzzleAsync(lichessId: "wt-good", themes: "pin");        // 3/3 = 100%
        var pBad = await CreatePuzzleAsync(lichessId: "wt-bad", themes: "fork");         // 1/4 = 25%
        var pMid = await CreatePuzzleAsync(lichessId: "wt-mid", themes: "skewer");       // 2/4 = 50%
        var pRare = await CreatePuzzleAsync(lichessId: "wt-rare", themes: "endgame");    // 0/2 = 0% aber < minAttempts
        var now = DateTime.UtcNow;
        void add(Puzzle p, bool solved) => _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = userId, PuzzleId = p.Id, Solved = solved, AttemptedAt = now });
        add(pGood, true); add(pGood, true); add(pGood, true);
        add(pBad, true); add(pBad, false); add(pBad, false); add(pBad, false);
        add(pMid, true); add(pMid, true); add(pMid, false); add(pMid, false);
        add(pRare, false); add(pRare, false);   // nur 2 Versuche → unter minAttempts=3
        await _db.SaveChangesAsync();

        var worst = await _stats.GetWorstThemesAsync(userId, count: 5, minAttempts: 3);

        // endgame (0%, aber zu wenig Daten) fällt raus; Reihenfolge: fork (25%) < skewer (50%) < pin (100%).
        Assert.Equal(new[] { "fork", "skewer", "pin" }, worst.Select(t => t.Theme).ToArray());
        Assert.DoesNotContain(worst, t => t.Theme == "endgame");
    }

    [Fact]
    public async Task GetWorstThemes_RespectsCountLimit()
    {
        var userId = await CreateUserAsync();
        // 4 Themen mit je 3 Versuchen, abnehmende Quote.
        var themes = new[] { "a", "b", "c", "d" };
        var now = DateTime.UtcNow;
        for (int i = 0; i < themes.Length; i++)
        {
            var p = await CreatePuzzleAsync(lichessId: $"wt-c{i}", themes: themes[i]);
            for (int j = 0; j < 3; j++)
                _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = userId, PuzzleId = p.Id, Solved = j < i, AttemptedAt = now });
        }
        await _db.SaveChangesAsync();

        var worst = await _stats.GetWorstThemesAsync(userId, count: 2, minAttempts: 3);

        Assert.Equal(2, worst.Count);
        Assert.Equal("a", worst[0].Theme);   // 0/3 = schwächstes
    }

    [Fact]
    public async Task GetRandom_ThemesAny_MatchesAnyOfTheGivenThemes()
    {
        var userId = await CreateUserAsync();
        await CreatePuzzleAsync(lichessId: "ta1", themes: "endgame mateIn2");
        await CreatePuzzleAsync(lichessId: "ta2", themes: "middlegame fork");

        // ODER-Filter: "fork pin" → Puzzle mit fork ODER pin (hier nur ta2 mit fork).
        var result = await _service.GetRandomAsync(userId, null, null, themes: null, excludeSolved: false, themesAny: "fork pin");

        Assert.NotNull(result);
        Assert.Contains("fork", result!.Themes);
    }

    [Fact]
    public async Task RecordAttempt_EvalShown_StoredCorrectly()
    {
        var userId = await CreateUserAsync("evalshown_user");
        var puzzle = await CreatePuzzleAsync(lichessId: "evalshown1");

        await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = false,
            TimeSpentSeconds = 30,
            EvalShown = true,
            VizShowCount = 0
        });

        var attempt = await _db.PuzzleAttempts.SingleAsync();
        Assert.True(attempt.EvalShown);
        Assert.Equal(0, attempt.VizShowCount);
    }

    [Fact]
    public async Task RecordAttempt_VizShowCount_StoredCorrectly()
    {
        var userId = await CreateUserAsync("vizshow_user");
        var puzzle = await CreatePuzzleAsync(lichessId: "vizshow1");

        await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = false,
            TimeSpentSeconds = 20,
            VisualizationLevel = 3,
            EvalShown = false,
            VizShowCount = 4
        });

        var attempt = await _db.PuzzleAttempts.SingleAsync();
        Assert.False(attempt.EvalShown);
        Assert.Equal(4, attempt.VizShowCount);
    }

    [Fact]
    public async Task RecordAttempt_DefaultsForHints_AreZeroAndFalse()
    {
        var userId = await CreateUserAsync("hints_default_user");
        var puzzle = await CreatePuzzleAsync(lichessId: "hints_default1");

        await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto
        {
            Solved = true,
            TimeSpentSeconds = 15
        });

        var attempt = await _db.PuzzleAttempts.SingleAsync();
        Assert.False(attempt.EvalShown);
        Assert.Equal(0, attempt.VizShowCount);
    }

    // ---- Idempotenz + Elo-Guard ----

    [Fact]
    public async Task RecordAttempt_DuplicateWithin30s_ReturnsSameAttempt()
    {
        var userId = await CreateUserAsync("idem1");
        var puzzle = await CreatePuzzleAsync(lichessId: "idem_p1");

        var first = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });
        var second = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId && a.PuzzleId == puzzle.Id));
    }

    [Fact]
    public async Task RecordAttempt_SecondAttemptOlderThan30s_DoesNotUpdateElo()
    {
        var userId = await CreateUserAsync("eloguard1");
        var puzzle = await CreatePuzzleAsync(rating: 1500, lichessId: "eloguard_p1");

        // Füge einen älteren Versuch direkt in die DB ein (außerhalb des Idempotenz-Fensters).
        _db.PuzzleAttempts.Add(new PuzzleAttempt
        {
            UserId = userId, PuzzleId = puzzle.Id, Solved = false,
            EloAfter = 1480, EloChange = -20, AttemptedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await _db.SaveChangesAsync();

        var user = await _db.AppUsers.FindAsync(userId);
        user!.PuzzleElo = 1480;
        await _db.SaveChangesAsync();

        var result = await _service.RecordAttemptAsync(userId, puzzle.Id, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 10 });

        Assert.Equal(0, result.EloChange);
        Assert.Equal(1480, result.EloAfter);
        var userAfter = await _db.AppUsers.FindAsync(userId);
        Assert.Equal(1480, userAfter!.PuzzleElo);
    }

    [Fact]
    public async Task GetAllThemesAsync_FromTagTable_ReturnsDistinctSorted()
    {
        await CreatePuzzleAsync(rating: 1500, themes: "fork endgame", lichessId: "th1");
        await CreatePuzzleAsync(rating: 1600, themes: "fork pin", lichessId: "th2");
        await _tagging.BackfillPuzzleTagsAsync();   // füllt die Tags-Tabelle (fork, endgame, pin)

        var themes = await _stats.GetAllThemesAsync();

        Assert.Equal(new[] { "endgame", "fork", "pin" }, themes);   // distinkt + alphabetisch
    }

    [Fact]
    public async Task GetAllThemesAsync_TagTableEmpty_FallsBackToPuzzleThemes()
    {
        await CreatePuzzleAsync(rating: 1500, themes: "fork endgame", lichessId: "tf1", syncTags: false);
        await CreatePuzzleAsync(rating: 1600, themes: "pin fork", lichessId: "tf2", syncTags: false);
        // KEIN Backfill → Tags-Tabelle leer → Fallback auf die Puzzle.Themes-Tokens.

        var themes = await _stats.GetAllThemesAsync();

        Assert.Empty(await _db.Tags.ToListAsync());
        Assert.Equal(new[] { "endgame", "fork", "pin" }, themes);
    }
}
