using System.Collections.Concurrent;
using System.Reflection;
using SkiaSharp;
using Svg.Skia;

namespace RookHub.Api.Services.Og;

/// <summary>
/// Rendert aus einer FEN ein Brett-Bild (PNG 1200×630) für Link-Vorschauen (Open Graph / Twitter Card).
/// Bewusst TEXTFREI — Spieler/Ergebnis/„X am Zug" stehen in og:title/og:description, damit hier keine
/// Server-Schrift (fontconfig) nötig ist. Figuren = eingebettete cburnett-SVGs (App-Default), aufs Brett
/// gerastert via Svg.Skia. Ergebnis wird pro (FEN, Orientierung) im Speicher gecacht.
/// </summary>
public class OgImageService
{
    // Zielformat: 1200×630 ist das von allen Plattformen (Reddit/Discord/Signal/Twitter/Teams) erwartete
    // og:image-Seitenverhältnis (1.91:1). Brett quadratisch, mittig, mit Marken-Hintergrund.
    private const int Width = 1200;
    private const int Height = 630;
    private const int Board = 560;          // Kantenlänge des Bretts
    private static readonly int OriginX = (Width - Board) / 2;
    private static readonly int OriginY = (Height - Board) / 2;
    private static readonly float Square = Board / 8f;

    // App-Default-Theme „brown" (siehe board-theme.util.ts).
    private static readonly SKColor Light = SKColor.Parse("#f0d9b5");
    private static readonly SKColor Dark = SKColor.Parse("#b58863");
    private static readonly SKColor Background = SKColor.Parse("#1f1d2b"); // RookHub-Dark
    private static readonly SKColor BorderColor = SKColor.Parse("#3a3750");

    private readonly ILogger<OgImageService> _logger;

    // Piece-SVGs sind unveränderlich → einmal laden, als SKPicture cachen.
    private static readonly ConcurrentDictionary<char, SKPicture?> PieceCache = new();
    // Gerendertes Board-PNG je (FEN|orientation) cachen (Link-Vorschauen sind hochgradig wiederholt).
    private static readonly ConcurrentDictionary<string, byte[]> RenderCache = new();

    public OgImageService(ILogger<OgImageService> logger) => _logger = logger;

    /// <summary>Rendert die Brett-Stellung der FEN als PNG. <paramref name="flip"/>=true zeigt aus Schwarz-Sicht.</summary>
    public byte[] RenderBoard(string fen, bool flip = false)
    {
        var key = $"{fen}|{(flip ? "b" : "w")}";
        if (RenderCache.TryGetValue(key, out var cached)) return cached;

        var png = Render(fen, flip);
        // Cache begrenzen (simple Schutzobergrenze; Vorschau-URLs sind endlich, aber nie unbegrenzt).
        if (RenderCache.Count > 2000) RenderCache.Clear();
        RenderCache[key] = png;
        return png;
    }

    private byte[] Render(string fen, bool flip)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(Background);

        // Dezenter Rahmen ums Brett.
        using (var border = new SKPaint { Color = BorderColor, Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            const int pad = 12;
            canvas.DrawRoundRect(OriginX - pad, OriginY - pad, Board + 2 * pad, Board + 2 * pad, 14, 14, border);
        }

        // Felder.
        for (var rank = 0; rank < 8; rank++)
        {
            for (var file = 0; file < 8; file++)
            {
                var isLight = (rank + file) % 2 == 0;
                using var paint = new SKPaint { Color = isLight ? Light : Dark, Style = SKPaintStyle.Fill };
                var x = OriginX + file * Square;
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
                DrawPiece(canvas, c, displayFile, displayRank);
                col++;
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private void DrawPiece(SKCanvas canvas, char fenChar, int file, int rank)
    {
        var picture = GetPiece(fenChar);
        if (picture is null) return;

        var x = OriginX + file * Square;
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
