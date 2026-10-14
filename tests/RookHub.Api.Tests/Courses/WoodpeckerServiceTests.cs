using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Tests.Courses;

public class WoodpeckerServiceTests : IDisposable
{
    private sealed class Clock : TimeProvider
    {
        public DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private const int UserId = 1;
    private const int OtherUserId = 2;
    private readonly AppDbContext _db;
    private readonly Clock _clock = new();
    private readonly WoodpeckerService _service;

    public WoodpeckerServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _db.AppUsers.Add(new AppUser { Id = UserId, Username = "u", Email = "u@t.com", PasswordHash = "h" });
        _db.AppUsers.Add(new AppUser { Id = OtherUserId, Username = "o", Email = "o@t.com", PasswordHash = "h" });
        _db.SaveChanges();
        _service = new WoodpeckerService(_db, _clock);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Kurs des Nutzers mit <paramref name="count"/> Aufgaben (Runde = Lesereihenfolge); optional Erklärlinien.</summary>
    private async Task<(int BookId, List<int> PuzzleIds)> SeedBookAsync(string name, int count, int infoLines = 0,
        bool calculation = false, int? owner = UserId)
    {
        var book = new Book
        {
            FileName = name + ".pgn", DisplayName = name, OwnerUserId = owner, IsCalculation = calculation,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource(),
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var ids = new List<int>();
        for (var i = 0; i < count + infoLines; i++)
        {
            var p = new BookPuzzle
            {
                LineId = $"{name}-{i}", BookFileName = book.FileName, BookId = book.Id,
                Round = (i + 1).ToString(), Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1a2",
                IsInfoOnly = i >= count,
            };
            _db.BookPuzzles.Add(p);
            await _db.SaveChangesAsync();
            if (i < count) ids.Add(p.Id);
        }
        return (book.Id, ids);
    }

    private Task<WoodpeckerPlanDto> CreateAsync(params int[] bookIds) =>
        _service.CreateAsync(UserId, false, new CreateWoodpeckerPlanDto { BookIds = bookIds.ToList() });

    private Task<WoodpeckerPlanDto> SolveAsync(int planId, int puzzleId, bool solved = true) =>
        _service.RecordAttemptAsync(UserId, planId, new RecordWoodpeckerAttemptDto { BookPuzzleId = puzzleId, Solved = solved, TimeSeconds = 30 });

    [Fact]
    public void TargetDays_HalvesRoundedUp_AsInTheBook()
    {
        var days = Enumerable.Range(1, WoodpeckerRules.MaxCycles).Select(WoodpeckerRules.TargetDays).ToArray();
        Assert.Equal(new[] { 28, 14, 7, 4, 2, 1, 1 }, days);
    }

    [Fact]
    public async Task Create_FixedRules_FirstCycleFourWeeks()
    {
        var (bookId, _) = await SeedBookAsync("WP", 3, infoLines: 2);
        var plan = await CreateAsync(bookId);

        Assert.Equal("WP", plan.Name);
        Assert.Equal(1, plan.CurrentCycle);
        Assert.Equal("solving", plan.Status);
        Assert.Equal(3, plan.AvailableCount);   // Erklärlinien zählen nicht
        var cycle = Assert.Single(plan.Cycles);
        Assert.Equal(28, cycle.TargetDays);
        Assert.Equal(_clock.Now.AddDays(28), cycle.Deadline);
    }

    [Fact]
    public async Task Create_FutureStart_Rejected()
    {
        var (bookId, _) = await SeedBookAsync("WP", 3);
        await Assert.ThrowsAsync<DomainValidationException>(() => _service.CreateAsync(UserId, false,
            new CreateWoodpeckerPlanDto { BookIds = { bookId }, StartedAt = _clock.Now.AddHours(1) }));
    }

    [Fact]
    public async Task Create_InaccessibleOrCalculationCourse_Rejected()
    {
        var (foreign, _) = await SeedBookAsync("Fremd", 2, owner: OtherUserId);
        var (calc, _) = await SeedBookAsync("Kalk", 2, calculation: true);
        await Assert.ThrowsAsync<NotFoundException>(() => CreateAsync(foreign));
        await Assert.ThrowsAsync<DomainValidationException>(() => CreateAsync(calc));
    }

    [Fact]
    public async Task Create_Backdated_TakesCourseAttemptsSinceStart()
    {
        var (bookId, ids) = await SeedBookAsync("WP", 5);
        var start = _clock.Now.AddHours(-6);
        _db.CourseAttempts.AddRange(
            new CourseAttempt { UserId = UserId, BookId = bookId, BookPuzzleId = ids[0], Solved = true, TimeSeconds = 20, AttemptedAt = start.AddHours(-1) },   // vor dem Start
            new CourseAttempt { UserId = UserId, BookId = bookId, BookPuzzleId = ids[2], Solved = false, TimeSeconds = 40, AttemptedAt = start.AddHours(1) },
            new CourseAttempt { UserId = UserId, BookId = bookId, BookPuzzleId = ids[2], Solved = true, TimeSeconds = 10, AttemptedAt = start.AddHours(2) },  // zweiter Versuch zählt nicht
            new CourseAttempt { UserId = UserId, BookId = bookId, BookPuzzleId = ids[1], Solved = true, TimeSeconds = 15, AttemptedAt = start.AddHours(3) },
            new CourseAttempt { UserId = OtherUserId, BookId = bookId, BookPuzzleId = ids[3], Solved = true, TimeSeconds = 15, AttemptedAt = start.AddHours(3) });
        await _db.SaveChangesAsync();

        var plan = await _service.CreateAsync(UserId, false, new CreateWoodpeckerPlanDto { BookIds = { bookId }, StartedAt = start });

        Assert.Equal(2, plan.SetSize);
        var cycle = Assert.Single(plan.Cycles);
        Assert.Equal(2, cycle.Attempted);
        Assert.Equal(1, cycle.Solved);          // ids[2] zuerst verfehlt, ids[1] gelöst
        Assert.Equal(55, cycle.TimeSeconds);
        Assert.Equal(start, cycle.StartedAt);
        // Reihenfolge im Satz = zeitliche Reihenfolge
        var order = await _db.WoodpeckerSetItems.OrderBy(i => i.Position).Select(i => i.BookPuzzleId).ToListAsync();
        Assert.Equal(new[] { ids[2], ids[1] }, order);
    }

    [Fact]
    public async Task Next_FirstCycle_GoesThroughCoursesInOrder()
    {
        var (a, aIds) = await SeedBookAsync("A", 2);
        var (b, bIds) = await SeedBookAsync("B", 1);
        var plan = await CreateAsync(b, a);   // B zuerst

        var seen = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var next = await _service.GetNextAsync(UserId, plan.Id);
            Assert.NotNull(next.Puzzle);
            seen.Add(next.Puzzle!.Id);
            await SolveAsync(plan.Id, next.Puzzle.Id);
        }
        Assert.Equal(new[] { bIds[0], aIds[0], aIds[1] }, seen);

        // Alle Aufgaben im Satz → Zyklus 1 ist damit zu Ende, Pause beginnt.
        var after = await _service.GetNextAsync(UserId, plan.Id);
        Assert.Null(after.Puzzle);
        Assert.Equal("break", after.Plan.Status);
        Assert.Equal(3, after.Plan.SetSize);
    }

    [Fact]
    public async Task RecordAttempt_OnlyFirstAttemptPerCycleCounts()
    {
        var (bookId, ids) = await SeedBookAsync("WP", 3);
        var plan = await CreateAsync(bookId);
        await SolveAsync(plan.Id, ids[0], solved: false);
        var dto = await SolveAsync(plan.Id, ids[0], solved: true);
        Assert.Equal(1, dto.SetSize);
        Assert.Equal(0, dto.Cycles[0].Solved);
    }

    [Fact]
    public async Task RecordAttempt_PuzzleOfOtherCourse_NotFound()
    {
        var (bookId, _) = await SeedBookAsync("WP", 3);
        var (_, otherIds) = await SeedBookAsync("Andere", 1);
        var plan = await CreateAsync(bookId);
        await Assert.ThrowsAsync<NotFoundException>(() => SolveAsync(plan.Id, otherIds[0]));
    }

    [Fact]
    public async Task FirstCycle_EndsAtDeadline_ThenBreakOfOneDay_ThenRandomFromSetOnly()
    {
        var (bookId, ids) = await SeedBookAsync("WP", 10);
        var plan = await CreateAsync(bookId);
        await SolveAsync(plan.Id, ids[0]);
        await SolveAsync(plan.Id, ids[1]);
        await SolveAsync(plan.Id, ids[2]);

        _clock.Now = _clock.Now.AddDays(28).AddHours(1);
        var dto = await _service.GetAsync(UserId, plan.Id);
        Assert.Equal("break", dto.Status);                         // Frist um 1 h vorbei, Pause läuft noch
        Assert.Equal(dto.Cycles[0].Deadline, dto.Cycles[0].FinishedAt);
        await Assert.ThrowsAsync<ConflictException>(() => _service.StartNextCycleAsync(UserId, plan.Id));

        _clock.Now = _clock.Now.AddDays(1);
        Assert.Equal("ready", (await _service.GetAsync(UserId, plan.Id)).Status);
        dto = await _service.StartNextCycleAsync(UserId, plan.Id);
        Assert.Equal(2, dto.CurrentCycle);
        Assert.Equal(14, dto.Cycles[1].TargetDays);

        var seen = new HashSet<int>();
        for (var i = 0; i < 3; i++)
        {
            var next = await _service.GetNextAsync(UserId, plan.Id);
            Assert.NotNull(next.Puzzle);
            Assert.Contains(next.Puzzle!.Id, ids.Take(3));        // nur der Satz, nie Aufgabe 4–10
            Assert.True(seen.Add(next.Puzzle.Id));                // keine Wiederholung im Zyklus
            await SolveAsync(plan.Id, next.Puzzle.Id);
        }
        dto = await _service.GetAsync(UserId, plan.Id);
        Assert.NotNull(dto.Cycles[1].FinishedAt);
        // Zyklus 2 innerhalb eines Tages geschafft → Methode abgeschlossen.
        Assert.Equal("completed", dto.Status);
        Assert.NotNull(dto.CompletedAt);
    }

    [Fact]
    public async Task SecondCycle_TakingLongerThanADay_ContinuesWithThirdCycle()
    {
        var (bookId, ids) = await SeedBookAsync("WP", 2);
        var plan = await CreateAsync(bookId);
        await SolveAsync(plan.Id, ids[0]);
        await _service.FinishSetAsync(UserId, plan.Id);
        _clock.Now = _clock.Now.AddDays(2);
        await _service.StartNextCycleAsync(UserId, plan.Id);
        _clock.Now = _clock.Now.AddDays(3);
        var dto = await SolveAsync(plan.Id, ids[0]);
        Assert.Null(dto.CompletedAt);
        Assert.Equal("break", dto.Status);
        _clock.Now = _clock.Now.AddDays(1);
        dto = await _service.StartNextCycleAsync(UserId, plan.Id);
        Assert.Equal(3, dto.CurrentCycle);
        Assert.Equal(7, dto.Cycles[2].TargetDays);
    }

    [Fact]
    public async Task FinishSet_WithoutAnyPuzzle_Rejected()
    {
        var (bookId, _) = await SeedBookAsync("WP", 2);
        var plan = await CreateAsync(bookId);
        await Assert.ThrowsAsync<DomainValidationException>(() => _service.FinishSetAsync(UserId, plan.Id));
    }

    [Fact]
    public async Task UpdateStart_Backdates_ButNotAfterFirstAttempt()
    {
        var (bookId, ids) = await SeedBookAsync("WP", 3);
        var plan = await CreateAsync(bookId);
        _clock.Now = _clock.Now.AddHours(2);
        await SolveAsync(plan.Id, ids[0]);

        var earlier = _clock.Now.AddDays(-3);
        var dto = await _service.UpdateStartAsync(UserId, plan.Id, earlier);
        Assert.Equal(earlier, dto.Cycles[0].StartedAt);
        _clock.Now = _clock.Now.AddMinutes(10);   // erster Versuch liegt jetzt 10 min zurück
        await Assert.ThrowsAsync<DomainValidationException>(() => _service.UpdateStartAsync(UserId, plan.Id, _clock.Now.AddMinutes(-1)));
    }

    [Fact]
    public async Task OtherUsersPlan_NotFound()
    {
        var (bookId, _) = await SeedBookAsync("WP", 2);
        var plan = await CreateAsync(bookId);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(OtherUserId, plan.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteAsync(OtherUserId, plan.Id));
    }

    [Fact]
    public async Task Delete_RemovesPlanWithSetAndAttempts()
    {
        var (bookId, ids) = await SeedBookAsync("WP", 2);
        var plan = await CreateAsync(bookId);
        await SolveAsync(plan.Id, ids[0]);
        await _service.DeleteAsync(UserId, plan.Id);
        Assert.Empty(await _service.ListAsync(UserId));
        Assert.Equal(0, await _db.WoodpeckerSetItems.CountAsync());
        Assert.Equal(0, await _db.WoodpeckerAttempts.CountAsync());
    }
}
