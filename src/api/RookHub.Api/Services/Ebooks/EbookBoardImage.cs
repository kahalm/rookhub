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
        if (bmp.ColorType == SKColorType.Gray8)
        {
            // Schneller Weg für Graubilder (gerenderte Scan-Seiten) — dieselbe Formel wie unten mit R = G = B.
            var span = bmp.GetPixelSpan();
            var row = bmp.RowBytes;
            var lut = new float[256];
            for (var k = 0; k < 256; k++) lut[k] = (float)((k * 0.299 + k * 0.587 + k * 0.114) / 255.0);
            for (var y = 0; y < bmp.Height; y++)
                for (var x = 0; x < bmp.Width; x++) g[y, x] = lut[span[y * row + x]];
            return g;
        }
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
        var peeled = Peel(g, x0, y0, x1, y1);
        if (peeled == null) return null;
        (x0, y0, x1, y1) = peeled.Value;
        w = x1 - x0; h = y1 - y0;
        if (w < 64 || h < 64 || !(0.9 < (double)w / h && (double)w / h < 1.1)) return null;
        return (x0, y0, x1, y1);
    }

    /// <summary>Rahmenlinien (Zeile/Spalte zu &gt; 80 % schwarz) und weiße Ränder (&gt; 97 % weiß) abschälen.</summary>
    private static (int X0, int Y0, int X1, int Y1)? Peel(float[,] g, int x0, int y0, int x1, int y1)
    {
        const float light = 235f / 255f;
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
        return x1 > x0 && y1 > y0 ? (x0, y0, x1, y1) : null;
    }

    /// <summary>
    /// Alle Bretter einer gescannten SEITE (0.776.0): jede dunkle Komponente (8er-Nachbarschaft, &lt; 235/255, zweiter Durchgang &lt; 210/255) mit annähernd
    /// quadratischem Rechteck ab 100 px, abgeschält. Fehlt im Scan eine Rahmenlinie, endet die Komponente an der ersten
    /// Schraffur — das Rechteck wird dann auf ein Quadrat ergänzt, auf der Seite, deren Schachbrettmuster am besten passt
    /// (<see cref="GridScore"/>). Ob es wirklich ein Brett ist, entscheidet der Aufrufer über <see cref="GridScore"/>.
    /// </summary>
    public static List<(int X0, int Y0, int X1, int Y1)> FindBoards(float[,] g)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var result = new List<(int X0, int Y0, int X1, int Y1)>();
        var found = new List<((int X0, int Y0, int X1, int Y1) Box, double Fit)>();
        var stack = new Stack<(int, int)>();
        // Zweiter Durchgang strenger (0.797.2): auf grau gerasterten Kapitelseiten (Yusupov) verschmilzt das Brett bei 235
        // mit dem Raster zu einer großen Fläche — bei 210 steht es wieder für sich. Doppelte Treffer räumt das Ende ab.
        foreach (var light in new[] { 235f / 255f, 210f / 255f })
        {
        var lab = new bool[H, W];
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
                int w = xmax + 1 - xmin, h = ymax + 1 - ymin;
                if (w < 100 || h < 100 || !(0.85 < (double)w / h && (double)w / h < 1.18)) continue;
                var p = Peel(g, xmin, ymin, xmax + 1, ymax + 1);
                if (p == null) continue;
                var (x0, y0, x1, y1) = p.Value;
                w = x1 - x0; h = y1 - y0;
                if (w < 96 || h < 96 || !(0.85 < (double)w / h && (double)w / h < 1.18)) continue;
                var d = Math.Abs(w - h);
                var cands = new List<(int, int, int, int)> { (x0, y0, x1, y1) };
                if (w < h) cands = [(Math.Max(0, x0 - d), y0, x1, y1), (x0, y0, Math.Min(W, x1 + d), y1), (Math.Max(0, x0 - d / 2), y0, Math.Min(W, x1 + d - d / 2), y1)];
                else if (h < w) cands = [(x0, Math.Max(0, y0 - d), x1, y1), (x0, y0, x1, Math.Min(H, y1 + d)), (x0, Math.Max(0, y0 - d / 2), x1, Math.Min(H, y1 + d - d / 2))];
                var best = cands[0]; var bestScore = double.MinValue;
                foreach (var c in cands)
                {
                    var sc = GridFit(Cells(g, c));
                    if (sc > bestScore) { bestScore = sc; best = c; }
                }
                found.Add((best, bestScore));
            }
        }
        // Dasselbe Brett zweimal (doppelter Rahmen: äußere Linie + inneres Brett, 0.780.5): von überlappenden Treffern
        // bleibt der mit dem besseren Schachbrettmuster — der äußere liest sonst mit verschobenem Raster lauter Figuren.
        foreach (var (box, _) in found.OrderByDescending(f => f.Fit))
            if (!result.Any(r => Overlap(r, box) > 0.5)) result.Add(box);
        return result.OrderBy(b => b.Y0).ThenBy(b => b.X0).ToList();
    }

    /// <summary>Überdeckung zweier Rechtecke, bezogen auf das kleinere (0–1).</summary>
    internal static double Overlap((int X0, int Y0, int X1, int Y1) a, (int X0, int Y0, int X1, int Y1) b)
    {
        long w = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0), h = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
        if (w <= 0 || h <= 0) return 0;
        long small = Math.Min((long)(a.X1 - a.X0) * (a.Y1 - a.Y0), (long)(b.X1 - b.X0) * (b.Y1 - b.Y0));
        return small <= 0 ? 0 : (double)(w * h) / small;
    }

    /// <summary>
    /// Zugrecht-Marker links neben einem gescannten Brett (Gambit/Batsford: kursives „W“ bzw. „B“), gelesen auf der Seite in
    /// voller Auflösung (300 dpi; <paramref name="box"/> in deren Koordinaten). Im Streifen links (¼ Brettbreite) werden
    /// dunkle Striche, die höchstens 4 px auseinanderliegen, zu Zeichen zusammengefasst (dünne kursive Lettern zerfallen
    /// sonst in Einzelstriche); das größte Zeichen mit 3–12 % der Brettbreite Höhe zählt. „B“ hat geschlossene Innenräume,
    /// „W“ keine — das trennt beide sicher (Hansen/Beim 23 von 23), anders als das Seitenverhältnis allein. Ohne Innenraum
    /// muss das Zeichen deutlich breiter als hoch sein (≥ 1,05), sonst <c>null</c>.
    /// </summary>
    public static char? SideMarker(float[,] g, (int X0, int Y0, int X1, int Y1) box)
    {
        // Dreieck rechts über dem Brett (Quality Chess, z. B. Yusupov): △ hohl = Weiß, ▼ voll = Schwarz (0.797.0).
        if (TriangleMarker(g, box) is char tri) return tri;
        int H = g.GetLength(0);
        var bw = box.X1 - box.X0;
        int sx0 = Math.Max(0, box.X0 - bw / 4), sx1 = box.X0 - 6, sy0 = Math.Max(0, box.Y0), sy1 = Math.Min(H, box.Y1);
        if (sx1 - sx0 < 8 || sy1 <= sy0) return null;
        var comps = Components(g, sx0, sx1, sy0, sy1, 4)
            .Where(c => c.H >= bw * 0.03 && c.H <= bw * 0.12 && c.W <= bw * 0.15).ToList();
        // Randziffern 1–8 untereinander sind Koordinaten, kein Zugrecht (die Löcher von „8“, „6“, „4“ lasen sich als „B“).
        if (comps.Count >= 4) return null;
        var best = comps.OrderByDescending(c => c.Points.Count).FirstOrDefault();
        if (best == null) return null;
        if (Holes(best) > 0) return 'b';
        return (double)best.W / best.H >= 1.05 ? 'w' : null;
    }

    /// <summary>Dreieck rechts oberhalb des Bretts: hohl (Innenraum) = Weiß am Zug, gefüllt = Schwarz; sonst <c>null</c>.</summary>
    internal static char? TriangleMarker(float[,] g, (int X0, int Y0, int X1, int Y1) box)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var bw = box.X1 - box.X0;
        int sx0 = Math.Max(0, box.X1 - bw / 5), sx1 = Math.Min(W, box.X1 + bw / 20), sy0 = Math.Max(0, box.Y0 - (int)(bw * 0.14)), sy1 = box.Y0 - 3;
        if (sx1 - sx0 < 8 || sy1 - sy0 < 8) return null;
        var tri = Components(g, sx0, sx1, sy0, sy1, 1)
            .Where(c => c.H >= bw * 0.025 && c.H <= bw * 0.09 && c.W >= c.H * 0.7 && c.W <= c.H * 1.5)
            .OrderByDescending(c => c.X0).FirstOrDefault();
        if (tri == null) return null;
        // Ein Dreieck füllt sein Rechteck etwa zur Hälfte: gefüllt ≥ 35 %, hohl nur der Umriss — aber mit Innenraum.
        if (Holes(tri) > 0) return 'w';
        return tri.Points.Count >= 0.35 * tri.W * tri.H ? 'b' : null;
    }

    private sealed record Component(List<(int Y, int X)> Points, int X0, int Y0, int W, int H);

    /// <summary>Dunkle Zusammenhangskomponenten im Ausschnitt; Striche bis <paramref name="reach"/> px Abstand gehören zusammen.</summary>
    private static List<Component> Components(float[,] g, int sx0, int sx1, int sy0, int sy1, int reach)
    {
        int sw = sx1 - sx0, sh = sy1 - sy0;
        var ink = new bool[sh, sw];
        for (var y = 0; y < sh; y++) for (var x = 0; x < sw; x++) ink[y, x] = g[sy0 + y, sx0 + x] < 0.5f;
        var seen = new bool[sh, sw];
        var stack = new Stack<(int, int)>();
        var list = new List<Component>();
        for (var y = 0; y < sh; y++)
            for (var x = 0; x < sw; x++)
            {
                if (!ink[y, x] || seen[y, x]) continue;
                var comp = new List<(int Y, int X)>();
                seen[y, x] = true; stack.Push((y, x));
                while (stack.Count > 0)
                {
                    var (cy, cx) = stack.Pop(); comp.Add((cy, cx));
                    for (var ny = Math.Max(0, cy - reach); ny <= Math.Min(sh - 1, cy + reach); ny++)
                        for (var nx = Math.Max(0, cx - reach); nx <= Math.Min(sw - 1, cx + reach); nx++)
                            if (ink[ny, nx] && !seen[ny, nx]) { seen[ny, nx] = true; stack.Push((ny, nx)); }
                }
                int y0 = comp.Min(p => p.Y), x0 = comp.Min(p => p.X);
                list.Add(new Component(comp, x0, y0, comp.Max(p => p.X) - x0 + 1, comp.Max(p => p.Y) - y0 + 1));
            }
        return list;
    }

    /// <summary>Innenräume: weiße Bereiche im (um 1 Pixel gepolsterten) Rechteck, die den Rand nicht erreichen (4er-Nachbarschaft).</summary>
    private static int Holes(Component c)
    {
        int bh = c.H, bwid = c.W;
        var m = new bool[bh + 2, bwid + 2];
        foreach (var (py, px) in c.Points) m[py - c.Y0 + 1, px - c.X0 + 1] = true;
        var lab = new int[bh + 2, bwid + 2];
        var stack = new Stack<(int, int)>();
        var holes = 0; var id = 0;
        for (var y = 0; y < bh + 2; y++)
            for (var x = 0; x < bwid + 2; x++)
            {
                if (m[y, x] || lab[y, x] != 0) continue;
                id++; var size = 0; var border = false;
                lab[y, x] = id; stack.Push((y, x));
                while (stack.Count > 0)
                {
                    var (cy, cx) = stack.Pop(); size++;
                    if (cy == 0 || cx == 0 || cy == bh + 1 || cx == bwid + 1) border = true;
                    foreach (var (ny, nx) in new[] { (cy - 1, cx), (cy + 1, cx), (cy, cx - 1), (cy, cx + 1) })
                        if (ny >= 0 && nx >= 0 && ny < bh + 2 && nx < bwid + 2 && !m[ny, nx] && lab[ny, nx] == 0) { lab[ny, nx] = id; stack.Push((ny, nx)); }
                }
                if (!border && size >= 3) holes++;
            }
        return holes;
    }

    /// <summary>Hintergrund je Brett (Scans): Median der eigenen hellen bzw. dunklen Felder.</summary>
    public static (float[,] Light, float[,] Dark) ParityBackgrounds(float[][,] cells)
    {
        static bool Even(int i) => (i / 8 + i % 8) % 2 == 0;
        return (BoardImageMath.Median(Enumerable.Range(0, 64).Where(Even).Select(i => cells[i]).ToList()),
                BoardImageMath.Median(Enumerable.Range(0, 64).Where(i => !Even(i)).Select(i => cells[i]).ToList()));
    }

    /// <summary>
    /// Wie gut passt ein Schachbrettmuster: Zahl der Felder (0–64), deren Randzone (außerhalb 6…26) näher am eigenen als am
    /// anderen Paritäts-Hintergrund liegt; −100 extra, wenn helle und dunkle Felder sich kaum unterscheiden. Ein echtes Brett
    /// kommt auf ≥ 52, ein als Brett gefundener Ausschnitt aus 3 × 3 Feldern nicht.
    /// </summary>
    public static int GridScore(float[][,] cells) => Grid(cells).Hits;

    /// <summary>Stetiges Maß für dasselbe Muster (mittlerer Vorsprung des eigenen vor dem fremden Hintergrund in der
    /// Randzone) — entscheidet zwischen fast gleich guten Rechtecken, wo die Trefferzahl gleich ist.</summary>
    public static double GridFit(float[][,] cells) => Grid(cells).Fit;

    private static (int Hits, double Fit) Grid(float[][,] cells)
    {
        var (light, dark) = ParityBackgrounds(cells);
        var hits = 0; double fit = 0;
        for (var i = 0; i < 64; i++)
        {
            var even = (i / 8 + i % 8) % 2 == 0;
            var own = even ? light : dark; var other = even ? dark : light;
            double a = 0, b = 0; var n = 0;
            for (var y = 0; y < Cell; y++)
                for (var x = 0; x < Cell; x++)
                {
                    if (y >= 6 && y < 26 && x >= 6 && x < 26) continue;
                    a += Math.Abs(cells[i][y, x] - own[y, x]); b += Math.Abs(cells[i][y, x] - other[y, x]); n++;
                }
            if (a / n < b / n) hits++;
            fit += (b - a) / n / 64;
        }
        var contrast = Math.Abs(Mean(light) - Mean(dark)) >= 0.08;
        return (hits + (contrast ? 0 : -100), contrast ? fit : fit - 100);
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
        return GridToBoard(grid);
    }

    /// <summary>64 Felder (Reihe 8 zuerst, '.' leer) → Brettteil der FEN, oder <c>null</c>, wenn die Stellung nicht sein
    /// kann (nicht genau ein König je Seite, Bauern auf der Grundreihe, unmögliches Material).</summary>
    public static string? GridToBoard(char[] grid)
    {
        if (grid.Count(c => c == 'K') != 1 || grid.Count(c => c == 'k') != 1) return null;
        for (var i = 0; i < 8; i++) if (grid[i] is 'P' or 'p' || grid[56 + i] is 'P' or 'p') return null;
        if (!PossibleMaterial(grid)) return null;
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

    /// <summary>
    /// Kann das Material in einer Partie vorkommen (0.780.5)? Je Farbe höchstens 8 Bauern, und alles über der
    /// Grundausstattung (D 1, T/L/S je 2) muss durch umgewandelte Bauern erklärbar sein. Fünf weiße Springer und vier weiße
    /// Türme neben sechs Bauern heißt: das Bild wurde falsch gelesen.
    /// </summary>
    public static bool PossibleMaterial(IReadOnlyList<char> grid)
    {
        foreach (var white in new[] { true, false })
        {
            int Count(char c) => grid.Count(x => x == (white ? char.ToUpperInvariant(c) : c));
            int pawns = Count('p');
            if (pawns > 8) return false;
            var extra = Math.Max(0, Count('q') - 1) + Math.Max(0, Count('r') - 2) + Math.Max(0, Count('b') - 2) + Math.Max(0, Count('n') - 2);
            if (extra > 8 - pawns) return false;
        }
        return true;
    }

    private static double Mean(float[,] a) { double s = 0; foreach (var v in a) s += v; return s / a.Length; }
}
