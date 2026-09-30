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

    /// <summary>Größtes Bild (Breite × Höhe laut Kopf), das überhaupt angenommen wird. Ohne Deckel dekodierte
    /// <see cref="Prepare"/> jedes Bild in voller Größe: ein PNG mit 1-Bit-Palette und 60 000 × 60 000 Pixeln ist unter
    /// 1 MB groß, entpackt aber 14 GB — der API-Prozess (samt Einlese-Worker) fiel dem OOM-Killer zum Opfer, und nach dem
    /// Neustart kam dieselbe Einlesung bis zu dreimal wieder. 120 MP lassen noch jedes Handyfoto bis 108 MP durch.</summary>
    public const long MaxPixels = 120_000_000;

    /// <summary>Bis hierhin wird in voller Auflösung dekodiert, wie immer (48/50-MP-Handyfotos: bis 200 MB, das
    /// Ergebnis bleibt byte-gleich). Größere Bilder dekodiert der Codec gleich verkleinert (JPEG in Achteln, WebP
    /// beliebig); ein Format, das das nicht kann (PNG), wird oberhalb dieser Grenze abgelehnt.</summary>
    public const long MaxDecodePixels = 50_000_000;

    /// <summary>Lässt sich das als Bild lesen — und ohne übergroße Bitmap? (Die Endung/der MIME-Typ allein beweist
    /// nichts.)</summary>
    public static bool CanDecode(byte[] data) => CanDecode(data, MaxPixels, MaxDecodePixels);

    internal static bool CanDecode(byte[] data, long maxPixels, long maxDecodePixels)
    {
        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(data));
            return codec != null && DecodeInfo(codec, maxPixels, maxDecodePixels) != null;
        }
        catch { return false; }
    }

    /// <summary>In welcher Größe dekodiert wird: das Bild selbst bis <paramref name="maxDecodePixels"/>, darüber die
    /// größte Stufe, die der Codec verkleinert liefern kann und die unter der Grenze bleibt. <c>null</c> = ablehnen
    /// (unlesbar, über <paramref name="maxPixels"/>, oder zu groß und nicht verkleinerbar). Liest nur den Kopf.</summary>
    private static SKImageInfo? DecodeInfo(SKCodec codec, long maxPixels, long maxDecodePixels)
    {
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0) return null;
        var pixels = (long)info.Width * info.Height;
        if (pixels > maxPixels) return null;
        if (pixels <= maxDecodePixels) return info;
        // JPEG rundet auf die nächste Achtel-Stufe AUF — bleibt die über der Grenze, eine Stufe kleiner versuchen.
        // PNG kann nicht verkleinert dekodieren und liefert immer die volle Größe: nach den Versuchen abgelehnt.
        var scale = Math.Sqrt((double)maxDecodePixels / pixels);
        for (var i = 0; i < 16; i++, scale *= 0.9)
        {
            var d = codec.GetScaledDimensions((float)scale);
            if (d.Width > 0 && d.Height > 0 && (long)d.Width * d.Height <= maxDecodePixels)
                return info.WithSize(d.Width, d.Height);
        }
        return null;
    }

    /// <summary>Breite × Höhe laut Bildkopf (ohne zu dekodieren); <c>null</c>, wenn sich das Bild nicht lesen lässt.
    /// Für ein mit <see cref="Prepare"/> erzeugtes JPEG sind das die Maße, die das Modell sieht — es ist schon aufrecht.</summary>
    public static (int Width, int Height)? Size(byte[] data)
    {
        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(data));
            return codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0
                ? null : (codec.Info.Width, codec.Info.Height);
        }
        catch { return null; }
    }

    /// <summary>Aufrecht, längste Seite höchstens <paramref name="maxEdge"/> Pixel, JPEG. <c>null</c>, wenn
    /// sich das Bild nicht lesen lässt oder zu groß ist (<see cref="MaxPixels"/>, <see cref="MaxDecodePixels"/>).</summary>
    public static byte[]? Prepare(byte[] data, int maxEdge, int quality = 88) =>
        Prepare(data, maxEdge, quality, MaxPixels, MaxDecodePixels);

    internal static byte[]? Prepare(byte[] data, int maxEdge, int quality, long maxPixels, long maxDecodePixels)
    {
        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(data));
            if (codec == null) return null;
            if (DecodeInfo(codec, maxPixels, maxDecodePixels) is not { } target) return null;
            using var decoded = target.Width == codec.Info.Width && target.Height == codec.Info.Height
                ? SKBitmap.Decode(codec)
                : DecodeScaled(codec, target);
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

    /// <summary>Wie <see cref="SKBitmap.Decode(SKCodec)"/> (premultipliziert, ohne Farbraum), nur in der verkleinerten
    /// Größe, die <see cref="DecodeInfo"/> gewählt hat.</summary>
    private static SKBitmap? DecodeScaled(SKCodec codec, SKImageInfo target)
    {
        if (target.AlphaType == SKAlphaType.Unpremul) target.AlphaType = SKAlphaType.Premul;
        target.ColorSpace = null;
        return SKBitmap.Decode(codec, target);
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
