using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Externe Diagramm-Worker (0.870.0, <c>/api/diagram-worker</c>): ein Rechner außerhalb des Servers (Patriks Laptop) nimmt der
/// ausgelasteten API die Diagramm-Erkennung ab. Er leiht das nächste wartende Buch (<see cref="ClaimAsync"/>, dieselbe
/// Reihenfolge wie der eigene Worker), lädt die Dateien, erkennt mit <c>tools/DiagramCli</c> (derselbe Erkennungscode,
/// <see cref="EbookDiagramRecognizer"/>) und schickt das Ergebnis (<see cref="SubmitAsync"/>). Der Server prüft es
/// (<see cref="Validate"/>) und speichert über <see cref="EbookDiagramResults"/> — denselben Weg wie der eigene Worker.
/// Ein Worker, der abstürzt, verliert die Leihe nach <see cref="EbookDiagramLease.ExternalLease"/>; ein Fehler
/// (<see cref="ReleaseAsync"/> mit Text) lässt das Buch für externe Worker liegen, der eigene Worker macht es dann wie bisher.
/// </summary>
public sealed class EbookDiagramWorkerService
{
    /// <summary>Obergrenze je Ergebnis (Riesenbände haben einige Tausend Diagramme).</summary>
    public const int MaxDiagrams = 50_000;

    /// <summary>Lebenszeichen spätestens alle … (ein Drittel der Leihe).</summary>
    public static int HeartbeatSeconds => (int)(EbookDiagramLease.ExternalLease.TotalSeconds / 3);

    /// <summary>Was die Erkennung als <c>FontFamily</c> schreibt — „opus“/„hand“ (nachgetragen) darf ein Worker nicht setzen.</summary>
    public static readonly IReadOnlySet<string> AllowedFamilies = new HashSet<string>(
        ChessDiagramFonts.FamilyNames.Concat(["scan", "cnn", "image", "image-cnn", EbookDiagramVisibility.Unread]), StringComparer.Ordinal);

    private readonly AppDbContext _db;
    private readonly EbookStorage _storage;
    private readonly IServiceProvider _services;
    private readonly ILogger<EbookDiagramWorkerService> _logger;

    public EbookDiagramWorkerService(AppDbContext db, EbookStorage storage, IServiceProvider services, ILogger<EbookDiagramWorkerService> logger)
    {
        _db = db;
        _storage = storage;
        _services = services;
        _logger = logger;
    }

    /// <summary>Bücher, die ein externer Worker überhaupt nehmen kann: wartend, frei, nicht schon an einem externen Worker
    /// gescheitert, mit PDF (auch umgewandeltem DJVU) oder echtem EPUB, und nicht „kuratiert ohne Ergänzen“ (das erledigt der
    /// Server ohne Erkennung in Millisekunden). DJVU ohne PDF-Fassung bleibt dem Server.</summary>
    internal static IQueryable<Ebook> Claimable(IQueryable<Ebook> books, DateTime now) =>
        EbookDiagramLease.Free(books.Where(b => b.DiagramState == EbookDiagramState.Pending && !b.DiagramExternalFailed
            && (!b.DiagramsCurated || b.DiagramSupplement)
            && b.Files.Any(f => f.Format == "pdf" || (f.Format == "epub" && !f.Derived))), now);

    /// <summary>Nächstes Buch leihen → Auftrag, oder <c>null</c>, wenn nichts wartet.</summary>
    public async Task<DiagramWorkerJobDto?> ClaimAsync(string owner, string? worker, CancellationToken ct)
    {
        for (var round = 0; round < 3; round++)
        {
            var ids = await EbookDiagramWorker.QueueOrder(Claimable(_db.Ebooks, DateTime.UtcNow)).Select(b => b.Id).Take(5).ToListAsync(ct);
            if (ids.Count == 0) return null;
            foreach (var id in ids)
            {
                var lease = await EbookDiagramLease.TryClaimAsync(_db, id, owner, EbookDiagramLease.ExternalLease, ct);
                if (lease == null) continue;
                _db.ChangeTracker.Clear();
                var book = await _db.Ebooks.AsNoTracking().Include(b => b.Files).FirstAsync(b => b.Id == id, ct);
                var plan = EbookDiagramRecognizer.Choose(book.Files);
                var job = new DiagramWorkerJobDto
                {
                    JobId = lease.Value, EbookId = id, Title = book.Title, Supplement = book.DiagramSupplement, PageCount = book.PageCount,
                    LeaseUntil = book.DiagramLeaseUntil ?? DateTime.UtcNow + EbookDiagramLease.ExternalLease, HeartbeatSeconds = HeartbeatSeconds,
                };
                foreach (var (file, role) in new[] { (plan.Primary, "primary"), (plan.Reading, "reading"), (plan.Epub, "epub") })
                    if (file != null)
                        job.Files.Add(new DiagramWorkerFileDto
                        {
                            FileId = file.Id, Role = role, Format = file.Format, SizeBytes = file.SizeBytes,
                            Url = $"api/diagram-worker/jobs/{lease.Value}/files/{file.Id}",
                        });
                _logger.LogInformation("Diagramm-Worker {Worker} ({Owner}): E-Book {EbookId} übernommen, Leihe {JobId}",
                    Clip(worker, 60) ?? "?", owner, id, lease.Value);
                return job;
            }
        }
        return null;
    }

    /// <summary>Das geliehene Buch (getrackt, mit Dateien); fremde oder abgelaufene Leihe → 409, unbekannt → 404.</summary>
    private async Task<Ebook> HeldAsync(Guid jobId, string owner, CancellationToken ct)
    {
        var book = await _db.Ebooks.Include(b => b.Files).FirstOrDefaultAsync(b => b.DiagramLeaseId == jobId, ct)
            ?? throw new NotFoundException("Job not found (lease expired and taken over, or book finished).");
        if (!EbookDiagramLease.Holds(book, jobId, owner, DateTime.UtcNow))
            throw new ConflictException("Lease is no longer valid (expired or held by someone else).");
        return book;
    }

    /// <summary>Pfad einer Datei des Auftrags (nur die im Auftrag genannten Dateien).</summary>
    public async Task<(string Path, EbookFile File)> FileAsync(Guid jobId, string owner, int fileId, CancellationToken ct)
    {
        var book = await HeldAsync(jobId, owner, ct);
        var plan = EbookDiagramRecognizer.Choose(book.Files);
        var file = new[] { plan.Primary, plan.Reading, plan.Epub }.FirstOrDefault(f => f?.Id == fileId)
            ?? throw new NotFoundException("File is not part of this job.");
        var path = _storage.ResolveFile(book, file);
        if (!File.Exists(path)) throw new NotFoundException("File is missing on the server.") { Code = ApiErrorCodes.EbookFileMissing };
        return (path, file);
    }

    /// <summary>Lebenszeichen: Leihe um <see cref="EbookDiagramLease.ExternalLease"/> verlängern → neues Ende.</summary>
    public async Task<DateTime> HeartbeatAsync(Guid jobId, string owner, CancellationToken ct)
    {
        var book = await HeldAsync(jobId, owner, ct);
        book.DiagramLeaseUntil = DateTime.UtcNow + EbookDiagramLease.ExternalLease;
        await _db.SaveChangesAsync(ct);
        return book.DiagramLeaseUntil.Value;
    }

    /// <summary>Buch zurückgeben. Mit Fehlertext: externe Worker nehmen es nicht wieder (<see cref="Ebook.DiagramExternalFailed"/>).
    /// Eine schon verlorene Leihe ist kein Fehler (der Worker räumt nur auf).</summary>
    public async Task ReleaseAsync(Guid jobId, string owner, string? error, CancellationToken ct)
    {
        var book = await _db.Ebooks.FirstOrDefaultAsync(b => b.DiagramLeaseId == jobId && b.DiagramLeaseOwner == owner, ct);
        if (book == null) return;
        EbookDiagramLease.Clear(_db, book);
        if (!string.IsNullOrWhiteSpace(error)) book.DiagramExternalFailed = true;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Diagramm-Worker ({Owner}): E-Book {EbookId} zurückgegeben{Error}", owner, book.Id,
            string.IsNullOrWhiteSpace(error) ? "" : " — Fehler: " + Clip(error, 300));
    }

    /// <summary>Ergebnis prüfen und speichern (Weg des eigenen Workers). Ungültig → 400, Leihe weg → 409.</summary>
    public async Task<DiagramWorkerResultAcceptedDto> SubmitAsync(Guid jobId, string owner, DiagramWorkerResultDto dto, CancellationToken ct)
    {
        var book = await HeldAsync(jobId, owner, ct);
        if (dto.EbookId != book.Id) throw new DomainValidationException($"Result is for e-book {dto.EbookId}, the job is for another book.");
        var error = Validate(dto);
        if (error != null) throw new DomainValidationException(error);
        var result = new EbookDiagramExtractor.Result(dto.Diagrams.Select(d => ToEntity(d, book.Id)).ToList(), dto.FontFound, dto.Pages, dto.Solutions);
        await new EbookDiagramResults(_services, _logger).SucceededAsync(book, result, ct);
        _logger.LogInformation("Diagramm-Worker ({Owner}, Programm {Version}): E-Book {EbookId} fertig — {Count} Diagramme, Stand {State}",
            owner, Clip(dto.CliVersion, 40) ?? "?", book.Id, dto.Diagrams.Count, book.DiagramState);
        return new DiagramWorkerResultAcceptedDto { EbookId = book.Id, State = book.DiagramState.ToString(), DiagramCount = book.DiagramCount };
    }

    // ── Prüfen und Abbilden ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fehlertext oder <c>null</c>. Prüft Mengen, Lage, Längen (wie die Spalten), Font-Familie, Lösungsquelle und die
    /// FEN: Syntax immer; für lesbare Diagramme zusätzlich höchstens ein König je Farbe und 16 Steine je Farbe.
    /// (Illustrationen ohne König gibt es in Lehrbüchern — die bleiben erlaubt.)</summary>
    public static string? Validate(DiagramWorkerResultDto dto)
    {
        if (dto.Diagrams == null) return "diagrams missing";
        if (dto.Diagrams.Count > MaxDiagrams) return $"too many diagrams ({dto.Diagrams.Count} > {MaxDiagrams})";
        if (dto.Pages < 0 || dto.Pages > 100_000) return "pages out of range";
        for (var i = 0; i < dto.Diagrams.Count; i++)
        {
            var e = ValidateDiagram(dto.Diagrams[i]);
            if (e != null) return $"diagram {i}: {e}";
        }
        return null;
    }

    private static string? ValidateDiagram(DiagramWorkerDiagramDto? d)
    {
        if (d == null) return "null";
        if (d.Page < 0 || d.Page > 100_000) return "page out of range";
        foreach (var v in new[] { d.X, d.Y, d.W, d.H })
        {
            if (!double.IsFinite(v) || v < 0) return "position out of range";
            // PDF-Lage ist ein Seitenanteil; bei EPUB-Bildern ist Y die Reihenfolge im Kapitel.
            if (d.ImageRef == null && v > 1.0001) return "position out of range";
            if (v > 1e7) return "position out of range";
        }
        if (d.Confidence is { } c && (!float.IsFinite(c) || c < 0 || c > 1)) return "confidence out of range";
        if (d.EpubChapter is < 0 || d.SolutionPage is < 0) return "negative chapter/page";
        if (d.FontFamily == null || !AllowedFamilies.Contains(d.FontFamily)) return $"font family '{d.FontFamily}' not allowed";
        if (d.SolutionSource is not (null or "book")) return "solution source must be 'book'";
        if (TooLong(d.Fen, 100) || TooLong(d.Label, 40) || TooLong(d.Caption, 200) || TooLong(d.ImageRef, 300) || TooLong(d.FontFamily, 60)
            || TooLong(d.SolutionUci, 1200) || TooLong(d.SolutionSan, 1500) || TooLong(d.SolutionText, 3000) || TooLong(d.SolutionSource, 10))
            return "text too long";
        return FenError(d.Fen, strict: d.FontFamily != EbookDiagramVisibility.Unread);
    }

    private static bool TooLong(string? s, int max) => s != null && s.Length > max;

    /// <summary>Fehler in einer FEN oder <c>null</c>. <paramref name="strict"/>: zusätzlich höchstens ein König und 16 Steine je Farbe.</summary>
    internal static string? FenError(string? fen, bool strict)
    {
        if (string.IsNullOrWhiteSpace(fen)) return "FEN missing";
        var parts = fen.Split(' ');
        if (parts.Length != 6) return "FEN needs 6 fields";
        var ranks = parts[0].Split('/');
        if (ranks.Length != 8) return "FEN needs 8 ranks";
        int wk = 0, bk = 0, white = 0, black = 0;
        foreach (var rank in ranks)
        {
            var files = 0;
            foreach (var ch in rank)
            {
                if (ch is >= '1' and <= '8') { files += ch - '0'; continue; }
                if ("PNBRQK".Contains(ch)) { white++; if (ch == 'K') wk++; }
                else if ("pnbrqk".Contains(ch)) { black++; if (ch == 'k') bk++; }
                else return $"FEN has invalid character '{ch}'";
                files++;
            }
            if (files != 8) return "FEN rank does not have 8 files";
        }
        if (parts[1] is not ("w" or "b")) return "FEN side must be w or b";
        if (parts[2] != "-" && (parts[2].Length > 4 || parts[2].Any(ch => !"KQkq".Contains(ch)) || parts[2].Distinct().Count() != parts[2].Length))
            return "FEN castling invalid";
        if (parts[3] != "-" && !(parts[3].Length == 2 && parts[3][0] is >= 'a' and <= 'h' && parts[3][1] is '3' or '6'))
            return "FEN en passant invalid";
        if (!int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            || !int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out var move) || move < 1)
            return "FEN move counters invalid";
        if (strict && (wk > 1 || bk > 1)) return "implausible FEN: more than one king of a colour";
        if (strict && (white > 16 || black > 16)) return "implausible FEN: more than 16 pieces of a colour";
        return null;
    }

    /// <summary>Ein erkanntes Diagramm als Austauschform (das schreibt <c>tools/DiagramCli</c>).</summary>
    public static DiagramWorkerDiagramDto ToDto(EbookDiagram d) => new()
    {
        Page = d.Page, X = d.X, Y = d.Y, W = d.W, H = d.H, Fen = d.Fen, Label = d.Label, Caption = d.Caption, SideKnown = d.SideKnown,
        ImageRef = d.ImageRef, Confidence = d.Confidence, EpubChapter = d.EpubChapter, FontFamily = d.FontFamily,
        SolutionUci = d.SolutionUci, SolutionSan = d.SolutionSan, SolutionText = d.SolutionText, SolutionPage = d.SolutionPage,
        SolutionSource = d.SolutionSource,
    };

    /// <summary>Ergebnis der Erkennung als Austauschform.</summary>
    public static DiagramWorkerResultDto ToDto(int ebookId, EbookDiagramExtractor.Result r, string? cliVersion) => new()
    {
        EbookId = ebookId, FontFound = r.FontFound, Pages = r.Pages, Solutions = r.Solutions, CliVersion = cliVersion,
        Diagrams = r.Diagrams.Select(ToDto).ToList(),
    };

    internal static EbookDiagram ToEntity(DiagramWorkerDiagramDto d, int ebookId) => new()
    {
        EbookId = ebookId, Page = d.Page, X = d.X, Y = d.Y, W = d.W, H = d.H, Fen = d.Fen, Label = d.Label, Caption = d.Caption,
        SideKnown = d.SideKnown, ImageRef = d.ImageRef, Confidence = d.Confidence, EpubChapter = d.EpubChapter, FontFamily = d.FontFamily,
        SolutionUci = d.SolutionUci, SolutionSan = d.SolutionSan, SolutionText = d.SolutionText, SolutionPage = d.SolutionPage,
        SolutionSource = d.SolutionSource,
    };

    private static string? Clip(string? s, int max) => s == null ? null : s.Length <= max ? s : s[..max];
}
