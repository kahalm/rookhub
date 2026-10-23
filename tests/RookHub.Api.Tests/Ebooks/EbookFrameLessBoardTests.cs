using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Rahmenlose Bretter mit schraffierten dunklen Feldern + hellgraues Papier (0.824.0, Kislik „Applying Logic in
/// Chess“: Internet-Archive-Scan, von ~400 Brettern fand die Erkennung 2).</summary>
public class EbookFrameLessBoardTests
{
    /// <summary>Weiße Seite, darauf ein Brett ohne Rahmen bei (60, 60), Kante <paramref name="size"/>: nur die dunklen Felder
    /// tragen eine dünne Schraffur — Nachbarfelder berühren sich nur an den Ecken.</summary>
    private static float[,] Page(int size, float paper = 1f)
    {
        var g = new float[size + 200, size + 200];
        for (var y = 0; y < g.GetLength(0); y++) for (var x = 0; x < g.GetLength(1); x++) g[y, x] = paper;
        var cell = size / 8;
        for (var r = 0; r < 8; r++)
            for (var c = 0; c < 8; c++)
            {
                if ((r + c) % 2 == 0) continue;
                for (var y = 1; y < cell - 1; y++)
                    for (var x = 1; x < cell - 1; x++)
                        if ((x + y) % 5 == 0) g[60 + r * cell + y, 60 + c * cell + x] = 0.55f * paper;   // Schraffur
            }
        return g;
    }

    [Fact]
    public void FindBoards_FindsAFrameLessHatchedBoard()
    {
        var boxes = EbookBoardImage.FindBoards(Page(160));
        var b = Assert.Single(boxes);
        Assert.InRange(b.X0, 55, 66); Assert.InRange(b.Y0, 55, 66);
        Assert.InRange(b.X1 - b.X0, 150, 172); Assert.InRange(b.Y1 - b.Y0, 150, 172);
    }

    [Fact]
    public void FindBoards_PlainTextLinesAreNoBoard()
    {
        var g = new float[400, 400];
        for (var y = 0; y < 400; y++) for (var x = 0; x < 400; x++) g[y, x] = 1f;
        for (var line = 0; line < 20; line++)
            for (var x = 40; x < 360; x++)
                if (x % 7 != 0) for (var y = 0; y < 6; y++) g[40 + line * 16 + y, x] = 0.2f;   // Textzeilen, 10 px Abstand
        Assert.DoesNotContain(EbookBoardImage.FindBoards(g), b => EbookBoardImage.GridScore(EbookBoardImage.Cells(g, b)) >= EbookScanDiagramExtractor.MinGridScore);
    }

    [Theory]
    [InlineData(0.906f, true)]    // Kislik: zwischen 0.9 und der Schwelle der Brettsuche (0.92) — vorher nicht aufgehellt
    [InlineData(0.94f, true)]
    [InlineData(0.97f, false)]    // weißes Papier bleibt
    public void NormalizePaper_LiftsLightGreyPaperAboveTheBoardThreshold(float paper, bool lifted)
    {
        var g = Page(160, paper);
        var n = EbookScanDiagramExtractor.NormalizePaper(g);
        Assert.Equal(lifted, !ReferenceEquals(g, n));
        if (lifted) Assert.True(n[5, 5] > 235f / 255f);
    }
}
