using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// EINE Regel für die Termin-Sperre der Kalkulations-Serie (<see cref="Models.CalcEdition"/>): welche
/// Kapitel eines Buchs sind für DIESEN Betrachter (noch) versteckt? Neben <see cref="CourseAccess"/>
/// herausgezogen, weil nicht nur der Kalkulations-Modus (<see cref="CalculationService"/>) die Wochen
/// ausliefert, sondern auch die Kurs-Detailseite (<see cref="CourseAuthoringService"/>: Kapitelliste und
/// Linien-Tabelle mit FEN/Kommentar). Vorher lebte die Regel privat im CalculationService (dort
/// dreimal samt Besitzer/Tester-Block ausgeschrieben) — die Detailseite kannte sie nicht und zeigte
/// terminierte Wochen vorzeitig (Codereview 2026-09-29).
/// </summary>
public static class CalcVisibility
{
    /// <summary>
    /// Kapitel (Namen wie in <see cref="Models.CalcEdition.Chapter"/>), die für diesen Betrachter noch
    /// VERSTECKT sind: es gibt eine terminierte Ausgabe und der maßgebliche Termin liegt in der Zukunft.
    /// Admin und Buch-Besitzer: nichts versteckt. Als Tester eingetragene Verteiler-Mitglieder nutzen den
    /// früheren <c>TesterPreviewAt</c>, sofern gesetzt und vor <c>PublishAt</c>. Kapitel OHNE Ausgabe
    /// erscheinen hier nicht → bleiben sichtbar. <paramref name="userId"/> = <c>null</c>: anonyme
    /// (öffentliche) Sicht — weder Besitzer noch Tester.
    /// </summary>
    public static async Task<HashSet<string>> HiddenChaptersAsync(AppDbContext db, int bookId, int? userId,
        bool isAdmin, CancellationToken ct = default)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        if (isAdmin) return hidden;

        var now = DateTime.UtcNow;
        var eds = await db.CalcEditions.Where(e => e.BookId == bookId)
            .Select(e => new { e.Chapter, e.PublishAt, e.TesterPreviewAt }).ToListAsync(ct);
        // Buch ohne Ausgaben (fast jeder Kurs): nichts zu verstecken — Besitzer/Tester gar nicht erst fragen.
        if (eds.Count == 0) return hidden;

        var isTester = false;
        if (userId is int uid)
        {
            var ownerId = await db.Books.Where(b => b.Id == bookId).Select(b => b.OwnerUserId).FirstOrDefaultAsync(ct);
            if (ownerId == uid) return hidden;
            isTester = await db.CalcSeriesMembers.AnyAsync(m => m.BookId == bookId && m.UserId == uid && m.IsTester, ct);
        }

        foreach (var e in eds)
        {
            var releaseAt = (isTester && e.TesterPreviewAt is DateTime tp && tp < e.PublishAt) ? tp : e.PublishAt;
            if (now < releaseAt) hidden.Add(e.Chapter);
        }
        return hidden;
    }
}
