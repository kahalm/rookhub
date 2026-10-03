namespace RookHub.Api.Services.Club;

/// <summary>
/// Der Kreis ums GESICHT im Bild eines Karteiblatts (Wunsch 2026-10-03: „mit einem Kreis sein Gesicht auswählen — das soll
/// beim Abhaken vorn beim Namen dabei sein"). Gespeichert wird er UNABHÄNGIG von der Pixelgröße: der Mittelpunkt als Anteil
/// von Breite und Höhe (0..1), der Radius als Anteil der KÜRZEREN Seite (höchstens 0,5 = der größte Kreis, der ins Bild
/// passt). Aus dem Quadrat um den Kreis entsteht das Vorschaubild; rund gezeigt wird es erst im Browser.
///
/// <para>Die Regel steht hier EINMAL für den Server; ihr Spiegel im Browser ist <c>clampFace</c> in
/// <c>src-clubhub/app/core/face.ts</c> — beide Seiten haben einen Test mit denselben LITERALEN Werten
/// (<c>ClubFaceTests</c> ↔ <c>face.spec.ts</c>). Wer hier etwas ändert, ändert es dort.</para>
/// </summary>
public static class ClubFace
{
    /// <summary>Kleinster Radius (Anteil der kürzeren Seite) — darunter wäre das Vorschaubild ein Pixelbrei.</summary>
    public const double MinR = 0.05;
    /// <summary>Größter Radius: der Kreis berührt beide Ränder der kürzeren Seite.</summary>
    public const double MaxR = 0.5;

    /// <summary>
    /// Den Kreis ins Bild holen: Radius in die Grenzen, Mittelpunkt so weit vom Rand, dass der ganze Kreis im Bild liegt.
    /// <c>null</c>, wenn die Angaben keine Zahlen sind oder das Bild keine Fläche hat.
    /// </summary>
    public static (double X, double Y, double R)? Normalize(double x, double y, double r, int width, int height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(r) || width <= 0 || height <= 0) return null;
        var shorter = Math.Min(width, height);
        r = Math.Clamp(r, MinR, MaxR);
        var radius = r * shorter;
        var cx = Math.Clamp(x * width, radius, width - radius);
        var cy = Math.Clamp(y * height, radius, height - radius);
        return (Round(cx / width), Round(cy / height), Round(r));
    }

    /// <summary>Das Quadrat um den (schon ins Bild geholten) Kreis in Pixeln — immer ganz im Bild, mindestens 1 px.</summary>
    public static (int Left, int Top, int Size) Square(double x, double y, double r, int width, int height)
    {
        var shorter = Math.Min(width, height);
        var size = Math.Clamp((int)Math.Round(2 * r * shorter), 1, shorter);
        var left = Math.Clamp((int)Math.Round(x * width - size / 2.0), 0, width - size);
        var top = Math.Clamp((int)Math.Round(y * height - size / 2.0), 0, height - size);
        return (left, top, size);
    }

    /// <summary>Vier Nachkommastellen genügen (ein Zehntel Pixel bei 1200 px) und halten die Werte lesbar.</summary>
    private static double Round(double v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}
