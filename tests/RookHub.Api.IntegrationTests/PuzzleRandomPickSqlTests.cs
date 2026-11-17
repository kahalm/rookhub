using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Puzzles;
using Xunit;
using Xunit.Abstractions;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Zufallsauswahl von <c>/api/puzzles/random(-batch)</c> gegen ECHTES MariaDB: Probe über den Primärschlüssel
/// (mit <c>LIKE</c> und Token-<c>CONCAT</c>), Rating-Seek über (Rating, Id) und (TagId, Rating, PuzzleId), EXISTS
/// für UND-Begriffe, <c>NOT IN</c> für gelöst/schon vergeben. InMemory prüft keine dieser Übersetzungen.
/// Die SQL-Texte gehen in die Testausgabe — dieselben Abfragen liefen mit EXPLAIN gegen Prod (0.989.5).
/// </summary>
public class PuzzleRandomPickSqlTests(PuzzleRandomPickSqlFixture fixture, ITestOutputHelper output)
    : IAsyncLifetime, IClassFixture<PuzzleRandomPickSqlFixture>
{
    private readonly List<string> _sql = new();

    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private AppDbContext NewLoggingContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.Schema.ConnectionString, DbServerVersion.Current)
            .AddInterceptors(new SqlCapture(_sql))
            .Options;
        return new AppDbContext(options);
    }

    private sealed class SqlCapture(List<string> sink) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            sink.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, ct);
        }
    }

    private async Task<int> SeedAsync()
    {
        await using var db = fixture.Schema.NewContext();
        var user = new AppUser { Username = "pick", Email = "pick@test.invalid", PasswordHash = "x" };
        db.AppUsers.Add(user);
        var themes = new[] { "fork middlegame", "pin fork short", "mateIn2 mate long", "mateIn1 mate", "veryLong endgame rookEndgame", "skewer" };
        var puzzles = new List<Puzzle>();
        for (var i = 0; i < 240; i++)
            puzzles.Add(new Puzzle
            {
                LichessId = $"pk{i}", Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1a2",
                Rating = 1000 + i % 120,   // jedes Rating 1000–1119 zweimal
                Themes = themes[i % themes.Length],
            });
        db.Puzzles.AddRange(puzzles);
        await db.SaveChangesAsync();
        var tagging = new PuzzleTaggingService(db, NullLogger<PuzzleTaggingService>.Instance);
        await tagging.SyncPuzzleTagsAsync(puzzles, new Dictionary<string, int>(), CancellationToken.None);
        // Alle fork-Puzzles unter 1060 gelöst.
        foreach (var p in puzzles.Where(p => p.Themes!.Contains("fork") && p.Rating < 1060))
            db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = user.Id, PuzzleId = p.Id, Solved = true, TimeSpentSeconds = 5 });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static List<(int, int)> Windows() =>
        Enumerable.Range(0, 12).Select(i => (1000 + i * 10, 1009 + i * 10)).ToList();

    [MySqlFact]
    public async Task Batch_AllVariants_TranslateAndKeepTheRules()
    {
        var userId = await SeedAsync();
        await using var db = NewLoggingContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new PuzzleService(db, cache, NullLogger<PuzzleService>.Instance,
            new PuzzleTaggingService(db, NullLogger<PuzzleTaggingService>.Instance));

        // Ohne Themen: je Fenster genau eins, eindeutig, in Fensterreihenfolge.
        var plain = await svc.GetRandomBatchAsync(null, Windows(), null, false);
        Assert.Equal(12, plain.Count);
        Assert.Equal(12, plain.Select(p => p.Id).Distinct().Count());
        for (var i = 0; i < plain.Count; i++) Assert.InRange(plain[i].Rating, 1000 + i * 10, 1009 + i * 10);

        // UND (Teilstring): „mate“ trifft mateIn1/mateIn2, „long“ trifft auch „veryLong“ — wie LIKE '%…%'.
        var and = await svc.GetRandomBatchAsync(null, Windows(), "mate long", false);
        Assert.NotEmpty(and);
        Assert.All(and, p => Assert.Contains("mateIn2", p.Themes));
        var veryLong = await svc.GetRandomBatchAsync(null, Windows(), "long endgame", false);
        Assert.NotEmpty(veryLong);
        Assert.All(veryLong, p => Assert.Contains("veryLong", p.Themes));

        // ODER (exakter Name): „long“ trifft NICHT „veryLong“.
        var any = await svc.GetRandomBatchAsync(null, Windows(), null, false, "long skewer");
        Assert.NotEmpty(any);
        Assert.All(any, p => Assert.True(p.Themes == "skewer" || p.Themes!.Contains("mateIn2"), p.Themes));

        // Kombiniert + excludeSolved: fork UND (pin ODER middlegame), gelöste (fork < 1060) fallen raus.
        var combo = await svc.GetRandomBatchAsync(userId, Windows(), "fork", true, "pin middlegame");
        Assert.NotEmpty(combo);
        Assert.All(combo, p => { Assert.Contains("fork", p.Themes); Assert.True(p.Rating >= 1060, $"gelöstes {p.Rating}"); });
        Assert.Equal(combo.Count, combo.Select(p => p.Id).Distinct().Count());

        // Unbekannter UND-Begriff → nichts (wie LIKE ohne Treffer).
        Assert.Empty(await svc.GetRandomBatchAsync(null, Windows(), "zzz", false));

        // Einzelabruf mit offener Grenze nimmt denselben Weg.
        var single = await svc.GetRandomAsync(null, 1100, null, null, false, "skewer");
        Assert.NotNull(single);
        Assert.Equal("skewer", single!.Themes);
        Assert.True(single.Rating >= 1100);

        foreach (var s in _sql.Distinct()) output.WriteLine(s.ReplaceLineEndings(" ") + ";");
        // Kein Aggregat über einen Rating-Bereich der Puzzles-Tabelle (das war der alte MIN/MAX(Id)-Weg).
        Assert.DoesNotContain(_sql, s => s.Contains("MIN(`p`.`Id`)") && s.Contains("`Rating` >="));
    }

    /// <summary>Dünner Filter: die Probe verfehlt (Treffer liegen weit auseinander), der Tag-Seek findet sie trotzdem —
    /// und zieht innerhalb eines Ratings verschiedene Puzzles (früher immer das erste ab dem Zufalls-Rating).</summary>
    [MySqlFact]
    public async Task SparseFilter_FallsBackToTagSeek_AndVariesWithinARating()
    {
        await using (var db = fixture.Schema.NewContext())
        {
            // Fünf Treffer mit weit auseinanderliegenden Ids (je 1 Mio.) zwischen Füllern: eine Probe über
            // ProbeSpan Ids trifft nur mit ~0,4 % — der Weg über den Tag-Index muss es richten.
            var puzzles = new List<Puzzle>();
            for (var k = 1; k <= 5; k++)
            {
                puzzles.Add(new Puzzle { Id = k * 1_000_000, LichessId = $"sp{k}", Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1a2", Rating = 1500, Themes = "smotheredMate mate" });
                puzzles.Add(new Puzzle { Id = k * 1_000_000 + 1, LichessId = $"sf{k}", Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1a2", Rating = 1500, Themes = "endgame" });
            }
            db.Puzzles.AddRange(puzzles);
            await db.SaveChangesAsync();
            await new PuzzleTaggingService(db, NullLogger<PuzzleTaggingService>.Instance)
                .SyncPuzzleTagsAsync(puzzles, new Dictionary<string, int>(), CancellationToken.None);
        }
        await using var ctx = NewLoggingContext();
        var svc = new PuzzleService(ctx, new MemoryCache(new MemoryCacheOptions()), NullLogger<PuzzleService>.Instance,
            new PuzzleTaggingService(ctx, NullLogger<PuzzleTaggingService>.Instance));
        var seen = new HashSet<int>();
        for (var i = 0; i < 30; i++)
        {
            var p = await svc.GetRandomAsync(null, 1490, 1510, null, false, "smotheredMate");
            Assert.NotNull(p);
            Assert.Contains("smotheredMate", p!.Themes);
            seen.Add(p.Id);
        }
        Assert.True(seen.Count >= 3, $"nur {seen.Count} verschiedene Puzzles");
        foreach (var s in _sql.Distinct()) output.WriteLine(s.ReplaceLineEndings(" ") + ";");
    }
}

public sealed class PuzzleRandomPickSqlFixture() : MariaDbClassFixture("pzpick", withApp: false);
