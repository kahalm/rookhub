using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Rendert das Titelbild eines Buchs nach <c>covers/&lt;Id&gt;.cover</c>: PDF über <c>pdftoppm</c> (die erste NICHT leere
/// der ersten 8 Seiten, 0.776.0 — alte Bücher und Scans beginnen oft mit Vorsatzblättern; 480 px breit, JPEG), DJVU über
/// <c>ddjvu</c> (die ersten 8 Seiten als PDF, dann wie PDF), EPUB und CBZ als Bild aus dem Zip (EPUB:
/// das im OPF ausgewiesene Cover, CBZ: das erste Bild). Nebenbei die Seitenzahl des PDFs (<c>pdfinfo</c>).
/// Werkzeuge fehlen → <see cref="CoverResult.Failed"/>, kein weiterer Versuch (<see cref="EbookCoverState.Failed"/>).
/// </summary>
public class EbookCoverRenderer
{
    private readonly EbookStorage _storage;
    private readonly EbookOptions _options;
    private readonly ILogger<EbookCoverRenderer> _logger;

    public EbookCoverRenderer(EbookStorage storage, IOptions<EbookOptions> options, ILogger<EbookCoverRenderer> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public sealed record CoverResult(bool Success, int? PageCount, string? Error)
    {
        public static CoverResult Failed(string error) => new(false, null, error);
    }

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    /// <summary>Welche Datei des Buchs das Titelbild liefert: PDF vor EPUB vor CBZ vor DJVU.</summary>
    internal static EbookFile? PickSource(IEnumerable<EbookFile> files)
    {
        string[] order = ["pdf", "epub", "cbz", "djvu"];
        return files.Where(f => EbookFormats.WithCover.Contains(f.Format))
            .OrderBy(f => Array.IndexOf(order, f.Format) is var i && i >= 0 ? i : 99)
            .FirstOrDefault();
    }

    public async Task<CoverResult> RenderAsync(Ebook ebook, CancellationToken ct)
    {
        var source = PickSource(ebook.Files);
        if (source == null) return CoverResult.Failed("no file with a cover");
        string input;
        try { input = _storage.ResolveFile(ebook, source); }
        catch (Exception ex) when (ex is InvalidOperationException or Exceptions.DomainValidationException) { return CoverResult.Failed(ex.Message); }
        if (!File.Exists(input)) return CoverResult.Failed("file missing");
        var output = _storage.CoverPath(ebook.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        try
        {
            return source.Format switch
            {
                "pdf" => await FromPdfAsync(input, output, ct),
                "djvu" => await FromDjvuAsync(input, output, ct),
                "epub" => FromEpub(input, output),
                "cbz" => FromCbz(input, output),
                _ => CoverResult.Failed("unsupported"),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "E-Book {EbookId}: Titelbild aus {Format} fehlgeschlagen", ebook.Id, source.Format);
            return CoverResult.Failed(ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Höchstens so viele Seiten werden nach einer nicht leeren Titelseite durchsucht (0.776.0).</summary>
    public const int MaxCoverPages = 8;

    /// <summary>Anteil dunkler Pixel (Grau &lt; 0,6), ab dem eine Seite als „nicht leer“ gilt. Leere Vorsatzblätter alter
    /// Bücher und Scans liegen bei 0, eine Titelseite mit wenig Text bei ≥ 0,5 %.</summary>
    public const double MinCoverInk = 0.002;

    private async Task<CoverResult> FromPdfAsync(string pdf, string output, CancellationToken ct)
    {
        int? pages = null;
        var info = await RunAsync(_options.PdfinfoPath, [pdf], ct);
        if (info.ExitCode == 0) pages = ParsePdfinfoPages(info.StdOut);
        // Viele alte Bücher und Scans beginnen mit leeren Seiten — die erste Seite mit Inhalt wird das Titelbild.
        var last = Math.Max(1, Math.Min(MaxCoverPages, pages ?? 1));
        string? fallback = null;
        for (var page = 1; page <= last; page++)
        {
            var prefix = output + ".p" + page;
            var run = await RunAsync(_options.PdftoppmPath,
                ["-singlefile", "-f", page.ToString(), "-l", page.ToString(), "-jpeg", "-jpegopt", "quality=82", "-scale-to", "480", pdf, prefix], ct);
            var produced = prefix + ".jpg";
            if (run.ExitCode != 0 || !File.Exists(produced))
            {
                if (File.Exists(produced)) File.Delete(produced);
                if (page == 1) return CoverResult.Failed($"pdftoppm exit {run.ExitCode}: {Trim(run.StdErr)}");
                break;
            }
            if (InkFraction(produced) >= MinCoverInk)
            {
                File.Move(produced, output, overwrite: true);
                if (fallback != null) File.Delete(fallback);
                return new CoverResult(true, pages, null);
            }
            if (fallback == null) fallback = produced; else File.Delete(produced);
        }
        if (fallback == null) return CoverResult.Failed("no page rendered");
        File.Move(fallback, output, overwrite: true);
        return new CoverResult(true, pages, null);
    }

    /// <summary>Anteil dunkler Pixel eines Bilds (0–1); nicht lesbar = 1 (gilt als Inhalt).</summary>
    internal static double InkFraction(string imagePath)
    {
        using var bmp = SkiaSharp.SKBitmap.Decode(imagePath);
        if (bmp == null || bmp.Width == 0 || bmp.Height == 0) return 1;
        long dark = 0;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Red * 0.299 + c.Green * 0.587 + c.Blue * 0.114 < 153) dark++;
            }
        return (double)dark / ((long)bmp.Width * bmp.Height);
    }

    private async Task<CoverResult> FromDjvuAsync(string djvu, string output, CancellationToken ct)
    {
        var tmpPdf = output + ".head.pdf";
        try
        {
            // Die ersten Seiten als ein PDF, dann wie beim PDF die erste nicht leere Seite.
            var run = await RunAsync(_options.DdjvuPath, ["-format=pdf", $"-page=1-{MaxCoverPages}", djvu, tmpPdf], ct);
            if (run.ExitCode != 0 || !File.Exists(tmpPdf))
            {
                // Kürzere Bücher: „1-8“ gibt es nicht — nur Seite 1.
                run = await RunAsync(_options.DdjvuPath, ["-format=pdf", "-page=1", djvu, tmpPdf], ct);
                if (run.ExitCode != 0 || !File.Exists(tmpPdf))
                    return CoverResult.Failed($"ddjvu exit {run.ExitCode}: {Trim(run.StdErr)}");
            }
            var r = await FromPdfAsync(tmpPdf, output, ct);
            return r.Success ? new CoverResult(true, null, null) : r;   // Seitenzahl des Ausschnitts ist keine Buchseitenzahl
        }
        finally
        {
            if (File.Exists(tmpPdf)) File.Delete(tmpPdf);
        }
    }

    /// <summary>„Pages:  312" aus der pdfinfo-Ausgabe.</summary>
    internal static int? ParsePdfinfoPages(string stdout)
    {
        var m = Regex.Match(stdout, @"^Pages:\s+(\d+)", RegexOptions.Multiline);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    private static CoverResult FromEpub(string epub, string output)
    {
        using var zip = ZipFile.OpenRead(epub);
        var entry = FindEpubCoverEntry(zip);
        if (entry == null) return CoverResult.Failed("epub without cover");
        Extract(entry, output);
        return new CoverResult(true, null, null);
    }

    private static CoverResult FromCbz(string cbz, string output)
    {
        using var zip = ZipFile.OpenRead(cbz);
        var entry = FindFirstImageEntry(zip);
        if (entry == null) return CoverResult.Failed("cbz without images");
        Extract(entry, output);
        return new CoverResult(true, null, null);
    }

    private static void Extract(ZipArchiveEntry entry, string output)
    {
        var tmp = output + ".tmp";
        using (var src = entry.Open())
        using (var dst = File.Create(tmp))
            src.CopyTo(dst);
        File.Move(tmp, output, overwrite: true);
    }

    /// <summary>Erstes Bild (alphabetisch) — bei Comic-Archiven die erste Seite.</summary>
    internal static ZipArchiveEntry? FindFirstImageEntry(ZipArchive zip) =>
        zip.Entries.Where(e => e.Length > 0 && ImageExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    /// <summary>
    /// Cover eines EPUB: <c>META-INF/container.xml</c> → OPF → <c>&lt;item properties="cover-image"&gt;</c> (EPUB 3) oder
    /// <c>&lt;meta name="cover" content="id"&gt;</c> (EPUB 2) → das Bild. Ohne beides: ein Bild, dessen Name „cover" enthält;
    /// sonst das erste Bild im Archiv.
    /// </summary>
    internal static ZipArchiveEntry? FindEpubCoverEntry(ZipArchive zip)
    {
        try
        {
            var container = zip.GetEntry("META-INF/container.xml");
            if (container != null)
            {
                XNamespace cns = "urn:oasis:names:tc:opendocument:xmlns:container";
                var cdoc = Load(container);
                var opfPath = cdoc?.Descendants(cns + "rootfile").Select(r => (string?)r.Attribute("full-path")).FirstOrDefault(p => !string.IsNullOrEmpty(p));
                var opf = opfPath == null ? null : zip.GetEntry(opfPath);
                if (opf != null)
                {
                    XNamespace ons = "http://www.idpf.org/2007/opf";
                    var odoc = Load(opf);
                    var items = odoc?.Descendants(ons + "item").ToList() ?? [];
                    string? href = items.FirstOrDefault(i => ((string?)i.Attribute("properties") ?? "").Split(' ').Contains("cover-image"))?.Attribute("href")?.Value;
                    if (href == null)
                    {
                        var coverId = odoc?.Descendants(ons + "meta")
                            .FirstOrDefault(m => string.Equals((string?)m.Attribute("name"), "cover", StringComparison.OrdinalIgnoreCase))
                            ?.Attribute("content")?.Value;
                        if (coverId != null)
                            href = items.FirstOrDefault(i => (string?)i.Attribute("id") == coverId)?.Attribute("href")?.Value;
                    }
                    if (href != null)
                    {
                        var baseDir = Path.GetDirectoryName(opfPath!)?.Replace('\\', '/') ?? string.Empty;
                        var full = string.IsNullOrEmpty(baseDir) ? href : baseDir + "/" + href;
                        full = Uri.UnescapeDataString(full);
                        var entry = zip.GetEntry(full) ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(href, StringComparison.OrdinalIgnoreCase));
                        if (entry != null && entry.Length > 0) return entry;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException)
        {
            // kaputtes Manifest → Rückfall auf den Namen
        }
        var byName = zip.Entries.Where(e => e.Length > 0 && ImageExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
            .FirstOrDefault(e => e.Name.Contains("cover", StringComparison.OrdinalIgnoreCase));
        return byName ?? FindFirstImageEntry(zip);
    }

    private static XDocument? Load(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

    private async Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new ProcessResult(-1, string.Empty, $"{exe} not available: {ex.Message}");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.CoverTimeoutSeconds)));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* schon weg */ }
            return new ProcessResult(-2, string.Empty, "timeout");
        }
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static string Trim(string s) => s.Length <= 200 ? s.Trim() : s[..200].Trim() + "…";
}
