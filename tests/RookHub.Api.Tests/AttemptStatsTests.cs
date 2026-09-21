using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Arithmetik der Versuchs-Statistiken, einzeln geprüft (ohne Datenbank). Sie lag bis 0.499.5
/// zweimal ausgeschrieben da — in <c>PuzzleStatsService</c> und in <c>CourseStatsService</c>. Getestet
/// war davon nur das Ergebnis der beiden Dienste; die Randfälle (leere Liste, Rundung, Gleichstand)
/// waren an keiner Stelle festgehalten, und genau dort laufen zwei Kopien auseinander.
/// </summary>
public class AttemptStatsTests
{
    // ── Serien ────────────────────────────────────────────────────────────

    [Fact]
    public void Streaks_Empty_IsZeroZero()
        => Assert.Equal((0, 0), AttemptStats.Streaks(Array.Empty<bool>()));

    [Fact]
    public void Streaks_AllSolved_CurrentEqualsBest()
        => Assert.Equal((4, 4), AttemptStats.Streaks(new[] { true, true, true, true }));

    /// <summary>Die Liste steht NEUESTER ZUERST: der erste Fehlversuch beendet die laufende Serie.</summary>
    [Fact]
    public void Streaks_StopsCurrentAtFirstFailure()
        => Assert.Equal((2, 2), AttemptStats.Streaks(new[] { true, true, false, true, true }));

    /// <summary>Der beste Lauf darf mitten in der Liste liegen — auch wenn die laufende Serie 0 ist.</summary>
    [Fact]
    public void Streaks_BestRunInTheMiddle_CountsEvenWithoutCurrent()
        => Assert.Equal((0, 3), AttemptStats.Streaks(new[] { false, true, true, true, false, true }));

    // ── Trefferquote ──────────────────────────────────────────────────────

    [Fact]
    public void Accuracy_NoAttempts_IsZero_NotDivisionByZero()
        => Assert.Equal(0, AttemptStats.Accuracy(0, 0));

    [Theory]
    [InlineData(1, 3, 33.3)]      // 33,333… → eine Nachkommastelle, abgerundet
    [InlineData(2, 3, 66.7)]      // 66,666… → aufgerundet
    [InlineData(7, 8, 87.5)]
    [InlineData(5, 5, 100)]
    [InlineData(0, 4, 0)]
    public void Accuracy_RoundsToOneDecimal(int solved, int total, double expected)
        => Assert.Equal(expected, AttemptStats.Accuracy(solved, total));

    // ── Rating-Bänder ─────────────────────────────────────────────────────

    /// <summary>Der Bucket kommt als Ganzzahl-Division aus dem SQL; hier entstehen die 200er-Grenzen
    /// UND die Sortierung (die Gruppierung liefert keine verlässliche Reihenfolge).</summary>
    [Fact]
    public void RatingBands_SortsAndSpansTwoHundred()
    {
        var bands = AttemptStats.RatingBands(new[] { (9, 5, 3), (6, 10, 8), (7, 1, 0) });

        Assert.Equal(new[] { 1200, 1400, 1800 }, bands.Select(b => b.From));
        Assert.Equal(new[] { 1399, 1599, 1999 }, bands.Select(b => b.To));
        Assert.Equal(new[] { 10, 1, 5 }, bands.Select(b => b.Attempts));
        Assert.Equal(new[] { 8, 0, 3 }, bands.Select(b => b.Solved));
    }

    [Fact]
    public void RatingBands_Empty_IsEmpty()
        => Assert.Empty(AttemptStats.RatingBands(Array.Empty<(int, int, int)>()));

    // ── Aktivität ─────────────────────────────────────────────────────────

    [Fact]
    public void Activity_SortsChronologicallyAndFormatsIsoDate()
    {
        var days = AttemptStats.Activity(new[]
        {
            (new DateTime(2026, 9, 21), 3),
            (new DateTime(2026, 1, 2), 7),
            (new DateTime(2026, 12, 31), 1),
        });

        Assert.Equal(new[] { "2026-01-02", "2026-09-21", "2026-12-31" }, days.Select(d => d.Date));
        Assert.Equal(new[] { 7, 3, 1 }, days.Select(d => d.Count));
    }

    /// <summary>365 Tage heißt „heute plus die 364 davor" — nicht 365 davor (sonst ist die Heatmap
    /// eine Spalte zu breit).</summary>
    [Fact]
    public void ActivityWindowStart_Is364DaysBeforeTodayAtMidnight()
    {
        var start = AttemptStats.ActivityWindowStart(new DateTime(2026, 9, 21, 13, 47, 12, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2025, 9, 22), start);
        Assert.Equal(TimeSpan.Zero, start.TimeOfDay);
    }

    // ── Themen ────────────────────────────────────────────────────────────

    /// <summary>Häufigste zuerst, bei Gleichstand nach Namen — und höchstens <c>take</c> Stück.</summary>
    [Fact]
    public void TopThemes_CapsAtTwentyAndBreaksTiesByName()
    {
        var themes = Enumerable.Range(0, 25)
            .Select(i => new ThemeStatDto { Theme = $"theme{i:00}", Attempts = 5, Solved = i })
            .Append(new ThemeStatDto { Theme = "zzz-selten", Attempts = 99, Solved = 1 })
            .ToList();

        var top = AttemptStats.TopThemes(themes);

        Assert.Equal(20, top.Count);
        Assert.Equal("zzz-selten", top[0].Theme);                       // Attempts schlägt Name
        Assert.Equal("theme00", top[1].Theme);                          // Gleichstand → alphabetisch
        Assert.Equal("theme18", top[^1].Theme);                         // theme19…24 fallen raus
    }

    [Fact]
    public void TopThemes_RespectsExplicitTake()
        => Assert.Equal(2, AttemptStats.TopThemes(new[]
        {
            new ThemeStatDto { Theme = "a", Attempts = 1 },
            new ThemeStatDto { Theme = "b", Attempts = 2 },
            new ThemeStatDto { Theme = "c", Attempts = 3 },
        }, 2).Count);
}
