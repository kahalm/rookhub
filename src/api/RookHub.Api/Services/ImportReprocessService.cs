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
    Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName, CancellationToken ct = default);
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

        return new ReprocessStatusDto
        {
            CurrentVersion = ImportPipeline.CurrentVersion,
            Total = total,
            Stale = stale.Count,
            ReprocessableLocally = stale.Count(b => b.HasSource),
            Refetchable = stale.Count(b => !b.HasSource && IsChessable(b.Tags, b.FileName)),
            NeedsReimport = stale.Count(b => !b.HasSource && !IsChessable(b.Tags, b.FileName)),
        };
    }

    public async Task<ReprocessResultDto> ReprocessCoursesAsync(int userId, bool isAdmin, CancellationToken ct = default)
    {
        var stale = await ManageableBooks(userId, isAdmin)
            .Where(b => b.ImportVersion < ImportPipeline.CurrentVersion)
            .ToListAsync(ct);

        var result = new ReprocessResultDto();
        foreach (var book in stale)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(book.SourcePgn))
            {
                // Lokal, verlustfrei, in-place (ImportFileAsync erkennt das veraltete Buch und aktualisiert).
                var res = await _pgnImport.ImportFileAsync(book.FileName, book.SourcePgn, ct);
                result.Reprocessed++;
                result.UpdatedLines += res.Updated;
            }
            else if (IsChessable(book.Tags, book.FileName) && TryParseBid(book.FileName, out var bid))
            {
                var ownerId = book.OwnerUserId ?? userId;
                var importId = await _chessableImport.EnqueueReimportAsync(ownerId, bid, "book", book.DisplayName, ct);
                if (importId != null) result.Enqueued++;
                else result.Skipped++; // kein Bearer hinterlegt
            }
            else
            {
                result.Skipped++; // keine Quelle, kein Re-Fetch → nur manueller Re-Import
            }
        }

        _logger.LogInformation(
            "Course-Reprocess für User {UserId} (admin={IsAdmin}): {Reprocessed} lokal ({UpdatedLines} Linien), {Enqueued} eingereiht, {Skipped} übersprungen",
            userId, isAdmin, result.Reprocessed, result.UpdatedLines, result.Enqueued, result.Skipped);
        return result;
    }

    // ===== Repertoires =====

    public async Task<ReprocessStatusDto> GetRepertoireStatusAsync(int userId, CancellationToken ct = default)
    {
        var reps = _db.Repertoires.Where(r => r.UserId == userId);
        var total = await reps.CountAsync(ct);
        var stale = await reps.CountAsync(r => r.ImportVersion < ImportPipeline.CurrentVersion, ct);
        return new ReprocessStatusDto
        {
            CurrentVersion = ImportPipeline.CurrentVersion,
            Total = total,
            Stale = stale,
            // Repertoire-Quelle (PgnContent) ist immer vorhanden → grundsätzlich aufbereitbar.
            ReprocessableLocally = stale,
        };
    }

    public async Task<ReprocessResultDto> ReprocessRepertoiresAsync(int userId, CancellationToken ct = default)
    {
        var stale = await _db.Repertoires
            .Where(r => r.UserId == userId && r.ImportVersion < ImportPipeline.CurrentVersion)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var r in stale)
        {
            // Heute keine gespeicherten abgeleiteten Daten zu erneuern (Auswertung läuft live) →
            // nur auf die aktuelle Pipeline-Version markieren. Zukunftssicher: kommt später ein
            // gecachtes/precomputed Repertoire-Artefakt dazu, wird es hier neu erzeugt.
            r.ImportVersion = ImportPipeline.CurrentVersion;
            r.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
        return new ReprocessResultDto { Reprocessed = stale.Count };
    }

    // ===== Helpers =====

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
}
