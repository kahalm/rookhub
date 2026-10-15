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
            if (ebook.DiagramsCurated)
            {
                // Nachgetragene Diagramme (Opus/Hand) nie überschreiben — nur den Stand zurücksetzen.
                ebook.DiagramCount = await db.EbookDiagrams.CountAsync(d => d.EbookId == ebook.Id, ct);
                ebook.DiagramState = EbookDiagramState.Done;
                await db.SaveChangesAsync(ct);
                Logger.LogInformation("E-Book {EbookId}: Diagramme nachgetragen, Neuerkennung übersprungen", ebook.Id);
                continue;
            }
            // PDF (Schach-Font) vor EPUB (Brettbilder, 0.775.0); DJVU-Scans erst mit Stufe 2.
            // Schach-Fonts stehen nur im ORIGINAL-PDF; gerendert (Scans) wird die leichte Lesefassung, falls es eine gibt.
            var file = ebook.Files.FirstOrDefault(f => f.Format == "pdf" && !f.Derived) ?? ebook.Files.FirstOrDefault(f => f.Format == "pdf")
                ?? ebook.Files.FirstOrDefault(f => f.Format == "epub");
            var reading = EbookFormats.ReadingPdf(ebook.Files);
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
                    : await Task.Run(() => extractor.Extract(path, ebook.Id, ct,
                        reading != null && reading != file ? storage.ResolveFile(ebook, reading) : null), ct);
                // PDF und EPUB im selben Buch (0.793.0): EIN Diagrammsatz aus dem PDF (exakt), verknüpft mit den Bildern im EPUB.
                // Ist das PDF nur ein Scan, sind die sauberen EPUB-Bilder die bessere Quelle (0.795.0) — sonst verlor ein
                // zusammengelegtes Buch fast alle Diagramme (Swindler: 44 Scan-Bretter statt 905 EPUB-Bilder).
                var epub = ebook.Files.FirstOrDefault(f => f.Format == "epub");
                if (file.Format == "pdf" && epub != null)
                {
                    try
                    {
                        var epubPath = storage.ResolveFile(ebook, epub);
                        var er = await Task.Run(() => services.GetRequiredService<EbookEpubDiagramExtractor>().Extract(epubPath, ebook.Id, ct), ct);
                        if (PreferEpub(result.Diagrams, er.Diagrams.Count))
                        {
                            Logger.LogInformation("E-Book {EbookId}: {Images} EPUB-Diagramme statt {Total} aus dem gescannten PDF",
                                ebook.Id, er.Diagrams.Count, result.Diagrams.Count);
                            result = er with { Pages = result.Pages };
                        }
                        else if (result.Diagrams.Count > 0)
                        {
                            var ordered = result.Diagrams.OrderBy(d => d.Page).ThenBy(d => d.Y).ThenBy(d => d.X).ToList();
                            var linked = EbookFormatLinker.Link(ordered, er.Diagrams);
                            Logger.LogInformation("E-Book {EbookId}: {Linked} von {Total} PDF-Diagrammen mit dem EPUB verknüpft ({Images} EPUB-Bilder)",
                                ebook.Id, linked, ordered.Count, er.Diagrams.Count);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { Logger.LogWarning(ex, "E-Book {EbookId}: EPUB-Verknüpfung fehlgeschlagen", ebook.Id); }
                }
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
            // Kurs-Verweise auf die neuen Diagramme umhängen (gleiche Stellung, nächste Seite) — sonst zeigte „Im Buch“
            // nach jedem Neulesen ins Leere.
            if (ebook.DiagramState == EbookDiagramState.Done) await CourseEbookLinker.RelinkEbookAsync(db, ebook.Id, ct);
            await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
        }
    }

    /// <summary>EPUB-Bilder statt PDF-Diagramme, wenn das PDF keinen Schach-Font hat (nur Scan-/CNN-Lesungen) und das EPUB
    /// mindestens gleich viele Bretter liefert. Ein PDF mit Schach-Font bleibt die Quelle — es liest die Stellungen exakt.</summary>
    internal static bool PreferEpub(IReadOnlyCollection<EbookDiagram> pdf, int epubCount)
        => epubCount > 0 && epubCount >= pdf.Count && pdf.All(d => d.FontFamily is "scan" or "cnn");
}
