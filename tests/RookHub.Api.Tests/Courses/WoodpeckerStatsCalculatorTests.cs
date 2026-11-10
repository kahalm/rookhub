using RookHub.Api.Services.Courses;
using A = RookHub.Api.Services.Courses.WoodpeckerStatsCalculator.Attempt;
using C = RookHub.Api.Services.Courses.WoodpeckerStatsCalculator.Cycle;

namespace RookHub.Api.Tests.Courses;

/// <summary>Rechner der ausführlichen Woodpecker-Statistik — mit LITERALEN Zahlen (Nutzerbeispiel 10.11.: „1 h für 50
/// Puzzle, dieselben Puzzle haben letztes Mal 2 h gebraucht“).</summary>
public class WoodpeckerStatsCalculatorTests
{
    private static readonly DateTime Start1 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End1 = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start2 = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
    /// <summary>Nach dem Tipp-Stichtag (Tipp-Zahlen belastbar).</summary>
    private static readonly DateTime Tracked = WoodpeckerStatsCalculator.HintsTrackedSince.AddHours(1);

    private static List<C> TwoCycles() => [new C(1, Start1, End1), new C(2, Start2, null)];

    private static A At(int cycle, int id, bool solved, int seconds, int hints = 0, DateTime? at = null) =>
        new(cycle, id, 7, solved, seconds, hints, at ?? (cycle == 1 ? Start1.AddDays(1) : Start2.AddHours(2)));

    [Fact]
    public void UserExample_FiftySharedPuzzles_HalfTime_FactorTwo()
    {
        // Zyklus 1: 100 Aufgaben à 144 s (= 14 400 s); Zyklus 2: die ersten 50 à 72 s (= 3 600 s, damals 7 200 s).
        var attempts = new List<A>();
        for (var id = 1; id <= 100; id++) attempts.Add(At(1, id, true, 144));
        for (var id = 1; id <= 50; id++) attempts.Add(At(2, id, true, 72));
        var set = Enumerable.Range(1, 100).ToList();

        var dto = WoodpeckerStatsCalculator.Compute(1, TwoCycles(), attempts, set, Now);

        var same = dto.Cycles[0].SameAsNow!;
        Assert.Equal(50, same.Count);
        Assert.Equal(3600, same.SecondsNow);
        Assert.Equal(7200, same.SecondsThen);
        Assert.Equal(0.5, same.TimeRatio!.Value, 6);
        Assert.Equal(2.0, same.SpeedFactor!.Value, 6);
        Assert.Null(dto.Cycles[1].SameAsNow);   // der laufende vergleicht sich nicht mit sich selbst

        // Hochrechnung: r = 0,5; offen 50 Aufgaben mit damals 7 200 s → Rest 3 600 s; gesamt 7 200 = 0,5 × 14 400.
        var f = dto.Forecast!;
        Assert.Equal(1, f.BasisCycle);
        Assert.Equal(50, f.Sample);
        Assert.Equal(0.5, f.TimeRatio, 6);
        Assert.Equal(14400, f.BasisTotalSeconds);
        Assert.Equal(3600, f.SpentSeconds);
        Assert.Equal(50, f.OpenCount);
        Assert.Equal(3600, f.RemainingSeconds);
        Assert.Equal(7200, f.ProjectedTotalSeconds);
        Assert.True(f.GoalReached);   // r ≤ 0,5 (Buchziel halbe Zeit)
    }

    [Fact]
    public void Overlap_PuzzlesOnlyInOneCycle_DoNotCount()
    {
        var attempts = new List<A>
        {
            At(1, 1, true, 100), At(1, 2, true, 200), At(1, 3, false, 300),   // 3 nur in Zyklus 1
            At(2, 1, true, 40), At(2, 2, false, 60), At(2, 9, true, 999),     // 9 nur in Zyklus 2
        };
        var dto = WoodpeckerStatsCalculator.Compute(1, TwoCycles(), attempts, [1, 2, 3, 9], Now);

        var same = dto.Cycles[0].SameAsNow!;
        Assert.Equal(2, same.Count);
        Assert.Equal(100, same.SecondsNow);
        Assert.Equal(300, same.SecondsThen);
        Assert.Equal(1, same.SolvedNow);
        Assert.Equal(2, same.SolvedThen);
        Assert.Equal(50.0, same.RatePctNow!.Value, 6);
        Assert.Equal(100.0, same.RatePctThen!.Value, 6);
        // Zyklus-Summen bleiben die ganzen Zyklen.
        Assert.Equal(3, dto.Cycles[0].Attempted);
        Assert.Equal(600, dto.Cycles[0].TimeSeconds);
        Assert.Equal(1099, dto.Cycles[1].TimeSeconds);
        Assert.Null(dto.Forecast);   // 2 gemeinsame < MinForecastSample
    }

    [Fact]
    public void Changes_CountsPerPuzzleAgainstPreviousCycle()
    {
        var attempts = new List<A>
        {
            At(1, 1, false, 100), At(2, 1, true, 30),    // verbessert, deutlich schneller (70 s)
            At(1, 2, true, 50), At(2, 2, false, 120),    // verschlechtert, deutlich langsamer (70 s)
            At(1, 3, true, 40), At(2, 3, true, 40),      // beide gelöst, gleich
            At(1, 4, false, 90), At(2, 4, false, 80),    // beide verfehlt, schneller (10 s)
            At(1, 5, true, 60),                          // offen in Zyklus 2
        };
        var c = WoodpeckerStatsCalculator.Compute(1, TwoCycles(), attempts, [1, 2, 3, 4, 5], Now).Changes!;
        Assert.Equal(1, c.Cycle);
        Assert.Equal(1, c.Improved);
        Assert.Equal(1, c.Worsened);
        Assert.Equal(1, c.BothSolved);
        Assert.Equal(1, c.BothMissed);
        Assert.Equal(2, c.Faster);
        Assert.Equal(1, c.Slower);
        Assert.Equal(1, c.SameTime);
        Assert.Equal(1, c.MuchFaster);
        Assert.Equal(1, c.MuchSlower);
        Assert.Equal(80, c.SavedSeconds);
        Assert.Equal(70, c.LostSeconds);
        // Zeitstufen auf denselben vier Aufgaben: jetzt 30/120/40/80 → 0 schnell, 4 mittel; damals 100/50/40/90 → 4 mittel.
        Assert.Equal(0, c.BucketsNow.Fast);
        Assert.Equal(4, c.BucketsNow.Medium);
        Assert.Equal(4, c.BucketsThen.Medium);
    }

    [Fact]
    public void Forecast_OpenPuzzlesUseTheirOwnPreviousTime_NotTheAverage()
    {
        // 10 gemeinsame Aufgaben: damals je 100 s, jetzt je 50 s → r = 0,5. Offen: eine schwere (damals 1 000 s).
        var attempts = new List<A>();
        for (var id = 1; id <= 10; id++) { attempts.Add(At(1, id, true, 100)); attempts.Add(At(2, id, true, 50)); }
        attempts.Add(At(1, 11, true, 1000));
        var f = WoodpeckerStatsCalculator.Compute(1, TwoCycles(), attempts, Enumerable.Range(1, 11).ToList(), Now).Forecast!;
        Assert.Equal(500, f.RemainingSeconds);           // 0,5 × 1 000 — „offen × Ø“ hätte 50 s gesagt
        Assert.Equal(1000, f.ProjectedTotalSeconds);     // 500 bisher + 500 = 0,5 × 2 000
        Assert.Equal(2000, f.BasisTotalSeconds);
        Assert.True(f.GoalReached);
    }

    [Fact]
    public void Forecast_SlowerThanHalf_GoalNotReached()
    {
        var attempts = new List<A>();
        for (var id = 1; id <= 10; id++) { attempts.Add(At(1, id, true, 100)); attempts.Add(At(2, id, true, 60)); }
        var f = WoodpeckerStatsCalculator.Compute(1, TwoCycles(), attempts, Enumerable.Range(1, 10).ToList(), Now).Forecast!;
        Assert.Equal(0.6, f.TimeRatio, 6);
        Assert.False(f.GoalReached);
        Assert.Equal(0, f.OpenCount);
        Assert.Equal(0, f.RemainingSeconds);
    }

    [Fact]
    public void FirstCycle_NoPreviousCycle_OnlyOwnRowsAndDays()
    {
        var attempts = new List<A>
        {
            new(1, 1, 7, true, 20, 0, Start1.AddHours(1)),
            new(1, 2, 7, false, 200, 0, Start1.AddHours(2)),
            new(1, 3, 7, true, 60, 0, Start1.AddDays(1).AddHours(1)),
        };
        var dto = WoodpeckerStatsCalculator.Compute(1, [new C(1, Start1, null)], attempts, [1, 2, 3], Start1.AddDays(2));
        Assert.Equal(1, dto.CurrentCycle);
        Assert.Single(dto.Cycles);
        Assert.Null(dto.Cycles[0].SameAsNow);
        Assert.Null(dto.Changes);
        Assert.Null(dto.Forecast);
        Assert.Null(dto.HintChanges);
        Assert.Equal(2.0, dto.Cycles[0].DaysUsed, 6);
        Assert.Equal(["2026-10-01", "2026-10-02"], dto.Days.Select(d => d.Date));
        Assert.Equal(2, dto.Days[0].Attempted);
        Assert.Equal(1, dto.Days[0].Solved);
        Assert.Equal(220, dto.Days[0].TimeSeconds);
        Assert.Equal(1, dto.TimeBuckets.Fast);
        Assert.Equal(1, dto.TimeBuckets.Medium);
        Assert.Equal(1, dto.TimeBuckets.Slow);
        Assert.Equal(200, dto.Slowest[0].SecondsNow);
        Assert.Equal(0, dto.AlwaysMissedCount);   // nur ein Zyklus — „immer verfehlt“ braucht zwei
    }

    [Fact]
    public void Days_UseViewerTimeZone()
    {
        // 23:30 UTC ist in Wien (UTC+1) schon der nächste Tag.
        var at = new DateTime(2026, 11, 1, 23, 30, 0, DateTimeKind.Utc);
        var dto = WoodpeckerStatsCalculator.Compute(1, [new C(1, Start1, null)], [new A(1, 1, 7, true, 5, 0, at)], [1], Now, 60);
        Assert.Equal("2026-11-02", dto.Days.Single().Date);
    }

    [Fact]
    public void NoAttempts_EmptyButValid()
    {
        var dto = WoodpeckerStatsCalculator.Compute(1, TwoCycles(), [], [], Now);
        Assert.Equal(2, dto.CurrentCycle);
        Assert.Equal(2, dto.Cycles.Count);
        Assert.Equal(0, dto.Cycles[1].Attempted);
        Assert.Null(dto.Cycles[1].RatePct);
        Assert.Null(dto.Cycles[1].AvgSeconds);
        Assert.Equal(0, dto.Cycles[0].SameAsNow!.Count);
        Assert.Null(dto.Cycles[0].SameAsNow!.TimeRatio);
        Assert.Null(dto.Changes);
        Assert.Null(dto.Forecast);
        Assert.Empty(dto.Days);
        Assert.Empty(dto.Slowest);
        Assert.Equal(0, dto.Hints.Tracked);
    }

    [Fact]
    public void NoCycles_ReturnsEmpty()
    {
        var dto = WoodpeckerStatsCalculator.Compute(5, [], [], [], Now);
        Assert.Equal(5, dto.PlanId);
        Assert.Empty(dto.Cycles);
    }

    [Fact]
    public void Hints_OnlyAttemptsSinceCutoffCount()
    {
        var before = WoodpeckerStatsCalculator.HintsTrackedSince.AddMinutes(-1);
        var cycles = new List<C> { new(1, Start1, End1), new(2, before.AddHours(-1), null) };
        var attempts = new List<A>
        {
            At(2, 1, true, 10, 0, Tracked), At(2, 2, true, 10, 1, Tracked), At(2, 3, true, 10, 2, Tracked),
            At(2, 4, true, 10, 3, Tracked), At(2, 5, false, 10, 0, Tracked), At(2, 6, false, 10, 2, Tracked),
            At(2, 7, true, 10, 0, before),   // vor dem Stichtag: Tipp-Zahl unbekannt
        };
        var dto = WoodpeckerStatsCalculator.Compute(1, cycles, attempts, Enumerable.Range(1, 7).ToList(), Tracked);
        var h = dto.Hints;
        Assert.Equal(6, h.Tracked);
        Assert.Equal(1, h.Untracked);
        Assert.Equal(1, h.SolvedNoHint);
        Assert.Equal(1, h.SolvedHint1);
        Assert.Equal(1, h.SolvedHint2);
        Assert.Equal(1, h.SolvedHint3);
        Assert.Equal(1, h.MissedNoHint);
        Assert.Equal(1, h.MissedWithHint);
        Assert.Equal(6, dto.Cycles[1].HintsTrackedCount);
        Assert.Equal(WoodpeckerStatsCalculator.HintsTrackedSince, dto.HintsTrackedSince);
    }

    [Fact]
    public void HintChanges_OnlyWhereBothAttemptsAreTracked()
    {
        var cycles = new List<C> { new(1, Tracked.AddDays(-1), Tracked), new(2, Tracked, null) };
        var t1 = Tracked.AddMinutes(-30);   // auch Zyklus 1 nach dem Stichtag
        var attempts = new List<A>
        {
            At(1, 1, true, 10, 0, t1), At(2, 1, true, 10, 2, Tracked.AddHours(1)),   // mehr
            At(1, 2, true, 10, 3, t1), At(2, 2, true, 10, 1, Tracked.AddHours(1)),   // weniger
            At(1, 3, true, 10, 1, t1), At(2, 3, true, 10, 1, Tracked.AddHours(1)),   // gleich
            At(1, 4, true, 10, 0, WoodpeckerStatsCalculator.HintsTrackedSince.AddDays(-5)),   // damals unbekannt → raus
            At(2, 4, true, 10, 3, Tracked.AddHours(1)),
        };
        var c = WoodpeckerStatsCalculator.Compute(1, cycles, attempts, [1, 2, 3, 4], Tracked.AddHours(2)).HintChanges!;
        Assert.Equal(3, c.Count);
        Assert.Equal(1, c.More);
        Assert.Equal(1, c.Fewer);
        Assert.Equal(1, c.Same);
        Assert.Equal(4, c.SumNow);
        Assert.Equal(4, c.SumThen);
    }

    [Fact]
    public void HintChanges_PreviousCycleBeforeCutoff_Null()
    {
        var attempts = new List<A> { At(1, 1, true, 10, 0), At(2, 1, true, 10, 2, Tracked) };
        Assert.Null(WoodpeckerStatsCalculator.Compute(1, TwoCycles(), attempts, [1], Tracked).HintChanges);
    }

    [Fact]
    public void AlwaysMissed_NeedsTwoCyclesAllMissed()
    {
        var attempts = new List<A>
        {
            At(1, 1, false, 100), At(2, 1, false, 90),   // immer verfehlt
            At(1, 2, false, 100), At(2, 2, true, 50),    // jetzt gelöst
            At(1, 3, false, 100),                        // nur einmal bearbeitet
        };
        var dto = WoodpeckerStatsCalculator.Compute(1, TwoCycles(), attempts, [1, 2, 3], Now);
        Assert.Equal(1, dto.AlwaysMissedCount);
        var m = dto.AlwaysMissed.Single();
        Assert.Equal(1, m.BookPuzzleId);
        Assert.Equal(2, m.Cycles);
        Assert.Equal(90, m.SecondsNow);
        Assert.Equal(100, m.SecondsPrev);
        // Langsamste des laufenden Zyklus mit der Zeit des Vorzyklus daneben.
        Assert.Equal([1, 2], dto.Slowest.Select(s => s.BookPuzzleId));
        Assert.Equal(100, dto.Slowest[0].SecondsPrev);
    }

    [Fact]
    public void ThirdCycle_ComparesWithEveryEarlierCycle()
    {
        var cycles = new List<C> { new(1, Start1, End1), new(2, Start2, Start2.AddDays(5)), new(3, Now, null) };
        var attempts = new List<A>
        {
            At(1, 1, true, 400), At(1, 2, true, 400),
            new(2, 1, 7, true, 200, 0, Start2.AddHours(1)), new(2, 2, 7, true, 200, 0, Start2.AddHours(1)),
            new(3, 1, 7, true, 100, 0, Now.AddHours(1)),
        };
        var dto = WoodpeckerStatsCalculator.Compute(1, cycles, attempts, [1, 2], Now.AddHours(2));
        Assert.Equal(400, dto.Cycles[0].SameAsNow!.SecondsThen);
        Assert.Equal(0.25, dto.Cycles[0].SameAsNow!.TimeRatio!.Value, 6);
        Assert.Equal(200, dto.Cycles[1].SameAsNow!.SecondsThen);
        Assert.Equal(0.5, dto.Cycles[1].SameAsNow!.TimeRatio!.Value, 6);
        Assert.Equal(5.0, dto.Cycles[1].DaysUsed, 6);
        Assert.Equal(2, dto.Changes!.Cycle);
    }
}
