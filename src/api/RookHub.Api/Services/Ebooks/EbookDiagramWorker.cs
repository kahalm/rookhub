using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Erkennt in kleinen Portionen die Diagramme der Bücher (<see cref="EbookDiagramExtractor"/>), die Upload und Import nur
/// VORMERKEN (<see cref="EbookDiagramState.Pending"/>). Ein 400-Seiten-Buch braucht Sekunden; ein Buch ohne PDF oder ohne
/// bekannten Schach-Font wird <see cref="EbookDiagramState.Unsupported"/> (Stufe 2: Bildverfahren).
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
            var pdf = ebook.Files.FirstOrDefault(f => f.Format == "pdf");
            if (pdf == null) { ebook.DiagramState = EbookDiagramState.Unsupported; await db.SaveChangesAsync(ct); continue; }
            try
            {
                var path = storage.ResolveFile(ebook, pdf);
                if (!File.Exists(path)) { ebook.DiagramState = EbookDiagramState.Failed; await db.SaveChangesAsync(ct); continue; }
                var result = await Task.Run(() => extractor.Extract(path, ebook.Id, ct), ct);
                db.EbookDiagrams.RemoveRange(db.EbookDiagrams.Where(d => d.EbookId == ebook.Id));
                db.EbookDiagrams.AddRange(result.Diagrams);
                ebook.DiagramCount = result.Diagrams.Count;
                ebook.DiagramState = result.FontFound ? EbookDiagramState.Done : EbookDiagramState.Unsupported;
                if (result.Pages > 0 && ebook.PageCount == null) ebook.PageCount = result.Pages;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "E-Book {EbookId}: Diagramme nicht lesbar", ebook.Id);
                ebook.DiagramState = EbookDiagramState.Failed;
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
