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
    /// <summary>
    /// Vergleich ZWEIER Linien in genau derselben Lesereihenfolge, in der überall sortiert wird:
    /// <c>Round.Length, Round, Id</c>. Die Länge zuerst, damit ungepolsterte Runden numerisch
    /// stimmen („9" vor „10"); rein ordinal stünde „10" vor „9".
    ///
    /// WARUM ES DIESE FUNKTION GIBT: der „Weiter"-Cursor verglich in-memory NUR ordinal
    /// (<c>CompareOrdinal(Round)</c>) und widersprach damit der SQL-Sortierung. In einem Buch mit
    /// Runden „1"…„20" fand er ab „9" keinen Nachfolger (alle „1x" sind ordinal kleiner) und fiel
    /// aufs ERSTE Puzzle zurück: Runde 10–20 waren über „Weiter" unerreichbar, und „Überspringen"
    /// im Kurs zeigte dieselbe Aufgabe endlos erneut. Chessable-Importe sind wegen der
    /// „000.000"-Polsterung nicht betroffen, handgepflegte Kurse und PGN-Uploads schon.
    /// </summary>
    public static int Compare(string? roundA, int idA, string? roundB, int idB)
    {
        var a = roundA ?? string.Empty;
        var b = roundB ?? string.Empty;
        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
        var byRound = string.CompareOrdinal(a, b);
        return byRound != 0 ? byRound : idA.CompareTo(idB);
    }

    /// <summary>null/leer/Whitespace → dieselbe Sammel-„ohne Kapitel"-Gruppe (null).</summary>
    public static string? NormalizeChapter(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw;

    /// <summary>Die eindeutigen (normalisierten) Kapitelnamen eines Buchs in Lesereihenfolge —
    /// Listenindex = stabiler Kapitel-Selektor des Frontends.</summary>
    public static async Task<List<string?>> GetOrderedChapterNamesAsync(AppDbContext db, int bookId)
    {
        var chapters = await db.BookPuzzles
            .Where(bp => bp.BookId == bookId && !bp.IsInfoOnly)
            .OrderBy(bp => bp.Round.Length).ThenBy(bp => bp.Round).ThenBy(bp => bp.Id)
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
