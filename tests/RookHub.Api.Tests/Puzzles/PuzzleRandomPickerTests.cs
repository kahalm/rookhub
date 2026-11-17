using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Puzzles;

namespace RookHub.Api.Tests.Puzzles;

/// <summary>
/// Zufallsauswahl für <c>/api/puzzles/random(-batch)</c> über <see cref="PuzzleRandomPicker"/> (0.989.5): Themen-Semantik
/// (UND = Teilstring, ODER = exakter Tag), Abwechslung innerhalb eines Ratings, Rückfall ohne PuzzleTags, Rating-Abschnitte.
/// Die SQL-Übersetzung prüft <c>PuzzleRandomPickSqlTests</c> gegen MariaDB.
/// </summary>
public class PuzzleRandomPickerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PuzzleService _service;
    private readonly Dictionary<string, int> _tagCache = new();

    public PuzzleRandomPickerTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tagging = new PuzzleTaggingService(_db, NullLogger<PuzzleTaggingService>.Instance);
        _service = new PuzzleService(_db, new MemoryCache(new MemoryCacheOptions()), NullLogger<PuzzleService>.Instance, tagging);
    }

    public void Dispose() => _db.Dispose();

    private async Task<List<Puzzle>> AddAsync(int rating, string themes, int count = 1, bool syncTags = true)
    {
        var list = Enumerable.Range(0, count).Select(_ => new Puzzle
        {
            LichessId = Guid.NewGuid().ToString()[..8], Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1a2",
            Rating = rating, Themes = themes,
        }).ToList();
        _db.Puzzles.AddRange(list);
        await _db.SaveChangesAsync();
        if (syncTags)
            await new PuzzleTaggingService(_db, NullLogger<PuzzleTaggingService>.Instance).SyncPuzzleTagsAsync(list, _tagCache, CancellationToken.None);
        return list;
    }

    [Fact]
    public async Task Batch_Themes_IsSubstringAnd_LikeTheThemesText()
    {
        await AddAsync(1000, "veryLong endgame");
        await AddAsync(1050, "long middlegame");
        await AddAsync(1100, "short endgame");

        // „long“ trifft auch „veryLong“ (Teilstring wie LIKE '%long%'), UND mit „endgame“ bleibt nur das erste.
        var result = await _service.GetRandomBatchAsync(null, new[] { (990, 1010), (1040, 1060), (1090, 1110) }, "long endgame", false);

        var p = Assert.Single(result);
        Assert.Equal("veryLong endgame", p.Themes);
    }

    [Fact]
    public async Task Batch_ThemesAny_IsExactTagName()
    {
        await AddAsync(1000, "veryLong endgame");
        var exact = (await AddAsync(1050, "long middlegame"))[0];

        var result = await _service.GetRandomBatchAsync(null, new[] { (990, 1010), (1040, 1060) }, null, false, "long");

        Assert.Equal(new[] { exact.Id }, result.Select(r => r.Id));   // „veryLong“ zählt bei ODER nicht
    }

    [Fact]
    public async Task Batch_UnknownAndTerm_ReturnsEmpty()
    {
        await AddAsync(1000, "fork");
        Assert.Empty(await _service.GetRandomBatchAsync(null, new[] { (990, 1010) }, "zzz", false));
    }

    [Fact]
    public async Task Random_ThemesAny_VariesWithinOneRating()
    {
        // Früher: je (Tag, Zufalls-Rating) immer die ERSTE Zeile ab dem Rating — bei einem einzigen Rating also immer
        // dasselbe Puzzle. Jetzt muss die Auswahl innerhalb des Ratings streuen.
        await AddAsync(1200, "fork", count: 30);
        await AddAsync(1200, "endgame", count: 30);

        var seen = new HashSet<int>();
        for (var i = 0; i < 60; i++)
        {
            var p = await _service.GetRandomAsync(null, 1200, 1200, null, false, "fork");
            Assert.NotNull(p);
            Assert.Equal("fork", p!.Themes);
            seen.Add(p.Id);
        }
        Assert.True(seen.Count >= 8, $"nur {seen.Count} verschiedene von 30");
    }

    [Fact]
    public async Task Batch_WithoutPuzzleTags_FallsBackToLikeScan()
    {
        await AddAsync(1000, "fork pin", syncTags: false);
        await AddAsync(1000, "endgame", syncTags: false);
        await AddAsync(1100, "veryLong fork", syncTags: false);

        var and = await _service.GetRandomBatchAsync(null, new[] { (990, 1010), (1090, 1110) }, "fork", false);
        Assert.Equal(2, and.Count);
        Assert.All(and, p => Assert.Contains("fork", p.Themes));

        // ODER im Rückfall jetzt ebenfalls exakt (ganzes Token), wie im Index-Pfad.
        var any = await _service.GetRandomBatchAsync(null, new[] { (990, 1010), (1090, 1110) }, null, false, "long pin");
        Assert.Equal("fork pin", Assert.Single(any).Themes);
    }

    [Fact]
    public async Task Batch_ExcludeSolved_AndUniquePerBatch_WithThemes()
    {
        var user = new AppUser { Username = "u", Email = "u@test.invalid", PasswordHash = "x", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var forks = await AddAsync(1000, "fork short", count: 3);
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = user.Id, PuzzleId = forks[0].Id, Solved = true, TimeSpentSeconds = 3 });
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = user.Id, PuzzleId = forks[1].Id, Solved = true, TimeSpentSeconds = 3 });
        await _db.SaveChangesAsync();

        // Drei gleiche Fenster, aber nur ein ungelöstes Puzzle: genau einmal.
        var result = await _service.GetRandomBatchAsync(user.Id, new[] { (990, 1010), (990, 1010), (990, 1010) }, "fork", true, "short");

        Assert.Equal(new[] { forks[2].Id }, result.Select(r => r.Id));
    }

    [Theory]
    [InlineData(1500, 1480, 1520)]
    [InlineData(1480, 1480, 1520)]
    [InlineData(1520, 1480, 1520)]
    [InlineData(1000, 600, 2800)]
    public void Chunks_CoverTheWindowExactlyOnce_StartingAtR(int r, int lo, int hi)
    {
        var chunks = PuzzleRandomPicker.Chunks(r, lo, hi).ToList();

        Assert.Equal((r, r), chunks[0]);
        var covered = chunks.SelectMany(c => Enumerable.Range(c.A, c.B - c.A + 1)).ToList();
        Assert.Equal(hi - lo + 1, covered.Count);                 // keine Überlappung
        Assert.Equal(Enumerable.Range(lo, hi - lo + 1), covered.OrderBy(x => x));   // lückenlos
        Assert.True(chunks.Count <= 14, $"{chunks.Count} Abschnitte");             // wächst geometrisch
    }
}
