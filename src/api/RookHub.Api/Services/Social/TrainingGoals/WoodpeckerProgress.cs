using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>
/// Quelle „woodpecker": Versuche im Woodpecker-Training (<see cref="Models.WoodpeckerAttempt"/>) — fest Taktik,
/// Deckel je Versuch wie beim Puzzle.
/// <para><b>Keine Doppelzählung:</b> Zyklus 1 übernimmt Kurs-Versuche an den Plan-Büchern als Woodpecker-Versuche
/// (<c>WoodpeckerService.ImportCourseAttemptsAsync</c>, gleiche Aufgabe, gleicher Zeitpunkt). Deren Zeit steckt schon in
/// der Quelle „courseBook"; ein Zyklus-1-Versuch, zu dem ein <see cref="Models.CourseAttempt"/> desselben Kontos mit
/// gleicher Aufgabe und gleichem <c>AttemptedAt</c> existiert, zählt hier deshalb nicht. Direkt im Woodpecker-Löser
/// gespielte Versuche legen keinen Kurs-Versuch an und zählen immer.</para>
/// </summary>
internal static class WoodpeckerProgress
{
    public sealed record Row(DateTime At, int Seconds, int Cycle, int BookPuzzleId);
    /// <summary><paramref name="ImportedKeys"/>: (Aufgabe, Zeitpunkt) der Kurs-Versuche, die als Zyklus-1-Versuch übernommen wurden.</summary>
    public sealed record Rows(List<Row> Attempts, HashSet<(int BookPuzzleId, DateTime At)> ImportedKeys);

    public static async Task AddAsync(AppDbContext db, int userId, DateTime windowStartUtc, TrainingDays days)
        => Merge(await LoadAsync(db, userId, windowStartUtc), days);

    public static async Task<Rows> LoadAsync(AppDbContext db, int userId, DateTime windowStartUtc)
    {
        var attempts = await (
            from a in db.WoodpeckerAttempts.AsNoTracking()
            where a.AttemptedAt >= windowStartUtc
            join p in db.WoodpeckerPlans.AsNoTracking() on a.PlanId equals p.Id
            where p.UserId == userId
            select new Row(a.AttemptedAt, a.TimeSeconds, a.Cycle, a.BookPuzzleId)).ToListAsync();

        // Nur die Kurs-Versuche, die tatsächlich als Zyklus-1-Versuch dieses Kontos übernommen wurden.
        var imported = await db.CourseAttempts.AsNoTracking()
            .Where(c => c.UserId == userId && c.AttemptedAt >= windowStartUtc)
            .Where(c => db.WoodpeckerAttempts.Any(w => w.Cycle == 1 && w.BookPuzzleId == c.BookPuzzleId
                && w.AttemptedAt == c.AttemptedAt && w.Plan!.UserId == userId))
            .Select(c => new { c.BookPuzzleId, c.AttemptedAt }).ToListAsync();

        return new Rows(attempts, imported.Select(c => (c.BookPuzzleId, c.AttemptedAt)).ToHashSet());
    }

    public static void Merge(Rows rows, TrainingDays days)
    {
        foreach (var a in rows.Attempts)
        {
            if (a.Cycle == 1 && rows.ImportedKeys.Contains((a.BookPuzzleId, a.At))) continue;   // schon als Kurs-Zeit gezählt
            days.AddTime(a.At, a.Seconds, TrainingCaps.PerPuzzleCapSeconds, TrainingSource.Woodpecker, TrainingTheme.Tactics);
        }
    }
}
