using System.Collections.Concurrent;
using System.Reflection;
using SkiaSharp;
using Svg.Skia;

namespace RookHub.Api.Services.Og;

/// <summary>
/// Rendert aus einer FEN ein Brett-Bild (PNG 1200×630) für Link-Vorschauen (Open Graph / Twitter Card).
/// Bewusst TEXTFREI — Spieler/Ergebnis/„X am Zug" stehen in og:title/og:description, damit hier keine
/// Server-Schrift (fontconfig) nötig ist. Figuren = eingebettete cburnett-SVGs (App-Default), aufs Brett
/// gerastert via Svg.Skia. Ergebnis wird pro (FEN, Orientierung, Kurve) im Speicher gecacht.
/// <para>Mit Bewertungskurve (analysierte Partie, 0.541.0) rückt das Brett nach links und rechts daneben steht die Kurve
/// wie auf der Partieseite (<c>EvalGraphComponent</c>, Lichess-Fläche): hell zwischen Mittellinie und Kurve, wo Weiß vorn
/// liegt, dunkel, wo Schwarz vorn liegt, geglättet — ebenfalls ohne Schrift. Die chess.com-Fläche (Weiß vom unteren Rand)
/// hat der Nutzer auf der Seite abgelehnt (0.521.1).</para>
/// </summary>
public class OgImageService
{
    // Zielformat: 1200×630 ist das von allen Plattformen (Reddit/Discord/Signal/Twitter/Teams) erwartete
    // og:image-Seitenverhältnis (1.91:1). Brett quadratisch, mittig, mit Marken-Hintergrund.
    private const int Width = 1200;
    private const int Height = 630;
    private const int Board = 560;          // Kantenlänge des Bretts
    private const int BoardPad = 12;        // Rahmen ums Brett
    private static readonly int CenteredX = (Width - Board) / 2;
    private static readonly int OriginY = (Height - Board) / 2;
    private static readonly float Square = Board / 8f;
    // Mit Kurve: Brett links (gleicher Abstand zum Rand wie oben/unten), Kurve im Rest rechts, senkrecht mittig.
    private static readonly int LeftX = OriginY;
    private static readonly SKRect GraphRect = SKRect.Create(LeftX + Board + BoardPad + 40, (Height - 300) / 2f,
        Width - OriginY - (LeftX + Board + BoardPad + 40), 300);

    // App-Default-Theme „brown" (siehe board-theme.util.ts).
    private static readonly SKColor Light = SKColor.Parse("#f0d9b5");
    private static readonly SKColor Dark = SKColor.Parse("#b58863");
    private static readonly SKColor Background = SKColor.Parse("#1f1d2b"); // RookHub-Dark
    private static readonly SKColor BorderColor = SKColor.Parse("#3a3750");
    // Farben wie EvalGraphComponent im Client.
    private static readonly SKColor GraphBackground = SKColor.Parse("#4a4a4a");
    private static readonly SKColor GraphWhite = SKColor.Parse("#ececec");
    private static readonly SKColor GraphBlack = SKColor.Parse("#161616");
    private static readonly SKColor GraphMid = new(255, 255, 255, 90);
    private static readonly SKColor GraphLine = SKColor.Parse("#9e9e9e");

    private readonly ILogger<OgImageService> _logger;

    // Piece-SVGs sind unveränderlich → einmal laden, als SKPicture cachen.
    private static readonly ConcurrentDictionary<char, SKPicture?> PieceCache = new();
    // Gerendertes Board-PNG je (FEN|orientation) cachen (Link-Vorschauen sind hochgradig wiederholt).
    private static readonly ConcurrentDictionary<string, byte[]> RenderCache = new();

    public OgImageService(ILogger<OgImageService> logger) => _logger = logger;

    /// <summary>Rendert die Brett-Stellung der FEN als PNG. <paramref name="flip"/>=true zeigt aus Schwarz-Sicht;
    /// <paramref name="curve"/> (Kurvenhöhen 0..100 je Stellung, <c>null</c> = Lücke) stellt die Bewertungskurve daneben.</summary>
    public byte[] RenderBoard(string fen, bool flip = false, IReadOnlyList<double?>? curve = null)
    {
        if (curve is { Count: < 2 }) curve = null;
        var key = $"{fen}|{(flip ? "b" : "w")}"
            + (curve == null ? "" : "|" + string.Join(",", curve.Select(v => v?.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) ?? "")));
        if (RenderCache.TryGetValue(key, out var cached)) return cached;

        var png = Render(fen, flip, curve);
        // Cache begrenzen (simple Schutzobergrenze; Vorschau-URLs sind endlich, aber nie unbegrenzt).
        if (RenderCache.Count > 2000) RenderCache.Clear();
        RenderCache[key] = png;
        return png;
    }

    private byte[] Render(string fen, bool flip, IReadOnlyList<double?>? curve)
    {
        var originX = curve == null ? CenteredX : LeftX;
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(Background);

        // Dezenter Rahmen ums Brett.
        using (var border = new SKPaint { Color = BorderColor, Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(originX - BoardPad, OriginY - BoardPad, Board + 2 * BoardPad, Board + 2 * BoardPad, 14, 14, border);
        }

        // Felder.
        for (var rank = 0; rank < 8; rank++)
        {
            for (var file = 0; file < 8; file++)
            {
                var isLight = (rank + file) % 2 == 0;
                using var paint = new SKPaint { Color = isLight ? Light : Dark, Style = SKPaintStyle.Fill };
                var x = originX + file * Square;
                var y = OriginY + rank * Square;
                canvas.DrawRect(x, y, Square, Square, paint);
            }
        }

        // Figuren aus der FEN.
        var placement = fen.Split(' ')[0];
        var rows = placement.Split('/');
        for (var r = 0; r < rows.Length && r < 8; r++)
        {
            var col = 0;
            foreach (var c in rows[r])
            {
                if (char.IsDigit(c)) { col += c - '0'; continue; }
                if (col >= 8) break;
                // r=0 ist Rang 8 (FEN oben). Bei flip Brett um 180° drehen.
                var displayRank = flip ? 7 - r : r;
                var displayFile = flip ? 7 - col : col;
                DrawPiece(canvas, c, originX, displayFile, displayRank);
                col++;
            }
        }

        if (curve != null) DrawCurve(canvas, curve, GraphRect);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private void DrawPiece(SKCanvas canvas, char fenChar, int originX, int file, int rank)
    {
        var picture = GetPiece(fenChar);
        if (picture is null) return;

        var x = originX + file * Square;
        var y = OriginY + rank * Square;

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // SVG mittig ins Feld skalieren (cburnett ist 45×45).
        var scale = Square / Math.Max(bounds.Width, bounds.Height);
        var drawW = bounds.Width * scale;
        var drawH = bounds.Height * scale;
        var offX = x + (Square - drawW) / 2f - bounds.Left * scale;
        var offY = y + (Square - drawH) / 2f - bounds.Top * scale;

        var matrix = SKMatrix.CreateScaleTranslation(scale, scale, offX, offY);
        canvas.DrawPicture(picture, ref matrix);
    }

    /// <summary>Die Kurve in <paramref name="rect"/>: die Fläche zwischen Mittellinie und Kurve, hell über der Mitte (Weiß
    /// vorn), dunkel darunter (Schwarz vorn) — dieselbe Fläche, einmal auf die obere, einmal auf die untere Hälfte beschnitten.
    /// Lücken übernehmen den Wert davor (am Anfang: ausgeglichen); im Bild gibt es keine Punkte, die sie zeigen könnten.</summary>
    private static void DrawCurve(SKCanvas canvas, IReadOnlyList<double?> curve, SKRect rect)
    {
        var last = 50.0;
        var points = new SKPoint[curve.Count];
        for (var i = 0; i < curve.Count; i++)
        {
            if (curve[i] is double v) last = Math.Clamp(v, 0, 100);
            points[i] = new SKPoint(rect.Left + rect.Width * i / (curve.Count - 1), (float)(rect.Bottom - rect.Height * last / 100));
        }

        canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(rect, 14, 14), antialias: true);
        using (var background = new SKPaint { Color = GraphBackground, Style = SKPaintStyle.Fill })
            canvas.DrawRect(rect, background);
        using var line = SmoothPath(points);
        using (var area = new SKPath(line))
        {
            area.LineTo(points[^1].X, rect.MidY);
            area.LineTo(points[0].X, rect.MidY);
            area.Close();
            foreach (var (half, color) in new[]
                     {
                         (SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height / 2), GraphWhite),
                         (SKRect.Create(rect.Left, rect.MidY, rect.Width, rect.Height / 2), GraphBlack),
                     })
            {
                canvas.Save();
                canvas.ClipRect(half);
                using var fill = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
                canvas.DrawPath(area, fill);
                canvas.Restore();
            }
        }
        using (var mid = new SKPaint { Color = GraphMid, StrokeWidth = 2, Style = SKPaintStyle.Stroke })
            canvas.DrawLine(rect.Left, rect.MidY, rect.Right, rect.MidY, mid);
        using (var stroke = new SKPaint { Color = GraphLine, StrokeWidth = 3, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round })
            canvas.DrawPath(line, stroke);
        canvas.Restore();
    }

    /// <summary>
    /// Monotone kubische Glättung (Fritsch–Carlson) durch die Punkte — wie <c>EvalGraphComponent.smooth</c> im Client: rund
    /// statt kantig, aber ohne Überschwingen über einen Extremwert hinaus (die Kurve erfände sonst Bewertungen).
    /// </summary>
    internal static SKPath SmoothPath(IReadOnlyList<SKPoint> p)
    {
        var path = new SKPath();
        if (p.Count == 0) return path;
        path.MoveTo(p[0]);
        var n = p.Count;
        if (n == 1) return path;
        var d = new float[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            var dx = p[i + 1].X - p[i].X;
            d[i] = dx == 0 ? 0 : (p[i + 1].Y - p[i].Y) / dx;
        }
        var m = new float[n];
        m[0] = d[0];
        m[n - 1] = d[n - 2];
        for (var i = 1; i < n - 1; i++) m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2;
        for (var i = 0; i < n - 1; i++)
        {
            if (d[i] == 0) { m[i] = 0; m[i + 1] = 0; continue; }
            var a = m[i] / d[i];
            var b = m[i + 1] / d[i];
            var s = a * a + b * b;
            if (s > 9)
            {
                var t = 3 / MathF.Sqrt(s);
                m[i] = t * a * d[i];
                m[i + 1] = t * b * d[i];
            }
        }
        for (var i = 0; i < n - 1; i++)
        {
            var h = (p[i + 1].X - p[i].X) / 3;
            path.CubicTo(p[i].X + h, p[i].Y + m[i] * h, p[i + 1].X - h, p[i + 1].Y - m[i + 1] * h, p[i + 1].X, p[i + 1].Y);
        }
        return path;
    }

    /// <summary>Lädt (gecacht) die SKPicture für ein FEN-Figurenzeichen (Großbuchstabe=Weiß).</summary>
    private SKPicture? GetPiece(char fenChar)
    {
        return PieceCache.GetOrAdd(fenChar, ch =>
        {
            var name = FenCharToFile(ch);
            if (name is null) return null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var resource = $"RookHub.Api.Assets.pieces.cburnett.{name}.svg";
                using var stream = asm.GetManifestResourceStream(resource);
                if (stream is null)
                {
                    _logger.LogWarning("OG: Piece-SVG {Resource} nicht gefunden (Embedded Resource?).", resource);
                    return null;
                }
                var svg = new SKSvg();
                svg.Load(stream);
                return svg.Picture;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OG: Piece-SVG für '{Char}' konnte nicht geladen werden.", ch);
                return null;
            }
        });
    }

    /// <summary>FEN-Zeichen → Dateiname des Piece-Sets (z. B. 'N' → wN, 'q' → bQ).</summary>
    internal static string? FenCharToFile(char c)
    {
        var role = char.ToUpperInvariant(c) switch
        {
            'K' => "K", 'Q' => "Q", 'R' => "R", 'B' => "B", 'N' => "N", 'P' => "P",
            _ => null,
        };
        if (role is null) return null;
        var color = char.IsUpper(c) ? "w" : "b";
        return color + role;
    }
}
