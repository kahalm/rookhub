using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RookHub.Api.Services.Ebooks;

// ---------------------------------------------------------------------------
// rookhub-diagrams — Diagramm-Erkennung eines Buchs auf einem fremden Rechner (0.870.0, externe Diagramm-Worker).
//
//   rookhub-diagrams --ebook-id 123 --primary buch.pdf [--reading lesefassung.pdf] [--epub buch.epub]
//                    [--format pdf|epub] [--supplement] [--threads 6] [--poppler C:\poppler\bin] [--out ergebnis.json]
//   rookhub-diagrams --version
//
// Die Dateien und ihre Rollen kommen aus dem Auftrag (POST /api/diagram-worker/claim → files[].role). Ausgabe: genau der
// Rumpf von POST /api/diagram-worker/jobs/{job}/result (DiagramWorkerResultDto, JSON). Protokoll auf stderr.
// Rückgabe: 0 fertig · 1 Erkennung gescheitert · 2 Aufruf falsch · 3 Poppler (pdftoppm/pdfinfo) fehlt · 4 Datei fehlt.
// ---------------------------------------------------------------------------

var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
var opts = Args.Parse(args);
if (opts == null) { Console.Error.WriteLine(Args.Usage); return 2; }
if (opts.ShowVersion) { Console.WriteLine(version); return 0; }

var threads = Math.Clamp(opts.Threads ?? Math.Max(1, Environment.ProcessorCount - 2), 1, 256);
using var loggerFactory = LoggerFactory.Create(b => b
    .SetMinimumLevel(opts.Verbose ? LogLevel.Debug : LogLevel.Information)
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
    .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)
    .AddFilter("Microsoft", LogLevel.Warning));
// Alles Protokoll auf stderr — stdout gehört dem Ergebnis, wenn --out fehlt.
var log = loggerFactory.CreateLogger("rookhub-diagrams");

foreach (var f in new[] { opts.Primary, opts.Reading, opts.Epub })
    if (f != null && !File.Exists(f)) { log.LogError("Datei fehlt: {File}", f); return 4; }

var format = opts.Format ?? (Path.GetExtension(opts.Primary!).Equals(".epub", StringComparison.OrdinalIgnoreCase) ? "epub" : "pdf");
var ebookOptions = new EbookOptions { ScanParallelism = threads, ScanDiagrams = true };
if (opts.Poppler != null)
{
    ebookOptions.PdftoppmPath = Path.Combine(opts.Poppler, OperatingSystem.IsWindows() ? "pdftoppm.exe" : "pdftoppm");
    ebookOptions.PdfinfoPath = Path.Combine(opts.Poppler, OperatingSystem.IsWindows() ? "pdfinfo.exe" : "pdfinfo");
}
// Ohne pdftoppm liefe ein Scan still mit 0 Diagrammen durch — und das Buch stünde als „fertig“ da. Lieber hart abbrechen.
if (format == "pdf")
    foreach (var tool in new[] { ebookOptions.PdftoppmPath, ebookOptions.PdfinfoPath })
        if (!Tool.Runs(tool)) { log.LogError("{Tool} nicht gefunden — Poppler installieren (start.ps1) oder --poppler <bin-Ordner>", tool); return 3; }

// Kerne: die Seitenstapel laufen parallel (threads), je Stapel rechnet das Netz einfädig.
EbookBoardCnn.IntraOpThreads = 1;
var options = Options.Create(ebookOptions);
var scanner = new EbookScanDiagramExtractor(options, loggerFactory.CreateLogger<EbookScanDiagramExtractor>());
var pdf = new EbookDiagramExtractor(loggerFactory.CreateLogger<EbookDiagramExtractor>(), scanner);
var epub = new EbookEpubDiagramExtractor(loggerFactory.CreateLogger<EbookEpubDiagramExtractor>());

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var sw = Stopwatch.StartNew();
try
{
    log.LogInformation("rookhub-diagrams {Version}: E-Book {EbookId}, {Format}, {Threads} Threads{Supplement}",
        version, opts.EbookId, format, threads, opts.Supplement ? ", ergänzen" : "");
    var result = await EbookDiagramRecognizer.RecognizeAsync(opts.Primary!, format, opts.Reading, opts.Epub, opts.EbookId,
        opts.Supplement, pdf, () => epub, log, cts.Token);
    var dto = EbookDiagramWorkerService.ToDto(opts.EbookId, result, version);
    var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    if (opts.Out != null)
    {
        var tmp = opts.Out + ".tmp";
        await File.WriteAllTextAsync(tmp, json);
        File.Move(tmp, opts.Out, overwrite: true);
    }
    else Console.WriteLine(json);
    var error = EbookDiagramWorkerService.Validate(dto);
    if (error != null) log.LogWarning("Ergebnis würde vom Server abgelehnt: {Error}", error);
    log.LogInformation("Fertig: {Count} Diagramme, {Pages} Seiten, {Solutions} Lösungen in {Seconds:F0} s",
        dto.Diagrams.Count, dto.Pages, dto.Solutions, sw.Elapsed.TotalSeconds);
    return 0;
}
catch (OperationCanceledException)
{
    log.LogWarning("Abgebrochen");
    return 1;
}
catch (Exception ex)
{
    log.LogError(ex, "Erkennung gescheitert");
    return 1;
}

internal sealed record Args(int EbookId, string? Primary, string? Reading, string? Epub, string? Format, bool Supplement, int? Threads,
    string? Poppler, string? Out, bool Verbose, bool ShowVersion)
{
    public const string Usage = """
        rookhub-diagrams --ebook-id <id> --primary <datei> [--reading <pdf>] [--epub <epub>] [--format pdf|epub]
                         [--supplement] [--threads <n>] [--poppler <bin-ordner>] [--out <ergebnis.json>] [--verbose]
        rookhub-diagrams --version
        """;

    public static Args? Parse(string[] argv)
    {
        int id = 0; string? primary = null, reading = null, epub = null, format = null, poppler = null, output = null;
        int? threads = null; bool supplement = false, verbose = false, version = false;
        for (var i = 0; i < argv.Length; i++)
        {
            string? Next() => i + 1 < argv.Length ? argv[++i] : null;
            switch (argv[i])
            {
                case "--version": version = true; break;
                case "--ebook-id": if (!int.TryParse(Next(), out id)) return null; break;
                case "--primary": primary = Next(); break;
                case "--reading": reading = Next(); break;
                case "--epub": epub = Next(); break;
                case "--format": format = Next()?.ToLowerInvariant(); if (format is not ("pdf" or "epub")) return null; break;
                case "--supplement": supplement = true; break;
                case "--threads": if (!int.TryParse(Next(), out var t) || t < 1) return null; threads = t; break;
                case "--poppler": poppler = Next(); break;
                case "--out": output = Next(); break;
                case "--verbose": verbose = true; break;
                default: return null;
            }
        }
        if (version) return new Args(0, null, null, null, null, false, null, null, null, false, true);
        if (id <= 0 || string.IsNullOrEmpty(primary)) return null;
        return new Args(id, primary, reading, epub, format, supplement, threads, poppler, output, verbose, false);
    }
}

internal static class Tool
{
    /// <summary>Startet <paramref name="exe"/> mit <c>-v</c> (Poppler druckt die Version) — <c>false</c>, wenn es nicht startet.</summary>
    public static bool Runs(string exe)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, "-v")
                { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return false;
            _ = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(15_000)) { try { p.Kill(true); } catch { /* weg */ } return false; }
            return true;
        }
        catch (Exception) { return false; }
    }
}
