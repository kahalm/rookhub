using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Kapitel je Diagramm aus dem Inhaltsverzeichnis EINES Buchs (0.805.0 für „Als Kurs anlegen“, seit 0.962.0 auch für die
/// Buchbeispiele der Empfohlenen Übungen): PDF-Diagramme (mit Lage, W &gt; 0) über die Lesezeichen des Original-PDFs,
/// EPUB-Diagramme über das Inhaltsverzeichnis des Original-EPUBs; PDF ohne Lesezeichen über das Kapitel im EPUB
/// (<see cref="EbookDiagram.EpubChapter"/>, echtes EPUB des Buchs oder das Fließtext-EPUB). Die Verzeichnisse werden erst gelesen,
/// wenn ein Diagramm sie braucht, und dann für dieses Buch behalten.
/// </summary>
public sealed class EbookChapterLookup
{
    private readonly Ebook? _ebook;
    private readonly EbookStorage? _storage;
    private IReadOnlyList<EbookChapterIndex.Entry>? _pdf, _epub, _reflow;

    private EbookChapterLookup(Ebook? ebook, EbookStorage? storage)
    {
        _ebook = ebook;
        _storage = storage;
    }

    /// <summary>Ohne Ablage (<see cref="EbookStorage.Root"/>) oder unbekanntes Buch: nie ein Kapitel.</summary>
    public static async Task<EbookChapterLookup> ForBookAsync(AppDbContext db, EbookStorage? storage, int ebookId, CancellationToken ct = default)
    {
        if (storage?.Root == null) return new EbookChapterLookup(null, null);
        var ebook = await db.Ebooks.Include(b => b.Files).AsNoTracking().FirstOrDefaultAsync(b => b.Id == ebookId, ct);
        return new EbookChapterLookup(ebook, storage);
    }

    private IReadOnlyList<EbookChapterIndex.Entry> Load(string format, bool derived, Func<string, IReadOnlyList<EbookChapterIndex.Entry>> read)
    {
        if (_ebook == null || _storage == null) return [];
        var file = _ebook.Files.FirstOrDefault(f => f.Format == format && f.Derived == derived);
        var path = file == null ? null : _storage.ResolveFile(_ebook, file);
        return path != null && File.Exists(path) ? read(path) : [];
    }

    private IReadOnlyList<EbookChapterIndex.Entry> Pdf => _pdf ??= Load("pdf", false, EbookChapterIndex.FromPdf);
    private IReadOnlyList<EbookChapterIndex.Entry> Epub => _epub ??= Load("epub", false, EbookChapterIndex.FromEpub);
    private IReadOnlyList<EbookChapterIndex.Entry> Reflow => _reflow ??= Epub.Count > 0 ? Epub : Load("epub", true, EbookChapterIndex.FromEpub);

    /// <summary>Kapitelname des Diagramms, <c>null</c> = keins bekannt.</summary>
    public string? Of(EbookDiagram d)
    {
        if (_ebook == null) return null;
        if (d.W <= 0) return EbookChapterIndex.Pick(Epub, d.Page);
        if (Pdf.Count > 0) return EbookChapterIndex.Pick(Pdf, d.Page);
        return d.EpubChapter is int ec ? EbookChapterIndex.Pick(Reflow, ec) : null;
    }
}
