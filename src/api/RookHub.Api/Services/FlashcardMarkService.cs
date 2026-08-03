using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Persistente Flashcard-Markierungen: einzelne KURS-Linien (BookPuzzleId) bzw. REPERTOIRE-Linien
/// (LineKey) je User an-/abwählbar; die Flashcards-Ansicht `?marked=1` zeigt nur diese. Zugriff
/// wie überall: Kurs über <see cref="CourseAccess"/> (404-Semantik = null), Repertoire über
/// <see cref="RepertoireAccess"/> (Besitzer oder Freigabe-Empfänger). Alle Operationen idempotent.
/// </summary>
public class FlashcardMarkService
{
    private readonly AppDbContext _db;

    public FlashcardMarkService(AppDbContext db) => _db = db;

    // ===== Kurs ==============================================================

    /// <summary>Markierte Linien-Ids des Users in diesem Kurs; null = kein Kurs-Zugriff.</summary>
    public async Task<List<int>?> GetCourseMarksAsync(int userId, int bookId, bool isAdmin,
        CancellationToken ct = default)
    {
        if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin, ct)) return null;
        return await _db.CourseFlashcardMarks
            .Where(m => m.UserId == userId && m.BookId == bookId)
            .Select(m => m.BookPuzzleId)
            .ToListAsync(ct);
    }

    /// <summary>Setzt/entfernt die Markierung einer Kurs-Linie. null = kein Zugriff/Linie fremd,
    /// sonst der neue Zustand (true = markiert).</summary>
    public async Task<bool?> SetCourseMarkAsync(int userId, int bookId, int bookPuzzleId, bool marked,
        bool isAdmin, CancellationToken ct = default)
    {
        if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin, ct)) return null;
        var belongs = await _db.BookPuzzles.AnyAsync(bp => bp.Id == bookPuzzleId && bp.BookId == bookId, ct);
        if (!belongs) return null;

        var existing = await _db.CourseFlashcardMarks
            .FirstOrDefaultAsync(m => m.UserId == userId && m.BookPuzzleId == bookPuzzleId, ct);
        if (marked && existing is null)
        {
            _db.CourseFlashcardMarks.Add(new CourseFlashcardMark
            {
                UserId = userId, BookId = bookId, BookPuzzleId = bookPuzzleId,
            });
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { _db.ChangeTracker.Clear(); }   // Unique-Race → schon markiert
        }
        else if (!marked && existing is not null)
        {
            _db.CourseFlashcardMarks.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
        return marked;
    }

    // ===== Repertoire ========================================================

    /// <summary>Markierte Linien-Schlüssel des Users in diesem Repertoire; null = kein Zugriff.</summary>
    public async Task<List<string>?> GetRepertoireMarksAsync(int userId, int repertoireId,
        CancellationToken ct = default)
    {
        if (!await RepertoireAccess.CanReadAsync(_db, repertoireId, userId, ct)) return null;
        return await _db.RepertoireFlashcardMarks
            .Where(m => m.UserId == userId && m.RepertoireId == repertoireId)
            .Select(m => m.LineKey)
            .ToListAsync(ct);
    }

    /// <summary>Setzt/entfernt die Markierung einer Repertoire-Linie (LineKey). null = kein Zugriff.</summary>
    public async Task<bool?> SetRepertoireMarkAsync(int userId, int repertoireId, string lineKey, bool marked,
        CancellationToken ct = default)
    {
        lineKey = (lineKey ?? string.Empty).Trim();
        if (lineKey.Length == 0 || lineKey.Length > 120) return null;
        if (!await RepertoireAccess.CanReadAsync(_db, repertoireId, userId, ct)) return null;

        var existing = await _db.RepertoireFlashcardMarks
            .FirstOrDefaultAsync(m => m.UserId == userId && m.RepertoireId == repertoireId && m.LineKey == lineKey, ct);
        if (marked && existing is null)
        {
            _db.RepertoireFlashcardMarks.Add(new RepertoireFlashcardMark
            {
                UserId = userId, RepertoireId = repertoireId, LineKey = lineKey,
            });
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
        }
        else if (!marked && existing is not null)
        {
            _db.RepertoireFlashcardMarks.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
        return marked;
    }
}
