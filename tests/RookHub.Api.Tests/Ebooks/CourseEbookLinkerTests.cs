using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Kurslinie → E-Book-Diagramm über die Stellung (Wunsch 2026-10-15: „aus dem Kurs zum Buch springen").</summary>
public class CourseEbookLinkerTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";

    [Fact]
    public void PlacementAfter_HandlesNormalCastlingEnPassantPromotion()
    {
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR", CourseEbookLinker.PlacementAfter(Start, "e2e4"));
        Assert.Equal("4k3/8/8/8/8/8/8/R4RK1", CourseEbookLinker.PlacementAfter("4k3/8/8/8/8/8/8/R3K2R", "e1g1"));
        Assert.Equal("4k3/8/8/8/8/8/8/2KR3R", CourseEbookLinker.PlacementAfter("4k3/8/8/8/8/8/8/R3K2R", "e1c1"));
        Assert.Equal("4k3/8/3P4/8/8/8/8/4K3", CourseEbookLinker.PlacementAfter("4k3/8/8/3pP3/8/8/8/4K3", "e5d6"));
        Assert.Equal("3Nk3/8/8/8/8/8/8/4K3", CourseEbookLinker.PlacementAfter("4k3/3P4/8/8/8/8/8/4K3", "d7d8n"));
        Assert.Null(CourseEbookLinker.PlacementAfter(Start, "e4e5"));   // leeres Feld
    }

    [Fact]
    public void Match_FollowsBookOrderForRepeatedPositions()
    {
        // Dieselbe Stellung steht zweimal im Buch: zweite Linie nimmt das zweite Vorkommen (ab der vorigen Stelle).
        var lines = new List<(int, string[])> { (1, ["A"]), (2, ["B"]), (3, ["A"]), (4, ["X", "C"]) };
        var diagrams = new List<(int, string)> { (10, "A"), (11, "B"), (12, "A"), (13, "C") };
        var pairs = CourseEbookLinker.Match(lines, diagrams);
        Assert.Equal([(1, 10), (2, 11), (3, 12), (4, 13)], pairs);
    }

    [Fact]
    public async Task Link_FindsTheBook_MatchesAfterSetupMove_AndRefsAreReadable()
    {
        var owner = await _kit.UserAsync("owner");
        var other = new Ebook { Title = "Anderes", OwnerUserId = owner.Id, Source = EbookSource.Upload, DiagramState = EbookDiagramState.Done, DiagramCount = 1 };
        var ebook = new Ebook { Title = "Woodpecker", OwnerUserId = owner.Id, Source = EbookSource.Upload, DiagramState = EbookDiagramState.Done, DiagramCount = 4 };
        _kit.Db.Ebooks.AddRange(other, ebook);
        var course = new Book { FileName = "wp.pgn", DisplayName = "WP", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
        _kit.Db.Books.Add(course);
        await _kit.Db.SaveChangesAsync();

        string[] fens = ["4k3/8/8/8/8/8/8/R3K3", "4k3/8/8/8/8/8/4P3/4K3", "4k3/8/8/8/8/8/8/4K2R", "7k/8/8/8/8/8/8/4K2R"];
        // Linie 1–3 stehen direkt so im Buch; Linie 4 beginnt mit dem Gegnerzug h8g8 — das Buch zeigt die Stellung danach.
        var lines = new List<BookPuzzle>();
        for (int i = 0; i < 4; i++)
            lines.Add(new BookPuzzle { LineId = $"l{i}", BookId = course.Id, BookFileName = course.FileName, Round = $"00{i}", Chapter = "C",
                Fen = fens[i] + " w - - 0 1", Moves = i == 3 ? "h8g8 h1h8" : "e1e2" });
        _kit.Db.BookPuzzles.AddRange(lines);
        _kit.Db.EbookDiagrams.Add(new EbookDiagram { EbookId = other.Id, Page = 1, Fen = fens[0] + " w - - 0 1" });
        for (int i = 0; i < 3; i++)
            _kit.Db.EbookDiagrams.Add(new EbookDiagram { EbookId = ebook.Id, Page = 10 + i, Fen = fens[i] + " w - - 0 1", Label = $"{i + 1}" });
        _kit.Db.EbookDiagrams.Add(new EbookDiagram { EbookId = ebook.Id, Page = 13, Fen = "6k1/8/8/8/8/8/8/4K2R w - - 0 1", Label = "4" });
        await _kit.Db.SaveChangesAsync();

        var linker = new CourseEbookLinker(_kit.Db);
        var r = await linker.LinkAsync(owner.Id, course.Id, isAdmin: false, ebookId: null);
        Assert.Equal(ebook.Id, r.EbookId);
        Assert.Equal(4, r.Linked);
        Assert.Equal(4, r.Lines);

        var refs = await linker.GetRefsAsync(owner.Id, course.Id, isAdmin: false);
        Assert.Equal([10, 11, 12, 13], refs.OrderBy(x => x.Page).Select(x => x.Page));
        Assert.Equal(lines[3].Id, refs.Single(x => x.Page == 13).LineId);

        // Erneut verknüpfen ersetzt statt zu verdoppeln.
        await linker.LinkAsync(owner.Id, course.Id, isAdmin: false, ebookId: ebook.Id);
        Assert.Equal(4, await _kit.Db.CourseLineEbookRefs.CountAsync());
    }

    [Fact]
    public async Task Link_TooFewMatches_LinksNothing()
    {
        var owner = await _kit.UserAsync("owner");
        var ebook = new Ebook { Title = "B", OwnerUserId = owner.Id, Source = EbookSource.Upload, DiagramState = EbookDiagramState.Done, DiagramCount = 1 };
        _kit.Db.Ebooks.Add(ebook);
        var course = new Book { FileName = "c.pgn", DisplayName = "C", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
        _kit.Db.Books.Add(course);
        await _kit.Db.SaveChangesAsync();
        _kit.Db.BookPuzzles.Add(new BookPuzzle { LineId = "x", BookId = course.Id, BookFileName = "c.pgn", Round = "1", Fen = Start + " w KQkq - 0 1", Moves = "e2e4" });
        _kit.Db.EbookDiagrams.Add(new EbookDiagram { EbookId = ebook.Id, Page = 1, Fen = Start + " w KQkq - 0 1" });
        await _kit.Db.SaveChangesAsync();

        var r = await new CourseEbookLinker(_kit.Db).LinkAsync(owner.Id, course.Id, isAdmin: false, ebookId: null);
        Assert.Null(r.EbookId);
        Assert.Equal(0, await _kit.Db.CourseLineEbookRefs.CountAsync());
    }

    [Fact]
    public async Task Reread_RefsFindTheNewDiagramsByPosition_NearestPageWins()
    {
        var owner = await _kit.UserAsync("owner");
        var ebook = new Ebook { Title = "WP", OwnerUserId = owner.Id, Source = EbookSource.Upload, DiagramState = EbookDiagramState.Done, DiagramCount = 3 };
        _kit.Db.Ebooks.Add(ebook);
        var course = new Book { FileName = "c.pgn", DisplayName = "C", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
        _kit.Db.Books.Add(course);
        await _kit.Db.SaveChangesAsync();
        string[] fens = ["4k3/8/8/8/8/8/8/R3K3", "4k3/8/8/8/8/8/4P3/4K3", "4k3/8/8/8/8/8/8/4K2R"];
        var lines = fens.Select((f, i) => new BookPuzzle { LineId = $"l{i}", BookId = course.Id, BookFileName = "c.pgn", Round = $"{i}", Fen = f + " w - - 0 1", Moves = "e1e2" }).ToList();
        _kit.Db.BookPuzzles.AddRange(lines);
        for (int i = 0; i < 3; i++)
            _kit.Db.EbookDiagrams.Add(new EbookDiagram { EbookId = ebook.Id, Page = 10 + i, Fen = fens[i] + " w - - 0 1" });
        await _kit.Db.SaveChangesAsync();
        var linker = new CourseEbookLinker(_kit.Db);
        Assert.Equal(3, (await linker.LinkAsync(owner.Id, course.Id, isAdmin: false, ebookId: ebook.Id)).Linked);

        // Buch neu gelesen: alle Diagramme weg, neue Ids. Stellung 1 jetzt zweimal (S. 3 und S. 11 — 11 liegt näher an 10),
        // Stellung 2 unverändert auf S. 11, Stellung 3 falsch gelesen (anderer Turm) → nicht wiederfindbar.
        _kit.Db.EbookDiagrams.RemoveRange(_kit.Db.EbookDiagrams.Where(d => d.EbookId == ebook.Id));
        await _kit.Db.SaveChangesAsync();
        _kit.Db.EbookDiagrams.AddRange(
            new EbookDiagram { EbookId = ebook.Id, Page = 3, Fen = fens[0] + " w - - 0 1" },
            new EbookDiagram { EbookId = ebook.Id, Page = 11, Fen = fens[0] + " b - - 0 1" },
            new EbookDiagram { EbookId = ebook.Id, Page = 11, Fen = fens[1] + " w - - 0 1" },
            new EbookDiagram { EbookId = ebook.Id, Page = 12, Fen = "4k3/8/8/8/8/8/8/4K1R1 w - - 0 1" });
        await _kit.Db.SaveChangesAsync();

        // 2 umgehängt; der dritte stand schon beim Löschen auf null (SetNull) und bleibt es.
        Assert.Equal(2, await CourseEbookLinker.RelinkEbookAsync(_kit.Db, ebook.Id));
        var refs = await linker.GetRefsAsync(owner.Id, course.Id, isAdmin: false);
        Assert.Equal(2, refs.Count);
        Assert.Equal(11, refs.Single(r => r.LineId == lines[0].Id).Page);
        Assert.Equal(11, refs.Single(r => r.LineId == lines[1].Id).Page);
        // Der dritte Verweis bleibt gespeichert und kommt zurück, sobald die Stellung wieder gelesen wird.
        var lost = await _kit.Db.CourseLineEbookRefs.SingleAsync(r => r.BookPuzzleId == lines[2].Id);
        Assert.Null(lost.EbookDiagramId);
        _kit.Db.EbookDiagrams.Add(new EbookDiagram { EbookId = ebook.Id, Page = 12, Fen = fens[2] + " w - - 0 1" });
        await _kit.Db.SaveChangesAsync();
        Assert.Equal(3, (await linker.GetRefsAsync(owner.Id, course.Id, isAdmin: false)).Count);   // Abruf heilt selbst
    }
}
