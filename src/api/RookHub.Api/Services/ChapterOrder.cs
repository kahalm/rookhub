using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Geteilte Kapitel-Reihenfolge-Logik für Kurse und Wochenposts — EINE Quelle der Wahrheit für den
/// Kapitel-Index-Kontrakt: Der Index, den <c>CourseService.GetChaptersAsync</c> ans Frontend liefert,
/// MUSS bei der Auflösung (<c>?chapterIndex</c> in Kurs-Next bzw. Wochenpost-aus-Kapitel) denselben
/// Kapitelnamen ergeben. Reihenfolge = erste Erscheinung in (Round, Id)-Sortierung, NUR über
/// Quiz-Linien (<c>!IsInfoOnly</c>) — Kapitel, die ausschließlich aus Info-/Erklärlinien bestehen
/// (z. B. Chessable-Intro-Kapitel), tauchen in der Frontend-Kapitelliste nicht auf und dürfen daher
/// auch hier keinen Index belegen, sonst verschiebt sich jedes spätere Kapitel um eins.
/// </summary>
public static class ChapterOrder
{
    /// <summary>null/leer/Whitespace → dieselbe Sammel-„ohne Kapitel"-Gruppe (null).</summary>
    public static string? NormalizeChapter(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw;

    /// <summary>Die eindeutigen (normalisierten) Kapitelnamen eines Buchs in Lesereihenfolge —
    /// Listenindex = stabiler Kapitel-Selektor des Frontends.</summary>
    public static async Task<List<string?>> GetOrderedChapterNamesAsync(AppDbContext db, int bookId)
    {
        var chapters = await db.BookPuzzles
            .Where(bp => bp.BookId == bookId && !bp.IsInfoOnly)
            .OrderBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .Select(bp => bp.Chapter)
            .ToListAsync();
        var names = new List<string?>();
        var seen = new HashSet<string>();
        foreach (var c in chapters)
        {
            var name = NormalizeChapter(c);
            if (seen.Add(name ?? "\0__none__")) names.Add(name);
        }
        return names;
    }
}
