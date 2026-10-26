using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Puzzles;

namespace RookHub.Api.Tests.Puzzles;

/// <summary>Die vier anonymen Versuchs-Senken (A2-012): Frist und Gesamtdeckel je Senke, Kontozeilen bleiben. Feste Uhr.</summary>
public class AnonymousSinkRetentionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private static readonly RetentionOptions Defaults = new();

    private void Seed(int ageDays, bool anonymous, string tag)
    {
        var at = Now.AddDays(-ageDays);
        int? user = anonymous ? null : 7;
        var sid = anonymous ? tag : null;
        var session = new GuessSession { UserId = user, AnonymousSessionId = sid, GameAnalysisId = 1, StartedAt = at };
        session.Moves.Add(new GuessMove { Ply = 1 });
        _db.GuessSessions.Add(session);
        _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt { BookPuzzleId = 1, UserId = user, AnonymousSessionId = sid, AttemptedAt = at });
        _db.SharedPuzzleAttempts.Add(new SharedPuzzleAttempt { BookPuzzleId = 1, IdentityKey = anonymous ? "s:" + tag : "u:7", CreatedAt = at });
        _db.PuzzleAttempts.Add(new PuzzleAttempt { PuzzleId = 1, UserId = user, AnonymousSessionId = sid, AttemptedAt = at });
    }

    private async Task<(int Guess, int Moves, int Book, int Shared, int Puzzle)> CountsAsync() =>
        (await _db.GuessSessions.CountAsync(), await _db.GuessMoves.CountAsync(), await _db.BookPuzzleAttempts.CountAsync(),
            await _db.SharedPuzzleAttempts.CountAsync(), await _db.PuzzleAttempts.CountAsync());

    [Fact]
    public async Task OlderThan90Days_AnonymousGone_FreshAndAccountRowsStay()
    {
        Seed(91, anonymous: true, "alt");
        Seed(89, anonymous: true, "neu");
        Seed(400, anonymous: false, "konto");
        await _db.SaveChangesAsync();

        Assert.Equal(4, await AnonymousSinkRetention.PruneAsync(_db, Now, Defaults));

        Assert.Equal((2, 2, 2, 2, 2), await CountsAsync());
        Assert.DoesNotContain(await _db.GuessSessions.Select(s => s.AnonymousSessionId).ToListAsync(), x => x == "alt");
        Assert.DoesNotContain(await _db.SharedPuzzleAttempts.Select(a => a.IdentityKey).ToListAsync(), x => x == "s:alt");
    }

    [Fact]
    public async Task FinishedGuessSession_CountsFromItsEnd()
    {
        _db.GuessSessions.Add(new GuessSession { AnonymousSessionId = "x", GameAnalysisId = 1, StartedAt = Now.AddDays(-120), FinishedAt = Now.AddDays(-10) });
        await _db.SaveChangesAsync();
        Assert.Equal(0, await AnonymousSinkRetention.PruneAsync(_db, Now, Defaults));
    }

    [Fact]
    public async Task OverTheCap_OldestGoFirst_InPortions_AccountRowsNeverCount()
    {
        for (var i = 0; i < 5; i++) Seed(10 + i, anonymous: true, $"a{i}");   // a4 ist die älteste
        Seed(1, anonymous: false, "konto");
        await _db.SaveChangesAsync();

        var removed = await AnonymousSinkRetention.PruneAsync(_db, Now, Defaults with { AnonymousCap = 2 }, chunkSize: 2);

        Assert.Equal(12, removed);                                                   // je Senke 3 über dem Deckel
        Assert.Equal((3, 3, 3, 3, 3), await CountsAsync());
        Assert.Equal(new[] { "a0", "a1" }, await _db.PuzzleAttempts.Where(a => a.UserId == null)
            .OrderBy(a => a.AnonymousSessionId).Select(a => a.AnonymousSessionId!).ToArrayAsync());
    }

    [Fact]
    public async Task EmptyDatabase_IsNoOp() => Assert.Equal(0, await AnonymousSinkRetention.PruneAsync(_db, Now, Defaults));

    [Fact]
    public void Options_FromConfig_DefaultsOnMissingOrInvalid()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Retention:AnonymousDays"] = "30", ["Retention:AnonymousCap"] = "-1", ["Retention:MessagesDays"] = "x",
        }).Build();
        var o = RetentionOptions.From(config);
        Assert.Equal((30, 200_000, 365, 30, 14),
            (o.AnonymousDays, o.AnonymousCap, o.MessagesDays, o.ScoresheetFailedDays, o.ScoresheetOpenAnonymousDays));
        Assert.Equal(new RetentionOptions(), RetentionOptions.From(null));
    }
}
