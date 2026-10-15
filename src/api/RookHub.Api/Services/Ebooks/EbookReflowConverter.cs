namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Fließtext-EPUB aus einem Text-PDF (0.802.0, Prototyp): liest das PDF (<see cref="EbookReflowPdfReader"/>), baut daraus
/// Absätze, Überschriften und Kapitel (<see cref="EbookReflowLayout"/>) und schreibt ein EPUB 3 (<see cref="EbookEpubWriter"/>),
/// in dem jedes erkannte Diagramm als Brettbild aus seiner FEN steht. Für Bücher mit Schach-Font (Diagramme exakt); reine
/// Scans haben keine Textebene und werden abgelehnt (<see cref="MinCharsPerPage"/>).
/// </summary>
public static class EbookReflowConverter
{
    /// <summary>So viele Textzeichen je Seite braucht ein PDF im Schnitt — darunter ist es ein Scan (ohne OCR).</summary>
    public const int MinCharsPerPage = 300;

    /// <summary>Wandelt die PDF-Datei um und schreibt das EPUB nach <paramref name="output"/>.</summary>
    public static ReflowResult Convert(string pdfPath, string title, string author, string identifier,
        IReadOnlyList<ReflowDiagram> diagrams, Stream output, CancellationToken ct)
    {
        var doc = EbookReflowPdfReader.Read(pdfPath, ct);
        return Convert(doc, title, author, identifier, diagrams, output, ct);
    }

    /// <summary>Wie oben, aus schon gelesenen Seiten (Tests).</summary>
    public static ReflowResult Convert(EbookReflowPdfReader.Document doc, string title, string author, string identifier,
        IReadOnlyList<ReflowDiagram> diagrams, Stream output, CancellationToken ct)
    {
        var chars = doc.Pages.Sum(p => p.Glyphs.Sum(g => g.Text.Length));
        if (doc.Pages.Count == 0 || chars < MinCharsPerPage * doc.Pages.Count)
            throw new ReflowNotPossibleException($"The PDF has too little text ({chars} characters on {doc.Pages.Count} pages) — probably a scan.");
        ct.ThrowIfCancellationRequested();
        var blocks = EbookReflowLayout.Blocks(doc.Pages, diagrams, doc.Outline.Select(o => o.Title).Append(title));
        ct.ThrowIfCancellationRequested();
        var chapters = EbookReflowLayout.Chapters(blocks, doc.Outline, doc.Pages.Count);
        var book = new ReflowBook(
            string.IsNullOrWhiteSpace(title) ? (doc.Title ?? "Untitled") : title,
            string.IsNullOrWhiteSpace(author) ? (doc.Author ?? string.Empty) : author,
            EbookReflowLayout.GuessLanguage(blocks), identifier, chapters);
        var images = EbookEpubWriter.Write(book, output, DateTime.UtcNow);
        return new ReflowResult(doc.Pages.Count, chapters.Count, blocks.Count(b => b is ReflowParagraph), chars, images);
    }
}

/// <summary>Das PDF taugt nicht für ein Fließtext-EPUB (keine Textebene).</summary>
public sealed class ReflowNotPossibleException(string message) : Exception(message);
