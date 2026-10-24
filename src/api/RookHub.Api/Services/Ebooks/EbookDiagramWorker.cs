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
    /// (&gt; <see cref="HugeBookPages"/> Seiten) ans Ende, sonst nach Id.
    /// </summary>
    internal static IQueryable<Ebook> QueueOrder(IQueryable<Ebook> pending) => pending
        .OrderBy(b => b.Source == EbookSource.Upload && !b.DiagramSupplement ? 0 : !b.DiagramSupplement && b.DiagramCount == 0 ? 1 : 2)
        .ThenBy(b => b.PageCount != null && b.PageCount > HugeBookPages ? 1 : 0)
        .ThenBy(b => b.Id);

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var storage = services.GetRequiredService<EbookStorage>();
        var extractor = services.GetRequiredService<EbookDiagramExtractor>();
        var batch = await QueueOrder(db.Ebooks.Include(b => b.Files).Where(b => b.DiagramState == EbookDiagramState.Pending))
            .Take(2).ToListAsync(ct);
        foreach (var ebook in batch)
        {
            ct.ThrowIfCancellationRequested();
            var supplement = ebook.DiagramSupplement;
            if (ebook.DiagramsCurated && !supplement)
            {
                // Nachgetragene Diagramme (Opus/Hand) nie überschreiben — nur den Stand zurücksetzen.
                ebook.DiagramCount = await db.EbookDiagrams.Where(d => d.EbookId == ebook.Id).Readable().CountAsync(ct);
                ebook.DiagramState = EbookDiagramState.Done;
                await db.SaveChangesAsync(ct);
                Logger.LogInformation("E-Book {EbookId}: Diagramme nachgetragen, Neuerkennung übersprungen", ebook.Id);
                continue;
            }
            // PDF (Schach-Font) vor EPUB (Brettbilder, 0.775.0); DJVU-Scans erst mit Stufe 2.
            // Schach-Fonts stehen nur im ORIGINAL-PDF; gerendert (Scans) wird die leichte Lesefassung, falls es eine gibt.
            var file = ebook.Files.FirstOrDefault(f => f.Format == "pdf" && !f.Derived) ?? ebook.Files.FirstOrDefault(f => f.Format == "pdf")
                ?? ebook.Files.FirstOrDefault(f => f.Format == "epub" && !f.Derived);
            var reading = EbookFormats.ReadingPdf(ebook.Files);
            if (file == null)
            {
                if (supplement) { await FinishSupplementAsync(db, ebook, null, ct); continue; }
                ebook.DiagramState = EbookDiagramState.Unsupported;
                await db.SaveChangesAsync(ct);
                await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
                continue;
            }
            try
            {
                var path = storage.ResolveFile(ebook, file);
                if (!File.Exists(path))
                {
                    if (supplement) { await FinishSupplementAsync(db, ebook, null, ct); continue; }
                    ebook.DiagramState = EbookDiagramState.Failed; await db.SaveChangesAsync(ct); continue;
                }
                var result = file.Format == "epub"
                    ? await Task.Run(() => services.GetRequiredService<EbookEpubDiagramExtractor>().Extract(path, ebook.Id, ct, keepUnread: supplement), ct)
                    : await Task.Run(() => extractor.Extract(path, ebook.Id, ct,
                        reading != null && reading != file ? storage.ResolveFile(ebook, reading) : null, keepUnread: supplement), ct);
                // PDF und EPUB im selben Buch (0.793.0): EIN Diagrammsatz aus dem PDF (exakt), verknüpft mit den Bildern im EPUB.
                // Ist das PDF nur ein Scan, sind die sauberen EPUB-Bilder die bessere Quelle (0.795.0) — sonst verlor ein
                // zusammengelegtes Buch fast alle Diagramme (Swindler: 44 Scan-Bretter statt 905 EPUB-Bilder).
                // Nur ein ECHTES EPUB: das Fließtext-EPUB (0.802.0) entsteht aus diesen Diagrammen, nicht umgekehrt.
                var epub = ebook.Files.FirstOrDefault(f => f.Format == "epub" && !f.Derived);
                if (file.Format == "pdf" && epub != null)
                {
                    try
                    {
                        var epubPath = storage.ResolveFile(ebook, epub);
                        var er = await Task.Run(() => services.GetRequiredService<EbookEpubDiagramExtractor>().Extract(epubPath, ebook.Id, ct, keepUnread: supplement), ct);
                        // Ungelesene EPUB-Bilder zählen und verknüpfen nicht — ihre Stellung stimmt ja nicht.
                        var readableEpub = er.Diagrams.Where(EbookDiagramVisibility.IsReadable).ToList();
                        if (PreferEpub(result.Diagrams.Where(EbookDiagramVisibility.IsReadable).ToList(), readableEpub.Count))
                        {
                            Logger.LogInformation("E-Book {EbookId}: {Images} EPUB-Diagramme statt {Total} aus dem gescannten PDF",
                                ebook.Id, er.Diagrams.Count, result.Diagrams.Count);
                            result = er with { Pages = result.Pages };
                        }
                        else if (result.Diagrams.Count > 0)
                        {
                            var ordered = result.Diagrams.Where(EbookDiagramVisibility.IsReadable).OrderBy(d => d.Page).ThenBy(d => d.Y).ThenBy(d => d.X).ToList();
                            var linked = EbookFormatLinker.Link(ordered, readableEpub);
                            Logger.LogInformation("E-Book {EbookId}: {Linked} von {Total} PDF-Diagrammen mit dem EPUB verknüpft ({Images} EPUB-Bilder)",
                                ebook.Id, linked, ordered.Count, readableEpub.Count);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { Logger.LogWarning(ex, "E-Book {EbookId}: EPUB-Verknüpfung fehlgeschlagen", ebook.Id); }
                }
                if (supplement)
                {
                    if (result.Pages > 0 && ebook.PageCount == null) ebook.PageCount = result.Pages;
                    await FinishSupplementAsync(db, ebook, result, ct);
                    if (ebook.DiagramState == EbookDiagramState.Done) await ReflowAsync(services, ebook, ct);
                    await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
                    continue;
                }
                db.EbookDiagrams.RemoveRange(db.EbookDiagrams.Where(d => d.EbookId == ebook.Id));
                db.EbookDiagrams.AddRange(result.Diagrams);
                ebook.DiagramCount = result.Diagrams.Count(EbookDiagramVisibility.IsReadable);
                ebook.DiagramState = result.FontFound ? EbookDiagramState.Done : EbookDiagramState.Unsupported;
                ebook.SolutionCheckState = EbookDiagramState.Pending;   // neue Diagramme → Stockfish prüft neu
                ebook.SolutionCheckQueuedAt = DateTime.UtcNow;
                if (result.Pages > 0 && ebook.PageCount == null) ebook.PageCount = result.Pages;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "E-Book {EbookId}: Diagramme nicht lesbar", ebook.Id);
                if (supplement)
                {
                    // Ergänzen gescheitert: die vorhandenen Diagramme bleiben sichtbar (kein „Failed“ über einem fertigen Buch).
                    db.ChangeTracker.Clear();
                    var fresh = await db.Ebooks.Include(b => b.Files).FirstAsync(b => b.Id == ebook.Id, ct);
                    await FinishSupplementAsync(db, fresh, null, ct);
                    continue;
                }
                ebook.DiagramState = EbookDiagramState.Failed;
            }
            await db.SaveChangesAsync(ct);
            // Kurs-Verweise auf die neuen Diagramme umhängen (gleiche Stellung, nächste Seite) — sonst zeigte „Im Buch“
            // nach jedem Neulesen ins Leere.
            if (ebook.DiagramState == EbookDiagramState.Done) await CourseEbookLinker.RelinkEbookAsync(db, ebook.Id, ct);
            if (ebook.DiagramState == EbookDiagramState.Done) await ReflowAsync(services, ebook, ct);
            await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
        }
    }

    /// <summary>
    /// Ergänzen-Modus abschließen (0.811.0, <see cref="Ebook.DiagramSupplement"/>): vorhandene Diagramme bleiben, neue aus
    /// <paramref name="result"/> kommen nur an freien Stellen dazu (<see cref="EbookDiagramMerge"/>). Danach ist das Feld
    /// gelöscht, der Stand <see cref="EbookDiagramState.Done"/> (sobald es überhaupt Diagramme gibt), die Zählung neu und —
    /// wenn etwas dazukam — die Stockfish-Prüfung eingereiht. <see cref="Ebook.DiagramsCurated"/> bleibt, wie es war.
    /// <paramref name="result"/> = <c>null</c>: Erkennung war nicht möglich, nur abschließen.
    /// </summary>
    internal async Task FinishSupplementAsync(AppDbContext db, Ebook ebook, EbookDiagramExtractor.Result? result, CancellationToken ct)
    {
        var existing = await db.EbookDiagrams.Where(d => d.EbookId == ebook.Id).ToListAsync(ct);
        var outcome = result == null ? new EbookDiagramMerge.Outcome([], 0) : EbookDiagramMerge.Merge(existing, result.Diagrams);
        foreach (var d in outcome.Added) d.EbookId = ebook.Id;
        db.EbookDiagrams.AddRange(outcome.Added);
        ebook.DiagramSupplement = false;
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
        Logger.LogInformation("E-Book {EbookId}: ergänzt — {Added} neue Diagramme ({Readable} lesbar, {Unread} ungelesen), {Kept} vorhandene, {Filled} mit nachgetragener Reihenfolge",
            ebook.Id, outcome.Added.Count, addedReadable, outcome.Added.Count - addedReadable, existing.Count, outcome.YFilled);
        // Ids vorhandener Diagramme ändern sich nicht; nur offene Kurs-Verweise (null) können jetzt ein Ziel finden.
        if (addedReadable > 0) await CourseEbookLinker.RelinkEbookAsync(db, ebook.Id, ct);
    }

    /// <summary>Fließtext-EPUB nach der Erkennung (0.802.0): neu bauen, wenn es schon eins gibt (die neuen Diagramme brauchen
    /// ihre Bildverweise), sonst nur mit <see cref="EbookOptions.ReflowAfterDiagrams"/> und nur bei Schach-Font-Diagrammen.
    /// Ein Fehler hier lässt die Erkennung gelten.</summary>
    private async Task ReflowAsync(IServiceProvider services, Ebook ebook, CancellationToken ct)
    {
        if (!EbookReflowService.Eligible(ebook)) return;
        var hasDerived = ebook.Files.Any(f => f.Format == "epub" && f.Derived);
        if (!hasDerived)
        {
            var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EbookOptions>>().Value;
            if (!options.ReflowAfterDiagrams) return;
            var fontDiagrams = await services.GetRequiredService<AppDbContext>().EbookDiagrams
                .AnyAsync(d => d.EbookId == ebook.Id && d.FontFamily != null && ChessDiagramFonts.FamilyNames.Contains(d.FontFamily), ct);
            if (!fontDiagrams) return;
        }
        try { await services.GetRequiredService<EbookReflowService>().ConvertAsync(ebook.Id, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { Logger.LogWarning(ex, "E-Book {EbookId}: Fließtext-EPUB nicht gebaut", ebook.Id); }
    }

    /// <summary>EPUB-Bilder statt PDF-Diagramme, wenn das PDF keinen Schach-Font hat (nur Scan-/CNN-Lesungen) und das EPUB
    /// mindestens gleich viele Bretter liefert. Ein PDF mit Schach-Font bleibt die Quelle — es liest die Stellungen exakt.</summary>
    internal static bool PreferEpub(IReadOnlyCollection<EbookDiagram> pdf, int epubCount)
        => epubCount > 0 && epubCount >= pdf.Count && pdf.All(d => d.FontFamily is "scan" or "cnn");
}
