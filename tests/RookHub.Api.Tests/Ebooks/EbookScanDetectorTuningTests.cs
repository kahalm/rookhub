using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Detektor-Paket 0.947.0 (Diagnose 10.11.2026): Kontrastsperre 0,03, lokale Papier-Ausleuchtung, unplausible
/// Scan-Bretter als „unread“ behalten.</summary>
public class EbookScanDetectorTuningTests
{
    private static float[,] Page(int h, int w, float v = 1f)
    {
        var g = new float[h, w];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) g[y, x] = v;
        return g;
    }

    /// <summary>Brett ohne Figuren: helle Felder 1, dunkle Felder gleichmäßig 1 − <paramref name="contrast"/>.</summary>
    private static float[,] PaleBoardPage(float contrast)
    {
        var g = Page(320, 320);
        for (var y = 0; y < 256; y++)
            for (var x = 0; x < 256; x++)
                if ((y / 32 + x / 32) % 2 == 1) g[32 + y, 32 + x] = 1f - contrast;
        return g;
    }

    [Theory]
    [InlineData(0.05f, true)]    // blasser Druck (4037/4007): seit 0.947.0 ein Brett
    [InlineData(0.01f, false)]   // unter 0,03: weiter gesperrt (bei 0,01 kam in der Messung der erste Textschnipsel)
    public void GridScore_PaleBoard_AcceptedAboveMinParityContrast(float contrast, bool accepted)
    {
        var g = PaleBoardPage(contrast);
        var score = EbookBoardImage.GridScore(EbookBoardImage.Cells(g, (32, 32, 288, 288)));
        Assert.Equal(accepted, score >= EbookScanDiagramExtractor.MinGridScore);
        Assert.Equal(0.03, EbookBoardImage.MinParityContrast);
    }

    [Fact]
    public void NormalizePaper_EvensOutAnIlluminationGradient_DarkStaysDark()
    {
        // Papier 0,8 links (Bundschatten) → 1,0 rechts, ein dunkles Rechteck (Druck) in der Mitte.
        const int H = 400, W = 600;
        var g = new float[H, W];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var paper = 0.8f + 0.2f * x / (W - 1);
                g[y, x] = x is >= 280 and < 320 && y is >= 150 and < 250 ? 0.1f * paper : paper;
            }
        var n = EbookScanDiagramExtractor.NormalizePaper(g);
        Assert.InRange(n[200, 5], 0.96f, 1f);
        Assert.InRange(n[200, 150], 0.96f, 1f);
        Assert.InRange(n[200, 595], 0.96f, 1f);
        Assert.True(n[200, 300] < 0.2f);
        // Vorher lag der linke Rand unter der Schwelle der Brettsuche (210/255) und verschmolz mit dem Brett.
        Assert.True(g[200, 5] < 210f / 255f);
        Assert.True(n[200, 5] > 235f / 255f);
    }

    [Fact]
    public void FlattenIllumination_BlackPage_FloorKeepsItBlack()
    {
        var g = Page(64, 64, 0.05f);
        var n = EbookScanDiagramExtractor.FlattenIllumination(g);
        Assert.Equal(0.05f / EbookScanDiagramExtractor.IlluminationFloor, n[30, 30], 4);
    }

    [Fact]
    public void GaussReflect_ConstantStaysConstant_MaxFilterTakesWindowMax()
    {
        var c = Page(10, 13, 0.7f);
        var b = EbookScanDiagramExtractor.GaussReflect(c, 8);
        Assert.All(new[] { b[0, 0], b[9, 12], b[5, 6] }, v => Assert.Equal(0.7f, v, 5));
        var a = Page(20, 20, 0f);
        a[10, 10] = 1f;
        var m = EbookScanDiagramExtractor.MaxFilter(a, 5);
        Assert.Equal(1f, m[8, 12]); Assert.Equal(0f, m[7, 10]); Assert.Equal(0f, m[10, 13]);
    }

    [Fact]
    public void ScanRenderedPage_ImplausibleBoard_KeptAsUnread()
    {
        // Leeres Brett (keine Könige → keine plausible Stellung): früher außerhalb des Ergänzen-Modus verworfen.
        var g = Page(800, 800);
        const int cell = 60, x0 = 100, y0 = 100, size = cell * 8;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                if ((y / cell + x / cell) % 2 == 1 && (x + y) % 4 == 0) g[y0 + y, x0 + x] = 0f;
        for (var i = -2; i <= size + 1; i++)
            for (var t = 1; t <= 2; t++)
            {
                g[y0 - t, x0 + i] = 0f; g[y0 + size - 1 + t, x0 + i] = 0f;
                g[y0 + i, x0 - t] = 0f; g[y0 + i, x0 + size - 1 + t] = 0f;
            }
        var list = EbookScanDiagramExtractor.ScanRenderedPage(g, 12, EbookBoardClassifier.Scan, EbookBoardClassifier.ScanAlt, cnn: null);
        var c = Assert.Single(list);
        Assert.Null(c.Board); Assert.Null(c.AltBoard);
        Assert.NotNull(c.RawBoard);
        Assert.InRange(c.X, 0.1, 0.14); Assert.InRange(c.W, 0.57, 0.63);
        var chosen = EbookScanDiagramExtractor.Choose(list, out _, keepUnread: true);
        Assert.True(Assert.Single(chosen).Unread);
    }
}
