using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Challenge-Abschluss durch den gespeicherten Versuch gegen ECHTES MariaDB (Codereview N9-001). Relational schließt
/// ein einzelnes <c>UPDATE … WHERE Status = Pending</c> (ExecuteUpdate, InMemory kennt es nicht): kommen Versuch und
/// <c>/resolve</c> gleichzeitig an, stellt nur einer um und nur der benachrichtigt den Absender.
/// </summary>
public class ChallengeCloseSqlTests(ChallengeCloseSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<ChallengeCloseSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static ChallengeService Challenges(AppDbContext db)
    {
        var notifications = new NotificationService(db);
        return new ChallengeService(db, new FriendService(db, notifications), notifications);
    }

    private async Task<(int Sender, int Recipient, int PuzzleId, int ChallengeId)> SeedAsync()
    {
        await using var db = fixture.Schema.NewContext();
        var sender = new AppUser { Username = "alice", Email = "a@test.invalid", PasswordHash = "x" };
        var recipient = new AppUser { Username = "bob", Email = "b@test.invalid", PasswordHash = "x" };
        db.AppUsers.AddRange(sender, recipient);
        var puzzle = new Puzzle { LichessId = "n9001", Fen = "fen", Moves = "e2e4", Rating = 1500 };
        db.Puzzles.Add(puzzle);
        await db.SaveChangesAsync();
        db.Friendships.Add(new Friendship { RequesterId = sender.Id, AddresseeId = recipient.Id, Status = FriendshipStatus.Accepted });
        var challenge = new PuzzleChallenge { FromUserId = sender.Id, ToUserId = recipient.Id, PuzzleId = puzzle.Id, Source = PuzzleSource.Standard };
        db.PuzzleChallenges.Add(challenge);
        await db.SaveChangesAsync();
        return (sender.Id, recipient.Id, puzzle.Id, challenge.Id);
    }

    private async Task<(PuzzleChallenge Challenge, int Bells)> StateAsync(int challengeId, int sender)
    {
        await using var db = fixture.Schema.NewContext();
        var c = await db.PuzzleChallenges.AsNoTracking().SingleAsync(x => x.Id == challengeId);
        var bells = await db.Notifications.CountAsync(n => n.UserId == sender && n.Type == NotificationType.ChallengeResolved);
        return (c, bells);
    }

    [MySqlFact]
    public async Task RecordedAttempt_ClosesTheChallenge_OnMariaDb()
    {
        var (sender, recipient, puzzleId, challengeId) = await SeedAsync();

        await using (var db = fixture.Schema.NewContext())
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var puzzles = new PuzzleService(db, cache, NullLogger<PuzzleService>.Instance,
                new PuzzleTaggingService(db, NullLogger<PuzzleTaggingService>.Instance), Challenges(db));
            await puzzles.RecordAttemptAsync(recipient, puzzleId, new RecordPuzzleAttemptDto { Solved = true, TimeSpentSeconds = 21 });
        }

        var (c, bells) = await StateAsync(challengeId, sender);
        Assert.Equal(ChallengeStatus.Solved, c.Status);
        Assert.Equal(21, c.TimeSpentSeconds);
        Assert.NotNull(c.ResolvedAt);
        Assert.Equal(1, bells);
    }

    /// <summary>Das Rennen, nachgestellt: /resolve hat die Challenge noch als offen geladen und den gelösten Versuch
    /// gefunden, da schließt der Versuch sie. Das bedingte UPDATE trifft keine Zeile mehr → 409, keine zweite Glocke.</summary>
    [MySqlFact]
    public async Task ResolveThatLostTheRace_DoesNotCloseOrNotifyAgain()
    {
        var (sender, recipient, puzzleId, challengeId) = await SeedAsync();

        await using var resolveDb = fixture.Schema.NewContext();
        // /resolve hat die Zeile schon geladen (offen) — sie bleibt im ChangeTracker dieses Kontexts.
        Assert.Equal(ChallengeStatus.Pending, (await resolveDb.PuzzleChallenges.FindAsync(challengeId))!.Status);

        await using (var attemptDb = fixture.Schema.NewContext())
        {
            attemptDb.PuzzleAttempts.Add(new PuzzleAttempt { UserId = recipient, PuzzleId = puzzleId, Solved = true, AttemptedAt = DateTime.UtcNow });
            await attemptDb.SaveChangesAsync();
            await Challenges(attemptDb).ResolveFromAttemptAsync(recipient, PuzzleSource.Standard, puzzleId, solved: true, timeSpentSeconds: 12);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Challenges(resolveDb).ResolveAsync(challengeId, recipient, clientSolved: true, timeSpentSeconds: 99));

        var (c, bells) = await StateAsync(challengeId, sender);
        Assert.Equal(ChallengeStatus.Solved, c.Status);
        Assert.Equal(12, c.TimeSpentSeconds);
        Assert.Equal(1, bells);
    }

    [MySqlFact]
    public async Task GaveUpViaResolve_BooksFailed_OnMariaDb()
    {
        var (sender, recipient, _, challengeId) = await SeedAsync();

        await using (var db = fixture.Schema.NewContext())
            Assert.True(await Challenges(db).ResolveAsync(challengeId, recipient, clientSolved: false, timeSpentSeconds: 5000));

        var (c, bells) = await StateAsync(challengeId, sender);
        Assert.Equal(ChallengeStatus.Failed, c.Status);
        Assert.Equal(3600, c.TimeSpentSeconds);
        Assert.Equal(1, bells);
    }
}

public sealed class ChallengeCloseSqlFixture() : MariaDbClassFixture("chclose", withApp: false);
