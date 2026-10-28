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

    /// <summary>Riesenbände (Nachschlagewerke wie „Chess Publishing ECO“, 3 891–6 789 Seiten) brauchen Stunden.</summary>
    internal const int HugeBookPages = 1000;

    /// <summary>
    /// Reihenfolge der Warteschlange (0.826.x): ein Nutzer, der gerade hochgeladen hat, wartet nicht hinter Admin-Läufen —
    /// am 23.10. stand ein Upload 10 h hinter 92 Neuerkennungen und einem 6 789-Seiten-Band. Zuerst Uploads, dann erstmals
    /// eingelesene Bibliotheksbücher (noch keine Diagramme), zuletzt Ergänzungen/Neuerkennungen; je Gruppe Riesenbände
    /// (&gt; <see cref="HugeBookPages"/> Seiten) ans Ende, sonst nach Id. Davor (0.870.0) Bücher mit <see cref="Ebook.DiagramPriority"/>:
    /// ein Admin hat sie eben neu eingereiht und wartet auf das Ergebnis. Dieselbe Reihenfolge gilt für die externen Worker.
    /// </summary>
    internal static IQueryable<Ebook> QueueOrder(IQueryable<Ebook> pending) => pending
        .OrderBy(b => b.DiagramPriority ? 0 : 1)
        .ThenBy(b => b.Source == EbookSource.Upload && !b.DiagramSupplement ? 0 : !b.DiagramSupplement && b.DiagramCount == 0 ? 1 : 2)
        .ThenBy(b => b.PageCount != null && b.PageCount > HugeBookPages ? 1 : 0)
        .ThenBy(b => b.Id);

    /// <summary>Leihen des eigenen Workers aus einem früheren Prozess (Neustart mitten in einem Buch) einmal zurückgeben.</summary>
    private bool _staleLeasesReleased;

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var storage = services.GetRequiredService<EbookStorage>();
        var extractor = services.GetRequiredService<EbookDiagramExtractor>();
        var results = new EbookDiagramResults(services, Logger);
        if (!_staleLeasesReleased)
        {
            await EbookDiagramLease.ReleaseOwnerAsync(db, EbookDiagramLease.ServerOwner, ct);
            _staleLeasesReleased = true;
        }
        // Bücher, die ein externer Diagramm-Worker gerade rechnet (0.870.0), bleiben liegen.
        var batch = await QueueOrder(EbookDiagramLease.Free(
                db.Ebooks.Include(b => b.Files).Where(b => b.DiagramState == EbookDiagramState.Pending), DateTime.UtcNow))
            .Take(2).ToListAsync(ct);
        foreach (var ebook in batch)
        {
            ct.ThrowIfCancellationRequested();
            if (await EbookDiagramLease.TryClaimAsync(db, ebook.Id, EbookDiagramLease.ServerOwner, EbookDiagramLease.ServerLease, ct, ebook) == null)
                continue;   // inzwischen von einem externen Worker übernommen
            var supplement = ebook.DiagramSupplement;
            if (ebook.DiagramsCurated && !supplement)
            {
                await results.CuratedSkippedAsync(ebook, ct);
                continue;
            }
            var plan = EbookDiagramRecognizer.Choose(ebook.Files);
            if (plan.Primary == null)
            {
                await results.NoFileAsync(ebook, ct);
                continue;
            }
            try
            {
                var path = storage.ResolveFile(ebook, plan.Primary);
                if (!File.Exists(path))
                {
                    await results.FileMissingAsync(ebook, ct);
                    continue;
                }
                var result = await EbookDiagramRecognizer.RecognizeAsync(path, plan.Primary.Format,
                    plan.Reading != null ? storage.ResolveFile(ebook, plan.Reading) : null,
                    plan.Epub != null ? storage.ResolveFile(ebook, plan.Epub) : null,
                    ebook.Id, supplement, extractor, services.GetRequiredService<EbookEpubDiagramExtractor>, Logger, ct);
                await results.SucceededAsync(ebook, result, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "E-Book {EbookId}: Diagramme nicht lesbar", ebook.Id);
                await results.FailedAsync(ebook, ct);
            }
        }
    }

    /// <summary>Siehe <see cref="EbookDiagramResults.FinishSupplementAsync"/> (Tests rufen den Ergänzen-Abschluss hier auf).</summary>
    internal Task FinishSupplementAsync(AppDbContext db, Ebook ebook, EbookDiagramExtractor.Result? result, CancellationToken ct)
        => new EbookDiagramResults(new SingleDbServices(db), Logger).FinishSupplementAsync(db, ebook, result, ct);

    /// <summary>Siehe <see cref="EbookDiagramRecognizer.PreferEpub"/>.</summary>
    internal static bool PreferEpub(IReadOnlyCollection<EbookDiagram> pdf, int epubCount) => EbookDiagramRecognizer.PreferEpub(pdf, epubCount);

    /// <summary>Kleinster Dienstanbieter für <see cref="FinishSupplementAsync"/>: nur der übergebene Kontext.</summary>
    private sealed class SingleDbServices(AppDbContext db) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(AppDbContext) ? db : null;
    }
}
