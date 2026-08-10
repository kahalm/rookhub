using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Roh-Senke für die getReview-Antworten der RepCheck-Extension (siehe <see cref="ChessableReviewLine"/>).
/// Eine Zeile je (User, Kurs-bid, Varianten-oid), per Upsert aktuell gehalten (letzter Stand gewinnt).
/// Das JSON wird hier NICHT geparst — nur roh abgelegt; der Aufbau zum Kurs (Fallback zu getGame)
/// passiert erst später über <see cref="ChessableReviewParser"/>.
/// </summary>
public class ChessableReviewLineService
{
    /// <summary>Deckel je Batch — ein Kurs-Training kommt in mehreren Batches (analog ProblemMoves).</summary>
    public const int MaxEntriesPerBatch = 500;

    /// <summary>Größen-Deckel je Eintrag (Missbrauchs-/Sanity-Schranke; getReview ist normal einige KB).</summary>
    public const int MaxJsonLength = 256 * 1024;

    private readonly AppDbContext _db;
    private readonly PgnImportService _pgnImport;

    public ChessableReviewLineService(AppDbContext db, PgnImportService pgnImport)
    {
        _db = db;
        _pgnImport = pgnImport;
    }

    /// <summary>
    /// Upsert je oid (letzter Stand im Batch gewinnt). Verworfen werden Einträge ohne gültige numerische
    /// oid (≤32), mit leerem/übergroßem JSON (&gt; <see cref="MaxJsonLength"/>). Liefert die Zahl der
    /// tatsächlich geschriebenen/aktualisierten Zeilen.
    /// </summary>
    public async Task<int> UpsertBatchAsync(int userId, string bid,
        List<ChessableReviewLineEntryDto> entries, CancellationToken ct = default)
    {
        var clean = (entries ?? new())
            .Where(e => e is not null
                && !string.IsNullOrWhiteSpace(e.Oid)
                && e.Oid.Trim().Length <= 32 && e.Oid.Trim().All(char.IsAsciiDigit)
                && !string.IsNullOrWhiteSpace(e.Json)
                && e.Json.Length <= MaxJsonLength)
            .GroupBy(e => e.Oid.Trim())
            .Select(g => g.Last())   // letzter Stand je oid gewinnt innerhalb des Batches
            .Take(MaxEntriesPerBatch)
            .ToList();
        if (clean.Count == 0) return 0;

        var oids = clean.Select(e => e.Oid.Trim()).ToList();
        var existing = await _db.ChessableReviewLines
            .Where(r => r.UserId == userId && r.Bid == bid && oids.Contains(r.Oid))
            .ToDictionaryAsync(r => r.Oid, ct);

        var now = DateTime.UtcNow;
        var written = 0;
        foreach (var e in clean)
        {
            var oid = e.Oid.Trim();
            if (!existing.TryGetValue(oid, out var row))
            {
                row = new ChessableReviewLine { UserId = userId, Bid = bid, Oid = oid };
                _db.ChessableReviewLines.Add(row);
                existing[oid] = row;
            }
            row.Json = e.Json;
            row.ChapterTitle = ExtractChapterTitle(e.Json);
            row.UpdatedAt = now;
            written++;
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Race auf dem Unique-Index (paralleler Flush desselben Users): Batch ist idempotent —
            // verwerfen, der nächste Flush bringt denselben Stand erneut.
            _db.ChangeTracker.Clear();
        }
        return written;
    }

    /// <summary>
    /// Lässt die gespeicherten <c>getReview</c>-Linien dieses Kurses in den Chessable-Kurs (Buch) des
    /// Users einfließen — als reiner LÜCKEN-Füller: getGame GEWINNT, Review überschreibt nie etwas.
    ///
    /// <para>Zielbuch ist <c>chessable-u{userId}-{bid}.pgn</c> (dieselbe Namenskonvention wie der
    /// getGame-Buch-Import in <see cref="ChessableImportService.ImportAsBookAsync"/> /
    /// <see cref="ChessableImportService.AppendLiveAsync"/>). Nur Review-Linien, deren <c>oid</c> im Buch
    /// NOCH KEIN <see cref="Models.BookPuzzle"/> ist, werden über <see cref="ChessableReviewParser"/> zu
    /// PGN konvertiert, zu EINEM Text gefügt und über <see cref="PgnImportService.ImportFileAsync"/>
    /// angehängt. Die dadurch NEU angelegten Linien werden mit <c>Source="review"</c> markiert — das ist
    /// die einzige Stelle, die <see cref="Models.BookPuzzle.Source"/> setzt; getGame-Linien bleiben
    /// <c>null</c>. Existiert das Buch noch nicht, wird es dabei angelegt (reiner Review-Kurs, den
    /// getGame später anreichert).</para>
    ///
    /// <para>Idempotent: ein zweiter Lauf findet keine Lücken mehr (die oids sind jetzt BookPuzzles) und
    /// legt nichts doppelt an. Liefert die Zahl der NEU angelegten Review-Linien.</para>
    /// </summary>
    public async Task<int> MergeIntoCourseAsync(int userId, string bid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bid)) return 0;

        var reviewRows = await _db.ChessableReviewLines
            .Where(r => r.UserId == userId && r.Bid == bid)
            .ToListAsync(ct);
        if (reviewRows.Count == 0) return 0;

        var fileName = $"chessable-u{userId}-{bid}.pgn";

        // Bereits als Buch vorhandene oids (JEDE Quelle) — die füllt Review NICHT, es überschreibt nichts.
        var existingOids = (await _db.BookPuzzles
                .Where(bp => bp.BookFileName == fileName && bp.ChessableOid != null)
                .Select(bp => bp.ChessableOid!)
                .ToListAsync(ct))
            .ToHashSet();

        // Nur die Lücken (oid noch kein BookPuzzle) zu PGN konvertieren; unbrauchbare Antworten überspringen.
        var pgns = new List<string>();
        var seen = new HashSet<string>();
        string? bookName = null;
        foreach (var row in reviewRows)
        {
            if (existingOids.Contains(row.Oid)) continue;
            if (!seen.Add(row.Oid)) continue;
            var converted = ChessableReviewParser.TryConvert(row.Json);
            if (converted is null) continue;
            pgns.Add(converted.Pgn);
            bookName ??= ExtractBookName(row.Json);
        }
        if (pgns.Count == 0) return 0;

        var combined = string.Join("\n\n\n", pgns);

        // Vor dem Import: welche Buch-Linien-Ids gibt es schon? → nur die dadurch NEU angelegten werden
        // als Source="review" markiert (nichts Bestehendes wird angefasst).
        var beforeIds = (await _db.BookPuzzles
                .Where(bp => bp.BookFileName == fileName)
                .Select(bp => bp.Id)
                .ToListAsync(ct))
            .ToHashSet();

        // preserveExistingSourcePgn: der Merge liefert NUR die Lücken-Linien; ein bereits von getGame
        // gesetztes (vollständiges) Book.SourcePgn darf davon NICHT überschrieben werden (sonst wäre die
        // Reprocessing-Quelle nur noch das Teil-PGN). Nur ein leeres SourcePgn wird erstmalig gesetzt.
        var res = await _pgnImport.ImportFileAsync(fileName, combined, ct, preserveExistingSourcePgn: true);

        // Buch als persönliches Chessable-Buch kennzeichnen (analog getGame-Buch-Import). Bei einem
        // frisch angelegten reinen Review-Kurs auch einen brauchbaren Anzeigenamen setzen (statt des
        // rohen Dateinamens); ein bereits von getGame gesetzter Name/Owner bleibt unangetastet.
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == res.BookId, ct);
        if (book is not null)
        {
            var freshBook = book.OwnerUserId is null;
            book.OwnerUserId ??= userId;
            if (string.IsNullOrWhiteSpace(book.Tags)) book.Tags = "chessable";
            if (freshBook && !string.IsNullOrWhiteSpace(bookName))
                book.DisplayName = bookName!.Length > 200 ? bookName[..200] : bookName;
            book.UpdatedAt = DateTime.UtcNow;
        }

        // Die durch DIESEN Merge NEU angelegten Linien mit Source="review" markieren (einzige Stelle,
        // die Source setzt). getGame-Linien bleiben Source=null.
        var newLines = await _db.BookPuzzles
            .Where(bp => bp.BookFileName == fileName && !beforeIds.Contains(bp.Id))
            .ToListAsync(ct);
        foreach (var bp in newLines) bp.Source = "review";

        await _db.SaveChangesAsync(ct);
        return newLines.Count;
    }

    /// <summary>Best-effort Kursname (<c>book_name</c>) aus der getReview-Antwort — nur für den
    /// Anzeigenamen eines frisch angelegten reinen Review-Kurses; kein Wurf.</summary>
    private static string? ExtractBookName(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("lesson", out var lesson)
                || lesson.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!lesson.TryGetProperty("moves", out var moves)
                || moves.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            foreach (var m in moves.EnumerateArray())
            {
                if (m.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (m.TryGetProperty("book_name", out var bn)
                    && bn.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var s = bn.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!.Trim();
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Best-effort Kapiteltitel aus der Antwort (nur für die Übersicht; kein Wurf).</summary>
    private static string? ExtractChapterTitle(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("lesson", out var lesson)
                || lesson.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (lesson.TryGetProperty("chapter", out var chapter)
                && chapter.ValueKind == System.Text.Json.JsonValueKind.Object
                && chapter.TryGetProperty("title", out var t)
                && t.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var s = t.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : (s!.Length > 300 ? s[..300] : s);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
