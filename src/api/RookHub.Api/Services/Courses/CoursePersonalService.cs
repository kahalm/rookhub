using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Persönliche Merkmale eines Users an Kursen, die er sehen darf: Dashboard-Pins und die Verknüpfung
/// zweier Kurse (Buch ↔ Workbook) für den Schnellwechsel im Solver. Aus <see cref="CourseService"/>
/// herausgelöst (Codereview 2026-09-29, A7-010/Z4); der <see cref="CourseService"/> bleibt die Fassade.
/// </summary>
public class CoursePersonalService
{
    private readonly AppDbContext _db;
    private readonly ILogger _logger;

    public CoursePersonalService(AppDbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Pinnt einen (zugänglichen) Kurs fürs Dashboard des Users an. Idempotent —
    /// ein zweiter Aufruf ändert nichts. Kein Zugriff → <see cref="NotFoundException"/> (404).</summary>
    public async Task PinCourseAsync(int userId, int bookId, bool isAdmin)
    {
        await CourseProgressRules.EnsureAccessAsync(_db, userId, bookId, isAdmin);
        if (await _db.CoursePins.AnyAsync(p => p.UserId == userId && p.BookId == bookId)) return;
        _db.CoursePins.Add(new CoursePin { UserId = userId, BookId = bookId, PinnedAt = DateTime.UtcNow });
        // Race: paralleler Pin desselben (User,Buch) → Unique-Index, ignorieren. Andere DB-Fehler
        // fliegen bewusst durch (siehe DbIdempotency).
        await _db.SaveIgnoringUniqueRaceAsync(_logger);
    }

    /// <summary>Löst einen angepinnten Kurs wieder. Idempotent (kein Pin → No-op).</summary>
    public async Task UnpinCourseAsync(int userId, int bookId)
    {
        var pin = await _db.CoursePins.FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == bookId);
        if (pin == null) return;
        _db.CoursePins.Remove(pin);
        await _db.SaveChangesAsync();
    }

    // --- Kurse verknüpfen (Buch ↔ Workbook), persönlich, für den Schnellwechsel ---------------

    /// <summary>Verknüpft zwei (für den User zugängliche) Kurse symmetrisch. Ersetzt etwaige
    /// bestehende Verknüpfungen BEIDER Bücher (je Buch max. ein Partner). 404 wenn ein Buch nicht
    /// zugänglich ist, 400 wenn beide identisch sind.</summary>
    public async Task LinkCoursesAsync(int userId, int bookId, int linkedBookId, bool isAdmin)
    {
        if (bookId == linkedBookId)
            throw new DomainValidationException("Cannot link a course to itself.");
        if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin)
            || !await CourseAccess.CanAccessAsync(_db, userId, linkedBookId, isAdmin))
            throw new NotFoundException("Book not found.");

        // Alte Verknüpfungen entfernen, die eines der beiden Bücher betreffen (1:1 je Buch).
        var stale = await _db.CourseLinks
            .Where(l => l.UserId == userId &&
                (l.BookId == bookId || l.LinkedBookId == bookId || l.BookId == linkedBookId || l.LinkedBookId == linkedBookId))
            .ToListAsync();
        if (stale.Count > 0) _db.CourseLinks.RemoveRange(stale);

        var now = DateTime.UtcNow;
        _db.CourseLinks.Add(new CourseLink { UserId = userId, BookId = bookId, LinkedBookId = linkedBookId, CreatedAt = now });
        _db.CourseLinks.Add(new CourseLink { UserId = userId, BookId = linkedBookId, LinkedBookId = bookId, CreatedAt = now });
        await _db.SaveChangesAsync();
    }

    /// <summary>Hebt die Verknüpfung eines Kurses auf (entfernt beide symmetrischen Zeilen). Idempotent.</summary>
    public async Task UnlinkCourseAsync(int userId, int bookId)
    {
        var rows = await _db.CourseLinks
            .Where(l => l.UserId == userId && (l.BookId == bookId || l.LinkedBookId == bookId))
            .ToListAsync();
        if (rows.Count == 0) return;
        _db.CourseLinks.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }

    /// <summary>Der aktuell verknüpfte Partner-Kurs eines (zugänglichen) Buchs — für den
    /// Schnellwechsel im Solver. Liefert leere Felder, wenn keine Verknüpfung besteht.</summary>
    public async Task<CourseLinkDto> GetLinkAsync(int userId, int bookId, bool isAdmin)
    {
        await CourseProgressRules.EnsureAccessAsync(_db, userId, bookId, isAdmin);
        var linkedId = await _db.CourseLinks
            .Where(l => l.UserId == userId && l.BookId == bookId)
            .Select(l => (int?)l.LinkedBookId)
            .FirstOrDefaultAsync();
        if (linkedId == null) return new CourseLinkDto();
        var name = await _db.Books.Where(b => b.Id == linkedId.Value).Select(b => b.DisplayName).FirstOrDefaultAsync();
        return new CourseLinkDto { LinkedBookId = linkedId, LinkedDisplayName = name };
    }
}
