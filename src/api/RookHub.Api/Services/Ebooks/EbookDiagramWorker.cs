using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Erkennt in kleinen Portionen die Diagramme der Bücher (<see cref="EbookDiagramExtractor"/>), die Upload und Import nur
/// VORMERKEN (<see cref="EbookDiagramState.Pending"/>). Ein 400-Seiten-Buch braucht Sekunden. EPUBs (0.775.0) liest der
/// <see cref="EbookEpubDiagramExtractor"/> aus den Brettbildern; ein PDF ohne bekannten Schach-Font, ein EPUB ohne Brettbilder
/// oder ein reines DJVU wird <see cref="EbookDiagramState.Unsupported"/> (Scans: Stufe 2).
/// </summary>
public class EbookDiagramWorker : PeriodicWorker
{
    public EbookDiagramWorker(IServiceScopeFactory scopeFactory, ILogger<EbookDiagramWorker> logger) : base(scopeFactory, logger) { }

    protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.FromSeconds(30));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromSeconds(60));
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "E-Book-Diagramme: Durchlauf fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var storage = services.GetRequiredService<EbookStorage>();
        var extractor = services.GetRequiredService<EbookDiagramExtractor>();
        var batch = await db.Ebooks.Include(b => b.Files)
            .Where(b => b.DiagramState == EbookDiagramState.Pending)
            .OrderBy(b => b.Id).Take(2).ToListAsync(ct);
        foreach (var ebook in batch)
        {
            ct.ThrowIfCancellationRequested();
            // PDF (Schach-Font) vor EPUB (Brettbilder, 0.775.0); DJVU-Scans erst mit Stufe 2.
            var file = ebook.Files.FirstOrDefault(f => f.Format == "pdf") ?? ebook.Files.FirstOrDefault(f => f.Format == "epub");
            if (file == null)
            {
                ebook.DiagramState = EbookDiagramState.Unsupported;
                await db.SaveChangesAsync(ct);
                await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
                continue;
            }
            try
            {
                var path = storage.ResolveFile(ebook, file);
                if (!File.Exists(path)) { ebook.DiagramState = EbookDiagramState.Failed; await db.SaveChangesAsync(ct); continue; }
                var result = file.Format == "epub"
                    ? await Task.Run(() => services.GetRequiredService<EbookEpubDiagramExtractor>().Extract(path, ebook.Id, ct), ct)
                    : await Task.Run(() => extractor.Extract(path, ebook.Id, ct), ct);
                db.EbookDiagrams.RemoveRange(db.EbookDiagrams.Where(d => d.EbookId == ebook.Id));
                db.EbookDiagrams.AddRange(result.Diagrams);
                ebook.DiagramCount = result.Diagrams.Count;
                ebook.DiagramState = result.FontFound ? EbookDiagramState.Done : EbookDiagramState.Unsupported;
                ebook.SolutionCheckState = EbookDiagramState.Pending;   // neue Diagramme → Stockfish prüft neu
                if (result.Pages > 0 && ebook.PageCount == null) ebook.PageCount = result.Pages;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "E-Book {EbookId}: Diagramme nicht lesbar", ebook.Id);
                ebook.DiagramState = EbookDiagramState.Failed;
            }
            await db.SaveChangesAsync(ct);
            await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
        }
    }
}
