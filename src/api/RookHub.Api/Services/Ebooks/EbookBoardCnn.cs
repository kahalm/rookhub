using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Faltungsnetz für Brett-Felder (0.794.0): ersetzt bei gescannten Büchern das kleine MLP. Trainiert auf ~3 400 von Opus
/// gelesenen Diagrammen aus 7 Büchern mit 5 Figurensätzen (Skripte: <c>~/claude/ebook-board-training/hr</c>), nachtrainiert
/// 0.815.1 mit ~5 000 weiteren Opus-geprüften Brettern aus 32 Büchern (<c>…/newlabels/train2.py</c>, zurückgehalten 82 → 92 %
/// exakt); in der
/// Gegenprobe (Buch nie gesehen) 82–100 % exakte Bretter bei ähnlichen Figurensätzen. Eingabe: das Brett aus der
/// 300-dpi-Seite, flächengemittelt auf 256 × 256, je Feld 32 × 32 Grauwerte 0–1 (64 Felder als ein Stapel); Ausgabe je Feld
/// 13 Werte in der Reihenfolge PNBRQKpnbrqk + leer. Modell als eingebettete Ressource <c>Assets/ebook-board-cnn.onnx</c>.
/// </summary>
public sealed class EbookBoardCnn : IDisposable
{
    public const int CellSize = 32;
    public const string Classes = "PNBRQKpnbrqk.";
    private readonly InferenceSession _session;
    private readonly string _input;

    private static readonly Lazy<EbookBoardCnn?> Default = new(() =>
    {
        using var s = typeof(EbookBoardCnn).Assembly.GetManifestResourceStream("RookHub.Api.Assets.ebook-board-cnn.onnx");
        if (s == null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return new EbookBoardCnn(ms.ToArray());
    });

    /// <summary>Threads je Lauf des Netzes (ONNX IntraOp). Der Server bleibt bei 2; der externe Diagramm-Worker
    /// (<c>tools/DiagramCli</c>, 0.870.0) setzt 1, weil er die Kerne über die parallelen Seitenstapel verteilt. Nur vor dem
    /// ersten Zugriff auf <see cref="Instance"/> wirksam.</summary>
    public static int IntraOpThreads { get; set; } = 2;

    /// <summary>Das eingebettete Modell (<c>null</c>, wenn es fehlt).</summary>
    public static EbookBoardCnn? Instance => Default.Value;

    public EbookBoardCnn(byte[] model)
    {
        var opts = new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = Math.Max(1, IntraOpThreads), InterOpNumThreads = 1 };
        _session = new InferenceSession(model, opts);
        _input = _session.InputMetadata.Keys.First();
    }

    /// <summary>Ein gelesenes Brett: 64 Zeichen (Reihe 8 zuerst, '.' = leer) und die Sicherheit des unsichersten Felds.</summary>
    public sealed record Reading(char[] Grid, float MinConfidence);

    /// <summary>Brett aus der Seite <paramref name="page"/> (Grauwerte 0–1, volle Auflösung) im Rechteck lesen.</summary>
    public Reading Read(float[,] page, int x0, int y0, int x1, int y1)
    {
        var board = AreaResize(page, x0, y0, x1, y1, 8 * CellSize, 8 * CellSize);
        var t = new DenseTensor<float>([64, 1, CellSize, CellSize]);
        for (var r = 0; r < 8; r++)
            for (var c = 0; c < 8; c++)
            {
                var i = r * 8 + c;
                for (var y = 0; y < CellSize; y++)
                    for (var x = 0; x < CellSize; x++)
                        t[i, 0, y, x] = board[r * CellSize + y, c * CellSize + x];
            }
        using var res = _session.Run([NamedOnnxValue.CreateFromTensor(_input, t)]);
        var logits = res.First().AsTensor<float>();
        var grid = new char[64]; var minConf = 1f;
        for (var i = 0; i < 64; i++)
        {
            var max = float.MinValue; var arg = 0;
            for (var k = 0; k < 13; k++) if (logits[i, k] > max) { max = logits[i, k]; arg = k; }
            double sum = 0;
            for (var k = 0; k < 13; k++) sum += Math.Exp(logits[i, k] - max);
            var p = (float)(1 / sum);
            grid[i] = Classes[arg];
            minConf = Math.Min(minConf, p);
        }
        return new Reading(grid, minConf);
    }

    /// <summary>Rechteck auf <paramref name="w"/> × <paramref name="h"/> bringen: jeder Zielpunkt = Flächenmittel seines
    /// Quellbereichs (anteilig an den Rändern) — beim Verkleinern wie PILs geglättetes Skalieren im Training.</summary>
    public static float[,] AreaResize(float[,] src, int x0, int y0, int x1, int y1, int w, int h)
    {
        int sh = src.GetLength(0), sw = src.GetLength(1);
        x0 = Math.Clamp(x0, 0, sw - 1); x1 = Math.Clamp(x1, x0 + 1, sw); y0 = Math.Clamp(y0, 0, sh - 1); y1 = Math.Clamp(y1, y0 + 1, sh);
        var cols = Weights(x0, x1, w); var rows = Weights(y0, y1, h);
        var tmp = new float[y1 - y0, w];
        for (var y = y0; y < y1; y++)
            for (var tx = 0; tx < w; tx++)
            {
                double acc = 0, ws = 0;
                foreach (var (sx, wt) in cols[tx]) { acc += src[y, sx] * wt; ws += wt; }
                tmp[y - y0, tx] = (float)(acc / ws);
            }
        var o = new float[h, w];
        for (var ty = 0; ty < h; ty++)
            for (var tx = 0; tx < w; tx++)
            {
                double acc = 0, ws = 0;
                foreach (var (sy, wt) in rows[ty]) { acc += tmp[sy - y0, tx] * wt; ws += wt; }
                o[ty, tx] = (float)(acc / ws);
            }
        return o;
    }

    private static List<(int Src, double W)>[] Weights(int a, int b, int n)
    {
        var scale = (double)(b - a) / n;
        var res = new List<(int, double)>[n];
        for (var t = 0; t < n; t++)
        {
            var lo = a + t * scale; var hi = lo + Math.Max(scale, 1e-9);
            var list = new List<(int, double)>();
            if (scale < 1)
            {
                // Vergrößern: lineare Interpolation zwischen den Nachbarpunkten.
                var c = a + (t + 0.5) * scale - 0.5;
                var i0 = (int)Math.Floor(c); var f = c - i0;
                list.Add((Math.Clamp(i0, a, b - 1), 1 - f)); list.Add((Math.Clamp(i0 + 1, a, b - 1), f));
            }
            else
                for (var s = (int)Math.Floor(lo); s < Math.Ceiling(hi); s++)
                {
                    var wt = Math.Min(hi, s + 1) - Math.Max(lo, s);
                    if (wt > 0) list.Add((Math.Clamp(s, a, b - 1), wt));
                }
            res[t] = list;
        }
        return res;
    }

    public void Dispose() => _session.Dispose();
}
