using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Der ERKENNUNGS-Teil des <see cref="EbookDiagramWorker"/> (0.870.0 herausgezogen): welche Datei(en) eines Buchs gelesen
/// werden und wie aus ihnen EIN Diagrammsatz wird — ohne Datenbank. Denselben Code ruft der externe Diagramm-Worker
/// (<c>tools/DiagramCli</c>) auf dem Laptop auf; das Speichern macht in beiden Fällen <see cref="EbookDiagramResults"/>.
/// </summary>
public static class EbookDiagramRecognizer
{
    /// <summary>Welche Dateien die Erkennung braucht: <see cref="Primary"/> wird gelesen (Original-PDF vor abgeleitetem PDF vor
    /// echtem EPUB), <see cref="Reading"/> ist die leichte Lesefassung für das Rendern von Scans (nur, wenn sie eine andere
    /// Datei als das Primär-PDF ist), <see cref="Epub"/> das echte EPUB, mit dem ein PDF verknüpft wird.</summary>
    public sealed record Plan(EbookFile? Primary, EbookFile? Reading, EbookFile? Epub);

    /// <summary>Dateiauswahl wie bisher im Worker. PDF (Schach-Font) vor EPUB (Brettbilder, 0.775.0); DJVU-Scans erst mit
    /// der PDF-Fassung. Schach-Fonts stehen nur im ORIGINAL-PDF; gerendert (Scans) wird die leichte Lesefassung, falls es eine gibt.</summary>
    public static Plan Choose(IReadOnlyCollection<EbookFile> files)
    {
        var primary = files.FirstOrDefault(f => f.Format == "pdf" && !f.Derived) ?? files.FirstOrDefault(f => f.Format == "pdf")
            ?? files.FirstOrDefault(f => f.Format == "epub" && !f.Derived);
        if (primary == null) return new Plan(null, null, null);
        var reading = EbookFormats.ReadingPdf(files);
        var isPdf = primary.Format == "pdf";
        // Nur ein ECHTES EPUB: das Fließtext-EPUB (0.802.0) entsteht aus diesen Diagrammen, nicht umgekehrt.
        var epub = isPdf ? files.FirstOrDefault(f => f.Format == "epub" && !f.Derived) : null;
        return new Plan(primary, isPdf && reading != null && reading != primary ? reading : null, epub);
    }

    /// <summary>
    /// Erkennt die Diagramme aus lokalen Dateien. <paramref name="primaryFormat"/> „epub“ liest Brettbilder, sonst PDF (Schach-Font,
    /// ohne Font Scan über <paramref name="readingPath"/>). Gibt es zum PDF ein EPUB (<paramref name="epubPath"/>, 0.793.0), wird
    /// EIN Diagrammsatz daraus: ist das PDF nur ein Scan, sind die sauberen EPUB-Bilder die bessere Quelle (0.795.0,
    /// <see cref="PreferEpub"/>), sonst werden die PDF-Diagramme mit den EPUB-Bildern verknüpft. Ein Fehler beim EPUB lässt das
    /// PDF-Ergebnis gelten; ein Fehler beim Primär-Format wirft.
    /// </summary>
    public static async Task<EbookDiagramExtractor.Result> RecognizeAsync(string primaryPath, string primaryFormat, string? readingPath,
        string? epubPath, int ebookId, bool supplement, EbookDiagramExtractor pdfExtractor, Func<EbookEpubDiagramExtractor> epubExtractor,
        ILogger logger, CancellationToken ct)
    {
        var result = primaryFormat == "epub"
            ? await Task.Run(() => epubExtractor().Extract(primaryPath, ebookId, ct, keepUnread: supplement), ct)
            : await Task.Run(() => pdfExtractor.Extract(primaryPath, ebookId, ct, readingPath, keepUnread: supplement), ct);
        if (primaryFormat != "pdf" || epubPath == null) return result;
        try
        {
            var er = await Task.Run(() => epubExtractor().Extract(epubPath, ebookId, ct, keepUnread: supplement), ct);
            // Ungelesene EPUB-Bilder zählen und verknüpfen nicht — ihre Stellung stimmt ja nicht.
            var readableEpub = er.Diagrams.Where(EbookDiagramVisibility.IsReadable).ToList();
            if (PreferEpub(result.Diagrams.Where(EbookDiagramVisibility.IsReadable).ToList(), readableEpub.Count))
            {
                logger.LogInformation("E-Book {EbookId}: {Images} EPUB-Diagramme statt {Total} aus dem gescannten PDF",
                    ebookId, er.Diagrams.Count, result.Diagrams.Count);
                result = er with { Pages = result.Pages };
            }
            else if (result.Diagrams.Count > 0)
            {
                var ordered = result.Diagrams.Where(EbookDiagramVisibility.IsReadable).OrderBy(d => d.Page).ThenBy(d => d.Y).ThenBy(d => d.X).ToList();
                var linked = EbookFormatLinker.Link(ordered, readableEpub);
                logger.LogInformation("E-Book {EbookId}: {Linked} von {Total} PDF-Diagrammen mit dem EPUB verknüpft ({Images} EPUB-Bilder)",
                    ebookId, linked, ordered.Count, readableEpub.Count);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "E-Book {EbookId}: EPUB-Verknüpfung fehlgeschlagen", ebookId); }
        return result;
    }

    /// <summary>EPUB-Bilder statt PDF-Diagramme, wenn das PDF keinen Schach-Font hat (nur Scan-/CNN-Lesungen) und das EPUB
    /// mindestens gleich viele Bretter liefert. Ein PDF mit Schach-Font bleibt die Quelle — es liest die Stellungen exakt.</summary>
    public static bool PreferEpub(IReadOnlyCollection<EbookDiagram> pdf, int epubCount)
        => epubCount > 0 && epubCount >= pdf.Count && pdf.All(d => d.FontFamily is "scan" or "cnn");
}
