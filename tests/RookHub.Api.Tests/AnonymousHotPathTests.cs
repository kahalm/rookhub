using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Absicherung der anonym erreichbaren Heiß-Pfade: sie dürfen pro Request KEINE unbegrenzt
/// wachsende Datenmenge materialisieren bzw. teure Parses wiederholen (Zufalls-Puzzle mit
/// Themenfilter, Wochenpost-PGN, Tagespuzzle-Hall-of-Fame).
/// </summary>
public class AnonymousHotPathTests : IDisposable
{
    private readonly AppDbContext _db;

    public AnonymousHotPathTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    // ---- 1) Zufalls-Puzzle mit ODER-Themenfilter: Random-Seek statt Kandidatenliste ----

    private PuzzleService NewPuzzleService()
        => new(_db, NewCache(), NullLogger<PuzzleService>.Instance,
            new PuzzleTaggingService(_db, NullLogger<PuzzleTaggingService>.Instance));

    private async Task<Puzzle> AddTaggedPuzzleAsync(int rating, string themes, string lichessId)
    {
        var puzzle = new Puzzle
        {
            LichessId = lichessId,
            Fen = "r1bqkbnr/pppppppp/2n5/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 2",
            Moves = "e2e4 d7d5 e4d5",
            Rating = rating,
            Themes = themes,
        };
        _db.Puzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        foreach (var name in themes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Name == name);
            if (tag == null) { tag = new Tag { Name = name }; _db.Tags.Add(tag); await _db.SaveChangesAsync(); }
            _db.PuzzleTags.Add(new PuzzleTag { PuzzleId = puzzle.Id, TagId = tag.Id, Rating = rating });
        }
        await _db.SaveChangesAsync();
        return puzzle;
    }

    [Fact]
    public async Task GetRandom_ThemesAny_OpenRatingRange_StillFiltersSolvedAndExcluded()
    {
        // Der Einzel-Pfad zieht jetzt (wie der Batch-Pfad) per Random-Seek über den Index (TagId, Rating)
        // statt die komplette Tag-Kandidatenmenge zu laden. Offene Rating-Grenzen werden dabei mit der
        // Pool-Spanne geschlossen — die Ausschlüsse (gelöst / bereits vergeben) müssen weiter greifen.
        var user = new AppUser { Username = "seeker", Email = "seeker@example.com", PasswordHash = "h", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();

        var solved = await AddTaggedPuzzleAsync(1200, "fork", "seek-solved");
        var open = await AddTaggedPuzzleAsync(2400, "fork", "seek-open");
        var other = await AddTaggedPuzzleAsync(1800, "endgame", "seek-other");
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = user.Id, PuzzleId = solved.Id, Solved = true, AttemptedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var svc = NewPuzzleService();
        // Ohne Rating-Grenzen: nur das ungelöste fork-Puzzle darf kommen (nie das gelöste, nie „endgame").
        for (var i = 0; i < 10; i++)
        {
            var result = await svc.GetRandomAsync(user.Id, null, null, themes: null, excludeSolved: true, themesAny: "fork");
            Assert.NotNull(result);
            Assert.Equal(open.Id, result!.Id);
        }
        Assert.NotEqual(other.Id, open.Id);

        // excludeIds (Offline-Batch) schließt das letzte verbliebene Puzzle aus → kein Treffer.
        var none = await svc.GetRandomAsync(user.Id, null, null, themes: null, excludeSolved: true,
            themesAny: "fork", excludeIds: new[] { open.Id });
        Assert.Null(none);
    }

    [Fact]
    public async Task GetRandom_ThemesAny_RatingWindowOutsidePool_ReturnsNull()
    {
        // Grenzfall des geschlossenen Seek-Fensters: liegt die angefragte Spanne komplett neben dem
        // Pool, darf das kein Puzzle (und keine Exception) liefern.
        await AddTaggedPuzzleAsync(1500, "fork", "seek-narrow");

        var result = await NewPuzzleService()
            .GetRandomAsync(null, 2800, 3000, themes: null, excludeSolved: false, themesAny: "fork");

        Assert.Null(result);
    }

    // ---- 2) Wochenpost: PGN wird je (Post, UpdatedAt) nur EINMAL geparst ----

    private static string PgnWithGames(int count)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 1; i <= count; i++)
            sb.Append($"[Event \"W\"]\n[Round \"{i}\"]\n[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n\n2. Nf3 Nc6 *\n\n");
        return sb.ToString();
    }

    [Fact]
    public async Task GetPlayPuzzles_ParsesPgnOnce_UntilUpdatedAtChanges()
    {
        var post = new WeeklyPost
        {
            Title = "Woche 1",
            FileName = "woche1.pgn",
            PgnContent = PgnWithGames(2),
            ScheduledAt = DateTime.UtcNow,
            UpdatedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        _db.WeeklyPosts.Add(post);
        await _db.SaveChangesAsync();

        var svc = new WeeklyPostService(_db, NullLogger<WeeklyPostService>.Instance, null, NewCache());
        Assert.Equal(2, (await svc.GetPlayPuzzlesAsync(post)).Count);

        // Inhalt heimlich austauschen, OHNE UpdatedAt zu berühren → der Cache-Treffer greift
        // (Beleg dafür, dass nicht erneut geparst wird).
        post.PgnContent = PgnWithGames(3);
        Assert.Equal(2, (await svc.GetPlayPuzzlesAsync(post)).Count);

        // Eine echte Bearbeitung setzt UpdatedAt → neuer Key, frischer Parse (kein Stale-Inhalt).
        post.UpdatedAt = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(3, (await svc.GetPlayPuzzlesAsync(post)).Count);
    }

    [Fact]
    public async Task GetPlayPuzzles_WithoutCache_AlwaysParsesFresh()
    {
        // Ohne IMemoryCache (Test-/Worker-Konstruktion) muss der Alt-Pfad unverändert funktionieren.
        var post = new WeeklyPost
        {
            Title = "Woche 2",
            FileName = "woche2.pgn",
            PgnContent = PgnWithGames(2),
            ScheduledAt = DateTime.UtcNow,
        };
        _db.WeeklyPosts.Add(post);
        await _db.SaveChangesAsync();

        var svc = new WeeklyPostService(_db, NullLogger<WeeklyPostService>.Instance);
        Assert.Equal(2, (await svc.GetPlayPuzzlesAsync(post)).Count);
        post.PgnContent = PgnWithGames(3);
        Assert.Equal(3, (await svc.GetPlayPuzzlesAsync(post)).Count);
    }

    // ---- 3) Hall of Fame: all-time-Rohdaten werden gecacht ----

    private async Task<AppUser> AddUserAsync(string name)
    {
        var user = new AppUser { Username = name, Email = $"{name}@example.com", PasswordHash = "h", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<BookPuzzle> AddDailyAsync(DateOnly date, string lineId)
    {
        var puzzle = new BookPuzzle
        {
            LineId = lineId,
            BookFileName = "hof.pgn",
            Round = "1",
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
            Moves = "e7e5",
        };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        _db.DailyPuzzles.Add(new DailyPuzzle { Date = date, BookPuzzleId = puzzle.Id, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        return puzzle;
    }

    private async Task AddSolveAsync(BookPuzzle puzzle, AppUser user, int seconds, DateTime at)
    {
        _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt
        {
            BookPuzzleId = puzzle.Id,
            UserId = user.Id,
            Solved = true,
            TimeSeconds = seconds,
            AttemptedAt = at,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task DailyHallOfFame_ReusesCachedRows_FreshServiceSeesNewSolves()
    {
        var day1 = new DateOnly(2026, 5, 1);
        var day2 = new DateOnly(2026, 5, 2);
        var p1 = await AddDailyAsync(day1, "hof.pgn:1");
        var p2 = await AddDailyAsync(day2, "hof.pgn:2");
        var anna = await AddUserAsync("anna");
        await AddSolveAsync(p1, anna, 30, day1.ToDateTime(new TimeOnly(8, 0)));

        var cache = NewCache();
        var svc = new DailyLeaderboardService(_db, cache);
        Assert.Equal(1, (await svc.GetDailyHallOfFameAsync()).MostSolved[0].Value);

        // Neue Lösung: innerhalb der Cache-Zeit bleibt die Liste stehen (bewusster Trade-off —
        // die all-time-Rohdaten sind der teure Teil und ändern sich höchstens täglich).
        await AddSolveAsync(p2, anna, 10, day2.ToDateTime(new TimeOnly(8, 0)));
        Assert.Equal(1, (await svc.GetDailyHallOfFameAsync()).MostSolved[0].Value);

        // Frischer Cache (= abgelaufener Eintrag) sieht die neue Lösung.
        var fresh = new DailyLeaderboardService(_db, NewCache());
        var hof = await fresh.GetDailyHallOfFameAsync();
        Assert.Equal(2, hof.MostSolved[0].Value);
        Assert.Equal("anna", hof.MostSolved[0].Name);
    }
}
