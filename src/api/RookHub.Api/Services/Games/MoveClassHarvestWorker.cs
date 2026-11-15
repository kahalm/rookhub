using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        var db = services.GetRequiredService<AppDbContext>();
        // Erst die Meister-Buchzüge (die Klassen hängen daran), dann die Klassen. Ohne erreichbaren Explorer bleibt der erste
        // Schritt leer und die Klassen entstehen wie ohne Buch — sie werden nachgezogen, sobald die Buchzüge da sind.
        var books = await HarvestBooksAsync(db, services.GetService<GameMasterBook>(), BookBatchSize, ct, Logger);
        if (books > 0) Logger.LogInformation("Meister-Buchzüge ernten: {Count} Analysen geprüft", books);
        var n = await HarvestAsync(db, BatchSize, ct, Logger);
        if (n > 0) Logger.LogInformation("Zug-Klassen ernten: {Count} Analysen gerechnet (Regelstand {Version})", n, GameMoveClasses.Version);
    }

    /// <summary>So viele Analysen je Schritt bekommen ihre Meister-Buchzüge — bewusst wenige: je Analyse bis zu 60 Abfragen an
    /// den lokalen Explorer (meist aus dessen Speicher). Der Bestand ist damit nach und nach durch, nicht auf einen Schlag.</summary>
    public const int BookBatchSize = 25;

    /// <summary>
    /// Meister-Buchzüge nachtragen (0.978.0): fertige Analysen ohne <see cref="GameAnalysis.MasterBookPlies"/>, die jüngsten
    /// zuerst. Legt das Ergebnis ab und rechnet die Klassen dieser Analyse gleich neu. Kommt für eine Analyse nichts zurück
    /// (Explorer nicht eingerichtet/erreichbar, Zeit um), bleibt sie offen; nach <see cref="BookMaxMisses"/> solchen in Folge
    /// HÖRT der Schritt AUF — der nächste Takt versucht es wieder, statt jede Minute alle durchzuprobieren. Nicht schon nach
    /// der ersten: eine einzelne Analyse, auf die der Explorer nie antwortet, stünde sonst jede Minute vorn (jüngste zuerst)
    /// und hielte alle älteren auf (dieselbe Falle wie R4-007 bei den Klassen). Gibt die Zahl der geprüften Analysen zurück.
    /// </summary>
    public const int BookMaxMisses = 3;

    public static async Task<int> HarvestBooksAsync(AppDbContext db, GameMasterBook? book, int max, CancellationToken ct = default,
        ILogger? logger = null)
    {
        if (book is null) return 0;
        var ids = await db.GameAnalyses.AsNoTracking()
            .Where(a => a.Status == GameAnalysisStatus.Done && a.MasterBookPlies == null)
            .OrderByDescending(a => a.Id).Select(a => a.Id).Take(max).ToListAsync(ct);
        var done = 0;
        var misses = 0;
        foreach (var id in ids)
        {
            try
            {
                var analysis = await db.GameAnalyses.Include(a => a.Positions).FirstOrDefaultAsync(a => a.Id == id, ct);
                if (analysis is null) continue;
                if (await book.ComputeAsync(analysis.Positions, analysis.PlyCount, ct) is not { } plies)
                {
                    if (++misses >= BookMaxMisses) break;
                    continue;
                }
                misses = 0;
                analysis.MasterBookPlies = plies;
                // Nur mit Buchzügen ändert sich etwas an den Klassen; eine werfende Analyse überlässt das dem Klassen-Schritt.
                if (plies.Length > 0)
                {
                    try { GameMoveClasses.Store(analysis); }
                    catch (Exception ex) { logger?.LogWarning(ex, "Meister-Buchzüge ernten: Klassen der Analyse {AnalysisId} nicht gerechnet", id); }
                }
                await db.SaveChangesAsync(ct);
                done++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger?.LogWarning(ex, "Meister-Buchzüge ernten: Analyse {AnalysisId} ausgelassen, nächster Takt versucht sie erneut", id);
                break;
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        return done;
    }

    /// <summary>Höchstens <paramref name="max"/> Analysen nachrechnen; gibt die Zahl der gerechneten zurück (öffentlich für
    /// Tests). Fehlerfang je Analyse (R4-007): eine werfende Analyse stand vorher bei jedem Takt wieder vorn
    /// (OrderByDescending(Id)) und hielt alle älteren dauerhaft auf. Jetzt wird sie mit dem aktuellen Regelstand und OHNE
    /// Klassen vermerkt (Warnung im Log) — die nächste Regeländerung versucht sie erneut. Das gilt nur für Fehler, die an der
    /// Analyse selbst hängen: ein Datenbank- oder Abbruchfehler (<see cref="IsTransient"/>, z. B. Lock-Wait-Timeout im
    /// nächtlichen Backup-Fenster) lässt die Analyse unvermerkt, der nächste Takt versucht sie erneut (GL-BE3-01).
    /// <paramref name="store"/> nur für Tests (Vorgabe <see cref="GameMoveClasses.Store"/>).</summary>
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
            catch (Exception ex) when (!ct.IsCancellationRequested && IsTransient(ex))
            {
                logger?.LogWarning(ex, "Zug-Klassen ernten: Analyse {AnalysisId} wegen eines Datenbankfehlers ausgelassen, nächster Takt versucht sie erneut", id);
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

    /// <summary>Vorübergehende Fehler, die nichts über die Analyse sagen: Speichern/Laden in der Datenbank (auch nach
    /// erschöpften Wiederholungen der Execution-Strategy) und Abbrüche/Zeitüberschreitungen.</summary>
    internal static bool IsTransient(Exception ex) =>
        ex is DbUpdateException or DbException or RetryLimitExceededException or OperationCanceledException or TimeoutException;

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
