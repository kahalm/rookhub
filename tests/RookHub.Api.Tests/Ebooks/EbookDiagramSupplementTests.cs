using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>„Ergänzen statt ersetzen“ (0.811.0): Zusammenführen, „unread“-Diagramme, Admin-Endpunkt je Buch.</summary>
public class EbookDiagramSupplementTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private const string KingsFen = "8/8/8/8/8/8/8/K6k w - - 0 1";
    private const string OtherFen = "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1";

    private EbookDiagramWorker Worker()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_kit.Db);
        services.AddSingleton(_kit.Storage);
        services.AddSingleton(new EbookDiagramExtractor(NullLogger<EbookDiagramExtractor>.Instance));
        services.AddSingleton(new EbookEpubDiagramExtractor(NullLogger<EbookEpubDiagramExtractor>.Instance));
        var sp = services.BuildServiceProvider();
        return new EbookDiagramWorker(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<EbookDiagramWorker>.Instance);
    }

    private async Task<Ebook> CuratedBookAsync(string format, params EbookDiagram[] diagrams)
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        var book = await _kit.BookAsync("Predecessors", admin.Id, format);
        book.DiagramState = EbookDiagramState.Done; book.DiagramsCurated = true; book.DiagramCount = diagrams.Length;
        book.SolutionCheckState = EbookDiagramState.Done;
        foreach (var d in diagrams) { d.EbookId = book.Id; _kit.Db.EbookDiagrams.Add(d); }
        await _kit.Db.SaveChangesAsync();
        return book;
    }

    // ── Zusammenführen ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_Epub_SameImageRefIsDuplicate_NewImageAdded_YBackfilled()
    {
        var existing = new List<EbookDiagram>
        {
            new() { Id = 990101, Page = 3, ImageRef = "OEBPS/img/a.jpg", Fen = KingsFen, FontFamily = "opus" },
            new() { Id = 990102, Page = 3, ImageRef = "OEBPS/img/b.jpg", Fen = KingsFen, FontFamily = "opus", Y = 77 },
        };
        var found = new List<EbookDiagram>
        {
            new() { Page = 3, ImageRef = "OEBPS/img/a.jpg", Fen = OtherFen, FontFamily = "image", Y = 40 },   // dasselbe Bild
            new() { Page = 3, ImageRef = "OEBPS/img/b.jpg", Fen = OtherFen, FontFamily = "image", Y = 90 },
            new() { Page = 3, ImageRef = "OEBPS/img/c.jpg", Fen = OtherFen, FontFamily = "image", Y = 120 },  // neu
            new() { Page = 4, ImageRef = "OEBPS/img/d.jpg", Fen = "8/8/8/8/8/8/8/8 w - - 0 1", FontFamily = EbookDiagramVisibility.Unread },
        };

        var r = EbookDiagramMerge.Merge(existing, found);

        Assert.Equal(["OEBPS/img/c.jpg", "OEBPS/img/d.jpg"], r.Added.Select(d => d.ImageRef!).ToArray());
        // Vorhandene unverändert — bis auf die nachgetragene Höhe (nur dort, wo sie 0 war).
        Assert.Equal((KingsFen, "opus", 40.0), (existing[0].Fen, existing[0].FontFamily, existing[0].Y));
        Assert.Equal(77, existing[1].Y);
        Assert.Equal(1, r.YFilled);
    }

    [Fact]
    public void Merge_Pdf_OverlappingBoxIsDuplicate_OtherPlaceAdded_NoDuplicatesWithinNewResult()
    {
        var existing = new List<EbookDiagram>
        {
            new() { Id = 990201, Page = 10, X = 0.10, Y = 0.10, W = 0.40, H = 0.30, Fen = KingsFen, FontFamily = "opus" },
        };
        var found = new List<EbookDiagram>
        {
            new() { Page = 10, X = 0.12, Y = 0.11, W = 0.40, H = 0.30, Fen = OtherFen, FontFamily = "cnn" },  // IoU hoch
            new() { Page = 10, X = 0.25, Y = 0.20, W = 0.60, H = 0.60, Fen = OtherFen, FontFamily = "cnn" },  // IoU klein, aber alter Mittelpunkt liegt drin
            new() { Page = 10, X = 0.10, Y = 0.60, W = 0.40, H = 0.30, Fen = OtherFen, FontFamily = "cnn" },  // darunter: neu
            new() { Page = 10, X = 0.11, Y = 0.61, W = 0.40, H = 0.30, Fen = OtherFen, FontFamily = "cnn" },  // doppelt in der neuen Erkennung
            new() { Page = 11, X = 0.10, Y = 0.10, W = 0.40, H = 0.30, Fen = OtherFen, FontFamily = "cnn" },  // andere Seite: neu
        };

        var r = EbookDiagramMerge.Merge(existing, found);

        // Box 2: Mittelpunkt (0.55, 0.50) liegt außerhalb der alten Box, aber der alte Mittelpunkt (0.30, 0.25) liegt in ihr → gleich.
        Assert.Equal([(10, 0.60), (11, 0.10)], r.Added.Select(d => (d.Page, d.Y)).ToArray());
        Assert.Equal(0, r.YFilled);
    }

    [Theory]
    [InlineData(0.10, 0.10, 0.40, 0.30, true)]    // identisch
    [InlineData(0.20, 0.15, 0.40, 0.30, true)]    // stark überlappend
    [InlineData(0.55, 0.10, 0.40, 0.30, false)]   // daneben
    [InlineData(0.25, 0.20, 0.05, 0.05, true)]    // kleine Box mitten in der großen
    public void SamePlace_IouOrCentreInside(double x, double y, double w, double h, bool expected)
    {
        var a = new EbookDiagram { X = 0.10, Y = 0.10, W = 0.40, H = 0.30 };
        Assert.Equal(expected, EbookDiagramMerge.SamePlace(a, new EbookDiagram { X = x, Y = y, W = w, H = h }));
    }

    // ── Worker ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Worker_Supplement_KeepsCuratedDiagrams_AddsOnlyNewOnes_CountsReadable()
    {
        var book = await CuratedBookAsync("epub",
            new EbookDiagram { Id = 990301, Page = 2, ImageRef = "i/1.png", Fen = KingsFen, FontFamily = "opus", SolutionUci = "a1a2", SolutionSource = "book",
                SolutionCheck = EbookSolutionCheck.Confirmed });
        book.DiagramSupplement = true; book.DiagramState = EbookDiagramState.Pending;
        await _kit.Db.SaveChangesAsync();
        var result = new EbookDiagramExtractor.Result(
        [
            new EbookDiagram { EbookId = book.Id, Page = 2, ImageRef = "i/1.png", Y = 10, Fen = OtherFen, FontFamily = "image" },
            new EbookDiagram { EbookId = book.Id, Page = 2, ImageRef = "i/2.png", Y = 20, Fen = OtherFen, FontFamily = "image" },
            new EbookDiagram { EbookId = book.Id, Page = 2, ImageRef = "i/3.png", Y = 30, Fen = "8/8/8/8/8/8/8/8 w - - 0 1",
                FontFamily = EbookDiagramVisibility.Unread, Confidence = 0 },
        ], true, 0);

        await Worker().FinishSupplementAsync(_kit.Db, book, result, CancellationToken.None);

        var all = await _kit.Db.EbookDiagrams.OrderBy(d => d.Y).ToListAsync();
        Assert.Equal(3, all.Count);
        var kept = all.Single(d => d.Id == 990301);
        Assert.Equal((KingsFen, "opus", "a1a2", EbookSolutionCheck.Confirmed, 10.0), (kept.Fen, kept.FontFamily, kept.SolutionUci, kept.SolutionCheck, kept.Y));
        var b = await _kit.Db.Ebooks.SingleAsync();
        Assert.False(b.DiagramSupplement);
        Assert.True(b.DiagramsCurated);
        Assert.Equal(EbookDiagramState.Done, b.DiagramState);
        Assert.Equal(2, b.DiagramCount);   // das ungelesene zählt nicht
        Assert.Equal(EbookDiagramState.Pending, b.SolutionCheckState);
    }

    [Fact]
    public async Task Worker_Supplement_RunsDespiteCurated_FileMissing_KeepsBookDone()
    {
        var book = await CuratedBookAsync("pdf",
            new EbookDiagram { Id = 990401, Page = 5, X = 0.1, Y = 0.1, W = 0.4, H = 0.3, Fen = KingsFen, FontFamily = "opus" });
        await _kit.Service.QueueDiagramSupplementAsync(book.Id);

        Assert.True(await Worker().RunStepAsync(CancellationToken.None));

        var b = await _kit.Db.Ebooks.SingleAsync();
        Assert.False(b.DiagramSupplement);
        Assert.True(b.DiagramsCurated);
        Assert.Equal(EbookDiagramState.Done, b.DiagramState);   // nicht „Failed“ über den vorhandenen Diagrammen
        Assert.Equal(1, b.DiagramCount);
        Assert.Equal(EbookDiagramState.Done, b.SolutionCheckState);   // nichts dazugekommen → keine neue Prüfung
        Assert.Equal("opus", (await _kit.Db.EbookDiagrams.SingleAsync()).FontFamily);
    }

    // ── „unread“ ist für Nutzer unsichtbar ────────────────────────────────────────────────────────

    [Fact]
    public async Task Unread_HiddenFromReader_Report_AndCourseLinks_VisibleAfterCorrection()
    {
        var book = await CuratedBookAsync("epub",
            new EbookDiagram { Id = 990501, Page = 1, Y = 10, ImageRef = "i/1.png", Fen = KingsFen, FontFamily = "opus" },
            new EbookDiagram { Id = 990502, Page = 1, Y = 20, ImageRef = "i/2.png", Fen = OtherFen, FontFamily = EbookDiagramVisibility.Unread });
        var owner = book.OwnerUserId!.Value;

        var list = await _kit.Service.GetDiagramsAsync(owner, true, book.Id);
        Assert.Equal([990501], list.Items.Select(d => d.Id).ToArray());
        await Assert.ThrowsAsync<NotFoundException>(() => _kit.Service.ReportDiagramAsync(owner, true, book.Id, 990502, true, null));

        // Ein Kurs-Verweis auf die Stellung des ungelesenen Diagramms findet es nicht.
        _kit.Db.CourseLineEbookRefs.Add(new CourseLineEbookRef { Id = 990503, BookPuzzleId = 990504, EbookId = book.Id, Placement = OtherFen.Split(' ')[0], Page = 1 });
        await _kit.Db.SaveChangesAsync();
        Assert.Equal(0, await CourseEbookLinker.RelinkEbookAsync(_kit.Db, book.Id));
        Assert.Null((await _kit.Db.CourseLineEbookRefs.SingleAsync()).EbookDiagramId);

        // Korrigiert → gelesen und sichtbar.
        var dto = await _kit.Service.CorrectDiagramPositionAsync(owner, true, book.Id, 990502, "4k3/8/8/8/8/8/4P3/4K3 b");
        Assert.Equal(990502, dto.Id);
        Assert.Equal("hand", (await _kit.Db.EbookDiagrams.SingleAsync(d => d.Id == 990502)).FontFamily);
        Assert.Equal(2, (await _kit.Service.GetDiagramsAsync(owner, true, book.Id)).Items.Count);
    }

    [Fact]
    public async Task Readable_FiltersOnlyUnread_KeepsNullFamily()
    {
        var book = await CuratedBookAsync("pdf",
            new EbookDiagram { Id = 990601, Page = 1, Fen = KingsFen, FontFamily = null },
            new EbookDiagram { Id = 990602, Page = 2, Fen = KingsFen, FontFamily = "cnn" },
            new EbookDiagram { Id = 990603, Page = 3, Fen = KingsFen, FontFamily = EbookDiagramVisibility.Unread });
        var ids = await _kit.Db.EbookDiagrams.Where(d => d.EbookId == book.Id).Readable().Select(d => d.Id).OrderBy(i => i).ToListAsync();
        Assert.Equal([990601, 990602], ids);
    }

    // ── Rohe Lesung + Scan-Auswahl ────────────────────────────────────────────────────────────────

    [Fact]
    public void GridToPlacement_ImplausibleGrid_StillSyntacticallyValid()
    {
        var grid = Enumerable.Repeat('.', 64).ToArray();
        grid[0] = 'P'; grid[1] = 'K'; grid[2] = 'K'; grid[63] = '?';   // Bauer auf Reihe 8, zwei Könige, Unsinn
        Assert.Null(EbookBoardImage.GridToBoard(grid));
        Assert.Equal("PKK5/8/8/8/8/8/8/8", EbookBoardImage.GridToPlacement(grid));
    }

    [Fact]
    public void ScanChoose_KeepUnread_KeepsRawBoardAsUnread()
    {
        EbookScanDiagramExtractor.ScanCandidate C(string? board, string? raw) => new(7, 0.1, 0.2, 0.3, 0.3, board, null, null, 0.9f, raw);
        var dropped = EbookScanDiagramExtractor.Choose([C("4k3/8/8/8/8/8/8/4K3", null), C(null, "8/8/8/8/8/8/8/8")], out _);
        Assert.Single(dropped);
        var kept = EbookScanDiagramExtractor.Choose([C("4k3/8/8/8/8/8/8/4K3", null), C(null, "8/8/8/8/8/8/8/8")], out _, keepUnread: true);
        Assert.Equal([false, true], kept.Select(b => b.Unread).ToArray());
        Assert.Equal("8/8/8/8/8/8/8/8", kept[1].Board);
    }

    // ── Endpunkt ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SupplementEndpoint_SetsFlagAndPending_202_Unknown404()
    {
        var book = await CuratedBookAsync("epub");
        var controller = new EbooksAdminController(null!, _kit.Storage, _kit.Db, _kit.Service, null!);

        var res = await controller.SupplementDiagrams(book.Id, CancellationToken.None);

        Assert.IsType<AcceptedResult>(res);
        var b = await _kit.Db.Ebooks.SingleAsync();
        Assert.True(b.DiagramSupplement);
        Assert.True(b.DiagramsCurated);
        Assert.Equal(EbookDiagramState.Pending, b.DiagramState);
        await Assert.ThrowsAsync<NotFoundException>(() => controller.SupplementDiagrams(999999, CancellationToken.None));
    }
}
