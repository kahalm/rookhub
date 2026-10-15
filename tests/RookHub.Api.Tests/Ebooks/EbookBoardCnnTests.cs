using RookHub.Api.Services.Ebooks;
using SkiaSharp;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Das eingebettete Faltungsnetz liest echte Brett-Ausschnitte (Silman, gescannt) — und die Vorverarbeitung in C#
/// (Flächenmittel auf 256 × 256, Felder zu 32 × 32) passt zum Training in Python.</summary>
public class EbookBoardCnnTests
{
    private static float[,] Load(string name)
    {
        using var bmp = SKBitmap.Decode(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ebook-cnn", name));
        var a = new float[bmp.Height, bmp.Width];
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                a[y, x] = (c.Red * 0.299f + c.Green * 0.587f + c.Blue * 0.114f) / 255f;
            }
        return a;
    }

    [Theory]
    [InlineData("cnn-silman-p025_0.png", "8/8/8/k7/4K3/8/8/8")]
    [InlineData("cnn-silman-p183_0.png", "8/5k2/8/P7/8/8/8/7K")]
    [InlineData("cnn-silman-p501_0.png", "8/4k3/8/1p2Pp2/p7/P1K1P3/1P6/8")]
    public void ReadsScannedBoards(string file, string board)
    {
        var cnn = EbookBoardCnn.Instance;
        Assert.NotNull(cnn);
        var img = Load(file);
        var r = cnn!.Read(img, 0, 0, img.GetLength(1), img.GetLength(0));
        Assert.Equal(board, EbookBoardImage.GridToBoard(r.Grid));
        Assert.InRange(r.MinConfidence, 0.5f, 1f);
    }

    [Fact]
    public void AreaResize_AveragesAreas()
    {
        var src = new float[4, 4];
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++) src[y, x] = x < 2 ? 0 : 1;
        var o = EbookBoardCnn.AreaResize(src, 0, 0, 4, 4, 2, 2);
        Assert.Equal(0f, o[0, 0]); Assert.Equal(1f, o[0, 1]);
    }
}
