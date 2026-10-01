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
    /// <summary>Fehlertext an Auftrag und Analyse, wenn die Haus-Engine nicht mehr freigegeben ist (A4-004).</summary>
    public const string HouseEngineWithdrawnError = "Haus-Engine nicht mehr freigegeben";

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

    /// <summary>
    /// Gilt die Freigabe der Haus-Engine von <paramref name="ownerUserId"/> NOCH? Dieselbe Regel wie in
    /// <see cref="ResolveAsync"/> (Haekchen gesetzt, Admin, mindestens eine Hintergrund-Engine) — aber fuer die Arbeit,
    /// die schon angenommen ist: <see cref="ResolveAsync"/> prueft nur beim Einwurf, danach stand der Besitzer fest am
    /// Auftrag und an der Analyse. Wer das Haekchen wegnahm oder die Admin-Rolle verlor, rechnete trotzdem weiter fuer
    /// fremde Partien (Nachfuettern, Vertiefung, Neustart, Worker — Codereview 2026-09-29, A4-004).
    /// </summary>
    public static async Task<bool> IsHouseEngineSharedAsync(AppDbContext db, int ownerUserId, CancellationToken ct)
    {
        var cred = await db.LichessEngineCredentials.AsNoTracking()
            .Where(c => c.UserId == ownerUserId && c.ShareAsHouseEngine && c.BackgroundEngineIds != null && c.User!.IsAdmin)
            .FirstOrDefaultAsync(ct);
        return cred is not null && cred.BackgroundEngines.Count > 0;
    }
}
