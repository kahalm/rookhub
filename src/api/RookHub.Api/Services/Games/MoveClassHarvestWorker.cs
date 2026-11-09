using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Erntelauf der Zug-Klassen (0.854.0): fertige Analysen ohne <see cref="GameAnalysis.MoveClasses"/> oder mit älterem
/// <see cref="GameAnalysis.MoveClassesVersion"/> als <see cref="GameMoveClasses.Version"/> werden nachgerechnet — der Bestand
/// beim ersten Start und alles nach einer Regeländerung. Neue Analysen bekommen die Klassen schon beim Fertigwerden
/// (<see cref="GameAnalysisPump"/>). Je Minute höchstens <see cref="BatchSize"/> Analysen; pausierbar.
/// </summary>
public class MoveClassHarvestWorker(IServiceScopeFactory scopeFactory, ILogger<MoveClassHarvestWorker> logger)
    : PeriodicWorker(scopeFactory, logger)
{
    /// <summary>So viele Analysen je Schritt — eine braucht wenige Millisekunden, die Zeilen zu laden kostet mehr.</summary>
    public const int BatchSize = 200;

    protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.FromMinutes(1));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(3));
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "Zug-Klassen ernten: Durchlauf fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var n = await HarvestAsync(services.GetRequiredService<AppDbContext>(), BatchSize, ct, Logger);
        if (n > 0) Logger.LogInformation("Zug-Klassen ernten: {Count} Analysen gerechnet (Regelstand {Version})", n, GameMoveClasses.Version);
    }

    /// <summary>Höchstens <paramref name="max"/> Analysen nachrechnen; gibt die Zahl der gerechneten zurück (öffentlich für
    /// Tests). Fehlerfang je Analyse (R4-007): eine werfende Analyse stand vorher bei jedem Takt wieder vorn
    /// (OrderByDescending(Id)) und hielt alle älteren dauerhaft auf. Jetzt wird sie mit dem aktuellen Regelstand und OHNE
    /// Klassen vermerkt (Warnung im Log) — die nächste Regeländerung versucht sie erneut. <paramref name="store"/> nur für
    /// Tests (Vorgabe <see cref="GameMoveClasses.Store"/>).</summary>
    public static async Task<int> HarvestAsync(AppDbContext db, int max, CancellationToken ct = default, ILogger? logger = null,
        Action<GameAnalysis>? store = null)
    {
        store ??= GameMoveClasses.Store;
        var ids = await db.GameAnalyses.AsNoTracking()
            .Where(a => a.Status == GameAnalysisStatus.Done
                && (a.MoveClassesVersion == null || a.MoveClassesVersion < GameMoveClasses.Version))
            .OrderByDescending(a => a.Id)   // die jüngsten zuerst — die stehen in den Listen
            .Select(a => a.Id).Take(max).ToListAsync(ct);
        var done = 0;
        foreach (var id in ids)
        {
            try
            {
                var analysis = await db.GameAnalyses.Include(a => a.Positions).FirstOrDefaultAsync(a => a.Id == id, ct);
                if (analysis is null) continue;   // inzwischen gelöscht
                store(analysis);
                await db.SaveChangesAsync(ct);
                done++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                logger?.LogWarning(ex, "Zug-Klassen ernten: Analyse {AnalysisId} übersprungen", id);
                await SkipAsync(db, id, ct);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        return done;
    }

    /// <summary>Unrechenbare Analyse als „auf diesem Regelstand gesehen, ohne Klassen" vermerken.</summary>
    private static async Task SkipAsync(AppDbContext db, int id, CancellationToken ct)
    {
        var row = await db.GameAnalyses.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null) return;
        row.MoveClasses = null;
        row.MoveClassesVersion = GameMoveClasses.Version;
        await db.SaveChangesAsync(ct);
    }
}
