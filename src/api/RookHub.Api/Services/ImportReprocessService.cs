using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Schmale Abstraktion fürs erneute Einreihen eines Chessable-Kurs-Imports — entkoppelt den
/// <see cref="ImportReprocessService"/> von der vollen <see cref="ChessableImportService"/>
/// (die diese Schnittstelle implementiert) und macht den Re-Fetch-Pfad testbar.
/// </summary>
public interface ICourseReimporter
{
    Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName, int? targetRepertoireId = null, CancellationToken ct = default);
}

/// <summary>
/// Neu-Aufbereitung („Reprocessing") veralteter Datensätze, wenn die Import-Pipeline weiterentwickelt
/// wurde (<see cref="ImportPipeline"/>). Datensätze mit <c>ImportVersion &lt; CurrentVersion</c> gelten
/// als veraltet; der Reprocess-Knopf je Sektion ruft diesen Service.
///
/// <para><b>Kurse/Bücher:</b> bevorzugt lokal aus dem gespeicherten Roh-PGN (<c>Book.SourcePgn</c>)
/// — verlustfrei und in-place (Match per LineId, Fortschritt/Statistik bleiben erhalten). Fehlt die
/// Quelle (Altbestand), wird für Chessable-Kurse ein Re-Fetch-Hintergrund-Job eingereiht
/// (<see cref="ChessableImportService.EnqueueReimportAsync"/>). Sonst: nur per manuellem Re-Import.</para>
///
/// <para><b>Repertoires:</b> speichern ihr Roh-PGN selbst und werten live aus — heute gibt es keine
/// abgeleiteten Daten zu erneuern; der Lauf markiert sie nur auf die aktuelle Version (zukunftssicher).</para>
/// </summary>
public partial class ImportReprocessService
{
    private readonly AppDbContext _db;
    private readonly PgnImportService _pgnImport;
    private readonly ICourseReimporter _chessableImport;
    private readonly ILogger<ImportReprocessService> _logger;

    public ImportReprocessService(
        AppDbContext db,
        PgnImportService pgnImport,
        ICourseReimporter chessableImport,
        ILogger<ImportReprocessService> logger)
    {
        _db = db;
        _pgnImport = pgnImport;
        _chessableImport = chessableImport;
        _logger = logger;
    }

    // ===== Kurse / Bücher =====

    /// <summary>Verwaltbare Bücher: Admin = alle, sonst die eigenen (per OwnerUserId importierten) Kurse.</summary>
    private IQueryable<Book> ManageableBooks(int userId, bool isAdmin) =>
        isAdmin ? _db.Books : _db.Books.Where(b => b.OwnerUserId == userId);

    public async Task<ReprocessStatusDto> GetCourseStatusAsync(int userId, bool isAdmin, CancellationToken ct = default)
    {
        var books = ManageableBooks(userId, isAdmin);
        var total = await books.CountAsync(ct);
        // Nur die nötigen Felder der veralteten Bücher laden.
        var stale = await books
            .Where(b => b.ImportVersion < ImportPipeline.CurrentVersion)
            .Select(b => new { HasSource = b.SourcePgn != null, b.Tags, b.FileName })
            .ToListAsync(ct);

        // Chessable-Kurse werden IMMER per Re-Fetch aufbereitet (auch wenn ein Alt-PGN als
        // Quelle vorliegt): markerbasierte Pipeline-Schritte wie [%info]/[%alt] stehen NICHT im
        // gecachten PGN, ein lokales Reprocess würde sie also nicht setzen, aber die Version
        // dennoch hochmarkieren. Lokal aufbereitet wird nur Nicht-Chessable mit Quelle.
        return new ReprocessStatusDto
        {
            CurrentVersion = ImportPipeline.CurrentVersion,
            Total = total,
            Stale = stale.Count,
            Refetchable = stale.Count(b => CanRefetch(b.Tags, b.FileName)),
            ReprocessableLocally = stale.Count(b => !CanRefetch(b.Tags, b.FileName) && b.HasSource),
            NeedsReimport = stale.Count(b => !CanRefetch(b.Tags, b.FileName) && !b.HasSource),
        };
    }

    /// <param name="localOnly">true = nur aus dem serverseitig gespeicherten Quell-PGN aufbereiten
    /// („Aus Cache"), KEIN Chessable-Re-Fetch übers Netz. false = zusätzlich Chessable-Altbestand
    /// ohne Quelle als Re-Fetch-Job einreihen („Alle").</param>
    public async Task<ReprocessResultDto> ReprocessCoursesAsync(int userId, bool isAdmin, bool localOnly = false, CancellationToken ct = default)
    {
        var stale = await ManageableBooks(userId, isAdmin)
            .Where(b => b.ImportVersion < ImportPipeline.CurrentVersion)
            .ToListAsync(ct);

        var result = new ReprocessResultDto();
        foreach (var book in stale)
        {
            ct.ThrowIfCancellationRequested();
            if (IsChessable(book.Tags, book.FileName) && TryParseBid(book.FileName, out var bid))
            {
                // Chessable: vollständiger Re-Fetch. Das gecachte Alt-PGN enthält marker­basierte
                // Pipeline-Daten ([%info]/[%alt] …) NICHT, ein lokales Reprocess würde sie nicht
                // setzen, aber die Version hochmarkieren (still falsch) — daher VOR dem SourcePgn-Pfad.
                if (localOnly) continue; // „Aus Cache": Netz-Re-Fetch bewusst auslassen
                var ownerId = book.OwnerUserId ?? userId;
                var importId = await _chessableImport.EnqueueReimportAsync(ownerId, bid, "book", book.DisplayName, ct: ct);
                if (importId != null) result.Enqueued++;
                else result.Skipped++; // kein Bearer hinterlegt
            }
            else if (!string.IsNullOrEmpty(book.SourcePgn))
            {
                // Nicht-Chessable, lokal verlustfrei + in-place (ImportFileAsync erkennt das veraltete Buch).
                var res = await _pgnImport.ImportFileAsync(book.FileName, book.SourcePgn, ct);
                result.Reprocessed++;
                result.UpdatedLines += res.Updated;
            }
            else
            {
                result.Skipped++; // keine Quelle, kein Re-Fetch → nur manueller Re-Import
            }
        }

        _logger.LogInformation(
            "Course-Reprocess für User {UserId} (admin={IsAdmin}, localOnly={LocalOnly}): {Reprocessed} lokal ({UpdatedLines} Linien), {Enqueued} eingereiht, {Skipped} übersprungen",
            userId, isAdmin, localOnly, result.Reprocessed, result.UpdatedLines, result.Enqueued, result.Skipped);
        return result;
    }

    // ===== Repertoires =====

    public async Task<ReprocessStatusDto> GetRepertoireStatusAsync(int userId, CancellationToken ct = default)
    {
        var reps = await _db.Repertoires
            .Where(r => r.UserId == userId)
            .Include(r => r.Files)
            .ToListAsync(ct);
        var total = reps.Count;
        var stale = reps.Where(r => r.ImportVersion < ImportPipeline.CurrentVersion).ToList();
        // Chessable-Repertoires bekommen [%alt] nur per frischem Re-Fetch (Refetchable); alle anderen
        // haben keine Quelle für geduldete Züge → reiner Versions-Mark (lokal „aufbereitbar", inhaltl. No-op).
        var refetchable = stale.Count(r => ResolveRepertoireBid(r) != null);
        return new ReprocessStatusDto
        {
            CurrentVersion = ImportPipeline.CurrentVersion,
            Total = total,
            Stale = stale.Count,
            ReprocessableLocally = stale.Count - refetchable,
            Refetchable = refetchable,
        };
    }

    /// <param name="localOnly">true = nur lokal aufbereitbare Repertoires („Aus Cache": Nicht-Chessable
    /// → reiner Versions-Mark), KEIN Chessable-Re-Fetch übers Netz. false = zusätzlich Chessable-Repertoires
    /// frisch holen („Alle").</param>
    public async Task<ReprocessResultDto> ReprocessRepertoiresAsync(int userId, bool localOnly = false, CancellationToken ct = default)
    {
        var stale = await _db.Repertoires
            .Where(r => r.UserId == userId && r.ImportVersion < ImportPipeline.CurrentVersion)
            .Include(r => r.Files)
            .ToListAsync(ct);
        var result = new ReprocessResultDto();
        var now = DateTime.UtcNow;
        foreach (var r in stale)
        {
            var bid = ResolveRepertoireBid(r);
            if (bid != null)
            {
                if (localOnly) continue; // „Aus Cache": Chessable-Re-Fetch übers Netz bewusst auslassen
                // Chessable-Repertoire: frisch holen (inkl. [%alt]) und IN-PLACE ins bestehende
                // Repertoire schreiben (Id bleibt → Trainings-Fortschritt bleibt). ImportVersion wird
                // erst beim Abschluss des Hintergrund-Jobs hochgesetzt (Datei-Upload).
                var importId = await _chessableImport.EnqueueReimportAsync(
                    userId, bid, "repertoire", r.Name, targetRepertoireId: r.Id, ct: ct);
                if (importId != null) result.Enqueued++;
                else result.Skipped++; // z. B. kein hinterlegter Chessable-Bearer
            }
            else
            {
                // Nicht-Chessable: keine Quelle für geduldete Züge → nur auf aktuelle Version markieren.
                r.ImportVersion = ImportPipeline.CurrentVersion;
                r.UpdatedAt = now;
                result.Reprocessed++;
            }
        }
        await _db.SaveChangesAsync(ct);
        return result;
    }

    // ===== Helpers =====

    /// <summary>Ein Buch kann (vollständig) per Chessable-Re-Fetch aufbereitet werden, wenn es als
    /// Chessable-Import erkennbar ist UND sich die bid aus dem Dateinamen lösen lässt — sonst bleibt
    /// nur lokales Reprocess (mit Quelle) bzw. manueller Re-Import. Muss zur Verzweigung in
    /// <see cref="ReprocessCoursesAsync"/> passen, damit Status-Zählung und Ausführung übereinstimmen.</summary>
    private static bool CanRefetch(string? tags, string fileName) =>
        IsChessable(tags, fileName) && TryParseBid(fileName, out _);

    private static bool IsChessable(string? tags, string fileName) =>
        (tags ?? string.Empty).Contains("chessable", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("chessable-", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^chessable-u\d+-(.+)\.pgn$", RegexOptions.IgnoreCase)]
    private static partial Regex ChessableBidRegex();

    /// <summary>Holt die Chessable-bid aus dem konventionellen Buch-Dateinamen <c>chessable-u{uid}-{bid}.pgn</c>.</summary>
    private static bool TryParseBid(string fileName, out string bid)
    {
        var m = ChessableBidRegex().Match(fileName);
        bid = m.Success ? m.Groups[1].Value : string.Empty;
        return m.Success;
    }

    [GeneratedRegex(@"^chessable-(\d+)\.pgn$", RegexOptions.IgnoreCase)]
    private static partial Regex RepertoireBidRegex();

    /// <summary>Chessable-bid eines Repertoires: bevorzugt <see cref="Repertoire.ChessableCourseId"/>,
    /// sonst aus dem Repertoire-Dateinamen <c>chessable-{bid}.pgn</c> (Altbestand ohne gesetzte CourseId,
    /// z. B. vor dem [Site]-Auto-Extract importiert). null ⇒ kein Chessable-Repertoire / nicht auflösbar.</summary>
    private static string? ResolveRepertoireBid(Repertoire rep)
    {
        if (!string.IsNullOrWhiteSpace(rep.ChessableCourseId)) return rep.ChessableCourseId;
        foreach (var f in rep.Files)
        {
            var m = RepertoireBidRegex().Match(f.FileName);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }
}
