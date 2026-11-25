using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Tests.Courses;

/// <summary>Ab Zyklus 2 steht die (gemischte) Reihenfolge vom Start des Zyklus an fest (Wunsch 25.11.2026): Neu laden,
/// später weitermachen oder ein anderes Gerät bringen dieselbe Aufgabe. Bis 1.012 zog jedes <c>GET next</c> neu.</summary>
public class WoodpeckerFixedOrderTests : IDisposable
{
    private sealed class Clock : TimeProvider
    {
        public DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private const int UserId = 1;
    private readonly AppDbContext _db;
    private readonly Clock _clock = new();
    private readonly WoodpeckerService _service;

    public WoodpeckerFixedOrderTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.AppUsers.Add(new AppUser { Id = UserId, Username = "u", Email = "u@t.com", PasswordHash = "h" });
        _db.SaveChanges();
        _service = new WoodpeckerService(_db, _clock);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(int BookId, List<int> Ids)> SeedBookAsync(string name, int count)
    {
        var book = new Book
        {
            FileName = name + ".pgn", DisplayName = name, OwnerUserId = UserId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource(),
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var p = new BookPuzzle
            {
                LineId = $"{name}-{i}", BookFileName = book.FileName, BookId = book.Id, Round = (i + 1).ToString(),
                Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1a2",
            };
            _db.BookPuzzles.Add(p);
            await _db.SaveChangesAsync();
            ids.Add(p.Id);
        }
        return (book.Id, ids);
    }

    private Task SolveAsync(int planId, int puzzleId) =>
        _service.RecordAttemptAsync(UserId, planId, new RecordWoodpeckerAttemptDto { BookPuzzleId = puzzleId, Solved = true, TimeSeconds = 30 });

    /// <summary>Plan über einen Kurs mit <paramref name="count"/> Aufgaben; Zyklus 1 alle gelöst, Zyklus 2 läuft.</summary>
    private async Task<(int PlanId, List<int> Ids)> SecondCycleAsync(int count = 10)
    {
        var (bookId, ids) = await SeedBookAsync("WP", count);
        var plan = await _service.CreateAsync(UserId, false, new CreateWoodpeckerPlanDto { BookIds = { bookId } });
        foreach (var id in ids) await SolveAsync(plan.Id, id);   // Zyklus 1 endet von selbst
        _clock.Now = _clock.Now.AddDays(2);
        Assert.Equal(2, (await _service.StartNextCycleAsync(UserId, plan.Id)).CurrentCycle);
        return (plan.Id, ids);
    }

    private static List<int> Expected(int planId, int cycle, IEnumerable<int> ids) =>
        ids.OrderBy(id => WoodpeckerRules.ShuffleKey(planId, cycle, id)).ThenBy(id => id).ToList();

    private async Task<int> NextIdAsync(int planId) => (await _service.GetNextAsync(UserId, planId)).Puzzle!.Id;

    /// <summary>Durch den laufenden Zyklus gehen; vor der letzten Aufgabe 2 Tage vor, damit die Methode nicht endet.</summary>
    private async Task<List<int>> WalkCycleAsync(int planId, int count)
    {
        var seen = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var id = await NextIdAsync(planId);
            seen.Add(id);
            if (i == count - 1) _clock.Now = _clock.Now.AddDays(2);
            await SolveAsync(planId, id);
        }
        return seen;
    }

    [Fact]
    public void ShuffleKey_IsFixed_LiteralValues()
    {
        // Ändert sich der Schlüssel, springt jeder laufende Zyklus auf Prod um — diese Werte dürfen nie kippen.
        Assert.Equal(0xB815246F417CADE2UL, WoodpeckerRules.ShuffleKey(1, 2, 1));
        Assert.Equal(0x2EC512C6E52FD423UL, WoodpeckerRules.ShuffleKey(42, 3, 12345));
    }

    [Fact]
    public async Task Next_SecondCycle_RepeatedWithoutAttempt_SamePuzzle()
    {
        var (planId, _) = await SecondCycleAsync();
        var first = await NextIdAsync(planId);
        for (var i = 0; i < 5; i++) Assert.Equal(first, await NextIdAsync(planId));
        // auch nach Stunden (später weitermachen) dieselbe
        _clock.Now = _clock.Now.AddHours(10);
        Assert.Equal(first, await NextIdAsync(planId));
    }

    [Fact]
    public async Task Next_SecondCycle_AfterAttempt_FollowsFixedShuffledOrder()
    {
        var (planId, ids) = await SecondCycleAsync();
        var expected = Expected(planId, 2, ids);
        Assert.NotEqual(ids, expected);   // gemischt, nicht Lesereihenfolge

        var seen = await WalkCycleAsync(planId, ids.Count);
        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task Next_DifferentCycles_DifferentOrder()
    {
        var (planId, ids) = await SecondCycleAsync();
        var cycle2 = await WalkCycleAsync(planId, ids.Count);
        _clock.Now = _clock.Now.AddDays(2);
        Assert.Equal(3, (await _service.StartNextCycleAsync(UserId, planId)).CurrentCycle);
        var cycle3 = await WalkCycleAsync(planId, ids.Count);

        Assert.Equal(Expected(planId, 3, ids), cycle3);
        Assert.NotEqual(cycle2, cycle3);
        Assert.Equal(cycle2.OrderBy(x => x), cycle3.OrderBy(x => x));   // derselbe Satz
    }

    [Fact]
    public async Task Next_UnrelatedSetItemRemoved_OrderOfRemainingUnchanged()
    {
        var (planId, ids) = await SecondCycleAsync();
        var expected = Expected(planId, 2, ids);
        await SolveAsync(planId, await NextIdAsync(planId));   // erste der Reihe erledigt
        var next = await NextIdAsync(planId);
        Assert.Equal(expected[1], next);

        // Eine spätere Aufgabe fällt aus dem Satz (z. B. Kurs geändert) — die übrigen behalten ihre Reihenfolge.
        var removed = expected[5];
        _db.WoodpeckerSetItems.Remove(await _db.WoodpeckerSetItems.SingleAsync(i => i.PlanId == planId && i.BookPuzzleId == removed));
        await _db.SaveChangesAsync();

        Assert.Equal(next, await NextIdAsync(planId));
        var rest = new List<int>();
        for (var i = 0; i < ids.Count - 2; i++)
        {
            var id = await NextIdAsync(planId);
            rest.Add(id);
            await SolveAsync(planId, id);
        }
        Assert.Equal(expected.Skip(1).Where(id => id != removed), rest);
    }

    [Fact]
    public async Task Next_InaccessibleCourseResting_OtherCourseKeepsItsOrder()
    {
        // Zwei Kurse im Satz; der zweite wird privat-fremd (kein Zugang) → seine Aufgaben fallen heraus, die des
        // ersten kommen in derselben festen Reihenfolge wie vorher.
        var (bookA, idsA) = await SeedBookAsync("A", 5);
        var (bookB, idsB) = await SeedBookAsync("B", 5);
        var plan = await _service.CreateAsync(UserId, false, new CreateWoodpeckerPlanDto { BookIds = { bookA, bookB } });
        foreach (var id in idsA.Concat(idsB)) await SolveAsync(plan.Id, id);
        _clock.Now = _clock.Now.AddDays(2);
        await _service.StartNextCycleAsync(UserId, plan.Id);

        var all = Expected(plan.Id, 2, idsA.Concat(idsB));
        Assert.Equal(all[0], await NextIdAsync(plan.Id));

        var b = await _db.Books.SingleAsync(x => x.Id == bookB);
        b.OwnerUserId = 999;   // Kurs B gehört jetzt jemand anderem → kein Zugang
        await _db.SaveChangesAsync();

        var onlyA = all.Where(idsA.Contains).ToList();
        var seen = new List<int>();
        foreach (var _ in onlyA)
        {
            var id = await NextIdAsync(plan.Id);
            seen.Add(id);
            await SolveAsync(plan.Id, id);
        }
        Assert.Equal(onlyA, seen);
    }
}
