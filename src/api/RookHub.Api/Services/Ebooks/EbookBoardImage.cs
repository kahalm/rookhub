using SkiaSharp;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Liest Schachbretter aus BILDERN (0.775.0, EPUB-Diagramme): Brett im Bild finden, in 8×8 Felder schneiden, je Buch die
/// leeren Felder als Hintergrund lernen (Median), besetzte Felder erkennen und die Figur mit dem
/// <see cref="EbookBoardClassifier"/> bestimmen. Jeder Schritt entspricht der Python-Referenz, mit der das Modell trainiert
/// wurde (<see cref="BoardImageMath"/>) — auch die Schwellen. Ein Feld ist ein 32×32-Graubild (0 = schwarz, 1 = weiß).
/// </summary>
public static class EbookBoardImage
{
    public const int Cell = 32;
    private const string Pieces = "PNBRQKpnbrqk";

    /// <summary>Bild dekodieren und in Grau wandeln (0,299 R + 0,587 G + 0,114 B; Transparenz auf Weiß). <c>null</c> = kein Bild.</summary>
    public static float[,]? DecodeGray(byte[] data, int maxSide = 1600)
    {
        using var bmp = SKBitmap.Decode(data);
        if (bmp == null || bmp.Width < 64 || bmp.Height < 64 || bmp.Width > maxSide || bmp.Height > maxSide) return null;
        var g = new float[bmp.Height, bmp.Width];
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                double a = c.Alpha / 255.0;
                double r = c.Red * a + 255 * (1 - a), gr = c.Green * a + 255 * (1 - a), b = c.Blue * a + 255 * (1 - a);
                g[y, x] = (float)((r * 0.299 + gr * 0.587 + b * 0.114) / 255.0);
            }
        return g;
    }

    /// <summary>Brett-Rechteck (x0, y0, x1, y1): größte dunkle Komponente (8er-Nachbarschaft, &lt; 235/255), annähernd
    /// quadratisch, dann Rahmenlinien und weiße Ränder abschälen. <c>null</c> = kein Brett.</summary>
    public static (int X0, int Y0, int X1, int Y1)? FindBoard(float[,] g)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        const float light = 235f / 255f;
        var lab = new bool[H, W];
        (long Area, int Y0, int Y1, int X0, int X1)? best = null;
        var stack = new Stack<(int, int)>();
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                if (!(g[y, x] < light) || lab[y, x]) continue;
                lab[y, x] = true; stack.Push((y, x));
                int ymin = y, ymax = y, xmin = x, xmax = x;
                while (stack.Count > 0)
                {
                    var (cy, cx) = stack.Pop();
                    if (cy < ymin) ymin = cy; if (cy > ymax) ymax = cy;
                    if (cx < xmin) xmin = cx; if (cx > xmax) xmax = cx;
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            int ny = cy + dy, nx = cx + dx;
                            if (ny >= 0 && ny < H && nx >= 0 && nx < W && g[ny, nx] < light && !lab[ny, nx]) { lab[ny, nx] = true; stack.Push((ny, nx)); }
                        }
                }
                long area = (long)(ymax - ymin + 1) * (xmax - xmin + 1);
                if (best == null || area > best.Value.Area) best = (area, ymin, ymax + 1, xmin, xmax + 1);
            }
        if (best == null) return null;
        var (_, y0, y1, x0, x1) = best.Value;
        int w = x1 - x0, h = y1 - y0;
        if (w < 80 || h < 80 || !(0.85 < (double)w / h && (double)w / h < 1.18)) return null;
        for (var it = 0; it < 40; it++)
        {
            var changed = false;
            foreach (var side in "tblr")
            {
                if (x1 <= x0 || y1 <= y0) return null;
                int n = 0, dark = 0, white = 0;
                void Count(float v) { n++; if (v < 0.5f) dark++; if (v > light) white++; }
                switch (side)
                {
                    case 't': for (var x = x0; x < x1; x++) Count(g[y0, x]); break;
                    case 'b': for (var x = x0; x < x1; x++) Count(g[y1 - 1, x]); break;
                    case 'l': for (var y = y0; y < y1; y++) Count(g[y, x0]); break;
                    default: for (var y = y0; y < y1; y++) Count(g[y, x1 - 1]); break;
                }
                if ((double)dark / n > 0.8 || (double)white / n > 0.97)
                {
                    switch (side) { case 't': y0++; break; case 'b': y1--; break; case 'l': x0++; break; default: x1--; break; }
                    changed = true;
                }
            }
            if (!changed) break;
        }
        w = x1 - x0; h = y1 - y0;
        if (w < 64 || h < 64 || !(0.9 < (double)w / h && (double)w / h < 1.1)) return null;
        return (x0, y0, x1, y1);
    }

    /// <summary>64 Felder (a8 zuerst, zeilenweise), je 6 % Rand weg und auf 32×32 gemittelt.</summary>
    public static float[][,] Cells(float[,] g, (int X0, int Y0, int X1, int Y1) box)
    {
        var outp = new float[64][,];
        for (var r = 0; r < 8; r++)
            for (var c = 0; c < 8; c++)
            {
                double cx0 = box.X0 + (box.X1 - box.X0) * c / 8.0, cx1 = box.X0 + (box.X1 - box.X0) * (c + 1) / 8.0;
                double cy0 = box.Y0 + (box.Y1 - box.Y0) * r / 8.0, cy1 = box.Y0 + (box.Y1 - box.Y0) * (r + 1) / 8.0;
                var m = (cx1 - cx0) * 0.06;
                var sub = BoardImageMath.Sub(g, (int)(cy0 + m), (int)(cy1 - m), (int)(cx0 + m), (int)(cx1 - m));
                outp[r * 8 + c] = BoardImageMath.AreaResize(sub, Cell, Cell);
            }
        return outp;
    }

    /// <summary>Anteil der Pixel im Feldinneren (4…28), die sich vom Hintergrund deutlich (&gt; 0,18) unterscheiden, und die
    /// größte Abweichung — beides nach Weichzeichnen (σ 1,6), damit leichte Druck-/Kompressionsunterschiede nicht zählen.</summary>
    public static (double Frac, float Max) Dist(float[,] cell, float[,] blurredBg)
    {
        var a = BoardImageMath.Blur(cell, 1.6);
        int n = 0, hit = 0; float max = 0;
        for (var y = 4; y < 28; y++)
            for (var x = 4; x < 28; x++)
            {
                var d = Math.Abs(a[y, x] - blurredBg[y, x]);
                n++; if (d > 0.18f) hit++; if (d > max) max = d;
            }
        return ((double)hit / n, max);
    }

    public static bool OccupiedStrong((double Frac, float Max) d) => d.Frac > 0.06;
    public static bool OccupiedWeak((double Frac, float Max) d) => d.Max > 0.12f;

    /// <summary>Hintergrund je Feldposition: erst Median der hellen/dunklen Felder (Parität), damit die sicher besetzten
    /// Felder auffallen; dann Median nur über die leeren Felder dieser Position (genug davon), sonst über alle.</summary>
    public static float[][,] Backgrounds(IReadOnlyList<float[][,]> boards)
    {
        static bool Even(int i) => (i / 8 + i % 8) % 2 == 0;
        var light = BoardImageMath.Median(boards.SelectMany(b => Enumerable.Range(0, 64).Where(Even).Select(i => b[i])).ToList());
        var dark = BoardImageMath.Median(boards.SelectMany(b => Enumerable.Range(0, 64).Where(i => !Even(i)).Select(i => b[i])).ToList());
        var flip = Mean(light) < Mean(dark);
        var bLight = BoardImageMath.Blur(flip ? dark : light, 1.6);
        var bDark = BoardImageMath.Blur(flip ? light : dark, 1.6);
        var pos = new float[64][,];
        for (var i = 0; i < 64; i++)
        {
            var par = Even(i) ? bLight : bDark;
            var empty = boards.Select(b => b[i]).Where(c => !OccupiedStrong(Dist(c, par))).ToList();
            var pool = empty.Count >= Math.Max(5, boards.Count * 0.3) ? empty : boards.Select(b => b[i]).ToList();
            pos[i] = BoardImageMath.Median(pool);
        }
        return pos;
    }

    /// <summary>Merkmal für die Figurenart: nur der Vordergrund (Abweichung vom Hintergrund, um 1 Pixel erweitert), auf den
    /// Tintenschwerpunkt zentriert, 24×24, Tinte = 1.</summary>
    public static float[] Feature1(float[,] c, float[,] bg)
    {
        var a = BoardImageMath.Blur(c, 1.0); var b = BoardImageMath.Blur(bg, 1.0);
        var mask = new bool[Cell, Cell];
        for (var y = 0; y < Cell; y++) for (var x = 0; x < Cell; x++) mask[y, x] = Math.Abs(a[y, x] - b[y, x]) > 0.2f;
        var fg = new float[Cell, Cell];
        double s = 0, sy = 0, sx = 0;
        for (var y = 0; y < Cell; y++)
            for (var x = 0; x < Cell; x++)
            {
                var on = mask[y, x] || (y > 0 && mask[y - 1, x]) || (y < Cell - 1 && mask[y + 1, x]) || (x > 0 && mask[y, x - 1]) || (x < Cell - 1 && mask[y, x + 1]);
                fg[y, x] = on ? c[y, x] : 1f;
                double ink = 1.0 - fg[y, x]; s += ink; sy += ink * y; sx += ink * x;
            }
        if (s > 1) fg = BoardImageMath.Shift(fg, 15.5 - sy / s, 15.5 - sx / s);
        for (var y = 0; y < Cell; y++) for (var x = 0; x < Cell; x++) fg[y, x] = Math.Clamp(fg[y, x], 0f, 1f);
        var r = BoardImageMath.AreaResize(fg, 24, 24);
        var f = new float[576];
        for (var y = 0; y < 24; y++) for (var x = 0; x < 24; x++) f[y * 24 + x] = 1f - r[y, x];
        return f;
    }

    /// <summary>Merkmal für „leer oder Figur“: das Feld selbst und seine Abweichung vom Hintergrund, je 24×24.</summary>
    public static float[] Feature2(float[,] c, float[,] bg)
    {
        var raw = BoardImageMath.AreaResize(c, 24, 24);
        var a = BoardImageMath.Blur(c, 0.8); var b = BoardImageMath.Blur(bg, 0.8);
        var d = new float[Cell, Cell];
        for (var y = 0; y < Cell; y++) for (var x = 0; x < Cell; x++) d[y, x] = Math.Clamp(0.5f + (a[y, x] - b[y, x]), 0f, 1f);
        var dr = BoardImageMath.AreaResize(d, 24, 24);
        var f = new float[1152];
        for (var y = 0; y < 24; y++)
            for (var x = 0; x < 24; x++) { f[y * 24 + x] = 1f - raw[y, x]; f[576 + y * 24 + x] = (dr[y, x] - 0.5f) * 2f; }
        return f;
    }

    /// <summary>Stellung eines Bretts als FEN-Brettteil (a8 zuerst) — oder <c>null</c>, wenn sie nicht plausibel ist (je genau
    /// ein König, keine Bauern auf der 1./8. Reihe): dann war es kein Diagramm oder ein unbekannter Figurensatz.</summary>
    public static string? Recognise(float[][,] cells, float[][,] backgrounds, float[][,] blurredBackgrounds, EbookBoardClassifier model)
    {
        var grid = new char[64];
        for (var i = 0; i < 64; i++)
        {
            grid[i] = '.';
            var d = Dist(cells[i], blurredBackgrounds[i]);
            var strong = OccupiedStrong(d);
            if (!strong && !OccupiedWeak(d)) continue;
            if (!strong && model.EmptyProbability(Feature2(cells[i], backgrounds[i])) > 0.5) continue;
            grid[i] = Pieces[model.Piece(Feature1(cells[i], backgrounds[i]))];
        }
        if (grid.Count(c => c == 'K') != 1 || grid.Count(c => c == 'k') != 1) return null;
        for (var i = 0; i < 8; i++) if (grid[i] is 'P' or 'p' || grid[56 + i] is 'P' or 'p') return null;
        var rows = new List<string>();
        for (var r = 0; r < 8; r++)
        {
            var row = new System.Text.StringBuilder(); var e = 0;
            for (var c = 0; c < 8; c++)
            {
                var ch = grid[r * 8 + c];
                if (ch == '.') { e++; continue; }
                if (e > 0) { row.Append(e); e = 0; }
                row.Append(ch);
            }
            if (e > 0) row.Append(e);
            rows.Add(row.ToString());
        }
        return string.Join('/', rows);
    }

    private static double Mean(float[,] a) { double s = 0; foreach (var v in a) s += v; return s / a.Length; }
}
