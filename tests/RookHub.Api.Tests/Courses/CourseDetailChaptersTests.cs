using RookHub.Api.Services.Courses;

namespace RookHub.Api.Tests.Courses;

/// <summary>Reine Kapitel-Zählung der Detailseite (<see cref="CourseAuthoringService.BuildChapters"/>) — ohne
/// Datenbank (Zerlegung Z16).</summary>
public class CourseDetailChaptersTests
{
    private static CourseAuthoringService.DetailLine L(int id, string? chapter, bool info = false) =>
        new() { Id = id, Chapter = chapter, IsInfoOnly = info };

    [Fact]
    public void NormalCourse_CountsQuizOnly_KeepsReadingOrder_AndMapsSolverIndex()
    {
        var lines = new[]
        {
            L(1, "A"), L(2, " A "), L(3, "A", info: true), L(4, null), L(5, "", info: true), L(6, "Info", info: true), L(7, "B"),
        };
        var chapters = CourseAuthoringService.BuildChapters(lines, new HashSet<int> { 1, 3, 4 }, isCalc: false,
            solverNames: new List<string?> { "A", null, "B" });

        Assert.Equal(new string?[] { "A", null, "Info", "B" }, chapters.Select(c => c.Name));
        var a = chapters[0];
        Assert.Equal((3, 2, 1, 50, 0, 1), (a.LineCount, a.QuizCount, a.SolvedCount, a.ProgressPercent, a.SolverIndex, a.FirstLineId));
        var none = chapters[1];
        Assert.Equal((2, 1, 1, 100, 1, 4), (none.LineCount, none.QuizCount, none.SolvedCount, none.ProgressPercent, none.SolverIndex, none.FirstLineId));
        var info = chapters[2];
        Assert.Equal((1, 0, 0, 0, 6), (info.LineCount, info.QuizCount, info.SolvedCount, info.ProgressPercent, info.FirstLineId));
        Assert.Null(info.SolverIndex);
        Assert.Equal(2, chapters[3].SolverIndex);
    }

    [Fact]
    public void CalculationBook_CountsEveryLine()
    {
        var lines = new[] { L(1, "W1", info: true), L(2, "W1", info: true), L(3, "W2", info: true) };
        var chapters = CourseAuthoringService.BuildChapters(lines, new HashSet<int> { 2 }, isCalc: true,
            solverNames: new List<string?>());

        Assert.Equal((2, 0, 1, 50), (chapters[0].LineCount, chapters[0].QuizCount, chapters[0].SolvedCount, chapters[0].ProgressPercent));
        Assert.Equal((1, 0, 0), (chapters[1].LineCount, chapters[1].SolvedCount, chapters[1].ProgressPercent));
        Assert.All(chapters, c => Assert.Null(c.SolverIndex));
    }

    [Fact]
    public void NoLines_NoChapters() =>
        Assert.Empty(CourseAuthoringService.BuildChapters(Array.Empty<CourseAuthoringService.DetailLine>(),
            new HashSet<int>(), isCalc: false, solverNames: new List<string?>()));
}
