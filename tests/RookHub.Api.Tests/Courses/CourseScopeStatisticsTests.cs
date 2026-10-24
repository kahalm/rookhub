using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Tests.Courses;

/// <summary>Reine Zählregeln der Kurs-Statistik (<see cref="CourseScopeStatistics.BuildScope"/>) und die
/// gemeinsamen Helfer aus <see cref="CourseProgressRules"/> — ohne Datenbank (Zerlegung Z4).</summary>
public class CourseScopeStatisticsTests
{
    private static PuzzleAttemptSummary A(int id, int seconds, bool firstSolved, int firstHints = 0) =>
        new() { PuzzleId = id, Seconds = seconds, FirstSolved = firstSolved, FirstHints = firstHints };

    [Fact]
    public void BuildScope_SplitsCleanHelpedGaveUp_AndSumsTime()
    {
        // 1: im ersten Versuch ohne Tipp gelöst (sauber); 2: erst mit Tipp gelöst (Hilfe);
        // 3: gelöst ohne Versuch seit Reset (Altbestand → sauber); 4: versucht, nicht gelöst (aufgegeben).
        var solved = new HashSet<int> { 1, 2, 3 };
        var attempts = new List<PuzzleAttemptSummary> { A(1, 10, true), A(2, 20, true, firstHints: 1), A(4, 5, false) };

        var s = CourseScopeStatistics.BuildScope(solved, attempts, _ => true, total: 5);

        Assert.Equal(3, s.SolvedCount);
        Assert.Equal(5, s.Total);
        Assert.Equal(60, s.ProgressPercent);
        Assert.Equal(2, s.CleanCount);
        Assert.Equal(1, s.HelpedCount);
        Assert.Equal(1, s.GaveUpCount);
        Assert.Equal(35, s.TotalSeconds);
        Assert.Equal(3, s.AttemptedCount);
        Assert.Equal(2, s.FirstTryCorrect);
        Assert.Equal(67, s.AccuracyPercent);
    }

    [Fact]
    public void BuildScope_RespectsTheScopeFilter()
    {
        var solved = new HashSet<int> { 1, 2 };
        var attempts = new List<PuzzleAttemptSummary> { A(1, 10, false), A(2, 20, true) };

        var s = CourseScopeStatistics.BuildScope(solved, attempts, id => id == 1, total: 1);

        Assert.Equal(1, s.SolvedCount);
        Assert.Equal(0, s.CleanCount);
        Assert.Equal(1, s.HelpedCount);
        Assert.Equal(10, s.TotalSeconds);
        Assert.Equal(1, s.AttemptedCount);
        Assert.Equal(0, s.AccuracyPercent);
    }

    [Fact]
    public void BuildScope_CapsCountsAtTotal()
    {
        // Mehr Gelöstes als Linien (z. B. Ergebnisse an gelöschten Linien) wird gedeckelt.
        var s = CourseScopeStatistics.BuildScope(new HashSet<int> { 1, 2, 3 }, new List<PuzzleAttemptSummary>(),
            _ => true, total: 2);
        Assert.Equal(2, s.SolvedCount);
        Assert.Equal(2, s.CleanCount);
        Assert.Equal(100, s.ProgressPercent);
        Assert.Equal(0, s.AccuracyPercent);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 67)]
    [InlineData(5, 3, 100)]
    [InlineData(1, -1, 0)]
    public void Percent_RoundsAndCaps(int solved, int total, int expected) =>
        Assert.Equal(expected, CourseProgressRules.Percent(solved, total));

    [Theory]
    [InlineData("random", "random")]
    [InlineData(" RANDOM ", "random")]
    [InlineData("sequential", "sequential")]
    [InlineData("easy", "sequential")]
    [InlineData(null, "sequential")]
    public void NormalizeOrderMode_OnlyRandomOrSequential(string? raw, string expected) =>
        Assert.Equal(expected, CourseProgressRules.NormalizeOrderMode(raw));

    [Fact]
    public void NewOwnedCourseItem_IsOwnedWithoutProgress()
    {
        var book = new Book { Id = 7, FileName = "f.pgn", DisplayName = "Mein Kurs", Tags = "personal", Rating = 1200 };
        var item = CourseCreationService.NewOwnedCourseItem(book, 4);
        Assert.Equal(7, item.BookId);
        Assert.Equal("Mein Kurs", item.DisplayName);
        Assert.Equal(4, item.PuzzleCount);
        Assert.Equal(0, item.SolvedCount);
        Assert.Equal(0, item.ProgressPercent);
        Assert.Null(item.LastMode);
        Assert.True(item.IsOwned);
        Assert.False(item.IsPinned);
    }
}
