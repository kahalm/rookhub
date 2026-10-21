using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Legt zu einem Text-PDF mit Schach-Font ein Fließtext-EPUB an (0.802.0, Prototyp; <see cref="EbookReflowConverter"/>) und
/// speichert es als ABGELEITETE Datei <c>converted/&lt;id&gt;.epub</c> unter <c>Ebooks:Root</c> (<see cref="EbookFile.Derived"/>,
/// wie die PDF-Fassung der DJVU-Bücher) — wird mit dem Buch gelöscht, ein zweiter Lauf ersetzt sie. Weil der Leser EPUB
/// bevorzugt (<see cref="EbookFormats.Preference"/>), liest man das Buch danach fließend; „Als PDF lesen“ wechselt zurück.
/// Die Diagramme des Buchs bleiben, wie sie sind (auch kuratierte); sie bekommen nur <see cref="EbookDiagram.ImageRef"/> +
/// <see cref="EbookDiagram.EpubChapter"/> auf ihr Brettbild im EPUB, damit der Doppelklick im EPUB-Leser sie findet.
/// <para>Abgelehnt (400 <c>ebook_reflow_unsupported</c>): kein Original-PDF, schon ein echtes EPUB, Diagramme noch nicht
/// erkannt, keine Textebene (Scan).</para>
/// </summary>
public class EbookReflowService
{
    private readonly AppDbContext _db;
    private readonly EbookStorage _storage;
    private readonly EbookScanDiagramExtractor _pages;
    private readonly ILogger<EbookReflowService> _logger;

    public EbookReflowService(AppDbContext db, EbookStorage storage, EbookScanDiagramExtractor pages, ILogger<EbookReflowService> logger)
    {
        _db = db;
        _storage = storage;
        _pages = pages;
        _logger = logger;
    }

    public sealed record Outcome(int EbookId, int FileId, long SizeBytes, int Pages, int Chapters, int Paragraphs, int Diagrams, int DiagramsLinked);

    /// <summary>Darf zu diesem Buch ein Fließtext-EPUB entstehen? (Original-PDF da, kein echtes EPUB.)</summary>
    public static bool Eligible(Ebook ebook) =>
        ebook.Files.Any(f => f.Format == "pdf" && !f.Derived) && !ebook.Files.Any(f => f.Format == "epub" && !f.Derived);

    /// <summary>Baut das EPUB (synchron, Sekunden bis eine Minute) und ersetzt ein vorhandenes abgeleitetes.</summary>
    public async Task<Outcome> ConvertAsync(int ebookId, CancellationToken ct)
    {
        var ebook = await _db.Ebooks.Include(b => b.Files).FirstOrDefaultAsync(b => b.Id == ebookId, ct)
            ?? throw new NotFoundException("E-book not found.");
        if (_storage.Root == null)
            throw new DomainValidationException("The e-book storage is not configured on this server.") { Code = ApiErrorCodes.EbooksNotConfigured };
        var pdf = ebook.Files.FirstOrDefault(f => f.Format == "pdf" && !f.Derived);
        if (pdf == null) throw Unsupported("The book has no original PDF.");
        if (ebook.Files.Any(f => f.Format == "epub" && !f.Derived)) throw Unsupported("The book already has an EPUB.");
        if (ebook.DiagramState == EbookDiagramState.Pending) throw Unsupported("The diagrams of this book are not recognised yet.");
        var pdfPath = _storage.ResolveFile(ebook, pdf);
        if (!File.Exists(pdfPath)) throw new NotFoundException("The PDF is missing on the server.") { Code = ApiErrorCodes.EbookFileMissing };

        // Nur Diagramme mit Lage im PDF (W > 0); reine EPUB-Diagramme gibt es hier nicht (kein echtes EPUB).
        var diagrams = await _db.EbookDiagrams.Where(d => d.EbookId == ebookId && d.W > 0).Readable()
            .OrderBy(d => d.Page).ThenBy(d => d.Y).ThenBy(d => d.X).ToListAsync(ct);
        var input = diagrams.Select((d, i) => new ReflowDiagram(i, d.Page, d.X, d.Y, d.W, d.H, d.Fen, d.Label, d.SideKnown)).ToList();

        var relative = EbookStorage.ConvertedEpubPath(ebookId);
        var target = _storage.ResolvePath(EbookSource.Upload, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".part";
        ReflowResult result;
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                result = await Task.Run(() => EbookReflowConverter.Convert(pdfPath, ebook.Title, ebook.Author,
                    $"urn:rookhub:ebook:{ebookId}:reflow", input, fs, ct,
                    page => _pages.RenderPage(pdfPath, page, EbookFigurineRepair.Dpi, ct)), ct);
            File.Move(tmp, target, overwrite: true);
        }
        catch (ReflowNotPossibleException ex)
        {
            TryDelete(tmp);
            throw Unsupported(ex.Message);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }

        var size = new FileInfo(target).Length;
        var file = ebook.Files.FirstOrDefault(f => f.Format == "epub" && f.Derived);
        if (file == null)
        {
            file = new EbookFile { Format = "epub", Derived = true, RelativePath = relative };
            ebook.Files.Add(file);
        }
        file.RelativePath = relative;
        file.FileName = Path.GetFileNameWithoutExtension(pdf.FileName) + " (reflow).epub";
        file.SizeBytes = size;
        file.Sha256 = null;
        file.CreatedAt = DateTime.UtcNow;

        var linked = 0;
        for (var i = 0; i < diagrams.Count; i++)
        {
            if (result.Images.TryGetValue(i, out var img)) { diagrams[i].ImageRef = img.ImageRef; diagrams[i].EpubChapter = img.Spine; linked++; }
            else { diagrams[i].ImageRef = null; diagrams[i].EpubChapter = null; }
        }
        ebook.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("E-Book {EbookId}: Fließtext-EPUB {Bytes} Bytes, {Chapters} Kapitel, {Paragraphs} Absätze, {Linked}/{Diagrams} Diagramme als Bild",
            ebookId, size, result.Chapters, result.Paragraphs, linked, diagrams.Count);
        return new Outcome(ebookId, file.Id, size, result.Pages, result.Chapters, result.Paragraphs, diagrams.Count, linked);
    }

    private static DomainValidationException Unsupported(string message) =>
        new(message) { Code = ApiErrorCodes.EbookReflowUnsupported };

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "E-Books: Datei nicht gelöscht {Path}", path); }
    }
}
