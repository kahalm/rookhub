using RookHub.Api.Data;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>
/// Summiert je UTC-Tag (ab <c>windowStartUtc</c>) die Trainingssekunden (gegen Inflation gedeckelt),
/// aufgeschlüsselt nach Quelle und Thema, plus die Anzahl Rapid-/Classical-Partien (Spielen, separat) und den
/// Manuell-Marker — eine Abfrage-und-Zusammenführung je Quelle.
/// </summary>
internal static class TrainingAggregation
{
    public static async Task<Dictionary<DateOnly, DayBuckets>> AggregateAsync(AppDbContext db, int userId, DateTime windowStartUtc)
    {
        var days = new TrainingDays();
        await PuzzleProgress.AddAsync(db, userId, windowStartUtc, days);
        await CourseProgress.AddAsync(db, userId, windowStartUtc, days);
        await ChessableProgress.AddAsync(db, userId, windowStartUtc, days);
        await PlayProgress.AddAsync(db, userId, windowStartUtc, days);
        await ManualProgress.AddAsync(db, userId, windowStartUtc, days);
        return days.Days;
    }
}
