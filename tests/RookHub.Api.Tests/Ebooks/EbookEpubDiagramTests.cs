using System.IO.Compression;
using System.Text;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookEpubDiagramTests
{
    [Fact]
    public void Parse_InlineTagsKeepMovesTogether_BlocksSplit_ImagesResolved()
    {
        const string html = """
            <html><head><title>T</title><style>p { color: red }</style></head><body>
            <p>33.<span class="f">♖</span>d1! Now</p><div><img src="../Images/d%201.jpg" alt=""/></div>
            <p>White&#160;to move</p><svg><image xlink:href="pic.png"/></svg><img src="data:image/png;base64,AAA"/>
            </body></html>
            """;
        var b = EbookEpubContent.Parse(3, "OEBPS/Text/", html);
        Assert.Equal(new[] { "33.♖d1! Now", null, "White to move", null }, b.Select(x => x.Text).ToArray());
        Assert.Equal("OEBPS/Images/d 1.jpg", b[1].Image);
        Assert.Equal("OEBPS/Text/pic.png", b[3].Image);
        Assert.All(b, x => Assert.Equal(3, x.Spine));
        Assert.Equal([0, 1, 2, 3], b.Select(x => x.Order).ToArray());
    }

    [Theory]
    [InlineData("OEBPS/", "../a.jpg", "a.jpg")]
    [InlineData("OEBPS/Text/", "./img/b.png", "OEBPS/Text/img/b.png")]
    [InlineData("OEBPS/Text/", "/root.png", "root.png")]
    public void Resolve_RelativePaths(string dir, string href, string expected) => Assert.Equal(expected, EbookEpubContent.Resolve(dir, href));

    [Fact]
    public void Read_FollowsContainerOpfAndSpineOrder()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string name, string text) { using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8); w.Write(text); }
            Add("META-INF/container.xml", """<?xml version="1.0"?><container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OPS/book.opf"/></rootfiles></container>""");
            Add("OPS/book.opf", """<?xml version="1.0"?><package xmlns="http://www.idpf.org/2007/opf"><manifest><item id="a" href="ch%201.xhtml"/><item id="b" href="ch2.xhtml"/></manifest><spine><itemref idref="b"/><itemref idref="a"/></spine></package>""");
            Add("OPS/ch 1.xhtml", "<p>eins</p>");
            Add("OPS/ch2.xhtml", "<p>zwei</p><img src='i.png'/>");
        }
        ms.Position = 0;
        using var read = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Equal(["OPS/ch2.xhtml", "OPS/ch 1.xhtml"], EbookEpubContent.Spine(read));
        var blocks = EbookEpubContent.Read(read);
        Assert.Equal(new[] { "zwei", null, "eins" }, blocks.Select(x => x.Text).ToArray());
        Assert.Equal("OPS/i.png", blocks[1].Image);
        Assert.Equal([1, 1, 2], blocks.Select(x => x.Spine).ToArray());
    }

    private static List<EpubBlock> Blocks(params string?[] items) =>
        items.Select((t, i) => t == "IMG" ? new EpubBlock(1, i, null, $"i{i}.png") : new EpubBlock(1, i, t, null)).ToList();

    [Fact]
    public void GuessSide_FromNeighbouringParagraph()
    {
        Assert.Equal(('b', true), EbookEpubDiagramExtractor.GuessSide(Blocks("x", "IMG", "Black to play and win"), 1));
        Assert.Equal(('w', true), EbookEpubDiagramExtractor.GuessSide(Blocks("Weiß am Zug", "IMG", "text"), 1));
        Assert.Equal(('w', false), EbookEpubDiagramExtractor.GuessSide(Blocks("x", "IMG", "y"), 1));
        // Nicht über ein anderes Bild hinweg.
        Assert.Equal(('w', false), EbookEpubDiagramExtractor.GuessSide(Blocks("x", "IMG", "IMG", "Black to move"), 1));
    }

    [Fact]
    public void GuessLabel_NumberParagraphBeforeImage()
    {
        Assert.Equal("4", EbookEpubDiagramExtractor.GuessLabel(Blocks("4", "MIKHALCHISHIN - ASEEV", "Nikolaev 1983", "IMG"), 3));
        Assert.Equal("12", EbookEpubDiagramExtractor.GuessLabel(Blocks("Exercise 12", "IMG"), 1));
        Assert.Null(EbookEpubDiagramExtractor.GuessLabel(Blocks("33.Rd1!", "IMG", "Now"), 1));
    }

    [Fact]
    public void Math_ConstantImagesStayConstant_MedianOfEvenCountAverages()
    {
        var a = new float[50, 37];
        for (var y = 0; y < 50; y++) for (var x = 0; x < 37; x++) a[y, x] = 0.25f;
        var r = BoardImageMath.AreaResize(a, 32, 32);
        Assert.All(r.Cast<float>(), v => Assert.Equal(0.25f, v, 4));
        Assert.All(BoardImageMath.Blur(r, 1.6).Cast<float>(), v => Assert.Equal(0.25f, v, 4));
        var m = BoardImageMath.Median([new float[1, 1] { { 0f } }, new float[1, 1] { { 1f } }]);
        Assert.Equal(0.5f, m[0, 0]);
        var s = BoardImageMath.Shift(new float[2, 2] { { 0, 0 }, { 0, 0 } }, 1, 0);
        Assert.Equal(1f, s[0, 0]);   // von außen nachgeschoben = Weiß
        Assert.Equal(0f, s[1, 0]);
    }

    [Fact]
    public void FindBoard_PeelsFrameAndWhiteMargin()
    {
        // 300×300 weiß, Brett 240×240 bei (30, 30) mit 2-Pixel-Rahmen, Felder hell/dunkel.
        var g = new float[300, 300];
        for (var y = 0; y < 300; y++) for (var x = 0; x < 300; x++) g[y, x] = 1f;
        for (var y = 30; y < 270; y++)
            for (var x = 30; x < 270; x++)
            {
                var frame = y < 32 || y >= 268 || x < 32 || x >= 268;
                var dark = ((y - 32) / 29 + (x - 32) / 29) % 2 == 1;
                g[y, x] = frame ? 0f : dark ? 0.6f : 0.85f;
            }
        var box = EbookBoardImage.FindBoard(g);
        Assert.Equal((32, 32, 268, 268), box);
        Assert.Equal(64, EbookBoardImage.Cells(g, box!.Value).Length);
    }

    [Fact]
    public void FindBoard_RejectsNonSquareImages()
    {
        var g = new float[100, 300];
        for (var y = 0; y < 100; y++) for (var x = 0; x < 300; x++) g[y, x] = 0.5f;
        Assert.Null(EbookBoardImage.FindBoard(g));
    }

    [Fact]
    public void Classifier_EmbeddedModelLoadsAndAnswers()
    {
        var m = EbookBoardClassifier.Instance;
        var p = m.Piece(new float[576]);
        Assert.InRange(p, 0, 11);
        Assert.InRange(m.EmptyProbability(new float[1152]), 0.0, 1.0);
    }
}
