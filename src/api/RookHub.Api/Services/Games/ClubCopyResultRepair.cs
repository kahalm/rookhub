using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Einmaliger Start-Abgleich (1.002.1): bis 1.002.1 trug eine Korrektur von Namen/Ergebnis einer Vereinspartie
/// (<c>PUT /api/league/club/games/{id}</c>) nichts in die verbundenen Kopien in „Meine Partien" (Befund 2026-11-22:
/// Vereinspartie 259 = 0-1, Kopie 243 = <c>*</c>). Seitdem zieht <see cref="SavedGameClubHeaders"/> sie mit; dieser Lauf
/// bereinigt den Bestand.
///
/// <para><b>Bewusst eng</b>: nur Kopien ohne Ergebnis (<c>*</c>), deren Vereinspartie eines hat — die bekommen es (Spalte,
/// <c>[Result]</c>, Ende der Zugliste). Eine Kopie mit einem ANDEREN entschiedenen Ergebnis bleibt: sie kann richtiger sein
/// als die Vereinspartie (Prod 2026-11-22: Kopie 193 endet mit Matt durch Schwarz und 0-1, die Vereinspartie 10 sagt 1-0).
/// Namen gleicht der Lauf nicht ab — Kopien tragen oft absichtlich den echten Namen statt „Schwaz".</para>
///
/// <para>Einmalig über <see cref="DoneKey"/> in <c>SystemSettings</c> (Muster <c>PersonalTacticsLineRepair</c>); idempotent —
/// ein zweiter Lauf fände nichts mehr.</para>
/// </summary>
public class ClubCopyResultRepair(IServiceScopeFactory scopeFactory, ILogger<ClubCopyResultRepair> logger) : BackgroundService
{
    public const string DoneKey = "SavedGames.ClubCopyResultRepair";
    public const string DoneValue = "1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await RunOnceAsync(db, stoppingToken) is > 0 and var n)
                logger.LogInformation("Meine Partien: {Count} Kopien von Vereinspartien bekamen deren Ergebnis.", n);
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren während des Laufs: der nächste Start macht weiter (Marke noch nicht gesetzt).
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Meine Partien: Abgleich der Ergebnisse mit den Vereinspartien fehlgeschlagen.");
        }
    }

    /// <summary>Läuft nur, solange <see cref="DoneKey"/> nicht gesetzt ist; <c>null</c> = schon erledigt.</summary>
    internal static async Task<int?> RunOnceAsync(AppDbContext db, CancellationToken ct = default)
    {
        var mark = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == DoneKey, ct);
        if (mark?.Value == DoneValue) return null;
        var fixedCount = await RepairAsync(db, ct);
        if (mark is null) db.SystemSettings.Add(mark = new SystemSetting { Key = DoneKey });
        mark.Value = DoneValue;
        mark.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return fixedCount;
    }

    /// <summary>Die Regel auf den Bestand anwenden (ohne Marke). → Zahl der berichtigten Kopien.</summary>
    internal static async Task<int> RepairAsync(AppDbContext db, CancellationToken ct = default)
    {
        var rows = await (from s in db.SavedGames
                          join c in db.LeagueClubGames.IgnoreQueryFilters() on s.LeagueClubGameId equals c.Id
                          where (s.Result == null || s.Result == "*") && c.Result != "*"
                          select new { Copy = s, c.Result }).ToListAsync(ct);
        var count = 0;
        foreach (var r in rows.Where(r => SavedGameClubHeaders.Results.Contains(r.Result)))
        {
            SavedGameClubHeaders.SetResult(r.Copy, r.Result);
            count++;
        }
        if (count > 0) await db.SaveChangesAsync(ct);
        return count;
    }
}
