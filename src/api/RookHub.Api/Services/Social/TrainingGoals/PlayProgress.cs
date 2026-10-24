using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>Spielen: externe Rapid-/Classical-Partien je Tag (Lichess/chess.com) — separat, kein Zeit-Topf.</summary>
internal static class PlayProgress
{
    public sealed record Row(DateOnly Date, int Games);

    public static async Task AddAsync(AppDbContext db, int userId, DateTime windowStartUtc, TrainingDays days)
        => Merge(await LoadAsync(db, userId, windowStartUtc), days);

    public static Task<List<Row>> LoadAsync(AppDbContext db, int userId, DateTime windowStartUtc) =>
        db.PlayTimeDailies.AsNoTracking()
            .Where(p => p.UserId == userId && p.Date >= DateOnly.FromDateTime(windowStartUtc.Date))
            .Select(p => new Row(p.Date, p.Games)).ToListAsync();

    public static void Merge(IEnumerable<Row> rows, TrainingDays days)
    {
        foreach (var p in rows)
            days.Day(p.Date).PlayGames += Math.Max(0, p.Games);
    }
}
