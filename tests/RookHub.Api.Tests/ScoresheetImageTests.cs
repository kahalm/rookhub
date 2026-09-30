using System.IO.Compression;
using System.Text;
using RookHub.Api.Services;
using SkiaSharp;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Bildvorbereitung der Formular-Fotos (A6-002): ein Bild, das klein hochkommt und riesig entpackt, darf nicht in
/// voller Größe dekodiert werden — gewöhnliche Fotos kommen dabei unverändert heraus.
/// </summary>
public class ScoresheetImageTests
{
    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bmp = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(width / 4f, height / 4f, width / 2f, height / 2f, paint);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(format, 90);
        return data.ToArray();
    }

    /// <summary>Ein PNG mit 1-Bit-Palette, jede Zeile null: Breite × Höhe nur im Kopf, die Datei bleibt winzig.</summary>
    private static byte[] PaletteBomb(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        var ihdr = new byte[13];
        BigEndian(ihdr, 0, width);
        BigEndian(ihdr, 4, height);
        ihdr[8] = 1;                                                            // 1 Bit je Pixel
        ihdr[9] = 3;                                                            // Palette
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "PLTE", new byte[] { 255, 255, 255, 0, 0, 0 });
        using (var idat = new MemoryStream())
        {
            using (var z = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
            {
                var row = new byte[1 + (width + 7) / 8];                        // Filter 0 + Pixel, alles weiß
                for (var y = 0; y < height; y++) z.Write(row);
            }
            Chunk(png, "IDAT", idat.ToArray());
        }
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void BigEndian(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BigEndian(len, 0, data.Length);
        s.Write(len);
        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(body);
        var crc = new byte[4];
        BigEndian(crc, 0, (int)Crc32(body));
        s.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var c = 0xffffffffu;
        foreach (var b in data)
        {
            c ^= b;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xffffffffu;
    }

    [Fact]
    public void ADecompressionBomb_IsRefusedWithoutDecoding()
    {
        // 20 000 × 20 000 = 400 MP: als Datei ein paar Dutzend KB, dekodiert 1,6 GB — vorher nahm CanDecode es an, und
        // der Worker dekodierte es in voller Größe (bei 60 000² sprengte das den Container).
        var bomb = PaletteBomb(20_000, 20_000);
        Assert.True(bomb.Length < 1024 * 1024, $"{bomb.Length} Byte");
        Assert.Equal((20_000, 20_000), ScoresheetImage.Size(bomb));          // der Kopf ist lesbar …
        Assert.False(ScoresheetImage.CanDecode(bomb));                       // … angenommen wird es nicht
        Assert.Null(ScoresheetImage.Prepare(bomb, ScoresheetScanService.ModelEdge));
    }

    [Fact]
    public void AnOversizedJpeg_IsDecodedScaledDown_APngIsRefused()
    {
        // Mit kleinen Grenzen durchgespielt: 400 × 300 = 120 000 Pixel über dem Dekodier-Deckel von 30 000.
        var jpeg = Encode(400, 300, SKEncodedImageFormat.Jpeg);
        Assert.True(ScoresheetImage.CanDecode(jpeg, 1_000_000, 30_000));
        var scaled = ScoresheetImage.Prepare(jpeg, 2000, 88, 1_000_000, 30_000);
        Assert.NotNull(scaled);
        var (w, h) = ScoresheetImage.Size(scaled!)!.Value;
        Assert.InRange((long)w * h, 1, 30_000);                               // verkleinert dekodiert, nicht verworfen
        Assert.Equal(4.0 / 3, (double)w / h, 1);

        var png = Encode(400, 300, SKEncodedImageFormat.Png);                 // PNG kann nicht verkleinert dekodieren
        Assert.False(ScoresheetImage.CanDecode(png, 1_000_000, 30_000));
        Assert.Null(ScoresheetImage.Prepare(png, 2000, 88, 1_000_000, 30_000));
        Assert.False(ScoresheetImage.CanDecode(jpeg, 100_000, 30_000));      // über dem harten Deckel: nie
    }

    [Fact]
    public void AnOrdinaryPhoto_ComesOutByteIdentical()
    {
        // Unter MaxDecodePixels bleibt alles beim Alten: voll dekodieren, hochwertig verkleinern, JPEG 88.
        var jpeg = Encode(1200, 900, SKEncodedImageFormat.Jpeg);
        byte[] legacy;
        using (var codec = SKCodec.Create(new SKMemoryStream(jpeg)))
        using (var decoded = SKBitmap.Decode(codec))
        using (var resized = decoded.Resize(new SKImageInfo(600, 450), SKFilterQuality.High))
        using (var img = SKImage.FromBitmap(resized))
        using (var data = img.Encode(SKEncodedImageFormat.Jpeg, 88))
            legacy = data.ToArray();
        Assert.Equal(legacy, ScoresheetImage.Prepare(jpeg, 600));
        Assert.True(ScoresheetImage.CanDecode(jpeg));
    }
}
