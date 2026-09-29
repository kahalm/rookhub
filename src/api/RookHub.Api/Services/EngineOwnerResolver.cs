using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Wer rechnet einen Auftrag auf Zuruf, wenn der Nutzer selbst keine Maschine stehen hat? Erst die EIGENE
/// Hintergrund-Engine, sonst die Haus-Engine (ein Admin hat seine Hintergrund-Engines freigegeben), sonst niemand
/// (<c>null</c>). Geteilt von der Punktepartie (<see cref="GameAnalysisService"/>) und dem Zugvergleich
/// (<see cref="MoveComparisonService"/>) — beide werfen Arbeit auf fremde Rechenzeit.
/// </summary>
public static class EngineOwnerResolver
{
    public static async Task<int?> ResolveAsync(AppDbContext db, int userId, CancellationToken ct)
    {
        var own = await db.LichessEngineCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (own is not null && own.BackgroundEngines.Count > 0) return userId;

        // Haus-Engine: die Freigabe steht an den Zugangsdaten, gelten lassen wir sie aber nur bei
        // einem Admin — verliert jemand die Rechte, soll seine Maschine nicht weiter fuer fremde
        // Partien laufen, ohne dass jemand das Haekchen wegnimmt.
        var house = await db.LichessEngineCredentials.AsNoTracking()
            .Where(c => c.ShareAsHouseEngine && c.BackgroundEngineIds != null && c.User!.IsAdmin)
            .OrderBy(c => c.UserId)
            .ToListAsync(ct);
        return house.FirstOrDefault(c => c.BackgroundEngines.Count > 0)?.UserId;
    }
}
