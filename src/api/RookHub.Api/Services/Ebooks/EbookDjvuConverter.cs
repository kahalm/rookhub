using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Wandelt Bücher, die NUR als DJVU vorliegen, in ein PDF um (<c>ddjvu -format=pdf</c>, djvulibre im API-Image), damit der
/// Browser-Leser sie zeigt (0.770.0). Ziel: <c>&lt;Root&gt;/converted/&lt;id&gt;.pdf</c>, eingetragen als zusätzliche
/// <see cref="EbookFile"/> mit <see cref="EbookFile.Derived"/>. Gemessen: 227 Seiten in 13 s, ~6× die DJVU-Größe.
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
        if (djvu == null || ebook.Files.Any(f => f.Format == "pdf"))
        {
            ebook.ConversionState = EbookConversionState.NotNeeded;
            await db.SaveChangesAsync(ct);
            return;
        }
        var relative = EbookStorage.ConvertedPdfPath(ebook.Id);
        var target = _storage.ResolvePath(EbookSource.Upload, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".part";
        var ok = await RunAsync(options.DdjvuPath, ["-format=pdf", _storage.ResolveFile(ebook, djvu), tmp], ct);
        if (!ok || !File.Exists(tmp) || new FileInfo(tmp).Length == 0)
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            ebook.ConversionState = EbookConversionState.Failed;
            Logger.LogWarning("E-Book {EbookId}: DJVU→PDF fehlgeschlagen", ebook.Id);
            await db.SaveChangesAsync(ct);
            await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
            return;
        }
        File.Move(tmp, target, overwrite: true);
        ebook.Files.Add(new EbookFile
        {
            Format = "pdf", RelativePath = relative, Derived = true,
            FileName = Path.GetFileNameWithoutExtension(djvu.FileName) + ".pdf", SizeBytes = new FileInfo(target).Length,
        });
        ebook.ConversionState = EbookConversionState.Done;
        // Seitenzahl kommt mit dem Titelbild (pdfinfo); Diagramme: Scans → wird „unsupported", aber geprüft.
        if (ebook.CoverState != EbookCoverState.Done) ebook.CoverState = EbookCoverState.Pending;
        ebook.DiagramState = EbookDiagramState.Pending;
        ebook.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        Logger.LogInformation("E-Book {EbookId}: DJVU in PDF umgewandelt ({Bytes} Bytes)", ebook.Id, new FileInfo(target).Length);
    }

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
