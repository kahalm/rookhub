using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Bretter im grauen Passepartout mit Koordinaten (0.876.3, Prod #1292 Polgar „Amazing Artist – Dangerous
/// Tactician“): hellblaue JPEG-Bretter in einem Text-PDF ohne Schachfont. Der graue Rand hat den Grauwert der dunklen
/// Felder und hing an der Brett-Komponente — das Raster lag quer über Rand und Koordinaten (GridScore −58), von ~150 Brettern
/// wurde eines gefunden.</summary>
public class EbookPassepartoutBoardTests
{
    /// <summary>Ausschnitt aus Seite 310, wie <see cref="EbookScanDiagramExtractor"/> ihn rendert (300 dpi, grau).</summary>
    private static float[,] Page310()
    {
        var png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ebooks", "polgar-p310-board.png"));
        return EbookBoardImage.DecodeGray(png, maxSide: 4000)!;
    }

    [Fact]
    public void ScanPage_FindsAndReadsTheLightBlueBoardInsideTheGreyFrame()
    {
        var full = EbookScanDiagramExtractor.NormalizePaper(Page310());
        var g = EbookScanDiagramExtractor.PreparePage(full);
        var list = EbookScanDiagramExtractor.ScanPage(g, 310, EbookBoardClassifier.Scan, EbookBoardClassifier.ScanAlt, full, EbookBoardCnn.Instance);
        var c = Assert.Single(list);
        Assert.Equal("rnb2rk1/pp1nqppp/4p3/3pP3/2pP1P2/2NB1N2/PPP3PP/R2QK2R", c.Board);
        Assert.NotNull(c.Confidence);
        Assert.Equal('w', c.Side);   // △ rechts neben der Unterkante des Passepartouts
        // Rechteck = das Brett selbst (ohne Rand und Koordinaten): ~ 2/3 der Rahmenbreite.
        var W = g.GetLength(1);
        Assert.InRange(c.W * W, 360, 395);
    }

    [Fact]
    public void LightExtent_ShrinksToTheLightSquares()
    {
        var g = EbookScanDiagramExtractor.PreparePage(Page310());
        var box = Assert.Single(EbookBoardImage.FindBoards(g));
        Assert.True(EbookBoardImage.GridScore(EbookBoardImage.Cells(g, box)) < EbookScanDiagramExtractor.MinGridScore);
        var inner = EbookBoardImage.LightExtent(g, box);
        Assert.NotNull(inner);
        var (x0, y0, x1, y1) = inner.Value;
        Assert.InRange((double)(x1 - x0) / (box.X1 - box.X0), 0.7, 0.85);
        Assert.InRange((double)(x1 - x0) / (y1 - y0), 0.95, 1.05);
        Assert.True(EbookBoardImage.GridScore(EbookBoardImage.Cells(g, inner.Value)) >= EbookScanDiagramExtractor.MinGridScore);
    }

    [Fact]
    public void LightExtent_FramedBoardWithoutPassepartout_IsNull()
    {
        // Gewöhnliches Brett: schwarze Rahmenlinie, helle Felder bis an den Rahmen — nichts abzuschneiden.
        var g = new float[200, 200];
        for (var y = 0; y < 200; y++) for (var x = 0; x < 200; x++) g[y, x] = 1f;
        for (var y = 20; y < 180; y++)
            for (var x = 20; x < 180; x++)
            {
                var edge = y < 22 || y >= 178 || x < 22 || x >= 178;
                g[y, x] = edge ? 0f : ((y - 22) / 20 + (x - 22) / 20) % 2 == 1 ? 0.6f : 1f;
            }
        Assert.Null(EbookBoardImage.LightExtent(g, (20, 20, 180, 180)));
    }

    [Theory]
    [InlineData(true, 'w')]
    [InlineData(false, 'b')]
    public void BelowRightTriangle_ReadsHollowAndFilledTriangles(bool hollow, char side)
    {
        // Brett 0…400, Dreieck (Spitze oben bzw. unten) rechts neben der Unterkante.
        var g = new float[460, 480];
        for (var y = 0; y < 460; y++) for (var x = 0; x < 480; x++) g[y, x] = 1f;
        const int top = 375, size = 20, left = 410;
        for (var r = 0; r < size; r++)
        {
            var half = hollow ? r / 2 : (size - 1 - r) / 2;   // △ wird nach unten breiter, ▼ schmaler
            int cx = left + size / 2, a = cx - half, b = cx + half;
            for (var x = a; x <= b; x++)
            {
                var outline = x <= a + 1 || x >= b - 1 || r >= size - 2;
                if (!hollow || outline) g[top + r, x] = 0f;
            }
        }
        Assert.Equal(side, EbookBoardImage.BelowRightTriangle(g, (0, 0, 400, 400)));
    }

    [Fact]
    public void BelowRightTriangle_TextLettersAreNoMarker()
    {
        var g = new float[460, 480];
        for (var y = 0; y < 460; y++) for (var x = 0; x < 480; x++) g[y, x] = 1f;
        // „o“: Ring, oben und unten gleich breit.
        for (var y = 375; y < 395; y++)
            for (var x = 410; x < 430; x++)
            {
                var d = Math.Sqrt((y - 384.5) * (y - 384.5) + (x - 419.5) * (x - 419.5));
                if (d is > 7 and < 10) g[y, x] = 0f;
            }
        Assert.Null(EbookBoardImage.BelowRightTriangle(g, (0, 0, 400, 400)));
    }

    [Fact]
    public void BelowRightTriangle_TriangleInsideATextLineIsNoMarker()
    {
        // „A“-artige Zeichen in einer Textzeile rechts neben dem Brett: mehrere zeichengroße Teile → kein Marker.
        var g = new float[460, 480];
        for (var y = 0; y < 460; y++) for (var x = 0; x < 480; x++) g[y, x] = 1f;
        for (var k = 0; k < 3; k++)
            for (var r = 0; r < 10; r++)
                for (var x = 0; x <= r; x++) g[380 + r, 408 + k * 14 - r / 2 + x] = 0f;   // gefüllte Dreiecke, 4 px Abstand
        Assert.Null(EbookBoardImage.BelowRightTriangle(g, (0, 0, 400, 400)));
    }
}
