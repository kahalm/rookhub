using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Tests.Courses;

/// <summary>Reine Teilschritte der Kurs-Uebersetzung (Zerlegung Z16): offene Stellen bestimmen, Kapitelname aus
/// dem Woerterbuch, Besitz in Phase 1 — ohne Datenbank und ohne Modell.</summary>
public class CourseTranslationStepsTests
{
    private static CourseLineSlot Slot(int slot, string text) => new(slot, text, CourseTextHash.Of(text));

    [Fact]
    public void OpenSlots_MissingOrStaleAreOpen_VanishedAreOrphans()
    {
        var slots = new Dictionary<int, CourseLineSlot>
        {
            [0] = Slot(0, "fresh"), [1] = Slot(1, "changed"), [2] = Slot(2, "new"),
        };
        var existing = new Dictionary<int, CommentText>
        {
            [0] = new() { Ply = 0, Text = "frisch", SourceHash = CourseTextHash.Of("fresh") },
            [1] = new() { Ply = 1, Text = "alt", SourceHash = CourseTextHash.Of("old") },
            [5] = new() { Ply = 5, Text = "weg", SourceHash = "x" },
        };

        var (open, orphans) = CourseTranslationService.OpenSlots(slots, existing);

        Assert.Equal(new[] { 1, 2 }, open.Select(s => s.Slot).OrderBy(x => x));
        Assert.Equal(new[] { 5 }, orphans.Select(o => o.Ply));
    }

    [Fact]
    public void TakeChapter_WithoutDictionary_LeavesItOpen()
    {
        var open = new List<CourseLineSlot> { Slot(CourseTextSlots.Chapter, "Kapitel"), Slot(0, "a") };
        var assigned = new Dictionary<int, string>();
        Assert.Equal((0, 0), CourseTranslationService.TakeChapterFromDictionary(open, null, assigned));
        Assert.Equal(2, open.Count);
        Assert.Empty(assigned);
    }

    [Fact]
    public void TakeChapter_HitIsReused_MissIsPending_BothLeaveTheOpenList()
    {
        var chapter = Slot(CourseTextSlots.Chapter, "Kapitel");
        var hit = new Dictionary<string, string> { [chapter.Hash] = "Chapter" };

        var open = new List<CourseLineSlot> { chapter, Slot(0, "a") };
        var assigned = new Dictionary<int, string>();
        Assert.Equal((1, 0), CourseTranslationService.TakeChapterFromDictionary(open, hit, assigned));
        Assert.Equal("Chapter", assigned[CourseTextSlots.Chapter]);
        Assert.Single(open);

        var open2 = new List<CourseLineSlot> { chapter };
        var assigned2 = new Dictionary<int, string>();
        Assert.Equal((0, 1), CourseTranslationService.TakeChapterFromDictionary(open2, new Dictionary<string, string>(), assigned2));
        Assert.Empty(open2);
        Assert.Empty(assigned2);
    }

    [Fact]
    public void SplitOwned_KeepsOwned_CountsTheRestAsDeferred()
    {
        var a = Slot(0, "a"); var b = Slot(1, "b"); var a2 = Slot(2, "a");
        var (open, deferred) = CourseTranslationService.SplitOwned(new List<CourseLineSlot> { a, b, a2 },
            new HashSet<string> { a.Hash });
        Assert.Equal(new[] { 0, 2 }, open.Select(s => s.Slot));
        Assert.Equal(1, deferred);
    }

    [Fact]
    public void PlanPhase1_EachHashBelongsToTheFirstLine_LinesWithoutOwnHashesDrop()
    {
        var plan = CourseTranslationService.PlanPhase1(new List<OpenLine>
        {
            new(10, new[] { "h1", "h2" }),
            new(11, new[] { "h2" }),
            new(12, new[] { "h2", "h3" }),
        });

        Assert.Equal(new[] { 10, 12 }, plan.Select(p => p.Id));
        Assert.Equal(new[] { "h1", "h2" }, plan[0].Owned.OrderBy(x => x));
        Assert.Equal(new[] { "h3" }, plan[1].Owned);
    }
}
