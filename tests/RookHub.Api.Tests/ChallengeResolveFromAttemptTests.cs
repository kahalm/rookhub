using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Challenge und Revanche schließt der gespeicherte Versuch (Codereview 2026-09-29, N9-001). Der Client schickte
/// Versuch und Ergebnis-Meldung gleichzeitig ab, der Buch-Solver die Meldung sogar VOR dem Versuch — die Prüfung
/// gegen die Versuchstabelle lief fast immer, bevor der Versuch gespeichert war: echte Lösungen wurden endgültig als
/// „nicht gelöst" gebucht, die Revanche-Glocke fiel aus.
/// </summary>
public class ChallengeResolveFromAttemptTests : IDisposable
{
    private const int Sender = 1, Recipient = 2, OtherSender = 3;

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ChallengeService _challenges;
    private readonly RevengeNotificationService _revenge;

    public ChallengeResolveFromAttemptTests()
    {
        var notifications = new NotificationService(_db);
        var friends = new FriendService(_db, notifications);
        _challenges = new ChallengeService(_db, friends, notifications);
        _revenge = new RevengeNotificationService(_db, friends, notifications);
    }

    public void Dispose() { _db.Dispose(); _cache.Dispose(); }

    private PuzzleService Puzzles() => new(_db, _cache, NullLogger<PuzzleService>.Instance,
        new PuzzleTaggingService(_db, NullLogger<PuzzleTaggingService>.Instance), _challenges, _revenge);

    private BookPuzzleService BookPuzzles() => new(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue(),
        challenges: _challenges);

    private async Task SeedAsync()
    {
        foreach (var (id, name) in new[] { (Sender, "alice"), (Recipient, "bob"), (OtherSender, "carol") })
            _db.AppUsers.Add(new AppUser { Id = id, Username = name, Email = $"{name}@test.com", PasswordHash = "x", Profile = new UserProfile() });
        _db.Friendships.Add(new Friendship { RequesterId = Sender, AddresseeId = Recipient, Status = FriendshipStatus.Accepted });
        _db.Friendships.Add(new Friendship { RequesterId = OtherSender, AddresseeId = Recipient, Status = FriendshipStatus.Accepted });
        _db.Puzzles.Add(new Puzzle { Id = 100, LichessId = "p100", Fen = "fen", Moves = "e2e4", Rating = 1500 });
        _db.BookPuzzles.Add(new BookPuzzle { Id = 200, LineId = "b200", BookFileName = "book.pgn", Fen = "fen", Moves = "e2e4" });
        await _db.SaveChangesAsync();
    }

    private async Task<PuzzleChallenge> ChallengeAsync(PuzzleSource source, int puzzleId, int from = Sender)
    {
        var c = new PuzzleChallenge { FromUserId = from, ToUserId = Recipient, PuzzleId = puzzleId, Source = source };
        _db.PuzzleChallenges.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    private async Task<PuzzleChallenge> ReloadAsync(int id)
        => await _db.PuzzleChallenges.AsNoTracking().SingleAsync(c => c.Id == id);

    private Task<List<Notification>> ResolvedBellsAsync(int to)
        => _db.Notifications.Where(n => n.UserId == to && n.Type == NotificationType.ChallengeResolved).ToListAsync();

    private static RecordPuzzleAttemptDto Attempt(bool solved, int seconds = 14, int? revengeUserId = null)
        => new() { Solved = solved, TimeSpentSeconds = seconds, RevengeUserId = revengeUserId };

    // ── Standard-Puzzle ──────────────────────────────────────────────────────────────────────────

    /// <summary>Das gemeldete Fehlerbild: die Meldung „gelöst" überholt den Versuch. Vorher: Failed, endgültig, und
    /// die Glocke „bob hat es nicht gelöst". Jetzt bleibt die Challenge offen, der Versuch bucht sie als gelöst.</summary>
    [Fact]
    public async Task Standard_ResolveOvertakesAttempt_ChallengeEndsSolved_WithOneBell()
    {
        await SeedAsync();
        var c = await ChallengeAsync(PuzzleSource.Standard, 100);

        var resolved = await _challenges.ResolveAsync(c.Id, Recipient, clientSolved: true, timeSpentSeconds: 14);

        Assert.False(resolved);
        Assert.Equal(ChallengeStatus.Pending, (await ReloadAsync(c.Id)).Status);
        Assert.Empty(await ResolvedBellsAsync(Sender));

        await Puzzles().RecordAttemptAsync(Recipient, 100, Attempt(solved: true, seconds: 14));

        var after = await ReloadAsync(c.Id);
        Assert.Equal(ChallengeStatus.Solved, after.Status);
        Assert.Equal(14, after.TimeSpentSeconds);
        Assert.NotNull(after.ResolvedAt);
        var bell = Assert.Single(await ResolvedBellsAsync(Sender));
        Assert.Contains("\"solved\":\"true\"", bell.DataJson);
    }

    /// <summary>Kommt der Versuch zuerst, schließt er die Challenge; das nachlaufende /resolve findet sie abgeschlossen
    /// (409 wie bisher) und benachrichtigt nicht ein zweites Mal.</summary>
    [Fact]
    public async Task Standard_AttemptFirst_ClosesChallenge_LaterResolveIsRejected()
    {
        await SeedAsync();
        var c = await ChallengeAsync(PuzzleSource.Standard, 100);

        await Puzzles().RecordAttemptAsync(Recipient, 100, Attempt(solved: true));

        Assert.Equal(ChallengeStatus.Solved, (await ReloadAsync(c.Id)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _challenges.ResolveAsync(c.Id, Recipient, clientSolved: true, timeSpentSeconds: 14));
        Assert.Single(await ResolvedBellsAsync(Sender));
    }

    [Fact]
    public async Task Standard_FailedAttempt_BooksFailed()
    {
        await SeedAsync();
        var c = await ChallengeAsync(PuzzleSource.Standard, 100);

        await Puzzles().RecordAttemptAsync(Recipient, 100, Attempt(solved: false));

        Assert.Equal(ChallengeStatus.Failed, (await ReloadAsync(c.Id)).Status);
        var bell = Assert.Single(await ResolvedBellsAsync(Sender));
        Assert.Contains("\"solved\":\"false\"", bell.DataJson);
    }

    /// <summary>„Nicht gelöst/aufgegeben" bleibt der Weg über /resolve; der erste Ausgang zählt, ein späterer
    /// gelöster Versuch ändert die Challenge nicht mehr (wie im Client: challengeResolved nur einmal).</summary>
    [Fact]
    public async Task GaveUpViaResolve_StaysFailed_EvenIfSolvedLater()
    {
        await SeedAsync();
        var c = await ChallengeAsync(PuzzleSource.Standard, 100);

        Assert.True(await _challenges.ResolveAsync(c.Id, Recipient, clientSolved: false, timeSpentSeconds: 30));
        await Puzzles().RecordAttemptAsync(Recipient, 100, Attempt(solved: true));

        Assert.Equal(ChallengeStatus.Failed, (await ReloadAsync(c.Id)).Status);
        Assert.Single(await ResolvedBellsAsync(Sender));
    }

    [Fact]
    public async Task Attempt_ClosesEveryOpenChallengeForThatPuzzle_AndNothingElse()
    {
        await SeedAsync();
        _db.Puzzles.Add(new Puzzle { Id = 101, LichessId = "p101", Fen = "fen", Moves = "e2e4", Rating = 1500 });
        await _db.SaveChangesAsync();
        var fromAlice = await ChallengeAsync(PuzzleSource.Standard, 100);
        var fromCarol = await ChallengeAsync(PuzzleSource.Standard, 100, from: OtherSender);
        var otherPuzzle = await ChallengeAsync(PuzzleSource.Standard, 101);
        var bookWithSameId = await ChallengeAsync(PuzzleSource.Book, 100);

        await Puzzles().RecordAttemptAsync(Recipient, 100, Attempt(solved: true));

        Assert.Equal(ChallengeStatus.Solved, (await ReloadAsync(fromAlice.Id)).Status);
        Assert.Equal(ChallengeStatus.Solved, (await ReloadAsync(fromCarol.Id)).Status);
        Assert.Equal(ChallengeStatus.Pending, (await ReloadAsync(otherPuzzle.Id)).Status);
        Assert.Equal(ChallengeStatus.Pending, (await ReloadAsync(bookWithSameId.Id)).Status);
        Assert.Single(await ResolvedBellsAsync(Sender));
        Assert.Single(await ResolvedBellsAsync(OtherSender));
    }

    // ── Buch-Puzzle ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Der Buch-Solver meldete VOR dem Versuch — praktisch jede Buch-Challenge endete als „nicht gelöst".</summary>
    [Fact]
    public async Task Book_ResolveSentBeforeAttempt_ChallengeEndsSolved()
    {
        await SeedAsync();
        var c = await ChallengeAsync(PuzzleSource.Book, 200);

        Assert.False(await _challenges.ResolveAsync(c.Id, Recipient, clientSolved: true, timeSpentSeconds: 9));
        await BookPuzzles().RecordAttemptAsync(200, Recipient, new RecordBookAttemptDto { Solved = true, TimeSeconds = 9 });

        var after = await ReloadAsync(c.Id);
        Assert.Equal(ChallengeStatus.Solved, after.Status);
        Assert.Equal(9, after.TimeSpentSeconds);
        Assert.Single(await ResolvedBellsAsync(Sender));
    }

    // ── Revanche ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>/revenge/result lief parallel zum Versuch und fand keinen Versuch des Rächers → keine Glocke, der
    /// Client meldete nie erneut. Mit <c>revengeUserId</c> im Versuch legt der Server sie selbst an.</summary>
    [Fact]
    public async Task Revenge_WithRevengeUserIdInAttempt_NotifiesTheFriend()
    {
        await SeedAsync();
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = Sender, PuzzleId = 100, Solved = false, AttemptedAt = DateTime.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync();

        // Das alte Rennen: die Meldung kommt vor dem Versuch an → nichts angelegt.
        Assert.False(await _revenge.RecordAsync(Recipient, Sender, 100));

        await Puzzles().RecordAttemptAsync(Recipient, 100, Attempt(solved: true, revengeUserId: Sender));

        var n = Assert.Single(await _db.RevengeNotifications.ToListAsync());
        Assert.Equal(Recipient, n.AvengerUserId);
        Assert.Equal(Sender, n.TargetUserId);
        Assert.True(n.Solved);
        Assert.True(await _db.Notifications.AnyAsync(x => x.UserId == Sender && x.Type == NotificationType.RevengePerformed));
    }

    [Fact]
    public async Task Revenge_WithoutRevengeUserId_CreatesNothing()
    {
        await SeedAsync();
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = Sender, PuzzleId = 100, Solved = false, AttemptedAt = DateTime.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync();

        await Puzzles().RecordAttemptAsync(Recipient, 100, Attempt(solved: true));

        Assert.Empty(await _db.RevengeNotifications.ToListAsync());
    }

    // ── Verdrahtung ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Die Dienste bekommen ChallengeService/RevengeNotificationService als OPTIONALE Parameter (die Tests
    /// bauen sie ohne). Der DI-Container muss sie trotzdem einsetzen — sonst liefe der Fix in Produktion ins Leere.</summary>
    [Fact]
    public async Task DependencyInjection_FillsTheOptionalServices()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IWebhookTaskQueue, NoOpTaskQueue>();
        services.AddScoped<NotificationService>();
        services.AddScoped<FriendService>();
        services.AddScoped<ChallengeService>();
        services.AddScoped<RevengeNotificationService>();
        services.AddScoped<PuzzleTaggingService>();
        services.AddScoped<PuzzleService>();
        services.AddScoped<BookPuzzleService>();
        await using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AppUsers.Add(new AppUser { Id = Sender, Username = "alice", Email = "a@test.com", PasswordHash = "x" });
        db.AppUsers.Add(new AppUser { Id = Recipient, Username = "bob", Email = "b@test.com", PasswordHash = "x" });
        db.Puzzles.Add(new Puzzle { Id = 100, LichessId = "p100", Fen = "fen", Moves = "e2e4", Rating = 1500 });
        db.BookPuzzles.Add(new BookPuzzle { Id = 200, LineId = "b200", BookFileName = "book.pgn", Fen = "fen", Moves = "e2e4" });
        db.PuzzleChallenges.Add(new PuzzleChallenge { Id = 1, FromUserId = Sender, ToUserId = Recipient, PuzzleId = 100, Source = PuzzleSource.Standard });
        db.PuzzleChallenges.Add(new PuzzleChallenge { Id = 2, FromUserId = Sender, ToUserId = Recipient, PuzzleId = 200, Source = PuzzleSource.Book });
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<PuzzleService>().RecordAttemptAsync(Recipient, 100, Attempt(solved: true));
        await scope.ServiceProvider.GetRequiredService<BookPuzzleService>()
            .RecordAttemptAsync(200, Recipient, new RecordBookAttemptDto { Solved = true, TimeSeconds = 5 });

        var statuses = await db.PuzzleChallenges.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Status).ToListAsync();
        Assert.Equal([ChallengeStatus.Solved, ChallengeStatus.Solved], statuses);
    }
}
