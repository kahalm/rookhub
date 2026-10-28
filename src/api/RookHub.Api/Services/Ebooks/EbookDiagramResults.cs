using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Der SPEICHER-Teil der Diagramm-Erkennung (0.870.0 aus dem <see cref="EbookDiagramWorker"/> herausgezogen): was nach einer
/// Erkennung mit dem Buch passiert — ersetzen oder (Ergänzen-Modus, <see cref="Ebook.DiagramSupplement"/>) zusammenführen, Zählung,
/// Stand, Stockfish-Prüfung einreihen, Kurs-Verweise umhängen, Fließtext-EPUB, Benachrichtigung. Der eigene Worker und der
/// Ergebnis-Endpunkt der externen Worker (<c>POST /api/diagram-worker/jobs/{job}/result</c>) gehen durch DENSELBEN Code.
/// Jeder Abschluss löscht Leihe, Vorrang und die Fehlermarke der externen Worker (<see cref="EndRun"/>).
/// </summary>
public sealed class EbookDiagramResults
{
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;

    public EbookDiagramResults(IServiceProvider services, ILogger logger)
    {
        _services = services;
        _logger = logger;
    }

    private AppDbContext Db => _services.GetRequiredService<AppDbContext>();

    /// <summary>Lauf abgeschlossen: Leihe, Vorrang (<see cref="Ebook.DiagramPriority"/>) und Fehlermarke der externen Worker weg.</summary>
    internal static void EndRun(AppDbContext db, Ebook ebook)
    {
        EbookDiagramLease.Clear(db, ebook);
        ebook.DiagramPriority = false;
        ebook.DiagramExternalFailed = false;
    }

    /// <summary>Nachgetragene Diagramme (Opus/Hand) nie überschreiben — nur den Stand zurücksetzen.</summary>
    public async Task CuratedSkippedAsync(Ebook ebook, CancellationToken ct)
    {
        var db = Db;
        ebook.DiagramCount = await db.EbookDiagrams.Where(d => d.EbookId == ebook.Id).Readable().CountAsync(ct);
        ebook.DiagramState = EbookDiagramState.Done;
        EndRun(db, ebook);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("E-Book {EbookId}: Diagramme nachgetragen, Neuerkennung übersprungen", ebook.Id);
    }

    /// <summary>Kein lesbares Format (kein PDF, kein echtes EPUB).</summary>
    public async Task NoFileAsync(Ebook ebook, CancellationToken ct)
    {
        var db = Db;
        EndRun(db, ebook);
        if (ebook.DiagramSupplement) { await FinishSupplementAsync(db, ebook, null, ct); return; }
        ebook.DiagramState = EbookDiagramState.Unsupported;
        await db.SaveChangesAsync(ct);
        await EbookProcessing.NotifyIfReadyAsync(_services, ebook, ct);
    }

    /// <summary>Die Datei fehlt auf der Platte.</summary>
    public async Task FileMissingAsync(Ebook ebook, CancellationToken ct)
    {
        var db = Db;
        EndRun(db, ebook);
        if (ebook.DiagramSupplement) { await FinishSupplementAsync(db, ebook, null, ct); return; }
        ebook.DiagramState = EbookDiagramState.Failed;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Erkennung gelungen: ergänzen oder ersetzen, dann Kurs-Verweise, Fließtext-EPUB, Benachrichtigung.</summary>
    public async Task SucceededAsync(Ebook ebook, EbookDiagramExtractor.Result result, CancellationToken ct)
    {
        var db = Db;
        EndRun(db, ebook);
        if (ebook.DiagramSupplement)
        {
            if (result.Pages > 0 && ebook.PageCount == null) ebook.PageCount = result.Pages;
            await FinishSupplementAsync(db, ebook, result, ct);
            if (ebook.DiagramState == EbookDiagramState.Done) await ReflowAsync(ebook, ct);
            await EbookProcessing.NotifyIfReadyAsync(_services, ebook, ct);
            return;
        }
        foreach (var d in result.Diagrams) d.EbookId = ebook.Id;
        db.EbookDiagrams.RemoveRange(db.EbookDiagrams.Where(d => d.EbookId == ebook.Id));
        db.EbookDiagrams.AddRange(result.Diagrams);
        ebook.DiagramCount = result.Diagrams.Count(EbookDiagramVisibility.IsReadable);
        ebook.DiagramState = result.FontFound ? EbookDiagramState.Done : EbookDiagramState.Unsupported;
        ebook.SolutionCheckState = EbookDiagramState.Pending;   // neue Diagramme → Stockfish prüft neu
        ebook.SolutionCheckQueuedAt = DateTime.UtcNow;
        if (result.Pages > 0 && ebook.PageCount == null) ebook.PageCount = result.Pages;
        await SavedAsync(ebook, ct);
    }

    /// <summary>Erkennung gescheitert. Ergänzen: die vorhandenen Diagramme bleiben sichtbar (kein „Failed“ über einem fertigen
    /// Buch) — dafür wird der Tracker geleert und das Buch frisch geladen. Sonst <see cref="EbookDiagramState.Failed"/>.</summary>
    public async Task FailedAsync(Ebook ebook, CancellationToken ct)
    {
        var db = Db;
        if (ebook.DiagramSupplement)
        {
            db.ChangeTracker.Clear();
            var fresh = await db.Ebooks.Include(b => b.Files).FirstAsync(b => b.Id == ebook.Id, ct);
            EndRun(db, fresh);
            await FinishSupplementAsync(db, fresh, null, ct);
            return;
        }
        EndRun(db, ebook);
        ebook.DiagramState = EbookDiagramState.Failed;
        await SavedAsync(ebook, ct);
    }

    private async Task SavedAsync(Ebook ebook, CancellationToken ct)
    {
        var db = Db;
        await db.SaveChangesAsync(ct);
        // Kurs-Verweise auf die neuen Diagramme umhängen (gleiche Stellung, nächste Seite) — sonst zeigte „Im Buch“
        // nach jedem Neulesen ins Leere.
        if (ebook.DiagramState == EbookDiagramState.Done) await CourseEbookLinker.RelinkEbookAsync(db, ebook.Id, ct);
        if (ebook.DiagramState == EbookDiagramState.Done) await ReflowAsync(ebook, ct);
        await EbookProcessing.NotifyIfReadyAsync(_services, ebook, ct);
    }

    /// <summary>
    /// Ergänzen-Modus abschließen (0.811.0, <see cref="Ebook.DiagramSupplement"/>): vorhandene Diagramme bleiben, neue aus
    /// <paramref name="result"/> kommen nur an freien Stellen dazu (<see cref="EbookDiagramMerge"/>). Danach ist das Feld
    /// gelöscht, der Stand <see cref="EbookDiagramState.Done"/> (sobald es überhaupt Diagramme gibt), die Zählung neu und —
    /// wenn etwas dazukam — die Stockfish-Prüfung eingereiht. <see cref="Ebook.DiagramsCurated"/> bleibt, wie es war.
    /// <paramref name="result"/> = <c>null</c>: Erkennung war nicht möglich, nur abschließen.
    /// </summary>
    public async Task FinishSupplementAsync(AppDbContext db, Ebook ebook, EbookDiagramExtractor.Result? result, CancellationToken ct)
    {
        var existing = await db.EbookDiagrams.Where(d => d.EbookId == ebook.Id).ToListAsync(ct);
        var outcome = result == null ? new EbookDiagramMerge.Outcome([], 0) : EbookDiagramMerge.Merge(existing, result.Diagrams);
        foreach (var d in outcome.Added) d.EbookId = ebook.Id;
        db.EbookDiagrams.AddRange(outcome.Added);
        ebook.DiagramSupplement = false;
        EndRun(db, ebook);
        var any = existing.Count + outcome.Added.Count > 0;
        ebook.DiagramState = any || (result?.FontFound ?? false) ? EbookDiagramState.Done
            : result == null ? EbookDiagramState.Failed : EbookDiagramState.Unsupported;
        ebook.DiagramCount = existing.Concat(outcome.Added).Count(EbookDiagramVisibility.IsReadable);
        var addedReadable = outcome.Added.Count(EbookDiagramVisibility.IsReadable);
        if (addedReadable > 0)
        {
            ebook.SolutionCheckState = EbookDiagramState.Pending;   // neue Diagramme → Stockfish prüft (nur ungeprüfte rechnen neu)
            ebook.SolutionCheckQueuedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("E-Book {EbookId}: ergänzt — {Added} neue Diagramme ({Readable} lesbar, {Unread} ungelesen), {Kept} vorhandene, {Filled} mit nachgetragener Reihenfolge",
            ebook.Id, outcome.Added.Count, addedReadable, outcome.Added.Count - addedReadable, existing.Count, outcome.YFilled);
        // Ids vorhandener Diagramme ändern sich nicht; nur offene Kurs-Verweise (null) können jetzt ein Ziel finden.
        if (addedReadable > 0) await CourseEbookLinker.RelinkEbookAsync(db, ebook.Id, ct);
    }

    /// <summary>Fließtext-EPUB nach der Erkennung (0.802.0): neu bauen, wenn es schon eins gibt (die neuen Diagramme brauchen
    /// ihre Bildverweise), sonst nur mit <see cref="EbookOptions.ReflowAfterDiagrams"/> und nur bei Schach-Font-Diagrammen.
    /// Ein Fehler hier lässt die Erkennung gelten.</summary>
    private async Task ReflowAsync(Ebook ebook, CancellationToken ct)
    {
        if (!EbookReflowService.Eligible(ebook)) return;
        var hasDerived = ebook.Files.Any(f => f.Format == "epub" && f.Derived);
        if (!hasDerived)
        {
            var options = _services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EbookOptions>>().Value;
            if (!options.ReflowAfterDiagrams) return;
            var fontDiagrams = await Db.EbookDiagrams
                .AnyAsync(d => d.EbookId == ebook.Id && d.FontFamily != null && ChessDiagramFonts.FamilyNames.Contains(d.FontFamily), ct);
            if (!fontDiagrams) return;
        }
        try { await _services.GetRequiredService<EbookReflowService>().ConvertAsync(ebook.Id, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogWarning(ex, "E-Book {EbookId}: Fließtext-EPUB nicht gebaut", ebook.Id); }
    }
}
