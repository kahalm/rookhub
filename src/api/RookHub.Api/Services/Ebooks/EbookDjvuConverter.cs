using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Wandelt Bücher, die NUR als DJVU vorliegen, in ein PDF um (<c>ddjvu -format=pdf</c>, djvulibre im API-Image), damit der
/// Browser-Leser sie zeigt (0.770.0). Seit 0.775.4 auch übergroße Scan-PDFs (<see cref="EbookFormats.IsHeavyPdf"/>): aus
/// der beiliegenden DJVU oder neu gerendert in 150 dpi — der Leser nimmt dann diese Lesefassung (<see cref="EbookFormats.Primary"/>). Ziel: <c>&lt;Root&gt;/converted/&lt;id&gt;.pdf</c>, eingetragen als zusätzliche
/// <see cref="EbookFile"/> mit <see cref="EbookFile.Derived"/>. Seit 0.837.5 mit <see cref="ReadingDpi"/> und JPEG-Qualität
/// <see cref="DjvuQuality"/>: ohne diese Angaben schrieb <c>ddjvu</c> die Scan-Auflösung fast unkomprimiert (Euwe's Best
/// Games: 28-MB-DJVU → 622-MB-PDF, der Leser lud minutenlang), jetzt etwa ein Siebtel. Ein Buch, das schon eine
/// Lesefassung aus der DJVU hat und wieder <see cref="EbookConversionState.Pending"/> ist, bekommt sie neu (gleicher Pfad,
/// gleiche Seiten — Diagramme und Lesestände bleiben).
/// Ein Buch je Durchlauf, Zeitdeckel 30 min; danach Titelbild/Diagramme neu vormerken.
/// </summary>
public class EbookDjvuConverter : PeriodicWorker
{
    public static readonly TimeSpan MaxRuntime = TimeSpan.FromMinutes(30);
    private readonly EbookStorage _storage;

    public EbookDjvuConverter(IServiceScopeFactory scopeFactory, EbookStorage storage, ILogger<EbookDjvuConverter> logger)
        : base(scopeFactory, logger) => _storage = storage;

    protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.FromSeconds(30));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromSeconds(90));
    protected override bool Enabled => _storage.Root != null;
    protected override void LogDisabled() => Logger.LogInformation("E-Book-Umwandlung DJVU→PDF: kein Ebooks:Root konfiguriert — Dienst aus");
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "E-Book-Umwandlung DJVU→PDF: Durchlauf fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var options = services.GetRequiredService<IOptions<EbookOptions>>().Value;
        var ebook = await db.Ebooks.Include(b => b.Files)
            .Where(b => b.ConversionState == EbookConversionState.Pending).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
        if (ebook == null) return;
        var djvu = ebook.Files.FirstOrDefault(f => f.Format == "djvu");
        var heavy = ebook.Files.FirstOrDefault(f => EbookFormats.IsHeavyPdf(f, ebook.PageCount));
        // Neu erzeugen (0.837.5): vorhandene Lesefassung aus der DJVU, das Buch steht wieder auf „ausstehend“
        var redo = djvu != null ? ebook.Files.FirstOrDefault(f => f.Format == "pdf" && f.Derived) : null;
        if (redo == null && !EbookFormats.NeedsReadingPdf(ebook.Files, ebook.PageCount))
        {
            ebook.ConversionState = EbookConversionState.NotNeeded;
            await db.SaveChangesAsync(ct);
            return;
        }
        var relative = EbookStorage.ConvertedPdfPath(ebook.Id);
        var target = _storage.ResolvePath(EbookSource.Upload, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".part";
        // DJVU (klein, verlustarm) vor dem Neu-Rendern des übergroßen PDFs.
        var ok = djvu != null
            ? await RunAsync(options.DdjvuPath, ["-format=pdf", $"-scale={ReadingDpi}", $"-quality={DjvuQuality}", _storage.ResolveFile(ebook, djvu), tmp], ct)
            : heavy != null && await RasterizeAsync(options, _storage.ResolveFile(ebook, heavy), tmp, ct);
        if (!ok || !File.Exists(tmp) || new FileInfo(tmp).Length == 0)
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            ebook.ConversionState = EbookConversionState.Failed;
            Logger.LogWarning("E-Book {EbookId}: Lesefassung als PDF fehlgeschlagen", ebook.Id);
            await db.SaveChangesAsync(ct);
            await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
            return;
        }
        var source = djvu ?? heavy!;
        File.Move(tmp, target, overwrite: true);
        if (redo != null)
        {
            // Nur die Datei ist neu: Seiten, Titelbild und Diagramme bleiben gültig
            redo.SizeBytes = new FileInfo(target).Length;
            ebook.ConversionState = EbookConversionState.Done;
            ebook.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            Logger.LogInformation("E-Book {EbookId}: Lesefassung aus DJVU neu erzeugt ({Bytes} Bytes)", ebook.Id, redo.SizeBytes);
            return;
        }
        ebook.Files.Add(new EbookFile
        {
            Format = "pdf", RelativePath = relative, Derived = true,
            FileName = Path.GetFileNameWithoutExtension(source.FileName) + (djvu == null ? " (lesefassung).pdf" : ".pdf"), SizeBytes = new FileInfo(target).Length,
        });
        ebook.ConversionState = EbookConversionState.Done;
        // Seitenzahl kommt mit dem Titelbild (pdfinfo); Diagramme: Scans → wird „unsupported", aber geprüft.
        if (ebook.CoverState != EbookCoverState.Done) ebook.CoverState = EbookCoverState.Pending;
        ebook.DiagramState = EbookDiagramState.Pending;
        ebook.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        Logger.LogInformation("E-Book {EbookId}: Lesefassung als PDF aus {Format} ({Bytes} Bytes)", ebook.Id, source.Format, new FileInfo(target).Length);
    }

    /// <summary>Lesefassung eines übergroßen Scan-PDFs: Seiten mit <c>pdftoppm</c> in 150 dpi als JPEG (Qualität 80), in
    /// Portionen zu 20 Seiten, und unverändert in ein neues PDF (<see cref="ImagePdfWriter"/>).</summary>
    private async Task<bool> RasterizeAsync(EbookOptions options, string pdf, string output, CancellationToken ct)
    {
        const int dpi = ReadingDpi, batch = 20;
        var dir = output + ".pages";
        Directory.CreateDirectory(dir);
        try
        {
            using var writer = new ImagePdfWriter(File.Create(output));
            for (var first = 1; ; first += batch)
            {
                var last = first + batch - 1;
                var ok = await RunAsync(options.PdftoppmPath,
                    ["-r", dpi.ToString(), "-jpeg", "-jpegopt", "quality=80", "-f", first.ToString(), "-l", last.ToString(), pdf, Path.Combine(dir, "p")], ct);
                var files = Directory.GetFiles(dir, "*.jpg").OrderBy(f => f, StringComparer.Ordinal).ToList();
                foreach (var f in files)
                {
                    writer.AddJpegPage(await File.ReadAllBytesAsync(f, ct), dpi);
                    File.Delete(f);
                }
                // pdftoppm meldet bei kaputten Bilddaten oft einen Fehler, liefert die Seiten aber trotzdem.
                if (files.Count < batch) { if (!ok && files.Count == 0 && writer.PageCount == 0) return false; break; }
            }
            if (writer.PageCount == 0) return false;
            writer.Finish();
            return true;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { /* egal */ }
        }
    }

    /// <summary>Auflösung der Lesefassung übergroßer Scans und (seit 0.837.5) der DJVU-Umwandlung.</summary>
    public const int ReadingDpi = 150;

    /// <summary>JPEG-Qualität der Hintergrundebene bei der DJVU-Umwandlung (Diagramme bleiben bei 150 dpi gut lesbar).</summary>
    public const int DjvuQuality = 60;

    private async Task<bool> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (System.ComponentModel.Win32Exception ex) { Logger.LogWarning(ex, "ddjvu nicht verfügbar"); return false; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(MaxRuntime);
        _ = p.StandardOutput.ReadToEndAsync(timeout.Token);
        _ = p.StandardError.ReadToEndAsync(timeout.Token);
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* weg */ }
            return false;
        }
        return p.ExitCode == 0;
    }
}
