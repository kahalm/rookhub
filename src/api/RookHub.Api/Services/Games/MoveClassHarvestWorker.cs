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
        var n = await HarvestAsync(services.GetRequiredService<AppDbContext>(), BatchSize, ct);
        if (n > 0) Logger.LogInformation("Zug-Klassen ernten: {Count} Analysen gerechnet (Regelstand {Version})", n, GameMoveClasses.Version);
    }

    /// <summary>Höchstens <paramref name="max"/> Analysen nachrechnen; gibt die Zahl zurück (öffentlich für Tests).</summary>
    public static async Task<int> HarvestAsync(AppDbContext db, int max, CancellationToken ct = default)
    {
        var ids = await db.GameAnalyses.AsNoTracking()
            .Where(a => a.Status == GameAnalysisStatus.Done
                && (a.MoveClassesVersion == null || a.MoveClassesVersion < GameMoveClasses.Version))
            .OrderByDescending(a => a.Id)   // die jüngsten zuerst — die stehen in den Listen
            .Select(a => a.Id).Take(max).ToListAsync(ct);
        foreach (var id in ids)
        {
            var analysis = await db.GameAnalyses.Include(a => a.Positions).FirstAsync(a => a.Id == id, ct);
            GameMoveClasses.Store(analysis);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return ids.Count;
    }
}
