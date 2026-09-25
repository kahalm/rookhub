using SkiaSharp;

namespace RookHub.Api.Services;

/// <summary>
/// Bildvorbereitung für Formular-Fotos: aufrecht drehen (EXIF), verkleinern, als JPEG kodieren.
///
/// <para>Handyfotos liegen fast immer QUER in der Datei und tragen die Drehung nur als EXIF-Vermerk —
/// der Browser dreht sie beim Anzeigen, SkiaSharp beim Dekodieren nicht. Ungedreht bekäme das Modell ein
/// Formular auf der Seite liegend. Verkleinert wird, weil ein 12-Megapixel-Foto für die Handschrift nichts
/// bringt, aber ein Vielfaches an Übertragung kostet.</para>
/// </summary>
public static class ScoresheetImage
{
    /// <summary>Welche Upload-Typen angenommen werden (HEIC kann SkiaSharp nicht lesen; iOS wandelt beim
    /// Hochladen über ein Dateifeld ohnehin in JPEG um).</summary>
    public static readonly IReadOnlySet<string> AcceptedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp" };

    /// <summary>Lässt sich das als Bild lesen? (Die Endung/der MIME-Typ allein beweist nichts.)</summary>
    public static bool CanDecode(byte[] data)
    {
        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(data));
            return codec != null && codec.Info.Width > 0 && codec.Info.Height > 0;
        }
        catch { return false; }
    }

    /// <summary>Aufrecht, längste Seite höchstens <paramref name="maxEdge"/> Pixel, JPEG. <c>null</c>, wenn
    /// sich das Bild nicht lesen lässt.</summary>
    public static byte[]? Prepare(byte[] data, int maxEdge, int quality = 88)
    {
        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(data));
            if (codec == null) return null;
            using var decoded = SKBitmap.Decode(codec);
            if (decoded == null) return null;

            using var upright = Orient(decoded, codec.EncodedOrigin);
            var bmp = upright ?? decoded;

            var scale = Math.Min(1.0, (double)maxEdge / Math.Max(bmp.Width, bmp.Height));
            using var resized = scale < 1.0
                ? bmp.Resize(new SKImageInfo((int)Math.Round(bmp.Width * scale), (int)Math.Round(bmp.Height * scale)),
                    SKFilterQuality.High)
                : null;
            using var image = SKImage.FromBitmap(resized ?? bmp);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            return encoded?.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Wendet die EXIF-Drehung an; <c>null</c> = schon aufrecht.</summary>
    private static SKBitmap? Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopLeft:
                return null;
            case SKEncodedOrigin.BottomRight: // 180°
            {
                var dst = new SKBitmap(src.Width, src.Height);
                using var c = new SKCanvas(dst);
                c.RotateDegrees(180, src.Width / 2f, src.Height / 2f);
                c.DrawBitmap(src, 0, 0);
                return dst;
            }
            case SKEncodedOrigin.RightTop: // 90° im Uhrzeigersinn
            {
                var dst = new SKBitmap(src.Height, src.Width);
                using var c = new SKCanvas(dst);
                c.Translate(dst.Width, 0);
                c.RotateDegrees(90);
                c.DrawBitmap(src, 0, 0);
                return dst;
            }
            case SKEncodedOrigin.LeftBottom: // 90° gegen den Uhrzeigersinn
            {
                var dst = new SKBitmap(src.Height, src.Width);
                using var c = new SKCanvas(dst);
                c.Translate(0, dst.Height);
                c.RotateDegrees(270);
                c.DrawBitmap(src, 0, 0);
                return dst;
            }
            default:
                // Gespiegelte Varianten kommen aus Kameras praktisch nicht vor — lieber ungedreht als falsch.
                return null;
        }
    }
}
