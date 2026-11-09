using System.Text.Json;
using SkiaSharp;

namespace RookHub.Api.Services.Og;

/// <summary>Welche Seite das Bild trägt: Farben und Wortmarke (docs/DESIGN.md 3.1 bzw. 8.4, jeweils dunkel).</summary>
public enum OgBrand { RookHub, LeagueHub }

/// <summary>Ein Diagramm einer Karte. <paramref name="LastMove"/> (UCI) färbt Von- und Nach-Feld wie das Brett der App.</summary>
public sealed record OgDiagram(string Fen, bool Flip = false, string? LastMove = null);

/// <summary>Ein Spieler der Karte: Farbscheibe, Name, Elo.</summary>
public sealed record OgPlayer(bool White, string Name, int? Elo = null);

/// <summary>Eine Zeile der Tabellen-Karte (LeagueHub-Begegnung): Brett, Farbe des Spielers, Name, leise Angabe, Wert rechts.</summary>
public sealed record OgTableRow(string Board, bool? White, string Name, string? Meta = null, string? Value = null);

/// <summary>Tabelle einer Karte ohne Diagramm; <paramref name="More"/> = Zeilen, die nicht mehr aufs Bild passten.</summary>
public sealed record OgTable(string? Caption, IReadOnlyList<OgTableRow> Rows, int More = 0);

/// <summary>
/// Inhalt eines Vorschaubilds mit Text (seit 2026-11-09, Wunsch: „passende Info-Vorschaubilder für alles, was öfters geteilt wird").
/// Links das Diagramm (bzw. bis zu sechs Mini-Diagramme), rechts von oben nach unten: Kicker (Art der Seite), Titel, Spieler,
/// Ergebnis, „X am Zug", Zeilen, darunter die Bewertungskurve, unten rechts die Wortmarke. Ohne Diagramm und mit
/// <see cref="Table"/> eine Tabellen-Karte über die volle Breite. Jedes Feld darf fehlen — das Bild entsteht trotzdem.
/// </summary>
public sealed record OgCard
{
    public OgBrand Brand { get; init; } = OgBrand.RookHub;
    public IReadOnlyList<OgDiagram> Boards { get; init; } = [];
    public string? Kicker { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<OgPlayer> Players { get; init; } = [];
    public string? Result { get; init; }
    /// <summary>„Weiß/Schwarz am Zug"-Marke: <c>true</c> = Weiß, <c>false</c> = Schwarz, <c>null</c> = keine.</summary>
    public bool? ToMoveWhite { get; init; }
    /// <summary>Text der Marke (Sprache der Karte); fehlt er, „White to move"/„Black to move".</summary>
    public string? ToMoveText { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = [];
    public IReadOnlyList<double?>? Curve { get; init; }
    public OgTable? Table { get; init; }
}

public partial class OgImageService
{
    private sealed record Palette(SKColor Background, SKColor Frame, SKColor Ink, SKColor Muted, SKColor Accent, SKColor Panel, SKColor Line, string Wordmark);

    // RookHub: der bisherige Marken-Hintergrund, Akzent = Primärfarbe des dunklen Material-Themas (#abc7ff, DESIGN 3.1).
    // Farben als Literale, NICHT über die Felder der anderen Teildatei: die Reihenfolge statischer Initialisierer über
    // partielle Dateien ist nicht festgelegt — vorher standen hier transparente Farben (weißes Bild, unsichtbare Schrift).
    private static readonly Palette RookHubPalette = new(SKColor.Parse("#1f1d2b"), SKColor.Parse("#3a3750"), SKColor.Parse("#ffffff"), SKColor.Parse("#c9c5e0"),
        SKColor.Parse("#abc7ff"), SKColor.Parse("#2a2839"), SKColor.Parse("#3a3750"), "RookHub");

    // LeagueHub dunkel (DESIGN 8.4): --paper, --surface, --ink, --muted, --red (Tiroler Rot), --line.
    private static readonly Palette LeagueHubPalette = new(SKColor.Parse("#0f141b"), SKColor.Parse("#28323e"), SKColor.Parse("#e6ebf1"),
        SKColor.Parse("#97a5b5"), SKColor.Parse("#ff4d63"), SKColor.Parse("#161d26"), SKColor.Parse("#28323e"), "LeagueHub");

    // Zugmarke wie das Brett der App (chessground „last-move", auf beiden Feldfarben sichtbar).
    private static readonly SKColor LastMoveTint = new(155, 199, 0, 105);
    private static readonly SKColor CardLight = SKColor.Parse("#f0d9b5");
    private static readonly SKColor CardDark = SKColor.Parse("#b58863");

    private const int Margin = OriginYConst;
    private const int OriginYConst = (Height - Board) / 2; // 35 — derselbe Rand wie beim Brett-Bild

    /// <summary>Rendert eine Karte als PNG 1200×630 (gecacht je Inhalt).</summary>
    public byte[] RenderCard(OgCard card)
    {
        var key = "card|" + JsonSerializer.Serialize(card);
        if (RenderCache.TryGetValue(key, out var cached)) return cached;
        byte[] png;
        try { png = DrawCard(card); }
        catch (Exception ex)
        {
            // Nie ohne Bild: lieber eine leere Marken-Fläche als ein 500 — der Messenger zeigt sonst gar nichts.
            _logger.LogWarning(ex, "OG: Karte konnte nicht gezeichnet werden.");
            png = DrawCard(new OgCard { Brand = card.Brand });
        }
        if (RenderCache.Count > 2000) RenderCache.Clear();
        RenderCache[key] = png;
        return png;
    }

    private byte[] DrawCard(OgCard card)
    {
        var pal = card.Brand == OgBrand.LeagueHub ? LeagueHubPalette : RookHubPalette;
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(pal.Background);

        // Text rechts vom Diagramm-Bereich; ohne Diagramm über die volle Breite.
        float textLeft;
        var boards = card.Boards.Take(6).ToList();
        if (boards.Count == 0)
        {
            if (card.Brand == OgBrand.LeagueHub)
            {
                // Roter Balken links wie die Aufforderung `.cta` der LeagueHub-Seiten.
                using var bar = new SKPaint { Color = pal.Accent, Style = SKPaintStyle.Fill };
                canvas.DrawRect(0, 0, 12, Height, bar);
            }
            textLeft = Margin + 35;
        }
        else textLeft = DrawBoards(canvas, boards, pal) + 40;

        var right = Width - Margin;
        var width = right - textLeft;
        var bottom = Height - Margin - 44; // darunter die Wortmarke
        var y = (float)Margin;

        if (!string.IsNullOrWhiteSpace(card.Kicker))
        {
            y += 30;
            DrawLine(canvas, card.Kicker!, textLeft, y, 28, 18, width, FontBold.Value, pal.Accent);
            y += 18;
        }
        if (!string.IsNullOrWhiteSpace(card.Title))
        {
            var maxLines = card.Table != null ? 1 : boards.Count >= 5 ? 3 : 2;
            var size = card.Table != null ? 54f : boards.Count >= 5 ? 40f : 50f;
            y = DrawWrapped(canvas, card.Title!, textLeft, y, size, width, maxLines, FontBold.Value, pal.Ink) + 6;
        }
        foreach (var p in card.Players.Take(2))
        {
            y += 54;
            DrawPlayer(canvas, p, textLeft, y, width, pal);
        }
        if (card.Players.Count > 0) y += 8;
        if (!string.IsNullOrWhiteSpace(card.Result))
        {
            y += 50;
            DrawLine(canvas, PrettyResult(card.Result!), textLeft, y, 42, 24, width, FontBold.Value, pal.Accent);
            y += 4;
        }
        if (card.ToMoveWhite is bool white)
        {
            y += 22;
            DrawToMove(canvas, white, card.ToMoveText ?? (white ? "White to move" : "Black to move"), textLeft, y, width, pal);
            y += 56;
        }
        foreach (var line in card.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(card.Table != null ? 1 : 4))
        {
            if (y + 40 > bottom) break;
            y += 38;
            DrawLine(canvas, line, textLeft, y, 28, 18, width, FontRegular.Value, pal.Muted);
        }

        if (card.Table != null) DrawTable(canvas, card.Table, textLeft, y + 26, right, bottom + 30, pal);
        else if (card.Curve is { Count: >= 2 } curve && bottom - (y + 24) >= 120)
            DrawCurve(canvas, curve, SKRect.Create(textLeft, y + 24, width, bottom - (y + 24) - 8));

        // Wortmarke unten rechts — die Marke der Seite, auf der der Link landet.
        using (var mark = new SKPaint { Color = pal.Muted, IsAntialias = true, Typeface = FontBold.Value ?? SKTypeface.Default, TextSize = 26 })
        {
            var w = mark.MeasureText(pal.Wordmark);
            canvas.DrawText(pal.Wordmark, right - w, Height - Margin + 2, mark);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>Zeichnet ein Diagramm (Kantenlänge 560) bzw. ein Raster aus 2–6 Mini-Diagrammen; gibt die rechte Kante zurück.</summary>
    private float DrawBoards(SKCanvas canvas, IReadOnlyList<OgDiagram> boards, Palette pal)
    {
        if (boards.Count == 1)
        {
            DrawDiagram(canvas, boards[0], Margin, Margin, Board, BoardPad, pal);
            return Margin + Board + BoardPad;
        }
        var (cols, size) = boards.Count switch { 2 => (2, 300), <= 4 => (2, 268), _ => (3, 240) };
        const int gap = 24;
        var rows = (boards.Count + cols - 1) / cols;
        var gridH = rows * size + (rows - 1) * gap;
        var top = (Height - gridH) / 2f;
        for (var i = 0; i < boards.Count; i++)
        {
            var x = Margin + i % cols * (size + gap);
            var y = top + i / cols * (size + gap);
            DrawDiagram(canvas, boards[i], x, y, size, 6, pal);
        }
        return Margin + cols * size + (cols - 1) * gap + 6;
    }

    private void DrawDiagram(SKCanvas canvas, OgDiagram d, float x, float y, float size, float pad, Palette pal)
    {
        using (var border = new SKPaint { Color = pal.Frame, Style = SKPaintStyle.Fill, IsAntialias = true })
            canvas.DrawRoundRect(x - pad, y - pad, size + 2 * pad, size + 2 * pad, pad + 2, pad + 2, border);
        var sq = size / 8f;
        var marked = LastMoveSquares(d.LastMove);
        using var paint = new SKPaint { Style = SKPaintStyle.Fill };
        using var tint = new SKPaint { Color = LastMoveTint, Style = SKPaintStyle.Fill };
        for (var rank = 0; rank < 8; rank++)
            for (var file = 0; file < 8; file++)
            {
                paint.Color = (rank + file) % 2 == 0 ? CardLight : CardDark;
                canvas.DrawRect(x + file * sq, y + rank * sq, sq, sq, paint);
                // Feld auf dem Bild → Feld der FEN (0 = a8): gedreht steht h1 oben links.
                var boardFile = d.Flip ? 7 - file : file;
                var boardRank = d.Flip ? 7 - rank : rank;
                if (marked.Contains((boardFile, boardRank))) canvas.DrawRect(x + file * sq, y + rank * sq, sq, sq, tint);
            }

        var rowsFen = (d.Fen ?? string.Empty).Split(' ')[0].Split('/');
        for (var r = 0; r < rowsFen.Length && r < 8; r++)
        {
            var col = 0;
            foreach (var c in rowsFen[r])
            {
                if (char.IsDigit(c)) { col += c - '0'; continue; }
                if (col >= 8) break;
                var displayRank = d.Flip ? 7 - r : r;
                var displayFile = d.Flip ? 7 - col : col;
                DrawPieceAt(canvas, c, x + displayFile * sq, y + displayRank * sq, sq);
                col++;
            }
        }
    }

    /// <summary>Von- und Nach-Feld eines UCI-Zugs als (Linie 0..7, Reihe von oben 0..7 = 8. Reihe zuerst).</summary>
    internal static HashSet<(int File, int Rank)> LastMoveSquares(string? uci)
    {
        var set = new HashSet<(int, int)>();
        if (uci is not { Length: >= 4 }) return set;
        for (var i = 0; i < 4; i += 2)
        {
            var f = uci[i] - 'a';
            var r = uci[i + 1] - '1';
            if (f is < 0 or > 7 || r is < 0 or > 7) return new HashSet<(int, int)>();
            set.Add((f, 7 - r));
        }
        return set;
    }

    private void DrawPieceAt(SKCanvas canvas, char fenChar, float x, float y, float sq)
    {
        var picture = GetPiece(fenChar);
        if (picture is null) return;
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var scale = sq / Math.Max(bounds.Width, bounds.Height);
        var offX = x + (sq - bounds.Width * scale) / 2f - bounds.Left * scale;
        var offY = y + (sq - bounds.Height * scale) / 2f - bounds.Top * scale;
        var matrix = SKMatrix.CreateScaleTranslation(scale, scale, offX, offY);
        canvas.DrawPicture(picture, ref matrix);
    }

    /// <summary>Eine Zeile, die höchstens <paramref name="maxWidth"/> breit wird: erst schrumpft die Schrift bis
    /// <paramref name="minSize"/>, dann wird mit „…" gekürzt.</summary>
    private static void DrawLine(SKCanvas canvas, string text, float x, float y, float size, float minSize, float maxWidth, SKTypeface? face, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, Typeface = face ?? SKTypeface.Default, TextSize = size };
        while (paint.MeasureText(text) > maxWidth && paint.TextSize > minSize) paint.TextSize -= 1;
        canvas.DrawText(Ellipsize(text, paint, maxWidth), x, y, paint);
    }

    private static string Ellipsize(string text, SKPaint paint, float maxWidth)
    {
        if (paint.MeasureText(text) <= maxWidth) return text;
        var s = text;
        while (s.Length > 1 && paint.MeasureText(s + "…") > maxWidth) s = s[..^1];
        return s.TrimEnd() + "…";
    }

    /// <summary>Text in höchstens <paramref name="maxLines"/> Zeilen (Umbruch an Leerzeichen, letzte Zeile mit „…");
    /// zu lange Wörter lassen die Schrift bis 60 % schrumpfen. Gibt die Grundlinie der letzten Zeile zurück.</summary>
    private static float DrawWrapped(SKCanvas canvas, string text, float x, float y, float size, float maxWidth, int maxLines, SKTypeface? face, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, Typeface = face ?? SKTypeface.Default, TextSize = size };
        var lines = Wrap(text, paint, maxWidth, maxLines);
        // Passt der Text (auch die letzte, ggf. mit Rest gefüllte Zeile) nicht, schrumpft die Schrift.
        while (lines.Any(l => paint.MeasureText(l) > maxWidth) && paint.TextSize > size * 0.6f)
        {
            paint.TextSize -= 2;
            lines = Wrap(text, paint, maxWidth, maxLines);
        }
        var lineHeight = paint.TextSize * 1.18f;
        foreach (var line in lines)
        {
            y += lineHeight;
            canvas.DrawText(Ellipsize(line, paint, maxWidth), x, y, paint);
        }
        return y;
    }

    internal static List<string> Wrap(string text, SKPaint paint, float maxWidth, int maxLines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (maxLines <= 1) return [string.Join(' ', words)];
        var lines = new List<string>();
        var current = string.Empty;
        for (var i = 0; i < words.Length; i++)
        {
            var candidate = current.Length == 0 ? words[i] : current + " " + words[i];
            if (current.Length > 0 && paint.MeasureText(candidate) > maxWidth)
            {
                lines.Add(current);
                current = words[i];
                if (lines.Count == maxLines - 1)
                {
                    // Rest in die letzte Zeile — Ellipsize kürzt beim Zeichnen.
                    current = string.Join(' ', words[i..]);
                    break;
                }
            }
            else current = candidate;
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static void DrawDisc(SKCanvas canvas, bool white, float cx, float cy, float r, Palette pal)
    {
        using var fill = new SKPaint { Color = white ? SKColors.White : SKColor.Parse("#111111"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var ring = new SKPaint { Color = white ? pal.Muted : SKColor.Parse("#9e9e9e"), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        canvas.DrawCircle(cx, cy, r, fill);
        canvas.DrawCircle(cx, cy, r, ring);
    }

    private static void DrawPlayer(SKCanvas canvas, OgPlayer p, float x, float y, float width, Palette pal)
    {
        DrawDisc(canvas, p.White, x + 14, y - 13, 13, pal);
        var nameLeft = x + 40;
        var elo = p.Elo is int e ? e.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        float eloWidth = 0;
        using var eloPaint = new SKPaint { Color = pal.Muted, IsAntialias = true, Typeface = FontRegular.Value ?? SKTypeface.Default, TextSize = 30 };
        if (elo != null) eloWidth = eloPaint.MeasureText(elo) + 16;
        using var namePaint = new SKPaint { Color = pal.Ink, IsAntialias = true, Typeface = FontBold.Value ?? SKTypeface.Default, TextSize = 38 };
        var name = string.IsNullOrWhiteSpace(p.Name) ? "?" : p.Name;
        while (namePaint.MeasureText(name) > width - 40 - eloWidth && namePaint.TextSize > 22) namePaint.TextSize -= 1;
        var shown = Ellipsize(name, namePaint, width - 40 - eloWidth);
        canvas.DrawText(shown, nameLeft, y, namePaint);
        if (elo != null) canvas.DrawText(elo, nameLeft + namePaint.MeasureText(shown) + 16, y, eloPaint);
    }

    private static void DrawToMove(SKCanvas canvas, bool white, string text, float x, float y, float width, Palette pal)
    {
        using var paint = new SKPaint { Color = pal.Ink, IsAntialias = true, Typeface = FontBold.Value ?? SKTypeface.Default, TextSize = 34 };
        while (paint.MeasureText(text) > width - 90 && paint.TextSize > 20) paint.TextSize -= 1;
        var pillW = Math.Min(width, paint.MeasureText(text) + 92);
        using (var pill = new SKPaint { Color = pal.Panel, Style = SKPaintStyle.Fill, IsAntialias = true })
            canvas.DrawRoundRect(x, y, pillW, 60, 30, 30, pill);
        using (var ring = new SKPaint { Color = pal.Line, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true })
            canvas.DrawRoundRect(x, y, pillW, 60, 30, 30, ring);
        DrawDisc(canvas, white, x + 34, y + 30, 15, pal);
        canvas.DrawText(text, x + 62, y + 42, paint);
    }

    /// <summary>„1/2-1/2" → „½–½", „1-0" → „1–0".</summary>
    internal static string PrettyResult(string r) => r.Trim() switch
    {
        "1/2-1/2" => "½–½",
        "1-0" => "1–0",
        "0-1" => "0–1",
        var s => s.Replace("1/2", "½"),
    };

    /// <summary>Tabelle der Begegnung: bis 4 Zeilen einspaltig, darüber zweispaltig.</summary>
    private static void DrawTable(SKCanvas canvas, OgTable table, float left, float top, float right, float bottom, Palette pal)
    {
        using (var panel = new SKPaint { Color = pal.Panel, Style = SKPaintStyle.Fill, IsAntialias = true })
            canvas.DrawRoundRect(left - 20, top, right - left + 40 - 20, bottom - top, 10, 10, panel);
        using (var line = new SKPaint { Color = pal.Line, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true })
            canvas.DrawRoundRect(left - 20, top, right - left + 40 - 20, bottom - top, 10, 10, line);

        var y = top + 14;
        if (!string.IsNullOrWhiteSpace(table.Caption))
        {
            y += 26;
            DrawLine(canvas, table.Caption!, left, y, 24, 16, right - left - 20, FontBold.Value, pal.Muted);
            y += 6;
        }
        var rows = table.Rows.ToList();
        if (rows.Count == 0) return;
        var cols = rows.Count > 4 ? 2 : 1;
        var perCol = (rows.Count + cols - 1) / cols;
        var colGap = 40f;
        var colW = (right - left - 20 - (cols - 1) * colGap) / cols;
        var footer = table.More > 0 ? 30f : 0f;
        var rowH = Math.Min(64f, (bottom - y - 12 - footer) / perCol);
        var size = Math.Min(30f, rowH * 0.56f);
        for (var i = 0; i < rows.Count; i++)
        {
            var cx = left + i / perCol * (colW + colGap);
            var baseline = y + (i % perCol + 1) * rowH - (rowH - size) / 2f;
            DrawRow(canvas, rows[i], cx, baseline, colW, size, pal);
        }
        if (table.More > 0)
            DrawLine(canvas, $"+ {table.More}", left, bottom - 16, 22, 16, colW, FontRegular.Value, pal.Muted);
    }

    private static void DrawRow(SKCanvas canvas, OgTableRow row, float x, float y, float width, float size, Palette pal)
    {
        using var boardPaint = new SKPaint { Color = pal.Muted, IsAntialias = true, Typeface = FontBold.Value ?? SKTypeface.Default, TextSize = size * 0.9f };
        canvas.DrawText(row.Board, x, y, boardPaint);
        var cursor = x + size * 1.5f;
        if (row.White is bool white)
        {
            var s = size * 0.72f;
            using var fill = new SKPaint { Color = white ? SKColors.White : SKColor.Parse("#111111"), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var ring = new SKPaint { Color = SKColor.Parse("#97a5b5"), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            var rect = SKRect.Create(cursor, y - s * 0.95f, s, s);
            canvas.DrawRoundRect(rect, 3, 3, fill);
            canvas.DrawRoundRect(rect, 3, 3, ring);
            cursor += s + 14;
        }
        using var valuePaint = new SKPaint { Color = pal.Accent, IsAntialias = true, Typeface = FontBold.Value ?? SKTypeface.Default, TextSize = size };
        var valueW = string.IsNullOrEmpty(row.Value) ? 0 : valuePaint.MeasureText(row.Value) + 12;
        if (!string.IsNullOrEmpty(row.Value)) canvas.DrawText(row.Value, x + width - valueW + 12, y, valuePaint);

        using var metaPaint = new SKPaint { Color = pal.Muted, IsAntialias = true, Typeface = FontRegular.Value ?? SKTypeface.Default, TextSize = size * 0.8f };
        var metaW = string.IsNullOrEmpty(row.Meta) ? 0 : metaPaint.MeasureText(row.Meta) + 12;
        using var namePaint = new SKPaint { Color = pal.Ink, IsAntialias = true, Typeface = FontRegular.Value ?? SKTypeface.Default, TextSize = size };
        var room = x + width - valueW - metaW - cursor - 8;
        var name = Ellipsize(string.IsNullOrWhiteSpace(row.Name) ? "?" : row.Name, namePaint, Math.Max(40, room));
        canvas.DrawText(name, cursor, y, namePaint);
        if (!string.IsNullOrEmpty(row.Meta)) canvas.DrawText(row.Meta, cursor + namePaint.MeasureText(name) + 12, y, metaPaint);
    }
}
