using System.Diagnostics;
using Microsoft.Extensions.Options;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Diagramme aus GESCANNTEN Büchern (0.776.0, Stufe 2): Scan-PDFs ohne Schach-Font und die PDF-Fassung reiner DJVU-Bücher.
/// Jede Seite wird mit <c>pdftoppm</c> in Graustufen mit 300 dpi gerendert, 2 × 2 gemittelt und leicht weichgezeichnet (σ 1)
/// — Scans aus DJVU sind schwarz-weiß, pdftoppm glättet Bilder nicht, ohne das zerfällt die Schraffur der dunklen Felder in
/// lose Striche (Beim: 85 statt 166 Bretter). Portionen zu 20 Seiten in einen Zwischenordner;
/// <see cref="EbookBoardImage.FindBoards"/> sucht die Bretter, nur echte Schachbrettmuster (<see cref="EbookBoardImage.GridScore"/>
/// ≥ 52) zählen. Hintergrund je BRETT statt je Buch: die Schraffur der dunklen Felder liegt in jedem Scan etwas anders.
/// Figuren erkennt das Scan-Modell (<see cref="EbookBoardClassifier.Scan"/>, trainiert auf „gescannten“ Font-Diagrammen:
/// verkleinert, weichgezeichnet, verrauscht, binarisiert). Lage als Seitenanteil wie bei den Font-Diagrammen — der PDF-Leser
/// legt seine Zonen darüber. Zugrecht: Marker „W“/„B“ links vom Brett (Gambit/Batsford, <see cref="EbookBoardImage.SideMarker"/>), sonst Text aus
/// einer OCR-Ebene.
/// </summary>
public class EbookScanDiagramExtractor
{
    /// <summary>Arbeitsauflösung (gerendert wird doppelt so fein, dann 2 × 2 gemittelt).</summary>
    public const int Dpi = 150;

    /// <summary>Weichzeichner nach dem Verkleinern.</summary>
    public const double PageBlur = 1.0;
    public const int MinGridScore = 52;
    private const int Batch = 20;

    private readonly EbookOptions _options;
    private readonly ILogger<EbookScanDiagramExtractor> _logger;

    public EbookScanDiagramExtractor(IOptions<EbookOptions> options, ILogger<EbookScanDiagramExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Ein gefundenes Brett: Seite, Lage als Seitenanteil, Brettteil der FEN, Zugrecht-Marker (oder null).</summary>
    public sealed record ScanBoard(int Page, double X, double Y, double W, double H, string Board, char? Side, float? Confidence = null,
        bool Unread = false);

    /// <summary>Ein Brett mit den Lesungen beider Scan-Modelle (<c>null</c> = nicht plausibel).</summary>
    /// <param name="RawBoard">Ergänzen-Modus (0.811.0): die rohe (unplausible) Lesung, wenn beide Modelle versagen.</param>
    public sealed record ScanCandidate(int Page, double X, double Y, double W, double H, string? Board, string? AltBoard, char? Side, float? Confidence = null,
        string? RawBoard = null);

    /// <remarks>Bretter mit unplausibler Lesung bleiben IMMER als <see cref="ScanBoard.Unread"/> erhalten (0.947.0, vorher nur im
    /// Ergänzen-Modus): die Box ist geometrisch sauber gefunden, nur das Netz kennt den Figurensatz nicht (Chess Review 1941,
    /// vertauschte Könige) — so landet das Brett in der Unread-/Opus-Nachlese statt spurlos zu verschwinden. Nutzer sehen es nicht
    /// (<see cref="EbookDiagramVisibility.Readable"/>). Messung 10.11.2026: 29 von 29 solcher Bretter, 0 Fehltreffer.</remarks>
    public List<ScanBoard> Scan(string pdfPath, int pageCount, CancellationToken ct)
    {
        var model = EbookBoardClassifier.Scan; var alt = EbookBoardClassifier.ScanAlt;
        // Stapel zu je 20 Seiten parallel rendern und lesen (0.797.0) — vorher lief ein Buch strikt Seite für Seite auf einem Kern.
        var found = new System.Collections.Concurrent.ConcurrentBag<ScanCandidate>();
        var root = Path.Combine(Path.GetTempPath(), "rh-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var batches = Enumerable.Range(0, (pageCount + Batch - 1) / Batch).Select(i => 1 + i * Batch).ToList();
            var failed = 0;
            Parallel.ForEach(batches, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.ScanParallelism), CancellationToken = ct }, first =>
            {
                if (Volatile.Read(ref failed) != 0) return;
                var last = Math.Min(pageCount, first + Batch - 1);
                var dir = Path.Combine(root, first.ToString());
                Directory.CreateDirectory(dir);
                if (!Render(pdfPath, first, last, dir, ct)) { Interlocked.Exchange(ref failed, 1); return; }
                foreach (var file in Directory.GetFiles(dir, "*.pgm").OrderBy(f => f, StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    var page = PageOf(file);
                    try
                    {
                        var g = ReadPgm(File.ReadAllBytes(file));
                        if (g != null && page > 0) foreach (var c in ScanRenderedPage(g, page, model, alt, EbookBoardCnn.Instance)) found.Add(c);
                    }
                    finally { File.Delete(file); }
                }
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException)) { throw new OperationCanceledException(ct); }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { /* Zwischenordner, egal */ }
        }
        var result = found.OrderBy(c => c.Page).ThenBy(c => c.Y).ThenBy(c => c.X).ToList();
        var chosen = Choose(result, out var useAlt, keepUnread: true);
        _logger.LogInformation("Scan {Path}: {Count} Diagramme auf {Pages} Seiten (Modell {Model})", Path.GetFileName(pdfPath), chosen.Count, pageCount, useAlt ? "scan2" : "scan");
        return chosen;
    }

    /// <summary>Eine gerenderte 300-dpi-Seite vollständig lesen: Papier/Ausleuchtung angleichen (<see cref="NormalizePaper"/>),
    /// verkleinern, Bretter suchen — unplausible Lesungen bleiben als rohe Lesung erhalten (siehe <see cref="Scan"/>).</summary>
    internal static List<ScanCandidate> ScanRenderedPage(float[,] rendered, int page, EbookBoardClassifier model, EbookBoardClassifier? alt,
        EbookBoardCnn? cnn)
    {
        var g = NormalizePaper(rendered);
        return ScanPage(PreparePage(g), page, model, alt, g, cnn, keepUnread: true);
    }

    /// <summary>Je Buch das Modell mit mehr plausiblen Brettern; ein Brett, das es nicht lesen kann, nimmt das andere.</summary>
    internal static List<ScanBoard> Choose(IReadOnlyList<ScanCandidate> candidates, out bool useAlt, bool keepUnread = false)
    {
        useAlt = candidates.Count(c => c.AltBoard != null) > candidates.Count(c => c.Board != null);
        var alt = useAlt;
        return candidates
            .Select(c => (c, board: alt ? c.AltBoard ?? c.Board : c.Board ?? c.AltBoard))
            .Where(x => x.board != null || (keepUnread && x.c.RawBoard != null))
            .Select(x => x.board != null
                ? new ScanBoard(x.c.Page, x.c.X, x.c.Y, x.c.W, x.c.H, x.board, x.c.Side, x.board == x.c.Board ? x.c.Confidence : null)
                : new ScanBoard(x.c.Page, x.c.X, x.c.Y, x.c.W, x.c.H, x.c.RawBoard!, x.c.Side, 0, Unread: true))
            .ToList();
    }

    /// <summary>Papierton, ab dem eine Seite als getönt gilt und aufgehellt wird. 0.96 (0.824.0, vorher 0.9): muss über der
    /// Schwelle der Brettsuche liegen (235/255 ≈ 0.92) — Papier dazwischen (Internet-Archive-Scan Kislik, Papier 231–233)
    /// galt als weiß, lag aber unter der Schwelle, und die ganze Seite wurde eine dunkle Fläche.</summary>
    internal const float TintedPaper = 0.96f;

    /// <summary>
    /// Getöntes Papier (0.813.0): Farbscans aus Internet Archive / Google Books haben Papier mit Grauwert 155–190 — die ganze Seite
    /// liegt dann unter der Schwelle der Brettsuche (235), und es entsteht keine eigene Brett-Fläche. Der häufigste Grauwert über
    /// 0,3 gilt als Papier; liegt er unter <see cref="TintedPaper"/>, wird die Seite so geteilt, dass das Papier weiß wird.
    /// Danach wird die Ausleuchtung je Ort ausgeglichen (<see cref="FlattenIllumination"/>, 0.947.0).
    /// </summary>
    internal static float[,] NormalizePaper(float[,] g)
    {
        var tinted = NormalizeTint(g);
        return FlattenIllumination(tinted, inPlace: !ReferenceEquals(tinted, g));
    }

    /// <summary>Globaler Teil von <see cref="NormalizePaper"/>: getöntes Papier aufhellen; weißes Papier bleibt dasselbe Array.</summary>
    internal static float[,] NormalizeTint(float[,] g)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var hist = new int[256];
        for (var y = 0; y < H; y += 2)
            for (var x = 0; x < W; x += 2) hist[Math.Clamp((int)(g[y, x] * 255f + 0.5f), 0, 255)]++;
        var paper = 77;
        for (var k = 78; k < 256; k++) if (hist[k] > hist[paper]) paper = k;
        var white = paper / 255f;
        if (white >= TintedPaper) return g;
        var o = new float[H, W];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++) o[y, x] = Math.Min(1f, g[y, x] / white);
        return o;
    }

    /// <summary>Schrittweite der Ausleuchtungs-Schätzung (jedes 4. Pixel der 300-dpi-Seite ≈ 75 dpi).</summary>
    internal const int IlluminationStep = 4;
    /// <summary>Fenster des lokalen Maximums auf der ¼-Seite: 31 px ≈ 1 cm, größer als ein Brettfeld — im Fenster liegt immer Papier.</summary>
    internal const int IlluminationWindow = 31;
    /// <summary>Glättung des Papierfelds (Gauß σ auf der ¼-Seite).</summary>
    internal const double IlluminationSigma = 8;
    /// <summary>Untergrenze des Papierfelds — großflächig schwarze Stellen (Fotos, Balken) werden nicht zu Weiß aufgeblasen.</summary>
    internal const float IlluminationFloor = 0.3f;

    /// <summary>
    /// Ungleichmäßige Ausleuchtung ausgleichen (0.947.0): Schatten am Bund und graue Seitenränder (2293: Papier links 0,78–0,82)
    /// lagen unter der Schwelle der Brettsuche (210/255), das Brett verschmolz mit dem Rand zu einer Fläche ab x = 0 und war nie
    /// quadratisch. Papierhelligkeit je Ort = Maximum über <see cref="IlluminationWindow"/> Pixel der ¼-Seite (jedes
    /// <see cref="IlluminationStep"/>. Pixel), Gauß σ <see cref="IlluminationSigma"/>, blockweise hochgesetzt; die Seite wird dadurch
    /// geteilt (Untergrenze <see cref="IlluminationFloor"/>, höchstens 1). 1:1 wie <c>flatten</c> im Messinstrument
    /// (<c>ebook-review/detektor/det.py</c>, Variante H/M): holt alle 6 Bundschatten-Bretter, ändert sonst nichts.
    /// Kosten: separierbares Maximum und Gauß auf der ¼-Seite (≈ 1/16 der Pixel).
    /// </summary>
    internal static float[,] FlattenIllumination(float[,] g, bool inPlace = false)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        if (H == 0 || W == 0) return g;
        const int step = IlluminationStep;
        int h = (H + step - 1) / step, w = (W + step - 1) / step;
        var s = new float[h, w];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++) s[y, x] = g[y * step, x * step];
        var bg = GaussReflect(MaxFilter(s, IlluminationWindow), IlluminationSigma);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++) bg[y, x] = Math.Max(bg[y, x], IlluminationFloor);
        var o = inPlace ? g : new float[H, W];
        for (var y = 0; y < H; y++)
        {
            var by = y / step;
            for (var x = 0; x < W; x++)
            {
                var v = g[y, x] / bg[by, x / step];   // Division wie im Messinstrument (bitgleich), kein Kehrwert
                o[y, x] = v < 1f ? v : 1f;
            }
        }
        return o;
    }

    /// <summary>Maximum über ein Quadrat der Kantenlänge <paramref name="size"/> (ungerade, zentriert), am Rand nur über die
    /// vorhandenen Pixel (= <c>scipy.ndimage.maximum_filter</c> mit „reflect“) — separierbar: erst Zeilen, dann Spalten.</summary>
    internal static float[,] MaxFilter(float[,] a, int size)
    {
        int h = a.GetLength(0), w = a.GetLength(1), r = size / 2;
        var t = new float[h, w];
        var line = new float[Math.Max(h, w)];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++) line[x] = a[y, x];
            for (var x = 0; x < w; x++)
            {
                var m = float.MinValue;
                for (int k = Math.Max(0, x - r), e = Math.Min(w - 1, x + r); k <= e; k++) if (line[k] > m) m = line[k];
                t[y, x] = m;
            }
        }
        var o = new float[h, w];
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++) line[y] = t[y, x];
            for (var y = 0; y < h; y++)
            {
                var m = float.MinValue;
                for (int k = Math.Max(0, y - r), e = Math.Min(h - 1, y + r); k <= e; k++) if (line[k] > m) m = line[k];
                o[y, x] = m;
            }
        }
        return o;
    }

    /// <summary>Gauß-Weichzeichner wie <c>scipy.ndimage.gaussian_filter</c> (Radius 4 σ, Rand gespiegelt „reflect“ = d c b a | a b c d,
    /// Zwischenergebnis in float) — separierbar, erst Achse 0 (senkrecht), dann Achse 1.</summary>
    internal static float[,] GaussReflect(float[,] a, double sigma)
    {
        int h = a.GetLength(0), w = a.GetLength(1);
        var r = (int)(4.0 * sigma + 0.5);
        var k = new double[2 * r + 1];
        double sum = 0;
        for (var i = -r; i <= r; i++) sum += k[i + r] = Math.Exp(-0.5 * i * i / (sigma * sigma));
        for (var i = 0; i < k.Length; i++) k[i] /= sum;
        static int Reflect(int i, int n)
        {
            var p = 2 * n;
            i %= p; if (i < 0) i += p;
            return i < n ? i : p - 1 - i;
        }
        // Gespiegelte Zeile mit Rand r auf beiden Seiten — die Faltung läuft dann ohne Indexrechnung.
        void Convolve(float[] src, int n, float[] pad, double[] dst)
        {
            for (var i = -r; i < n + r; i++) pad[i + r] = src[Reflect(i, n)];
            for (var i = 0; i < n; i++)
            {
                double v = 0;
                for (var j = 0; j < k.Length; j++) v += k[j] * pad[i + j];
                dst[i] = v;
            }
        }
        var n0 = Math.Max(h, w);
        var src = new float[n0]; var pad = new float[n0 + 2 * r]; var dst = new double[n0];
        var t = new float[h, w];
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++) src[y] = a[y, x];
            Convolve(src, h, pad, dst);
            for (var y = 0; y < h; y++) t[y, x] = (float)dst[y];
        }
        var o = new float[h, w];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++) src[x] = t[y, x];
            Convolve(src, w, pad, dst);
            for (var x = 0; x < w; x++) o[y, x] = (float)dst[x];
        }
        return o;
    }

    /// <summary>300-dpi-Seite → Arbeitsbild: 2 × 2 mitteln, dann Gauß σ <see cref="PageBlur"/>.</summary>
    public static float[,] PreparePage(float[,] g)
    {
        int h = g.GetLength(0) / 2, w = g.GetLength(1) / 2;
        var o = new float[h, w];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                o[y, x] = (g[2 * y, 2 * x] + g[2 * y + 1, 2 * x] + g[2 * y, 2 * x + 1] + g[2 * y + 1, 2 * x + 1]) / 4f;
        return BoardImageMath.Blur(o, PageBlur);
    }

    /// <summary>Bretter einer vorbereiteten Seite (<see cref="PreparePage"/>) erkennen.</summary>
    /// <param name="full">Die Seite in voller Auflösung (doppelt so groß) für den Zugrecht-Marker; <c>null</c> = keiner.</param>
    /// <param name="cnn">Faltungsnetz (0.794.0): liest das Brett aus der vollen Auflösung; ist seine Lesung plausibel, gilt sie
    /// (mit Sicherheit), sonst die der alten Modelle.</param>
    /// <param name="keepUnread">Ergänzen-Modus (0.811.0): ein Brett, das kein Modell plausibel liest, kommt mit der rohen Lesung
    /// (<see cref="ScanCandidate.RawBoard"/>) statt gar nicht.</param>
    public static List<ScanCandidate> ScanPage(float[,] g, int page, EbookBoardClassifier model, EbookBoardClassifier? alt = null, float[,]? full = null,
        EbookBoardCnn? cnn = null, bool keepUnread = false)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var list = new List<ScanCandidate>();
        foreach (var found in EbookBoardImage.FindBoards(g))
        {
            var box = found;
            var cells = EbookBoardImage.Cells(g, box);
            var score = EbookBoardImage.GridScore(cells);
            // Dicker Rahmen (0.813.0, Stean „Simple Chess“): das Rechteck schließt ihn halb ein, das Feldraster ist verschoben —
            // nach innen nehmen, bis zu 3 % der Brettbreite, und das beste Raster behalten.
            for (var k = 1; score < MinGridScore && k <= Math.Max(2, (box.X1 - box.X0) * 3 / 100); k++)
            {
                var inner = (found.X0 + k, found.Y0 + k, found.X1 - k, found.Y1 - k);
                var c = EbookBoardImage.Cells(g, inner);
                var sc = EbookBoardImage.GridScore(c);
                if (sc > score) { score = sc; box = inner; cells = c; }
            }
            // Brett im grauen Passepartout mit Koordinaten (0.876.3, Polgar): bis an die hellen Felder nehmen. Der Zugrecht-Marker
            // liegt dann außerhalb des Passepartouts — gesucht wird er am äußeren Rechteck (innen stehen nur Koordinaten).
            var markerBox = (X0: 0, Y0: 0, X1: 0, Y1: 0);
            if (score < MinGridScore && EbookBoardImage.LightExtent(g, found) is { } lit)
            {
                var (sq, _) = EbookBoardImage.SquareUp(g, lit);
                var c = EbookBoardImage.Cells(g, sq);
                var sc = EbookBoardImage.GridScore(c);
                if (sc > score) { score = sc; box = sq; cells = c; markerBox = found; }
            }
            if (score < MinGridScore) continue;
            var trimmed = EbookBoardImage.TrimToFrame(g, box);
            if (trimmed != box) { box = trimmed; cells = EbookBoardImage.Cells(g, box); }
            var (light, dark) = EbookBoardImage.ParityBackgrounds(cells);
            var bg = Enumerable.Range(0, 64).Select(i => (i / 8 + i % 8) % 2 == 0 ? light : dark).ToArray();
            var bLight = BoardImageMath.Blur(light, 1.6); var bDark = BoardImageMath.Blur(dark, 1.6);
            var bgBlur = Enumerable.Range(0, 64).Select(i => (i / 8 + i % 8) % 2 == 0 ? bLight : bDark).ToArray();
            string? cnnBoard = null; float? conf = null; char[]? cnnGrid = null;
            if (cnn != null && full != null)
            {
                var reading = cnn.Read(full, box.X0 * 2, box.Y0 * 2, box.X1 * 2, box.Y1 * 2);
                cnnGrid = reading.Grid;
                cnnBoard = EbookBoardImage.GridToBoard(reading.Grid);
                if (cnnBoard != null) conf = reading.MinConfidence;
            }
            var board = cnnBoard ?? EbookBoardImage.Recognise(cells, bg, bgBlur, model);
            var altBoard = alt == null ? null : EbookBoardImage.Recognise(cells, bg, bgBlur, alt);
            if (cnnBoard != null) altBoard = null;   // das Netz gewinnt, wo es eine plausible Stellung liest
            string? raw = null;
            if (board == null && altBoard == null)
            {
                if (!keepUnread) continue;
                // Bestmögliche Lesung: die des Netzes, sonst die des Scan-Modells — ungeprüft.
                raw = EbookBoardImage.GridToPlacement(cnnGrid ?? EbookBoardImage.ReadGrid(cells, bg, bgBlur, model));
            }
            var mb = markerBox.X1 > 0 ? markerBox : box;
            list.Add(new ScanCandidate(page, (double)box.X0 / W, (double)box.Y0 / H, (double)(box.X1 - box.X0) / W, (double)(box.Y1 - box.Y0) / H,
                board, altBoard, full == null ? null : EbookBoardImage.SideMarker(full, (mb.X0 * 2, mb.Y0 * 2, mb.X1 * 2, mb.Y1 * 2), belowRight: markerBox.X1 > 0), conf, raw));
        }
        return list;
    }

    /// <summary>Längste Seite einer gerenderten Seite in Pixeln; größere Seiten werden mit weniger dpi gerendert (0.802.3).
    /// Ein Buch mit Scans, die als 61 × 45 Zoll ausgewiesen waren (Troitzky), schrieb 250 MB je Seite und füllte /tmp.</summary>
    internal const int MaxPagePixels = 6000;

    private bool Render(string pdf, int first, int last, string dir, CancellationToken ct)
    {
        var sizes = PageSizes(pdf, first, last, ct);
        var dpis = Enumerable.Range(first, last - first + 1).Select(p => RenderDpi(sizes.GetValueOrDefault(p))).ToList();
        if (dpis.All(d => d == Dpi * 2)) return RunPdftoppm(pdf, first, last, Dpi * 2, dir, ct);
        // Übergroße Seiten im Stapel: Seite für Seite, jede mit ihrer Auflösung.
        var ok = true;
        for (var i = 0; i < dpis.Count; i++) ok &= RunPdftoppm(pdf, first + i, first + i, dpis[i], dir, ct);
        return ok;
    }

    /// <summary>Kürzeste lange Seite in Pixeln (0.813.0): physisch winzige Seiten (Google-Vorschau-Auszüge, 2 × 3 Zoll mit einem
    /// 600-px-Bild) ergaben bei 300 dpi Bretter unter 100 px Arbeitsgröße, die die Brettsuche verwirft.</summary>
    internal const int MinPagePixels = 2400;

    /// <summary>dpi für eine Seite (Größe in Punkten): normal 300; kleine Seiten so fein, dass die lange Seite <see cref="MinPagePixels"/>
    /// erreicht; höchstens so viel, dass sie <see cref="MaxPagePixels"/> nicht übersteigt.</summary>
    internal static int RenderDpi((double W, double H) size)
    {
        var longest = Math.Max(size.W, size.H);
        if (longest <= 0) return Dpi * 2;
        var dpi = Math.Max(Dpi * 2, (int)Math.Ceiling(MinPagePixels * 72.0 / longest));
        return Math.Max(36, Math.Min(dpi, (int)(MaxPagePixels * 72.0 / longest)));
    }

    /// <summary>Seitengrößen (Punkte) aus <c>pdfinfo -f … -l …</c> („Page    3 size: 772.4 x 1210.35 pts“).</summary>
    private Dictionary<int, (double W, double H)> PageSizes(string pdf, int first, int last, CancellationToken ct)
    {
        // Gemeinsamer Starter (R3-006); fehlt pdfinfo, läuft es zu lange oder scheitert es → keine Größen (300 dpi wie bisher).
        var run = ExternalProcess.Run(_options.PdfinfoPath, ["-f", first.ToString(), "-l", last.ToString(), pdf], TimeSpan.FromSeconds(60), ct);
        if (run.NotFound || run.TimedOut)
        {
            _logger.LogDebug("pdfinfo Seitengrößen nicht lesbar: {Err}", run.StdErr);
            return [];
        }
        return ParsePageSizes(run.StdOut);
    }

    internal static Dictionary<int, (double W, double H)> ParsePageSizes(string pdfinfo)
    {
        var result = new Dictionary<int, (double W, double H)>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(pdfinfo,
                     @"^Page\s+(\d+)\s+size:\s+([\d.]+)\s+x\s+([\d.]+)", System.Text.RegularExpressions.RegexOptions.Multiline))
            result[int.Parse(m.Groups[1].Value)] = (double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
        return result;
    }

    /// <summary>Eine Seite als Graubild (0–1) in <paramref name="dpi"/> — für den Fließtext-Umwandler (Figurenzeichen, 0.818.0).
    /// <c>null</c>, wenn pdftoppm fehlt oder scheitert.</summary>
    public float[,]? RenderPage(string pdf, int page, int dpi, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rh-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            if (!RunPdftoppm(pdf, page, page, dpi, dir, ct)) return null;
            var file = Directory.GetFiles(dir, "*.pgm").FirstOrDefault();
            return file == null ? null : ReadPgm(File.ReadAllBytes(file));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { /* Zwischenordner, egal */ }
        }
    }

    private bool RunPdftoppm(string pdf, int first, int last, int dpi, string dir, CancellationToken ct)
    {
        // Gemeinsamer Starter (R3-006): Kill bei Zeitüberschreitung UND beim Abbruch von außen.
        var run = ExternalProcess.Run(_options.PdftoppmPath,
            ["-r", dpi.ToString(), "-gray", "-f", first.ToString(), "-l", last.ToString(), pdf, Path.Combine(dir, "p")],
            TimeSpan.FromSeconds(Math.Max(60, _options.CoverTimeoutSeconds * 4)), ct);
        if (run.NotFound)
        {
            _logger.LogWarning("pdftoppm nicht verfügbar: {Message}", run.StdErr);
            return false;
        }
        if (run.TimedOut)
        {
            _logger.LogWarning("pdftoppm Zeitüberschreitung bei Seiten {First}–{Last}", first, last);
            return false;
        }
        // Bewusst weiter „true“ bei Exit ≠ 0: scheitert in einem Stapel EINE Seite, liegen die übrigen gerendert im Ordner
        // und werden gelesen (fehlende Seiten fallen beim Einlesen auf).
        if (run.ExitCode != 0) _logger.LogWarning("pdftoppm exit {Code} bei Seiten {First}–{Last}: {Err}", run.ExitCode, first, last, run.StdErr);
        return true;
    }

    /// <summary>
    /// Graubild aus einer binären PGM (P5, 8 Bit) — pdftoppm schreibt sie zehnmal schneller als PNG (gemessen 3 s statt 39 s
    /// für 40 Seiten in 300 dpi). Wert k → k/255 wie <see cref="EbookBoardImage.DecodeGray"/>. <c>null</c> bei anderem Format.
    /// </summary>
    internal static float[,]? ReadPgm(byte[] data)
    {
        var pos = 0;
        string? Token()
        {
            while (pos < data.Length)
            {
                if (data[pos] == '#') { while (pos < data.Length && data[pos] != '\n') pos++; continue; }
                if (data[pos] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t') { pos++; continue; }
                break;
            }
            var start = pos;
            while (pos < data.Length && data[pos] is not ((byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')) pos++;
            return pos > start ? System.Text.Encoding.ASCII.GetString(data, start, pos - start) : null;
        }
        if (Token() != "P5") return null;
        if (!int.TryParse(Token(), out var w) || !int.TryParse(Token(), out var h) || !int.TryParse(Token(), out var max) || max != 255) return null;
        pos++;   // genau ein Trennzeichen vor den Daten
        if (w <= 0 || h <= 0 || data.Length - pos < (long)w * h) return null;
        var lut = new float[256];
        for (var k = 0; k < 256; k++) lut[k] = (float)((k * 0.299 + k * 0.587 + k * 0.114) / 255.0);
        var g = new float[h, w];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++) g[y, x] = lut[data[pos + y * w + x]];
        return g;
    }

    /// <summary>Seitennummer aus dem pdftoppm-Dateinamen („p-007.pgm“ → 7).</summary>
    internal static int PageOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(name[(dash + 1)..], out var n) ? n : 0;
    }
}
