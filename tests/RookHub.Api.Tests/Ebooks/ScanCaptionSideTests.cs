using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Zugrecht aus „White/Black to move“ unter Scan-Brettern (0.823.0, Pongó „Tactical Targets“: 12 von 870 Diagrammen
/// hatten ein Zugrecht, unter fast jedem Brett stand „Black to move“).</summary>
public class ScanCaptionSideTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private static PageWord W(double x0, double y, string text) => new(x0, y - 10, x0 + text.Length * 6, y, text, "Times");

    [Fact]
    public void Captions_ReadsOcrSplitWords_AndGerman()
    {
        var caps = ScanCaptionSide.Captions([W(50, 300, "B"), W(58, 300, "l"), W(63, 300, "ack"), W(90, 300, "to"), W(105, 300, "move"),
            W(250, 300, "Weiß"), W(290, 300, "am"), W(310, 300, "Zug"), W(50, 400, "The black king moves")]);
        Assert.Equal(['b', 'w'], caps.Select(c => c.Side));
    }

    [Fact]
    public void Find_PrefersCaptionBelow_OverTheOneOfTheBoardAbove()
    {
        // Brett 230–390; darüber (y 212) die Unterschrift des oberen Bretts „Black“, darunter (y 405) die eigene „White“.
        var caps = new List<(char, double, double)> { ('b', 120, 212), ('w', 120, 405) };
        Assert.Equal('w', ScanCaptionSide.Find(caps, 40, 230, 200, 390, 400, 600));
        Assert.Equal('b', ScanCaptionSide.Find([('b', 120, 212)], 40, 230, 200, 390, 400, 600));   // nur darüber → die
        Assert.Null(ScanCaptionSide.Find(caps, 220, 230, 380, 390, 400, 600));                       // andere Spalte
        Assert.Null(ScanCaptionSide.Find([('w', 120, 500)], 40, 230, 200, 390, 400, 600));           // zu weit unten
    }

    private static byte[] CaptionPdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.TimesRoman);
        var page = builder.AddPage(400, 600);
        page.AddText("B l ack to move", 9, new PdfPoint(80, 600 - 215), font);   // unter Brett A (40–200)
        page.AddText("White to move", 9, new PdfPoint(80, 600 - 405), font);     // unter Brett B (230–390)
        return builder.Build();
    }

    [Fact]
    public async Task Apply_SetsSideFromCaptions_WithoutMarkingTheBookCurated()
    {
        var owner = await _kit.UserAsync("owner");
        var book = await _kit.BookAsync("Tactical Targets", owner.Id);
        var file = book.Files.Single();
        var path = _kit.Storage.ResolveFile(book, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, CaptionPdf());
        EbookDiagram Diagram(double x, double y) => new()
        {
            EbookId = book.Id, Page = 1, X = x / 400, Y = y / 600, W = 160.0 / 400, H = 160.0 / 600,
            Fen = "6k1/8/8/8/8/8/8/R5K1 w - - 0 1", FontFamily = "cnn",
        };
        var a = Diagram(40, 40); var b = Diagram(40, 230); var c = Diagram(220, 40);
        _kit.Db.EbookDiagrams.AddRange(a, b, c);
        await _kit.Db.SaveChangesAsync();

        var r = await EbookSideFromCaptions.ApplyAsync(_kit.Db, _kit.Storage, book.Id, default);

        Assert.Equal((3, 2, 1), (r.Checked, r.Set, r.Changed));
        Assert.True(a.SideKnown); Assert.StartsWith("6k1/8/8/8/8/8/8/R5K1 b ", a.Fen);
        Assert.True(b.SideKnown); Assert.StartsWith("6k1/8/8/8/8/8/8/R5K1 w ", b.Fen);
        Assert.False(c.SideKnown);
        Assert.False((await _kit.Db.Ebooks.SingleAsync(e => e.Id == book.Id)).DiagramsCurated);
    }
}
