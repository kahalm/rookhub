using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Fließtext-EPUB aus Text-PDFs (0.802.0): Layout-Regeln an kleinen synthetischen Seiten, EPUB-Aufbau, Dienst.</summary>
public class EbookReflowTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private const double W = 400, H = 600, Size = 10;

    /// <summary>Eine Textzeile als Glyphen (ein Zeichen je 5 pt, Leerzeichen als Lücke).</summary>
    private static IEnumerable<ReflowGlyph> Line(string text, double x, double baseline, double size = Size, bool bold = false, string font = "Garamond")
    {
        var cx = x;
        foreach (var c in text)
        {
            if (c == ' ') { cx += size * 0.3; continue; }
            yield return new ReflowGlyph(cx, cx + size * 0.5, baseline, size, c.ToString(), font, bold, false);
            cx += size * 0.5;
        }
    }

    /// <summary>Text auf volle Zeilenbreite (40 … 360) bringen: Wörter auffüllen, bis die Zeile voll ist.</summary>
    private static string Full(string start, string end = "")
    {
        var s = start;
        while ((s.Length + end.Length + 4) * Size * 0.5 < 320) s += " word";
        return s + end;
    }

    private static ReflowPage Page(int n, params IEnumerable<ReflowGlyph>[] lines) => new(n, W, H, lines.SelectMany(l => l).ToList());

    private static List<ReflowParagraph> Paragraphs(IReadOnlyList<ReflowPage> pages, IReadOnlyList<ReflowDiagram>? diagrams = null) =>
        EbookReflowLayout.Blocks(pages, diagrams ?? []).OfType<ReflowParagraph>().ToList();

    [Fact]
    public void Blocks_JoinsHyphenatedWords_AndKeepsHyphenBeforeCapitals()
    {
        var page = Page(1,
            Line(Full("The", " decisive com-"), 40, 100),
            Line(Full("bination was", " a Nimzo-"), 40, 112),
            Line("Indian idea.", 40, 124));
        var p = Assert.Single(Paragraphs([page]));
        Assert.Contains("decisive combination", p.PlainText);
        Assert.Contains("Nimzo-Indian", p.PlainText);
        Assert.DoesNotContain("com-", p.PlainText);
    }

    [Fact]
    public void Blocks_ShortLineOrIndentStartsNewParagraph()
    {
        var page = Page(1,
            Line(Full("First paragraph runs"), 40, 100),
            Line("to its end.", 40, 112),
            Line(Full("Second one, indented"), 52, 124),
            Line("and done.", 40, 136));
        var ps = Paragraphs([page]);
        Assert.Equal(2, ps.Count);
        Assert.StartsWith("First paragraph", ps[0].PlainText);
        Assert.StartsWith("Second one", ps[1].PlainText);
    }

    [Fact]
    public void Blocks_RemovesRunningHeadsAndPageNumbers()
    {
        var pages = Enumerable.Range(1, 4).Select(n => Page(n,
            Line("General Introduction", 40, 30),
            Line(n.ToString(), 340, 30),
            Line(Full($"Body text of page {n} starts"), 40, 100),
            Line("here.", 40, 112),
            Line((100 + n).ToString(), 190, 580))).ToList();
        var text = string.Join("\n", Paragraphs(pages).Select(p => p.PlainText));
        Assert.DoesNotContain("General Introduction", text);
        Assert.DoesNotContain("101", text);
        Assert.Contains("Body text of page 3", text);
    }

    [Fact]
    public void Blocks_TwoColumns_LeftColumnFirst()
    {
        var lines = new List<IEnumerable<ReflowGlyph>>();
        for (var i = 0; i < 10; i++)
        {
            lines.Add(Line($"left {i} aaaa bbbb cccc", 20, 100 + 12 * i));
            lines.Add(Line($"right {i} aaaa bbbb cccc", 220, 100 + 12 * i));
        }
        var text = string.Join(" ", Paragraphs([Page(1, lines.ToArray())]).Select(p => p.PlainText));
        Assert.True(text.IndexOf("left 9", StringComparison.Ordinal) < text.IndexOf("right 0", StringComparison.Ordinal), text);
    }

    [Fact]
    public void Blocks_LargerFontBecomesHeading()
    {
        var pages = Enumerable.Range(1, 2).Select(n => Page(n,
            Line($"Chapter {n}", 40, 60, size: 18),
            Line(Full("Some body text that"), 40, 100),
            Line("ends here.", 40, 112))).ToList();
        var ps = Paragraphs(pages);
        Assert.Equal(1, ps.First(p => p.PlainText.StartsWith("Chapter 1")).Level);
        Assert.Equal(0, ps.First(p => p.PlainText.StartsWith("Some body")).Level);
    }

    [Fact]
    public void Translate_MapsFigurinesOnlyInFigurineFonts()
    {
        Assert.Equal("♔", EbookReflowPdfReader.Translate("¢", "SPAriesFig-Bold"));
        Assert.Equal("♘", EbookReflowPdfReader.Translate("", "SPTimeFig-Roman"));   // Symbolbereich
        Assert.Equal("£", EbookReflowPdfReader.Translate("£", "AGaramondPro-Regular"));   // Pfund bleibt Pfund
        Assert.Equal("♕", EbookReflowPdfReader.Translate("♕", "AGaramondPro-Regular"));
        Assert.Equal("+", EbookReflowPdfReader.Translate("†", "SPAriesFig-Bold"));
        Assert.Equal("✓", EbookReflowPdfReader.Translate("", "Wingdings-Regular"));
        Assert.Equal("", EbookReflowPdfReader.Translate("", "Wingdings3"));          // Zugrecht-Dreieck
        Assert.Equal("fi", EbookReflowPdfReader.Translate("ﬁ", "MinionPro"));
        Assert.Equal('K', EbookSolutionFinder.FigurinePiece('¢'));
        Assert.Equal("Kxf7", EbookSolutionFinder.Normalize("¢xf7"));   // Lösungssuche unverändert
    }

    private static readonly ReflowDiagram Board = new(7, 1, 0.3, 0.3, 0.4, 0.4 * W / H,
        "6k1/5ppp/8/8/8/8/5PPP/6K1 w - - 0 1", "12", true);

    private static ReflowPage DiagramPage()
    {
        // Brett von x 120…280, y 180…340 (Zelle 20 pt); darin „Zeichen“, darunter Koordinaten, links die Nummer.
        return Page(1,
            Line("Text before the diagram.", 40, 100),
            Line("12", 85, 195),
            Line("xxxxxxxx", 125, 250),
            Line("a b c d e f g h", 125, 352),
            Line("White to move and win.", 40, 400));
    }

    [Fact]
    public void Blocks_DiagramBecomesFigure_GridTextAndCoordinatesRemoved()
    {
        var blocks = EbookReflowLayout.Blocks([DiagramPage()], [Board]);
        var texts = blocks.OfType<ReflowParagraph>().Select(p => p.PlainText).ToList();
        Assert.DoesNotContain(texts, t => t.Contains("xxxx") || t.Contains("a b c") || t.Trim() == "12");
        var fig = Assert.Single(blocks.OfType<ReflowFigure>());
        var idx = blocks.IndexOf(fig);
        Assert.Contains("Text before", ((ReflowParagraph)blocks[idx - 1]).PlainText);
        Assert.Contains("White to move", ((ReflowParagraph)blocks[idx + 1]).PlainText);
    }

    [Fact]
    public void BoardSvg_DrawsPiecesFromTheFen()
    {
        var svg = EbookBoardSvg.Render("6k1/8/8/8/8/8/8/R5K1 w - - 0 1")!;
        var doc = XDocument.Parse(svg);
        Assert.Equal("svg", doc.Root!.Name.LocalName);
        Assert.Contains("id=\"wR\"", svg);
        Assert.Contains("id=\"bK\"", svg);
        Assert.Equal(3, svg.Split("<use ").Length - 1);
        Assert.Null(EbookBoardSvg.Render("not a fen"));
    }

    /// <summary>Schreibt das EPUB eines kleinen Buchs und liefert Archiv + Bildverweise.</summary>
    private static (ZipArchive Zip, IReadOnlyDictionary<int, (string ImageRef, int Spine)> Images) Epub()
    {
        var pages = new List<ReflowPage> { DiagramPage() };
        for (var n = 2; n <= 3; n++)
            pages.Add(Page(n, Line($"Chapter {n}", 40, 60, size: 18), Line(Full("Body & <text> “quoted”"), 40, 100), Line("end.", 40, 112)));
        var doc = new EbookReflowPdfReader.Document(pages, [new ReflowOutline("Start", 1, 0), new ReflowOutline("Second", 2, 0), new ReflowOutline("Third", 3, 0)], null, null);
        var ms = new MemoryStream();
        // Zu wenig Text für die Scan-Schwelle ist hier egal: die Layout-Stufen direkt aufrufen.
        var blocks = EbookReflowLayout.Blocks(doc.Pages, [Board]);
        var chapters = EbookReflowLayout.Chapters(blocks, doc.Outline, doc.Pages.Count);
        var images = EbookEpubWriter.Write(new ReflowBook("Test & Book", "A. Author", "en", "urn:test:1", chapters), ms, new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc));
        ms.Position = 0;
        return (new ZipArchive(ms, ZipArchiveMode.Read), images);
    }

    [Fact]
    public void Epub_IsStructurallyValid()
    {
        var (zip, _) = Epub();
        using (zip)
        {
            var first = zip.Entries[0];
            Assert.Equal("mimetype", first.FullName);
            Assert.Equal(first.Length, first.CompressedLength);   // unkomprimiert
            using (var r = new StreamReader(first.Open())) Assert.Equal("application/epub+zip", r.ReadToEnd());

            var container = Xml(zip, "META-INF/container.xml");
            var opfPath = container.Descendants().First(e => e.Name.LocalName == "rootfile").Attribute("full-path")!.Value;
            var opf = Xml(zip, opfPath);
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            Assert.Equal("Test & Book", opf.Descendants(dc + "title").Single().Value);
            Assert.Equal("en", opf.Descendants(dc + "language").Single().Value);
            Assert.Equal("urn:test:1", opf.Descendants(dc + "identifier").Single().Value);
            Assert.Contains(opf.Descendants(), e => e.Attribute("property")?.Value == "dcterms:modified");
            var items = opf.Descendants().Where(e => e.Name.LocalName == "item").ToList();
            Assert.Contains(items, i => i.Attribute("properties")?.Value == "nav");
            var dir = opfPath.Contains('/') ? opfPath[..(opfPath.LastIndexOf('/') + 1)] : "";
            foreach (var i in items) Assert.NotNull(zip.GetEntry(dir + i.Attribute("href")!.Value));
            var ids = items.Select(i => i.Attribute("id")!.Value).ToHashSet();
            var spine = opf.Descendants().Where(e => e.Name.LocalName == "itemref").Select(e => e.Attribute("idref")!.Value).ToList();
            Assert.True(spine.Count >= 3);
            Assert.All(spine, id => Assert.Contains(id, ids));
            // Jede XHTML-Datei ist wohlgeformtes XML (epub.js parst sie als application/xhtml+xml).
            foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".xhtml") || e.FullName.EndsWith(".svg"))) Xml(zip, e.FullName);
            var nav = Xml(zip, dir + "nav.xhtml");
            Assert.Contains(nav.Descendants(), e => e.Name.LocalName == "nav");
        }
    }

    [Fact]
    public void Epub_DiagramImageRefPointsIntoTheArchive_AndSpineCountsTheTitlePage()
    {
        var (zip, images) = Epub();
        using (zip)
        {
            var (imageRef, spine) = images[Board.Key];
            Assert.Equal("OEBPS/images/p0001-1.svg", imageRef);
            Assert.NotNull(zip.GetEntry(imageRef));
            Assert.Equal(2, spine);   // 1 = Titelblatt, 2 = erstes Kapitel
            var opf = Xml(zip, "OEBPS/content.opf");
            var hrefById = opf.Descendants().Where(e => e.Name.LocalName == "item").ToDictionary(e => e.Attribute("id")!.Value, e => e.Attribute("href")!.Value);
            var chapterHref = hrefById[opf.Descendants().Where(e => e.Name.LocalName == "itemref").ElementAt(spine - 1).Attribute("idref")!.Value];
            var chapter = Xml(zip, "OEBPS/" + chapterHref);
            var img = chapter.Descendants().Single(e => e.Name.LocalName == "img");
            Assert.Equal("../images/p0001-1.svg", img.Attribute("src")!.Value);
            Assert.Contains("12", chapter.Descendants().Single(e => e.Name.LocalName == "figcaption").Value);
        }
    }

    private static XDocument Xml(ZipArchive zip, string path)
    {
        using var s = zip.GetEntry(path)!.Open();
        return XDocument.Load(s);
    }

    // ── Dienst ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Text-PDF mit zwei Seiten Fließtext (Helvetica), genug Zeichen für die Scan-Schwelle.</summary>
    private static byte[] TextPdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var p = 0; p < 2; p++)
        {
            var page = builder.AddPage(400, 600);
            for (var i = 0; i < 30; i++)
                page.AddText($"Line {i} of page {p + 1} with enough words to be text.", 9, new PdfPoint(30, 560 - i * 12), font);
        }
        return builder.Build();
    }

    private async Task<Ebook> PdfBookAsync()
    {
        var owner = await _kit.UserAsync("owner");
        var book = await _kit.BookAsync("Reflow Book", owner.Id);
        var file = book.Files[0];
        var full = _kit.Storage.ResolvePath(EbookSource.Upload, file.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, TextPdf());
        book.DiagramState = EbookDiagramState.Done;
        _kit.Db.EbookDiagrams.Add(new EbookDiagram
        {
            EbookId = book.Id, Page = 1, X = 0.3, Y = 0.6, W = 0.4, H = 0.27, Fen = "6k1/8/8/8/8/8/8/R5K1 w - - 0 1", Label = "1", FontFamily = "Marroquin",
        });
        await _kit.Db.SaveChangesAsync();
        return book;
    }

    private EbookReflowService Reflow() => new(_kit.Db, _kit.Storage, NullLogger<EbookReflowService>.Instance);

    [Fact]
    public async Task Service_StoresDerivedEpub_LinksDiagrams_AndReplacesOnRerun()
    {
        var book = await PdfBookAsync();
        var r = await Reflow().ConvertAsync(book.Id, CancellationToken.None);
        Assert.Equal(1, r.DiagramsLinked);
        var epub = Assert.Single(_kit.Db.EbookFiles.Where(f => f.EbookId == book.Id && f.Format == "epub"));
        Assert.True(epub.Derived);
        Assert.Equal(EbookStorage.ConvertedEpubPath(book.Id), epub.RelativePath);
        Assert.True(File.Exists(_kit.Storage.ResolveFile(book, epub)));
        var d = _kit.Db.EbookDiagrams.Single(x => x.EbookId == book.Id);
        Assert.Equal("OEBPS/images/p0001-1.svg", d.ImageRef);
        Assert.Equal(2, d.EpubChapter);
        // Der Leser nimmt jetzt das EPUB (bevorzugtes Format), das PDF bleibt lesbar.
        Assert.Equal("epub", EbookFormats.Primary(book.Files)!.Format);

        await Reflow().ConvertAsync(book.Id, CancellationToken.None);
        Assert.Single(_kit.Db.EbookFiles.Where(f => f.EbookId == book.Id && f.Format == "epub"));

        // Buch löschen räumt die abgeleitete Datei mit ab.
        var path = _kit.Storage.ResolveFile(book, epub);
        await _kit.Service.DeleteAsync(book.OwnerUserId!.Value, false, book.Id);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Service_RejectsBooksWithARealEpub()
    {
        var book = await PdfBookAsync();
        book.Files.Add(new EbookFile { Format = "epub", RelativePath = "files/x.epub", FileName = "x.epub", SizeBytes = 1 });
        await _kit.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => Reflow().ConvertAsync(book.Id, CancellationToken.None));
        Assert.Equal(ApiErrorCodes.EbookReflowUnsupported, ex.Code);
    }

    [Fact]
    public async Task Service_RejectsScansWithoutText()
    {
        var book = await PdfBookAsync();
        await File.WriteAllBytesAsync(_kit.Storage.ResolveFile(book, book.Files[0]), EbookTestKit.TinyPdf());
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => Reflow().ConvertAsync(book.Id, CancellationToken.None));
        Assert.Equal(ApiErrorCodes.EbookReflowUnsupported, ex.Code);
        Assert.Empty(_kit.Db.EbookFiles.Where(f => f.EbookId == book.Id && f.Format == "epub"));
    }

    [Fact]
    public void Primary_PrefersARealEpubOverTheDerivedOne_ButTheDerivedPdf()
    {
        var real = new EbookFile { Format = "epub" };
        var derived = new EbookFile { Format = "epub", Derived = true };
        Assert.Same(real, EbookFormats.Primary([derived, real]));
        Assert.Same(real, EbookFormats.Primary([real, derived]));
        var pdf = new EbookFile { Format = "pdf" };
        var pdfDerived = new EbookFile { Format = "pdf", Derived = true };
        Assert.Same(pdfDerived, EbookFormats.Primary([pdf, pdfDerived]));
        Assert.Same(derived, EbookFormats.Primary([pdf, derived]));
    }
}
