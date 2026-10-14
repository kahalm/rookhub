namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Bildoperationen der Brett-Bilderkennung (0.775.0) — 1:1 nach der Python-Referenz, mit der der Klassifikator trainiert
/// wurde (Flächenmittel-Skalierung über das Integralbild, separabler Gauß mit gespiegeltem Rand, 4er-Dilatation,
/// bilineare Verschiebung). Abweichungen hier verschieben die Merkmale gegenüber dem Training — deshalb keine
/// Bibliotheksfunktionen (deren Interpolation unterscheidet sich je Bibliothek). Graustufen: 0 = schwarz, 1 = weiß.
/// </summary>
public static class BoardImageMath
{
    /// <summary>Flächenmittel auf <paramref name="h"/>×<paramref name="w"/>; Ränder der Zielzellen dürfen gebrochen liegen.</summary>
    public static float[,] AreaResize(float[,] a, int h, int w)
    {
        int H = a.GetLength(0), W = a.GetLength(1);
        var I = new double[H + 1, W + 1];
        for (var y = 0; y < H; y++)
        {
            double row = 0;
            for (var x = 0; x < W; x++) { row += a[y, x]; I[y + 1, x + 1] = I[y, x + 1] + row; }
        }
        double S(double y, double x)
        {
            var y0 = (int)Math.Floor(y); var x0 = (int)Math.Floor(x);
            var y1 = Math.Min(y0 + 1, H); var x1 = Math.Min(x0 + 1, W);
            double fy = y - y0, fx = x - x0;
            return I[y0, x0] * (1 - fy) * (1 - fx) + I[y1, x0] * fy * (1 - fx) + I[y0, x1] * (1 - fy) * fx + I[y1, x1] * fy * fx;
        }
        var ys = new double[h + 1]; var xs = new double[w + 1];
        for (var i = 0; i <= h; i++) ys[i] = (double)H * i / h;
        for (var i = 0; i <= w; i++) xs[i] = (double)W * i / w;
        var outp = new float[h, w];
        for (var r = 0; r < h; r++)
            for (var c = 0; c < w; c++)
            {
                var tot = S(ys[r + 1], xs[c + 1]) - S(ys[r], xs[c + 1]) - S(ys[r + 1], xs[c]) + S(ys[r], xs[c]);
                var area = Math.Max((ys[r + 1] - ys[r]) * (xs[c + 1] - xs[c]), 1e-9);
                outp[r, c] = (float)(tot / area);
            }
        return outp;
    }

    /// <summary>Separabler Gauß (Radius = ⌊4σ + 0,5⌋), Rand gespiegelt (… c b a | a b c …).</summary>
    public static float[,] Blur(float[,] a, double sigma)
    {
        var r = (int)(4 * sigma + 0.5);
        var k = new double[2 * r + 1]; double sum = 0;
        for (var i = -r; i <= r; i++) { k[i + r] = Math.Exp(-0.5 * (i / sigma) * (i / sigma)); sum += k[i + r]; }
        for (var i = 0; i < k.Length; i++) k[i] /= sum;
        int H = a.GetLength(0), W = a.GetLength(1);
        static int Reflect(int i, int n) { while (i < 0 || i >= n) i = i < 0 ? -i - 1 : 2 * n - i - 1; return i; }
        var tmp = new double[H, W];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                double s = 0;
                for (var i = -r; i <= r; i++) s += k[i + r] * a[y, Reflect(x + i, W)];
                tmp[y, x] = s;
            }
        var outp = new float[H, W];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                double s = 0;
                for (var i = -r; i <= r; i++) s += k[i + r] * tmp[Reflect(y + i, H), x];
                outp[y, x] = (float)s;
            }
        return outp;
    }

    /// <summary>Bilineare Verschiebung um (dy, dx); außerhalb = <paramref name="cval"/>.</summary>
    public static float[,] Shift(float[,] a, double dy, double dx, float cval = 1f)
    {
        int H = a.GetLength(0), W = a.GetLength(1);
        var o = new float[H, W];
        float At(int y, int x) => y >= 0 && y < H && x >= 0 && x < W ? a[y, x] : cval;
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                double sy = y - dy, sx = x - dx;
                var y0 = (int)Math.Floor(sy); var x0 = (int)Math.Floor(sx);
                double fy = sy - y0, fx = sx - x0;
                o[y, x] = (float)(At(y0, x0) * (1 - fy) * (1 - fx) + At(y0 + 1, x0) * fy * (1 - fx) + At(y0, x0 + 1) * (1 - fy) * fx + At(y0 + 1, x0 + 1) * fy * fx);
            }
        return o;
    }

    public static float[,] Sub(float[,] a, int y0, int y1, int x0, int x1)
    {
        var o = new float[y1 - y0, x1 - x0];
        for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++) o[y - y0, x - x0] = a[y, x];
        return o;
    }

    /// <summary>Median je Pixel über mehrere gleich große Bilder.</summary>
    public static float[,] Median(IReadOnlyList<float[,]> imgs)
    {
        int H = imgs[0].GetLength(0), W = imgs[0].GetLength(1); var o = new float[H, W]; var buf = new float[imgs.Count];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                for (var i = 0; i < imgs.Count; i++) buf[i] = imgs[i][y, x];
                Array.Sort(buf);
                var n = buf.Length;
                o[y, x] = n % 2 == 1 ? buf[n / 2] : (buf[n / 2 - 1] + buf[n / 2]) / 2f;
            }
        return o;
    }
}
