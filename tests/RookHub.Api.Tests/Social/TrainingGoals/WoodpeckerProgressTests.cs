using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Social;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;
using RookHub.Api.Services.Social.TrainingGoals;

namespace RookHub.Api.Tests.Social.TrainingGoals;

/// <summary>Woodpecker-Zeit in den Trainingszielen: eigene Quelle, fest Taktik, Deckel je Versuch, und aus Kurs-Versuchen
/// übernommene Zyklus-1-Versuche zählen nicht doppelt. Feste, eindeutige Ids (99 47xx), damit parallele Tests nicht kollidieren.</summary>
public class WoodpeckerProgressTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    // ----- reine Zusammenführung -----

    [Fact]
    public void Merge_CountsAsWoodpeckerTactics_CappedPerAttempt()
    {
        var days = new TrainingDays();
        WoodpeckerProgress.Merge(new WoodpeckerProgress.Rows(
            [new(Noon, 120, 2, 994701), new(Noon, 99999, 3, 994702)], []), days);

        var d = days.Days[DateOnly.FromDateTime(Noon)];
        Assert.Equal(120 + TrainingCaps.PerPuzzleCapSeconds, d.Total);
        Assert.Equal(d.Total, d.Source[(int)TrainingSource.Woodpecker]);
        Assert.Equal(d.Total, d.Theme[(int)TrainingTheme.Tactics]);
        Assert.Equal(0, d.Source[(int)TrainingSource.CourseBook]);
    }

    [Fact]
    public void Merge_ImportedCycleOneAttempt_IsSkipped_SameKeyInLaterCycleStillCounts()
    {
        var days = new TrainingDays();
        WoodpeckerProgress.Merge(new WoodpeckerProgress.Rows(
            [new(Noon, 300, 1, 994711), new(Noon, 200, 2, 994711), new(Noon.AddMinutes(1), 50, 1, 994711)],
            [(994711, Noon)]), days);

        // 300 (Zyklus 1, übernommen) fällt weg; Zyklus 2 und der andere Zeitpunkt zählen.
        Assert.Equal(250, days.Days[DateOnly.FromDateTime(Noon)].Source[(int)TrainingSource.Woodpecker]);
    }

    [Fact]
    public void Projection_MapsWoodpeckerSeconds_InDayAndPeriodSum()
    {
        var days = new TrainingDays();
        WoodpeckerProgress.Merge(new WoodpeckerProgress.Rows([new(Noon, 90, 2, 994721)], []), days);
        var goal = new TrainingGoalDto { DailyMinutes = 1, Source = "personal" };

        var tracker = TrainingDayProjection.Tracker(goal, days.Days);
        Assert.Equal(90, Assert.Single(tracker.Days).BySource.WoodpeckerSeconds);
        Assert.Equal(90, tracker.BreakdownBySource.WoodpeckerSeconds);
        Assert.Equal(90, tracker.BreakdownByTheme.TacticsSeconds);
        Assert.Equal("full", tracker.Days[0].Status);
    }

    [Fact]
    public void SourceBreakdownDto_FieldsAreTheFourSources()
    {
        // Spiegel im Frontend: SOURCE_KEYS (training-goals.service.ts), Gegenstück-Test in training-goals.spec.ts.
        Assert.Equal(["RandomPuzzleSeconds", "CourseBookSeconds", "ChessableSeconds", "WoodpeckerSeconds"],
            typeof(SourceBreakdownDto).GetProperties().Select(p => p.Name));
        Assert.Equal(4, TrainingBuckets.SourceCount);
    }

    // ----- Abfrage + Dienst (InMemory) -----

    [Fact]
    public async Task Service_ImportedCycleOneAttempt_CountsOnlyAsCourseTime_DirectAttemptsAsWoodpecker()
    {
        var user = new AppUser { Id = 994731, Username = "wp", Email = "wp@t.com", PasswordHash = "h" };
        var other = new AppUser { Id = 994732, Username = "wp2", Email = "wp2@t.com", PasswordHash = "h" };
        _db.AppUsers.AddRange(user, other);
        _db.Books.Add(new Book { Id = 994733, FileName = "wp.pgn", DisplayName = "WP", CreatedAt = Noon, UpdatedAt = Noon, Source = new BookSource() });
        foreach (var id in new[] { 994741, 994742, 994743 })
            _db.BookPuzzles.Add(new BookPuzzle { Id = id, LineId = $"l{id}", BookFileName = "wp.pgn", Fen = "x", Moves = "x", BookId = 994733 });
        var plan = new WoodpeckerPlan { Id = 994734, UserId = user.Id, Name = "Plan", CreatedAt = Noon.AddDays(-1) };
        var otherPlan = new WoodpeckerPlan { Id = 994735, UserId = other.Id, Name = "Fremd", CreatedAt = Noon.AddDays(-1) };
        _db.WoodpeckerPlans.AddRange(plan, otherPlan);

        // Kurs-Versuch, von Zyklus 1 übernommen (gleiche Aufgabe, gleicher Zeitpunkt).
        _db.CourseAttempts.Add(new CourseAttempt { UserId = user.Id, BookId = 994733, BookPuzzleId = 994741, Solved = true, TimeSeconds = 300, AttemptedAt = Noon });
        _db.WoodpeckerAttempts.Add(new WoodpeckerAttempt { PlanId = plan.Id, Cycle = 1, BookId = 994733, BookPuzzleId = 994741, Solved = true, TimeSeconds = 300, AttemptedAt = Noon });
        // Direkt im Woodpecker-Löser: Zyklus 1 (anderes Puzzle) und Zyklus 2 (das übernommene Puzzle erneut).
        _db.WoodpeckerAttempts.Add(new WoodpeckerAttempt { PlanId = plan.Id, Cycle = 1, BookId = 994733, BookPuzzleId = 994742, Solved = true, TimeSeconds = 100, AttemptedAt = Noon.AddMinutes(5) });
        _db.WoodpeckerAttempts.Add(new WoodpeckerAttempt { PlanId = plan.Id, Cycle = 2, BookId = 994733, BookPuzzleId = 994741, Solved = true, TimeSeconds = 40, AttemptedAt = Noon.AddMinutes(10) });
        // Fremdes Konto zählt nicht.
        _db.WoodpeckerAttempts.Add(new WoodpeckerAttempt { PlanId = otherPlan.Id, Cycle = 1, BookId = 994733, BookPuzzleId = 994743, Solved = true, TimeSeconds = 777, AttemptedAt = Noon });
        await _db.SaveChangesAsync();

        var series = await new TrainingGoalService(_db).GetDailySeriesAsync(user.Id);

        var day = Assert.Single(series.Days);
        Assert.Equal(300, day.BySource.CourseBookSeconds);
        Assert.Equal(140, day.BySource.WoodpeckerSeconds);
        Assert.Equal(440, day.TotalSeconds);
        Assert.Equal(440, day.ByTheme.TacticsSeconds);   // Buch ohne Themen → Taktik; Woodpecker fest Taktik
    }
}
