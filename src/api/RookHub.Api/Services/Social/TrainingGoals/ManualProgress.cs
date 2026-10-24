using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>
/// Manuell eingetragene Offline-Aktivitäten: je Art auf Quelle/Thema (Minuten, verbucht um 00:00 des Eintragsdatums)
/// bzw. Spielen; jeder Eintrag setzt den Manuell-Marker seines Tages.
/// </summary>
internal static class ManualProgress
{
    public sealed record Row(DateOnly Date, ManualActivityKind Kind, int Amount, ChessableTheme? Theme);

    public static async Task AddAsync(AppDbContext db, int userId, DateTime windowStartUtc, TrainingDays days)
        => Merge(await LoadAsync(db, userId, windowStartUtc), days);

    public static Task<List<Row>> LoadAsync(AppDbContext db, int userId, DateTime windowStartUtc)
    {
        var windowStartDate = DateOnly.FromDateTime(windowStartUtc.Date);
        return db.ManualActivities.AsNoTracking()
            .Where(m => m.UserId == userId && m.Date >= windowStartDate)
            .Select(m => new Row(m.Date, m.Kind, m.Amount, m.Theme)).ToListAsync();
    }

    public static void Merge(IEnumerable<Row> rows, TrainingDays days)
    {
        foreach (var m in rows)
        {
            days.Day(m.Date).HasManual = true;
            switch (m.Kind)
            {
                case ManualActivityKind.OtbGame:
                    // Themen-Zuordnung ist bei OtbGame zeitunwirksam (füttert nur PlayGames).
                    days.Day(m.Date).PlayGames += Math.Clamp(m.Amount, 0, TrainingCaps.ManualGamesCap);
                    break;
                case ManualActivityKind.OfflinePuzzle:
                    days.AddTime(m.Date.ToDateTime(TimeOnly.MinValue), m.Amount * 60, TrainingCaps.ManualMinutesCapSeconds,
                        TrainingSource.RandomPuzzle, m.Theme.HasValue ? TrainingThemes.FromChessable(m.Theme.Value) : TrainingTheme.Tactics);
                    break;
                case ManualActivityKind.OfflineStudy:
                case ManualActivityKind.Coaching:
                    days.AddTime(m.Date.ToDateTime(TimeOnly.MinValue), m.Amount * 60, TrainingCaps.ManualMinutesCapSeconds,
                        TrainingSource.CourseBook, m.Theme.HasValue ? TrainingThemes.FromChessable(m.Theme.Value) : TrainingTheme.Other);
                    break;
            }
        }
    }
}
