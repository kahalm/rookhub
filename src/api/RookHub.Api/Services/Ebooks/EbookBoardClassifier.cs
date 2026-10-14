namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Die zwei kleinen Netze der Brett-Bilderkennung (0.775.0), trainiert auf ~1 000 Font-Diagrammen der Bibliothek (Felder als
/// Bild gerendert, Stellung bekannt) plus synthetischen Brettern aus den Figurensätzen der App: Modell 1 = Figurenart
/// (12 Klassen PNBRQKpnbrqk), Modell 2 = leer oder besetzt (13 Klassen, Klasse 12 = leer). Je eine verdeckte Schicht (ReLU),
/// Softmax. Gewichte als eingebettete Ressource <c>Assets/ebook-board-model.bin</c>: „RHBM“, Anzahl Matrizen, dann je
/// Zeilen, Spalten (int32) und die Werte (float32, little endian, zeilenweise) in der Folge W1a b1a W2a b2a W1b b1b W2b b2b.
/// </summary>
public sealed class EbookBoardClassifier
{
    private readonly float[][] _w;
    private readonly (int Rows, int Cols)[] _shape;

    private static readonly Lazy<EbookBoardClassifier> Default = new(() =>
    {
        using var s = typeof(EbookBoardClassifier).Assembly.GetManifestResourceStream("RookHub.Api.Assets.ebook-board-model.bin")
            ?? throw new InvalidOperationException("ebook-board-model.bin fehlt");
        return new EbookBoardClassifier(s);
    });

    public static EbookBoardClassifier Instance => Default.Value;

    public EbookBoardClassifier(Stream stream)
    {
        using var r = new BinaryReader(stream);
        if (new string(r.ReadChars(4)) != "RHBM") throw new InvalidDataException("kein Brettmodell");
        var n = r.ReadInt32();
        _w = new float[n][]; _shape = new (int, int)[n];
        for (var i = 0; i < n; i++)
        {
            int rows = r.ReadInt32(), cols = r.ReadInt32();
            _shape[i] = (rows, cols);
            var a = new float[rows * cols];
            for (var j = 0; j < a.Length; j++) a[j] = r.ReadSingle();
            _w[i] = a;
        }
        if (n != 8) throw new InvalidDataException("Brettmodell braucht 8 Matrizen");
    }

    /// <summary>Index der Figur (0–11, PNBRQKpnbrqk).</summary>
    public int Piece(float[] feature1)
    {
        var p = Forward(0, feature1);
        var best = 0;
        for (var i = 1; i < p.Length; i++) if (p[i] > p[best]) best = i;
        return best;
    }

    /// <summary>Wahrscheinlichkeit „leeres Feld“ (Klasse 12 von Modell 2).</summary>
    public double EmptyProbability(float[] feature2) => Forward(4, feature2)[12];

    private double[] Forward(int at, float[] x)
    {
        var (inN, hid) = _shape[at];
        if (x.Length != inN) throw new ArgumentException($"Merkmal hat {x.Length} statt {inN} Werte");
        var W1 = _w[at]; var b1 = _w[at + 1]; var W2 = _w[at + 2]; var b2 = _w[at + 3];
        var outN = _shape[at + 2].Cols;
        var h = new double[hid];
        for (var j = 0; j < hid; j++) h[j] = b1[j];
        for (var i = 0; i < inN; i++)
        {
            var xi = x[i]; if (xi == 0) continue;
            var row = i * hid;
            for (var j = 0; j < hid; j++) h[j] += xi * W1[row + j];
        }
        for (var j = 0; j < hid; j++) if (h[j] < 0) h[j] = 0;
        var z = new double[outN];
        for (var k = 0; k < outN; k++) z[k] = b2[k];
        for (var j = 0; j < hid; j++)
        {
            if (h[j] == 0) continue;
            var row = j * outN;
            for (var k = 0; k < outN; k++) z[k] += h[j] * W2[row + k];
        }
        var max = z.Max(); double sum = 0;
        for (var k = 0; k < outN; k++) { z[k] = Math.Exp(z[k] - max); sum += z[k]; }
        for (var k = 0; k < outN; k++) z[k] /= sum;
        return z;
    }
}
