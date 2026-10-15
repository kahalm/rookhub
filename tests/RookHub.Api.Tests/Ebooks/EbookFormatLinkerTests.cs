using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookFormatLinkerTests
{
    private const string A = "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1";
    private const string B = "r3k3/8/8/8/8/8/8/R3K3 w - - 0 1";
    private const string C = "8/8/3k4/8/8/3K4/3P4/8 w - - 0 1";
    private const string Cmisread = "8/8/3k4/8/8/3K4/3B4/8 w - - 0 1";   // 1 Feld falsch gelesen

    private static EbookDiagram Pdf(int page, string fen) => new() { Page = page, Fen = fen, W = 0.3 };
    private static EbookDiagram Epub(int chapter, string fen, string img) => new() { Page = chapter, Fen = fen, ImageRef = img };

    [Fact]
    public void Similarity_CountsEqualSquares()
    {
        Assert.Equal(1.0, EbookFormatLinker.Similarity(A, A));
        Assert.Equal(2 / 3.0, EbookFormatLinker.Similarity(C, Cmisread), 6);
        Assert.Equal(0, EbookFormatLinker.Similarity(B, "8/8/8/8/8/8/8/8 w - - 0 1"));   // leeres Brett ≠ Endspiel
        Assert.Equal(0, EbookFormatLinker.Similarity(A, "kaputt"));
    }

    [Fact]
    public void Link_FollowsTheBookOrder_ToleratesMisreads_SkipsExtraAndMissingImages()
    {
        var pdf = new List<EbookDiagram> { Pdf(10, A), Pdf(11, B), Pdf(12, C) };
        var epub = new List<EbookDiagram>
        {
            Epub(3, A, "img/a.png"),
            Epub(3, "8/8/8/8/8/8/8/8 w - - 0 1", "img/titelbild.png"),   // kein Diagramm des PDF
            Epub(4, Cmisread, "img/c.png"),                              // B fehlt im EPUB, C falsch gelesen
        };
        Assert.Equal(2, EbookFormatLinker.Link(pdf, epub));
        Assert.Equal(("img/a.png", 3), (pdf[0].ImageRef, pdf[0].EpubChapter));
        Assert.Null(pdf[1].ImageRef);
        Assert.Equal(("img/c.png", 4), (pdf[2].ImageRef, pdf[2].EpubChapter));
        Assert.Equal(10, pdf[0].Page);    // PDF-Lage bleibt
    }
}
