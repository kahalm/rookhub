using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Tests.Courses;

/// <summary>
/// Charakterisierung der Kurs-Detailseite (<see cref="CourseAuthoringService.GetDetailAsync"/>) vor der
/// Zerlegung (Codereview 2026-09-29, Z16): normaler Kurs mit Kapiteln, Info-Linien, Fortschritt, Pin und
/// Verknüpfung sowie ein geteiltes Kalkulationsbuch mit gesperrter Woche, Bäumen und Bewertungen —
/// JEDES Feld als Text festgehalten. Ids durch Buchnamen ersetzt, Zeitstempel fest.
/// </summary>
public class CourseDetailCharacterizationTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db;
    private readonly CourseAuthoringService _authoring;

    public CourseDetailCharacterizationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _authoring = new CourseAuthoringService(_db);
    }

    public void Dispose() => _db.Dispose();

    private AppUser User(string name)
    {
        var u = new AppUser { Username = name, PasswordHash = "x", CreatedAt = T0 };
        _db.AppUsers.Add(u);
        return u;
    }

    private Book Book(string name, AppUser owner, bool calc = false)
    {
        var b = new Book
        {
            FileName = name + ".pgn", DisplayName = name, OwnerUserId = owner.Id, IsCalculation = calc,
            Difficulty = "schwer", Rating = 1800, MinElo = 1400, MaxElo = 2000, Tags = "personal", Themes = "endgame",
            Description = "Beschreibung " + name, CreatedAt = T0, UpdatedAt = T0.AddHours(1),
            Kind = BookKind.Study, ImportVersion = ImportPipeline.CurrentVersion, Source = new BookSource(),
        };
        _db.Books.Add(b);
        return b;
    }

    private BookPuzzle Line(Book book, string round, string? chapter, bool info = false)
    {
        var p = new BookPuzzle
        {
            Book = book, LineId = $"{book.FileName}:{round}", BookFileName = book.FileName, Round = round,
            Chapter = chapter, Fen = "8/8/8/4k3/8/8/4K3/8 w - - 0 1", Moves = info ? "" : "e2e3",
            StartPly = -1, IsInfoOnly = info,
        };
        _db.BookPuzzles.Add(p);
        return p;
    }

    private string Describe(CourseDetailDto d)
    {
        var names = _db.Books.ToDictionary(b => b.Id, b => b.DisplayName);
        var lines = _db.BookPuzzles.ToDictionary(p => p.Id, p => p.Round);
        string Val(object? v) => v switch
        {
            null => "null",
            DateTime t => t.ToString("O"),
            System.Collections.IEnumerable e and not string => "[" + string.Join(",", e.Cast<object>()) + "]",
            _ => v.ToString()!,
        };
        string Props(object o, Func<string, object?, object?> map) => string.Join(" ", o.GetType().GetProperties()
            .Where(p => p.Name != nameof(CourseDetailDto.Chapters))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={Val(map(p.Name, p.GetValue(o)))}"));
        var head = Props(d, (n, v) => n is nameof(CourseDetailDto.BookId) or nameof(CourseDetailDto.LinkedBookId) && v is int id
            ? names[id] : v);
        var chapters = d.Chapters.Select(c => "  " + Props(c, (n, v) =>
            n == nameof(CourseManageChapterDto.FirstLineId) && v is int id ? "r" + lines[id] : v));
        return string.Join("\n", new[] { head }.Concat(chapters));
    }

    [Fact]
    public async Task GetDetail_NormalCourse_EveryField()
    {
        var me = User("ich");
        await _db.SaveChangesAsync();
        var book = Book("Normal", me);
        var workbook = Book("Workbook", me);
        var a1 = Line(book, "1", "Kapitel A");
        Line(book, "2", " Kapitel A ");
        Line(book, "3", "Kapitel A", info: true);
        var n1 = Line(book, "4", null);
        Line(book, "5", "", info: true);
        Line(book, "6", "Nur Info", info: true);
        Line(book, "7", "Kapitel B");
        await _db.SaveChangesAsync();
        _db.CoursePuzzleResults.AddRange(
            new CoursePuzzleResult { UserId = me.Id, BookId = book.Id, BookPuzzleId = a1.Id, SolvedAt = T0 },
            new CoursePuzzleResult { UserId = me.Id, BookId = book.Id, BookPuzzleId = n1.Id, SolvedAt = T0 });
        _db.CourseProgresses.Add(new CourseProgress
        {
            UserId = me.Id, BookId = book.Id, LastMode = "sequential", SkipInfoLines = true, CreatedAt = T0, UpdatedAt = T0.AddDays(2),
        });
        _db.CoursePins.Add(new CoursePin { UserId = me.Id, BookId = book.Id, PinnedAt = T0 });
        _db.CourseLinks.Add(new CourseLink { UserId = me.Id, BookId = book.Id, LinkedBookId = workbook.Id, CreatedAt = T0 });
        await _db.SaveChangesAsync();

        var detail = await _authoring.GetDetailAsync(me.Id, book.Id, isAdmin: false);

        Assert.Equal(ExpectedNormal, Describe(detail));
    }

    [Fact]
    public async Task GetDetail_SharedCalculationBook_WithHiddenWeek_EveryField()
    {
        var owner = User("anna");
        var me = User("ich");
        await _db.SaveChangesAsync();
        var book = Book("Kalk", owner, calc: true);
        var w1a = Line(book, "1", "Woche 1", info: true);
        var w1b = Line(book, "2", "Woche 1", info: true);
        Line(book, "3", "Woche 2", info: true);
        Line(book, "4", "Woche 3", info: true);
        await _db.SaveChangesAsync();
        _db.CourseShares.Add(new CourseShare { BookId = book.Id, OwnerId = owner.Id, RecipientId = me.Id, SharedAt = T0 });
        _db.CalcEditions.AddRange(
            new CalcEdition { BookId = book.Id, Chapter = "Woche 1", PublishAt = T0 },
            new CalcEdition { BookId = book.Id, Chapter = "Woche 3", PublishAt = DateTime.UtcNow.AddYears(5) });
        _db.CalculationTrees.AddRange(
            new CalculationTree { UserId = me.Id, BookId = book.Id, BookPuzzleId = w1a.Id, TreeJson = "{}", Grade = 3 },
            new CalculationTree { UserId = me.Id, BookId = book.Id, BookPuzzleId = w1b.Id, TreeJson = "", Grade = 1 });
        await _db.SaveChangesAsync();

        var detail = await _authoring.GetDetailAsync(me.Id, book.Id, isAdmin: false);

        Assert.Equal(ExpectedCalc, Describe(detail));
    }

    /// <summary>Kapitelnamen mit Leerzeichen außen: Detailseite und Solver (Kapitelliste, Kapitel-Modus,
    /// Kapitel-Statistik) müssen dasselbe Kapitel sehen — sonst zeigt der <c>SolverIndex</c> der Detailseite aufs
    /// falsche Kapitel („Kapitel starten").</summary>
    [Fact]
    public async Task ChapterWithSurroundingSpaces_DetailAndSolverSeeTheSameChapters()
    {
        var me = User("ich");
        await _db.SaveChangesAsync();
        var book = Book("Spaces", me);
        Line(book, "1", "Kapitel A");
        Line(book, "2", " Kapitel A ");
        Line(book, "3", "Kapitel B");
        await _db.SaveChangesAsync();
        var courses = TestServices.Course(_db);

        var detail = await _authoring.GetDetailAsync(me.Id, book.Id, isAdmin: false);
        var solver = await courses.GetChaptersAsync(me.Id, book.Id, isAdmin: false);
        var next = await courses.GetNextAsync(me.Id, book.Id, "sequential", null, null, isAdmin: false, chapterIndex: 0);

        var shown = string.Join(" | ", detail.Chapters.Select(c => $"{c.Name}:{c.QuizCount}@{c.SolverIndex}"));
        var solverShown = string.Join(" | ", solver.Select(c => $"[{c.Name}]:{c.PuzzleCount}@{c.Index}"));
        var nextShown = $"{next.ChapterName}:{next.Total}/{next.Book?.Total}/{next.Chapter?.Total}";
        // Bis zum Fix trimmte nur die Detailseite: der Solver hatte zwei Kapitel „Kapitel A" („Kapitel A:2@0 |
        // Kapitel B:1@2" gegen „[Kapitel A]:1@0 | [ Kapitel A ]:1@1 | [Kapitel B]:1@2", Kapitel-Modus 1 Linie).
        // Jetzt eine Normalisierung überall (ChapterOrder.NormalizeChapter, Entscheidung User 25.10.).
        Assert.Equal("Kapitel A:2@0 | Kapitel B:1@1", shown);
        Assert.Equal("[Kapitel A]:2@0 | [Kapitel B]:1@1", solverShown);
        Assert.Equal("Kapitel A:2/3/2", nextShown);
        var all = await courses.GetAllPuzzlesAsync(me.Id, book.Id, isAdmin: false);
        Assert.Equal(["Kapitel A", "Kapitel A", "Kapitel B"], all.Select(p => p.Chapter));
    }

    private const string ExpectedNormal = """
        BookId=Normal CalcMaxPoints=null CalcPoints=null CanManage=True CreatedAt=2026-10-01T12:00:00.0000000Z Description=Beschreibung Normal Difficulty=schwer DisplayName=Normal FileName=Normal.pgn InfoLineCount=3 IsCalculation=False IsOwned=True IsPinned=True IsPublic=False IsShared=False Kind=Study LastActivityAt=2026-10-03T12:00:00.0000000Z LastMode=sequential LinkedBookId=Workbook LinkedDisplayName=Workbook MaxElo=2000 MinElo=1400 ProgressPercent=50 PublicSlug=null PuzzleCount=4 Rating=1800 SharedByUsername=null SkipInfoLines=True SolvedCount=2 Tags=personal Themes=[endgame] TotalLines=7 UpdatedAt=2026-10-01T13:00:00.0000000Z
          FirstLineId=r1 Label=null LineCount=3 Name=Kapitel A ProgressPercent=50 QuizCount=2 SolvedCount=1 SolverIndex=0
          FirstLineId=r4 Label=null LineCount=2 Name=null ProgressPercent=100 QuizCount=1 SolvedCount=1 SolverIndex=1
          FirstLineId=r6 Label=null LineCount=1 Name=Nur Info ProgressPercent=0 QuizCount=0 SolvedCount=0 SolverIndex=null
          FirstLineId=r7 Label=null LineCount=1 Name=Kapitel B ProgressPercent=0 QuizCount=1 SolvedCount=0 SolverIndex=2
        """;
    private const string ExpectedCalc = """
        BookId=Kalk CalcMaxPoints=12 CalcPoints=4 CanManage=False CreatedAt=2026-10-01T12:00:00.0000000Z Description=Beschreibung Kalk Difficulty=schwer DisplayName=Kalk FileName=Kalk.pgn InfoLineCount=3 IsCalculation=True IsOwned=False IsPinned=False IsPublic=False IsShared=True Kind=Study LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null MaxElo=2000 MinElo=1400 ProgressPercent=33 PublicSlug=null PuzzleCount=3 Rating=1800 SharedByUsername=anna SkipInfoLines=False SolvedCount=1 Tags=personal Themes=[endgame] TotalLines=3 UpdatedAt=2026-10-01T13:00:00.0000000Z
          FirstLineId=r1 Label=null LineCount=2 Name=Woche 1 ProgressPercent=50 QuizCount=0 SolvedCount=1 SolverIndex=null
          FirstLineId=r3 Label=null LineCount=1 Name=Woche 2 ProgressPercent=0 QuizCount=0 SolvedCount=0 SolverIndex=null
        """;
}
