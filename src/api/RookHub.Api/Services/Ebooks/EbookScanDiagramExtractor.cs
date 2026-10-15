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
    public sealed record ScanBoard(int Page, double X, double Y, double W, double H, string Board, char? Side, float? Confidence = null);

    /// <summary>Ein Brett mit den Lesungen beider Scan-Modelle (<c>null</c> = nicht plausibel).</summary>
    public sealed record ScanCandidate(int Page, double X, double Y, double W, double H, string? Board, string? AltBoard, char? Side, float? Confidence = null);

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
                        if (g != null && page > 0) foreach (var c in ScanPage(PreparePage(g), page, model, alt, g, EbookBoardCnn.Instance)) found.Add(c);
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
        var chosen = Choose(result, out var useAlt);
        _logger.LogInformation("Scan {Path}: {Count} Diagramme auf {Pages} Seiten (Modell {Model})", Path.GetFileName(pdfPath), chosen.Count, pageCount, useAlt ? "scan2" : "scan");
        return chosen;
    }

    /// <summary>Je Buch das Modell mit mehr plausiblen Brettern; ein Brett, das es nicht lesen kann, nimmt das andere.</summary>
    internal static List<ScanBoard> Choose(IReadOnlyList<ScanCandidate> candidates, out bool useAlt)
    {
        useAlt = candidates.Count(c => c.AltBoard != null) > candidates.Count(c => c.Board != null);
        var alt = useAlt;
        return candidates
            .Select(c => (c, board: alt ? c.AltBoard ?? c.Board : c.Board ?? c.AltBoard))
            .Where(x => x.board != null)
            .Select(x => new ScanBoard(x.c.Page, x.c.X, x.c.Y, x.c.W, x.c.H, x.board!, x.c.Side, x.board == x.c.Board ? x.c.Confidence : null))
            .ToList();
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
    public static List<ScanCandidate> ScanPage(float[,] g, int page, EbookBoardClassifier model, EbookBoardClassifier? alt = null, float[,]? full = null,
        EbookBoardCnn? cnn = null)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var list = new List<ScanCandidate>();
        foreach (var box in EbookBoardImage.FindBoards(g))
        {
            var cells = EbookBoardImage.Cells(g, box);
            if (EbookBoardImage.GridScore(cells) < MinGridScore) continue;
            var (light, dark) = EbookBoardImage.ParityBackgrounds(cells);
            var bg = Enumerable.Range(0, 64).Select(i => (i / 8 + i % 8) % 2 == 0 ? light : dark).ToArray();
            var bLight = BoardImageMath.Blur(light, 1.6); var bDark = BoardImageMath.Blur(dark, 1.6);
            var bgBlur = Enumerable.Range(0, 64).Select(i => (i / 8 + i % 8) % 2 == 0 ? bLight : bDark).ToArray();
            string? cnnBoard = null; float? conf = null;
            if (cnn != null && full != null)
            {
                var reading = cnn.Read(full, box.X0 * 2, box.Y0 * 2, box.X1 * 2, box.Y1 * 2);
                cnnBoard = EbookBoardImage.GridToBoard(reading.Grid);
                if (cnnBoard != null) conf = reading.MinConfidence;
            }
            var board = cnnBoard ?? EbookBoardImage.Recognise(cells, bg, bgBlur, model);
            var altBoard = alt == null ? null : EbookBoardImage.Recognise(cells, bg, bgBlur, alt);
            if (cnnBoard != null) altBoard = null;   // das Netz gewinnt, wo es eine plausible Stellung liest
            if (board == null && altBoard == null) continue;
            list.Add(new ScanCandidate(page, (double)box.X0 / W, (double)box.Y0 / H, (double)(box.X1 - box.X0) / W, (double)(box.Y1 - box.Y0) / H,
                board, altBoard, full == null ? null : EbookBoardImage.SideMarker(full, (box.X0 * 2, box.Y0 * 2, box.X1 * 2, box.Y1 * 2)), conf));
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

    /// <summary>dpi für eine Seite (Größe in Punkten): normal 300, höchstens so viel, dass die lange Seite <see cref="MaxPagePixels"/> nicht übersteigt.</summary>
    internal static int RenderDpi((double W, double H) size)
    {
        var longest = Math.Max(size.W, size.H);
        if (longest <= 0) return Dpi * 2;
        return Math.Max(36, Math.Min(Dpi * 2, (int)(MaxPagePixels * 72.0 / longest)));
    }

    /// <summary>Seitengrößen (Punkte) aus <c>pdfinfo -f … -l …</c> („Page    3 size: 772.4 x 1210.35 pts“).</summary>
    private Dictionary<int, (double W, double H)> PageSizes(string pdf, int first, int last, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(_options.PdfinfoPath) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var a in new[] { "-f", first.ToString(), "-l", last.ToString(), pdf }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync(ct);
            _ = p.StandardError.ReadToEndAsync(ct);
            if (!p.WaitForExit(TimeSpan.FromSeconds(60))) { try { p.Kill(true); } catch { /* weg */ } return []; }
            return ParsePageSizes(output.Result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "pdfinfo Seitengrößen nicht lesbar");
            return [];
        }
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

    private bool RunPdftoppm(string pdf, int first, int last, int dpi, string dir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_options.PdftoppmPath) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { "-r", dpi.ToString(), "-gray", "-f", first.ToString(), "-l", last.ToString(), pdf, Path.Combine(dir, "p") })
            psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogWarning("pdftoppm nicht verfügbar: {Message}", ex.Message);
            return false;
        }
        var err = p.StandardError.ReadToEndAsync(ct);
        _ = p.StandardOutput.ReadToEndAsync(ct);
        if (!p.WaitForExit(TimeSpan.FromSeconds(Math.Max(60, _options.CoverTimeoutSeconds * 4))))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* schon weg */ }
            _logger.LogWarning("pdftoppm Zeitüberschreitung bei Seiten {First}–{Last}", first, last);
            return false;
        }
        if (p.ExitCode != 0) _logger.LogWarning("pdftoppm exit {Code} bei Seiten {First}–{Last}: {Err}", p.ExitCode, first, last, err.IsCompleted ? err.Result : "");
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
