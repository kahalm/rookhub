using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>
/// Quelle „chessable": aktiv trainierte Zeit-Häppchen der Extension — Thema aus manueller Kurs-Zuordnung
/// (Vorrang), sonst aus CourseKind (Repertoire-Auto), sonst other.
/// </summary>
internal static class ChessableProgress
{
    public sealed record Row(DateTime At, int Seconds, RepertoireKind? CourseKind, string? CourseId);
    public sealed record Rows(Dictionary<string, ChessableTheme> ManualThemes, List<Row> Activities);

    public static async Task AddAsync(AppDbContext db, int userId, DateTime windowStartUtc, TrainingDays days)
        => Merge(await LoadAsync(db, userId, windowStartUtc), days);

    public static async Task<Rows> LoadAsync(AppDbContext db, int userId, DateTime windowStartUtc)
    {
        var themes = await db.ChessableCourseThemes.AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToDictionaryAsync(t => t.CourseId, t => t.Theme);
        var activities = await db.ChessableActivities.AsNoTracking()
            .Where(a => a.UserId == userId && a.AttemptedAt >= windowStartUtc)
            .Select(a => new Row(a.AttemptedAt, a.TimeSeconds, a.CourseKind, a.CourseId)).ToListAsync();
        return new Rows(themes, activities);
    }

    public static void Merge(Rows rows, TrainingDays days)
    {
        foreach (var a in rows.Activities)
        {
            var thm = a.CourseId != null && rows.ManualThemes.TryGetValue(a.CourseId, out var manual)
                ? TrainingThemes.FromChessable(manual)
                : TrainingThemes.FromCourseKind(a.CourseKind);
            days.AddTime(a.At, a.Seconds, TrainingCaps.PerChessableFlushCapSeconds, TrainingSource.Chessable, thm);
        }
    }
}
