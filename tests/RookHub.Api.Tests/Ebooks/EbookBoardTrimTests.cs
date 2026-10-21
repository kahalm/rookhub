using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary><see cref="EbookBoardImage.TrimToFrame"/>: ein zum Quadrat ergänztes Rechteck wird bis an den Brettrahmen gekürzt,
/// echte Brettreihen bleiben drin (0.815.3, Flear „Open Ruy Lopez“).</summary>
public class EbookBoardTrimTests
{
    /// <summary>Weiße Seite mit einem gerahmten, schraffierten 8×8-Brett: <paramref name="w"/> breit, <paramref name="h"/> hoch, links oben bei (40, 40).</summary>
    private static float[,] Page(int w, int h)
    {
        var g = new float[h + 200, w + 200];
        for (var y = 0; y < g.GetLength(0); y++) for (var x = 0; x < g.GetLength(1); x++) g[y, x] = 1f;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var frame = y < 3 || y >= h - 3 || x < 3 || x >= w - 3;
                var dark = (y * 8 / h + x * 8 / w) % 2 == 1 && (x + y) % 4 < 2;   // Schraffur der dunklen Felder
                g[40 + y, 40 + x] = frame || dark ? 0f : 1f;
            }
        return g;
    }

    [Fact]
    public void TrimToFrame_CutsWhiteStripBelowWideBoard()
    {
        var g = Page(400, 360);                        // breiter als hoch — Suche hat auf 400 × 400 ergänzt
        var box = (40, 40, 440, 440);
        Assert.Equal((40, 40, 440, 400), EbookBoardImage.TrimToFrame(g, box));
    }

    [Fact]
    public void TrimToFrame_LeavesExactBoxAlone()
    {
        var g = Page(400, 400);
        var box = (40, 40, 440, 440);
        Assert.Equal(box, EbookBoardImage.TrimToFrame(g, box));
    }
}
