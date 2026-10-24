using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>
/// Quelle „courseBook": alle Kurs-Versuche — Thema aus den EXPLIZITEN Buch-Themen-Tags (Book.Themes). Ein Buch
/// kann mehrere Tags tragen (z. B. Taktik+Endspiel); die Versuchszeit wird dann gleichmäßig auf die Themen
/// aufgeteilt (Gesamtzeit bleibt korrekt). Unset/leer → Default Taktik (jedes Buch ist standardmäßig Taktik).
/// Ersetzt die frühere Tags/Chapter-Heuristik, damit der Fortschritt exakt den im Kurs gesetzten Tags folgt.
/// </summary>
internal static class CourseProgress
{
    public sealed record Row(DateTime At, int Seconds, string? BookThemes);

    public static async Task AddAsync(AppDbContext db, int userId, DateTime windowStartUtc, TrainingDays days)
        => Merge(await LoadAsync(db, userId, windowStartUtc), days);

    public static Task<List<Row>> LoadAsync(AppDbContext db, int userId, DateTime windowStartUtc) => (
        from a in db.CourseAttempts.AsNoTracking()
        where a.UserId == userId && a.AttemptedAt >= windowStartUtc
        join b in db.Books.AsNoTracking() on a.BookId equals b.Id
        select new Row(a.AttemptedAt, a.TimeSeconds, b.Themes)).ToListAsync();

    public static void Merge(IEnumerable<Row> rows, TrainingDays days)
    {
        foreach (var a in rows)
            days.AddCourseTime(a.At, a.Seconds, TrainingThemes.BookThemes(a.BookThemes));
    }
}
