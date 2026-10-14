using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Courses;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Woodpecker-Training gegen echtes MariaDB: die Abfragen des Dienstes (korrelierte Unterabfragen „noch nicht im
/// Satz“, Gruppierung je Zyklus mit bedingtem Zählen, Lesereihenfolge) wertet InMemory klaglos im Speicher aus —
/// ob MySQL sie übersetzt, zeigt sich nur hier. Dazu der Restrict-FK auf BookPuzzle: Linien löschen muss über
/// <see cref="BookPuzzleDependents"/> durchgehen.
/// </summary>
public class WoodpeckerSqlTests(WoodpeckerFixture fixture) : IAsyncLifetime, IClassFixture<WoodpeckerFixture>
{
    private sealed class Clock : TimeProvider
    {
        public DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private AppDbContext _db = null!;
    private readonly Clock _clock = new();

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.Schema.ConnectionString, DbServerVersion.Current)
            .Options;
        _db = new AppDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    private async Task<(int UserId, int BookId, List<int> Ids)> SeedAsync(int count)
    {
        var user = new AppUser { Username = "wp" + Guid.NewGuid().ToString("N")[..6], Email = $"{Guid.NewGuid():N}@t.local", PasswordHash = "x" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var book = new Book { FileName = "wp.pgn", DisplayName = "WP", OwnerUserId = user.Id, Source = new BookSource() };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var ids = new List<int>();
        for (var i = 0; i < count + 1; i++)
        {
            var bp = new BookPuzzle
            {
                BookId = book.Id, BookFileName = book.FileName, Round = (i + 1).ToString(),
                LineId = Guid.NewGuid().ToString("N"), Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1a2",
                IsInfoOnly = i == count,   // eine Erklärlinie, die nie in den Satz darf
            };
            _db.BookPuzzles.Add(bp);
            await _db.SaveChangesAsync();
            if (i < count) ids.Add(bp.Id);
        }
        return (user.Id, book.Id, ids);
    }

    [MySqlFact]
    public async Task FullCycle_TranslatesOnMariaDb()
    {
        var (userId, bookId, ids) = await SeedAsync(4);
        var start = _clock.Now.AddHours(-5);
        _db.CourseAttempts.Add(new CourseAttempt
        {
            UserId = userId, BookId = bookId, BookPuzzleId = ids[1], Solved = true, TimeSeconds = 12, AttemptedAt = start.AddHours(1),
        });
        await _db.SaveChangesAsync();
        var service = new WoodpeckerService(_db, _clock);

        var plan = await service.CreateAsync(userId, false, new CreateWoodpeckerPlanDto { BookIds = { bookId }, StartedAt = start });
        Assert.Equal(1, plan.SetSize);   // aus dem Kurs übernommen
        Assert.Equal(4, plan.AvailableCount);

        // Zyklus 1 in Lesereihenfolge, ohne die schon übernommene Aufgabe.
        var next = await service.GetNextAsync(userId, plan.Id);
        Assert.Equal(ids[0], next.Puzzle!.Id);
        await service.RecordAttemptAsync(userId, plan.Id, new RecordWoodpeckerAttemptDto { BookPuzzleId = ids[0], Solved = false, TimeSeconds = 30 });
        next = await service.GetNextAsync(userId, plan.Id);
        Assert.Equal(ids[2], next.Puzzle!.Id);

        var dto = await service.FinishSetAsync(userId, plan.Id);
        Assert.Equal(2, dto.SetSize);
        Assert.Equal(2, dto.Cycles[0].Attempted);
        Assert.Equal(1, dto.Cycles[0].Solved);
        Assert.Equal(42, dto.Cycles[0].TimeSeconds);

        _clock.Now = _clock.Now.AddDays(1).AddMinutes(1);
        dto = await service.StartNextCycleAsync(userId, plan.Id);
        Assert.Equal(2, dto.CurrentCycle);
        var seen = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            next = await service.GetNextAsync(userId, plan.Id);
            seen.Add(next.Puzzle!.Id);
            await service.RecordAttemptAsync(userId, plan.Id, new RecordWoodpeckerAttemptDto { BookPuzzleId = next.Puzzle.Id, Solved = true, TimeSeconds = 5 });
        }
        Assert.Equal(new[] { ids[0], ids[1] }, seen.OrderBy(x => x));
        dto = await service.GetAsync(userId, plan.Id);
        Assert.Equal("completed", dto.Status);
        Assert.Single(await service.ListAsync(userId));
    }

    [MySqlFact]
    public async Task DeletingLines_ClearsSetAndAttempts_ThroughDependents()
    {
        var (userId, bookId, ids) = await SeedAsync(2);
        var service = new WoodpeckerService(_db, _clock);
        var plan = await service.CreateAsync(userId, false, new CreateWoodpeckerPlanDto { BookIds = { bookId } });
        await service.RecordAttemptAsync(userId, plan.Id, new RecordWoodpeckerAttemptDto { BookPuzzleId = ids[0], Solved = true, TimeSeconds = 5 });

        var lineIds = _db.BookPuzzles.Where(bp => bp.Id == ids[0]).Select(bp => bp.Id);
        await BookPuzzleDependents.RemoveForLinesAsync(_db, lineIds);
        _db.BookPuzzles.RemoveRange(await _db.BookPuzzles.Where(bp => bp.Id == ids[0]).ToListAsync());
        await _db.SaveChangesAsync();   // ohne Abräumen: FK-Fehler (Restrict)

        Assert.Equal(0, await _db.WoodpeckerSetItems.CountAsync());
        Assert.Equal(0, await _db.WoodpeckerAttempts.CountAsync());
    }

    [MySqlFact]
    public async Task DeletingPlan_RemovesChildren()
    {
        var (userId, bookId, ids) = await SeedAsync(2);
        var service = new WoodpeckerService(_db, _clock);
        var plan = await service.CreateAsync(userId, false, new CreateWoodpeckerPlanDto { BookIds = { bookId } });
        await service.RecordAttemptAsync(userId, plan.Id, new RecordWoodpeckerAttemptDto { BookPuzzleId = ids[0], Solved = true, TimeSeconds = 5 });
        await service.DeleteAsync(userId, plan.Id);
        Assert.Equal(0, await _db.WoodpeckerPlans.CountAsync());
        Assert.Equal(0, await _db.WoodpeckerCycles.CountAsync());
        Assert.Equal(0, await _db.WoodpeckerPlanBooks.CountAsync());
    }
}
