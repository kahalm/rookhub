using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Tests für die Kurs-Statistik (Pendant zur Standard-Puzzle-Statistik, aber auf
/// <see cref="CourseAttempt"/> und ohne Elo): GetStats/GetHistory/GetBreakdown.
/// </summary>
public class CourseServiceStatsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _service;
    private readonly CourseStatsService _stats;
    private const int UserId = 1;
    private const int OtherUserId = 2;

    public CourseServiceStatsTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db), new BookAdminService(_db), new RepertoireService(_db, new RepertoireAnalyzeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()))));
        _stats = new CourseStatsService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> SeedBookAsync()
    {
        var book = new Book { FileName = "b.pgn", DisplayName = "B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    private async Task<BookPuzzle> SeedPuzzleAsync(Book book, string lineId, string? tags = null, int? rating = null, string? title = null)
    {
        var p = new BookPuzzle
        {
            LineId = lineId,
            BookFileName = book.FileName,
            BookId = book.Id,
            Round = "1",
            Fen = "8/8/8/8/8/8/8/8 w - - 0 1",
            Moves = "e2e4",
            Tags = tags,
            BookRating = rating,
            Title = title,
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private async Task AddAttemptAsync(int userId, BookPuzzle puzzle, bool solved, DateTime at, int timeSeconds = 10)
    {
        _db.CourseAttempts.Add(new CourseAttempt
        {
            UserId = userId,
            BookId = puzzle.BookId!.Value,
            BookPuzzleId = puzzle.Id,
            Solved = solved,
            TimeSeconds = timeSeconds,
            AttemptedAt = at,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetStatsAsync_NoAttempts_ReturnsZeroes()
    {
        var stats = await _stats.GetStatsAsync(UserId);

        Assert.Equal(0, stats.TotalAttempts);
        Assert.Equal(0, stats.Solved);
        Assert.Equal(0, stats.Accuracy);
        Assert.Equal(0, stats.CurrentStreak);
        Assert.Equal(0, stats.BestStreak);
    }

    [Fact]
    public async Task GetStatsAsync_CountsAttemptsAccuracyAndStreaks()
    {
        var book = await SeedBookAsync();
        var p = await SeedPuzzleAsync(book, "l1");
        var baseTime = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        // Chronologisch: solved, solved, failed, solved, solved (neueste = letzter Eintrag)
        await AddAttemptAsync(UserId, p, true, baseTime.AddMinutes(1));
        await AddAttemptAsync(UserId, p, true, baseTime.AddMinutes(2));
        await AddAttemptAsync(UserId, p, false, baseTime.AddMinutes(3));
        await AddAttemptAsync(UserId, p, true, baseTime.AddMinutes(4));
        await AddAttemptAsync(UserId, p, true, baseTime.AddMinutes(5));
        // Versuch eines anderen Users darf nicht mitzählen
        await AddAttemptAsync(OtherUserId, p, false, baseTime.AddMinutes(6));

        var stats = await _stats.GetStatsAsync(UserId);

        Assert.Equal(5, stats.TotalAttempts);
        Assert.Equal(4, stats.Solved);
        Assert.Equal(80.0, stats.Accuracy);
        Assert.Equal(2, stats.CurrentStreak); // die letzten zwei (neuesten) waren gelöst
        Assert.Equal(2, stats.BestStreak);    // längste Solved-Serie = 2
    }

    [Fact]
    public async Task GetHistoryAsync_NewestFirst_MapsPuzzleInfo_AndPaginates()
    {
        var book = await SeedBookAsync();
        var p1 = await SeedPuzzleAsync(book, "line-1", rating: 1600, title: "Erstes");
        var p2 = await SeedPuzzleAsync(book, "line-2", rating: 1800, title: "Zweites");
        var baseTime = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        await AddAttemptAsync(UserId, p1, true, baseTime.AddMinutes(1), timeSeconds: 11);
        await AddAttemptAsync(UserId, p2, false, baseTime.AddMinutes(2), timeSeconds: 22);

        var page1 = await _stats.GetHistoryAsync(UserId, page: 1, pageSize: 1);
        Assert.Single(page1);
        Assert.Equal("line-2", page1[0].LineId); // neuester zuerst
        Assert.Equal(1800, page1[0].BookRating);
        Assert.Equal("Zweites", page1[0].Title);
        Assert.Equal(22, page1[0].TimeSeconds);
        Assert.False(page1[0].Solved);
        Assert.Equal(p2.Id, page1[0].BookPuzzleId);

        var page2 = await _stats.GetHistoryAsync(UserId, page: 2, pageSize: 1);
        Assert.Single(page2);
        Assert.Equal("line-1", page2[0].LineId);
        Assert.True(page2[0].Solved);
    }

    [Fact]
    public async Task GetBreakdownAsync_AggregatesThemesRatingBandsAndActivity()
    {
        var book = await SeedBookAsync();
        var pA = await SeedPuzzleAsync(book, "a", tags: "fork pin", rating: 1610);
        var pB = await SeedPuzzleAsync(book, "b", tags: "fork", rating: 1850);
        var pNoMeta = await SeedPuzzleAsync(book, "c", tags: null, rating: null);
        var day = new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc);

        await AddAttemptAsync(UserId, pA, true, day.AddMinutes(1));
        await AddAttemptAsync(UserId, pB, false, day.AddMinutes(2));
        await AddAttemptAsync(UserId, pNoMeta, true, day.AddMinutes(3));

        var bd = await _stats.GetBreakdownAsync(UserId);

        // Themen: fork (2 Versuche, 1 gelöst), pin (1 Versuch, 1 gelöst)
        var fork = bd.Themes.Single(t => t.Theme == "fork");
        Assert.Equal(2, fork.Attempts);
        Assert.Equal(1, fork.Solved);
        var pin = bd.Themes.Single(t => t.Theme == "pin");
        Assert.Equal(1, pin.Attempts);
        Assert.Equal(1, pin.Solved);

        // Rating-Bänder: 1600er (1610) und 1800er (1850); Puzzle ohne Rating wird ausgelassen
        Assert.Equal(2, bd.RatingBands.Count);
        var band1600 = bd.RatingBands.Single(b => b.From == 1600);
        Assert.Equal(1, band1600.Attempts);
        Assert.Equal(1, band1600.Solved);
        Assert.Equal(1799, band1600.To);
        Assert.Contains(bd.RatingBands, b => b.From == 1800);

        // Aktivität: alle drei am selben Tag
        var d = bd.Activity.Single();
        Assert.Equal("2026-06-10", d.Date);
        Assert.Equal(3, d.Count);
    }
}
