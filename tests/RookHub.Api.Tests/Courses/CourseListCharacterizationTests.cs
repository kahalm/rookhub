using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Tests.Courses;

/// <summary>
/// Charakterisierung der Kursliste (<see cref="CourseService.GetCoursesAsync"/>) vor der Zerlegung
/// (Codereview 2026-09-29, Z4): ein gemischter Bestand — normaler Kurs mit Fortschritt, Kalkulationsbuch
/// mit Bäumen und Bewertungen, Kurs nur aus Info-Linien, geteilter, angepinnter und verknüpfter Kurs —
/// und JEDES Feld jedes Listeneintrags als Text festgehalten. Die Zerlegung in Teilschritte darf daran
/// nichts ändern; Ids sind durch Buchnamen ersetzt (InMemory vergibt sie nicht stabil).
/// </summary>
public class CourseListCharacterizationTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db;
    private readonly CourseService _courses;

    public CourseListCharacterizationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _courses = TestServices.Course(_db);
    }

    public void Dispose() => _db.Dispose();

    private AppUser User(string name)
    {
        var u = new AppUser { Username = name, PasswordHash = "x", CreatedAt = T0 };
        _db.AppUsers.Add(u);
        return u;
    }

    private Book Book(string name, AppUser? owner, bool calc = false, bool isPublic = false, string? tags = null,
        string? themes = null)
    {
        var b = new Book
        {
            FileName = name + ".pgn", DisplayName = name, OwnerUserId = owner?.Id, IsCalculation = calc,
            IsPublic = isPublic, Tags = tags, Themes = themes, Difficulty = "mittel", Rating = 1500,
            Description = "Beschreibung " + name, CreatedAt = T0, UpdatedAt = T0,
            ImportVersion = ImportPipeline.CurrentVersion, Source = new BookSource(),
        };
        _db.Books.Add(b);
        return b;
    }

    private BookPuzzle Line(Book book, string round, bool info = false, string? chapter = null)
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

    private async Task<(AppUser Me, AppUser Other)> SeedAsync()
    {
        var me = User("ich");
        var other = User("anna");
        await _db.SaveChangesAsync();

        // Normaler eigener Kurs: 3 Quiz-Linien + 1 Info-Linie, 2 gelöst (eine davon an der Info-Linie
        // → zählt nicht), Fortschritt mit Modus, angepinnt, mit dem Workbook verknüpft.
        var normal = Book("Normal", me, themes: "endgame,tactics");
        var n1 = Line(normal, "1"); var n2 = Line(normal, "2"); Line(normal, "3");
        var nInfo = Line(normal, "4", info: true);
        var workbook = Book("Workbook", me);
        Line(workbook, "1");
        // Kalkulationsbuch: 3 Stellungen (alle Info), 2 Bäume (einer leer), Bewertungen 4 und 2.
        var calc = Book("Kalk", me, calc: true);
        var c1 = Line(calc, "1", info: true); var c2 = Line(calc, "2", info: true); var c3 = Line(calc, "3", info: true);
        // Nur Info-Linien, kein Quiz.
        var infoOnly = Book("NurInfo", me);
        Line(infoOnly, "1", info: true); Line(infoOnly, "2", info: true);
        // Fremder, mit mir geteilter Chessable-Kurs.
        var shared = Book("Geteilt", other, tags: "chessable");
        var s1 = Line(shared, "1");
        // Fremder öffentlicher Kurs, von mir angepinnt.
        var pinnedPublic = Book("Oeffentlich", other, isPublic: true);
        Line(pinnedPublic, "1");
        // Fremder privater Kurs — erscheint nicht (ausser für Admins).
        var foreign = Book("Fremd", other);
        Line(foreign, "1");
        // Veraltet (alte Pipeline-Version): Chessable ohne Quell-PGN (nicht holbar → „!") und eigenes
        // Buch mit Quell-PGN (wird beim Aktualisieren neu aufbereitet → keine Markierung).
        var staleChessable = Book("VeraltetChessable", me, tags: "chessable");
        staleChessable.ImportVersion = 0;
        Line(staleChessable, "1");
        var staleWithSource = Book("VeraltetMitQuelle", me);
        staleWithSource.ImportVersion = 0;
        staleWithSource.Source = new BookSource { SourcePgn = "[Event \"x\"]\n\n1. e4 *" };
        Line(staleWithSource, "1");
        await _db.SaveChangesAsync();

        _db.CoursePuzzleResults.AddRange(
            new CoursePuzzleResult { UserId = me.Id, BookId = normal.Id, BookPuzzleId = n1.Id, SolvedAt = T0 },
            new CoursePuzzleResult { UserId = me.Id, BookId = normal.Id, BookPuzzleId = n2.Id, SolvedAt = T0 },
            new CoursePuzzleResult { UserId = me.Id, BookId = normal.Id, BookPuzzleId = nInfo.Id, SolvedAt = T0 },
            new CoursePuzzleResult { UserId = me.Id, BookId = shared.Id, BookPuzzleId = s1.Id, SolvedAt = T0 });
        _db.CalculationTrees.AddRange(
            new CalculationTree { UserId = me.Id, BookId = calc.Id, BookPuzzleId = c1.Id, TreeJson = "{}", Grade = 4 },
            new CalculationTree { UserId = me.Id, BookId = calc.Id, BookPuzzleId = c2.Id, TreeJson = "", Grade = 2 },
            new CalculationTree { UserId = other.Id, BookId = calc.Id, BookPuzzleId = c3.Id, TreeJson = "{}", Grade = 4 });
        _db.CourseProgresses.Add(new CourseProgress
        {
            UserId = me.Id, BookId = normal.Id, LastMode = "random", CreatedAt = T0, UpdatedAt = T0.AddDays(1),
        });
        _db.CoursePins.AddRange(
            new CoursePin { UserId = me.Id, BookId = normal.Id, PinnedAt = T0 },
            new CoursePin { UserId = me.Id, BookId = pinnedPublic.Id, PinnedAt = T0 });
        _db.CourseShares.Add(new CourseShare { BookId = shared.Id, OwnerId = other.Id, RecipientId = me.Id, SharedAt = T0 });
        _db.CourseLinks.AddRange(
            new CourseLink { UserId = me.Id, BookId = normal.Id, LinkedBookId = workbook.Id, CreatedAt = T0 },
            new CourseLink { UserId = me.Id, BookId = workbook.Id, LinkedBookId = normal.Id, CreatedAt = T0 });
        await _db.SaveChangesAsync();
        return (me, other);
    }

    private string Describe(IEnumerable<CourseListItemDto> items)
    {
        var names = _db.Books.ToDictionary(b => b.Id, b => b.DisplayName);
        string Val(object? v) => v switch
        {
            null => "null",
            DateTime d => d.ToString("O"),
            System.Collections.IEnumerable e and not string => "[" + string.Join(",", e.Cast<object>()) + "]",
            _ => v.ToString()!,
        };
        return string.Join("\n", items.Select(i => string.Join(" ", typeof(CourseListItemDto).GetProperties()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p =>
            {
                var v = p.GetValue(i);
                if (p.Name is nameof(CourseListItemDto.BookId) or nameof(CourseListItemDto.LinkedBookId) && v is int id)
                    v = names[id];
                return $"{p.Name}={Val(v)}";
            }))));
    }

    [Fact]
    public async Task GetCourses_User_EveryFieldOfEveryItem()
    {
        var (me, _) = await SeedAsync();
        var list = await _courses.GetCoursesAsync(me.Id, isAdmin: false);
        Assert.Equal(ExpectedUser, Describe(list));
    }

    [Fact]
    public async Task GetCourses_Admin_EveryFieldOfEveryItem()
    {
        var (me, _) = await SeedAsync();
        var list = await _courses.GetCoursesAsync(me.Id, isAdmin: true);
        Assert.Equal(ExpectedAdmin, Describe(list));
    }

    private const string ExpectedUser = """
        BookId=Geteilt CalcMaxPoints=null CalcPoints=null Description=Beschreibung Geteilt Difficulty=mittel DisplayName=Geteilt FileName=Geteilt.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=True IsOwned=False IsPinned=False IsShared=True LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=null ProgressPercent=100 PuzzleCount=1 Rating=1500 SharedByUsername=anna SolvedCount=1 Tags=chessable Themes=[tactics]
        BookId=Kalk CalcMaxPoints=12 CalcPoints=6 Description=Beschreibung Kalk Difficulty=mittel DisplayName=Kalk FileName=Kalk.pgn InfoLineCount=0 IsCalculation=True IsChessableImport=False IsOwned=True IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=null ProgressPercent=33 PuzzleCount=3 Rating=1500 SharedByUsername=null SolvedCount=1 Tags=null Themes=[tactics]
        BookId=Normal CalcMaxPoints=null CalcPoints=null Description=Beschreibung Normal Difficulty=mittel DisplayName=Normal FileName=Normal.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=False IsOwned=True IsPinned=True IsShared=False LastActivityAt=2026-10-02T12:00:00.0000000Z LastMode=random LinkedBookId=Workbook LinkedDisplayName=Workbook NeedsReimport=False OwnerUsername=null ProgressPercent=67 PuzzleCount=3 Rating=1500 SharedByUsername=null SolvedCount=2 Tags=null Themes=[endgame,tactics]
        BookId=NurInfo CalcMaxPoints=null CalcPoints=null Description=Beschreibung NurInfo Difficulty=mittel DisplayName=NurInfo FileName=NurInfo.pgn InfoLineCount=2 IsCalculation=False IsChessableImport=False IsOwned=True IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=null ProgressPercent=0 PuzzleCount=0 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=null Themes=[tactics]
        BookId=Oeffentlich CalcMaxPoints=null CalcPoints=null Description=Beschreibung Oeffentlich Difficulty=mittel DisplayName=Oeffentlich FileName=Oeffentlich.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=False IsOwned=False IsPinned=True IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=null ProgressPercent=0 PuzzleCount=1 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=null Themes=[tactics]
        BookId=VeraltetChessable CalcMaxPoints=null CalcPoints=null Description=Beschreibung VeraltetChessable Difficulty=mittel DisplayName=VeraltetChessable FileName=VeraltetChessable.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=True IsOwned=True IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=True OwnerUsername=null ProgressPercent=0 PuzzleCount=1 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=chessable Themes=[tactics]
        BookId=VeraltetMitQuelle CalcMaxPoints=null CalcPoints=null Description=Beschreibung VeraltetMitQuelle Difficulty=mittel DisplayName=VeraltetMitQuelle FileName=VeraltetMitQuelle.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=False IsOwned=True IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=null ProgressPercent=0 PuzzleCount=1 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=null Themes=[tactics]
        BookId=Workbook CalcMaxPoints=null CalcPoints=null Description=Beschreibung Workbook Difficulty=mittel DisplayName=Workbook FileName=Workbook.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=False IsOwned=True IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=Normal LinkedDisplayName=Normal NeedsReimport=False OwnerUsername=null ProgressPercent=0 PuzzleCount=1 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=null Themes=[tactics]
        """;
    private const string ExpectedAdmin = """
        BookId=Fremd CalcMaxPoints=null CalcPoints=null Description=Beschreibung Fremd Difficulty=mittel DisplayName=Fremd FileName=Fremd.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=False IsOwned=False IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=anna ProgressPercent=0 PuzzleCount=1 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=null Themes=[tactics]
        BookId=Geteilt CalcMaxPoints=null CalcPoints=null Description=Beschreibung Geteilt Difficulty=mittel DisplayName=Geteilt FileName=Geteilt.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=True IsOwned=False IsPinned=False IsShared=True LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=anna ProgressPercent=100 PuzzleCount=1 Rating=1500 SharedByUsername=anna SolvedCount=1 Tags=chessable Themes=[tactics]
        BookId=Kalk CalcMaxPoints=12 CalcPoints=6 Description=Beschreibung Kalk Difficulty=mittel DisplayName=Kalk FileName=Kalk.pgn InfoLineCount=0 IsCalculation=True IsChessableImport=False IsOwned=True IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=ich ProgressPercent=33 PuzzleCount=3 Rating=1500 SharedByUsername=null SolvedCount=1 Tags=null Themes=[tactics]
        BookId=Normal CalcMaxPoints=null CalcPoints=null Description=Beschreibung Normal Difficulty=mittel DisplayName=Normal FileName=Normal.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=False IsOwned=True IsPinned=True IsShared=False LastActivityAt=2026-10-02T12:00:00.0000000Z LastMode=random LinkedBookId=Workbook LinkedDisplayName=Workbook NeedsReimport=False OwnerUsername=ich ProgressPercent=67 PuzzleCount=3 Rating=1500 SharedByUsername=null SolvedCount=2 Tags=null Themes=[endgame,tactics]
        BookId=NurInfo CalcMaxPoints=null CalcPoints=null Description=Beschreibung NurInfo Difficulty=mittel DisplayName=NurInfo FileName=NurInfo.pgn InfoLineCount=2 IsCalculation=False IsChessableImport=False IsOwned=True IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=ich ProgressPercent=0 PuzzleCount=0 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=null Themes=[tactics]
        BookId=Oeffentlich CalcMaxPoints=null CalcPoints=null Description=Beschreibung Oeffentlich Difficulty=mittel DisplayName=Oeffentlich FileName=Oeffentlich.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=False IsOwned=False IsPinned=True IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=anna ProgressPercent=0 PuzzleCount=1 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=null Themes=[tactics]
        BookId=VeraltetChessable CalcMaxPoints=null CalcPoints=null Description=Beschreibung VeraltetChessable Difficulty=mittel DisplayName=VeraltetChessable FileName=VeraltetChessable.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=True IsOwned=True IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=True OwnerUsername=ich ProgressPercent=0 PuzzleCount=1 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=chessable Themes=[tactics]
        BookId=VeraltetMitQuelle CalcMaxPoints=null CalcPoints=null Description=Beschreibung VeraltetMitQuelle Difficulty=mittel DisplayName=VeraltetMitQuelle FileName=VeraltetMitQuelle.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=False IsOwned=True IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=null LinkedDisplayName=null NeedsReimport=False OwnerUsername=ich ProgressPercent=0 PuzzleCount=1 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=null Themes=[tactics]
        BookId=Workbook CalcMaxPoints=null CalcPoints=null Description=Beschreibung Workbook Difficulty=mittel DisplayName=Workbook FileName=Workbook.pgn InfoLineCount=0 IsCalculation=False IsChessableImport=False IsOwned=True IsPinned=False IsShared=False LastActivityAt=null LastMode=null LinkedBookId=Normal LinkedDisplayName=Normal NeedsReimport=False OwnerUsername=ich ProgressPercent=0 PuzzleCount=1 Rating=1500 SharedByUsername=null SolvedCount=0 Tags=null Themes=[tactics]
        """;
}
