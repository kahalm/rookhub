using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Wendet <see cref="RepertoirePgnCleanup"/> auf gespeicherte Chessable-Repertoires an: holt für mehrdeutige oids die
/// Wahrheit aus dem geteilten Linien-Cache (piratechess), repariert die Datei und merkt sich den Regelstand
/// (<see cref="RepertoireFile.CleanupVersion"/>). Nie löschen, nur ausblenden.
/// </summary>
public class RepertoireCleanupService
{
    private readonly AppDbContext _db;
    private readonly ChessableProxyService _proxy;
    private readonly ILogger<RepertoireCleanupService> _logger;

    public RepertoireCleanupService(AppDbContext db, ChessableProxyService proxy, ILogger<RepertoireCleanupService> logger)
    {
        _db = db;
        _proxy = proxy;
        _logger = logger;
    }

    public sealed record FileReport(int FileId, int RepertoireId, int UserId, string Repertoire,
        IReadOnlyList<RepertoirePgnCleanup.CleanupAction> Actions);
    public sealed record Report(int Files, int Changed, int WaitingForPiratechess, IReadOnlyList<FileReport> ChangedFiles);

    /// <summary>
    /// Prüft/repariert EINE Datei (Änderungen nur im EF-Kontext; speichern muss der Aufrufer). <c>null</c> = für eine
    /// mehrdeutige oid wäre die Wahrheit nötig, piratechess ist aber nicht erreichbar → Datei unverändert, später erneut.
    /// </summary>
    internal static async Task<IReadOnlyList<RepertoirePgnCleanup.CleanupAction>?> CleanupFileAsync(
        RepertoireFile file, ChessableProxyService proxy, ILogger logger, bool apply, CancellationToken ct)
    {
        var pgn = file.PgnContent ?? string.Empty;
        var ambiguous = RepertoirePgnCleanup.AmbiguousOids(pgn);
        IReadOnlyDictionary<string, string> truth = new Dictionary<string, string>();
        if (ambiguous.Count > 0)
        {
            try
            {
                truth = await proxy.GetCachedLinePgnsAsync(ambiguous, ct: ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested
                                       && ex is ChessableProxyException or HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Repertoire-Bereinigung: piratechess für {Count} mehrdeutige oids nicht erreichbar — Datei {FileId} später erneut",
                    ambiguous.Count, file.Id);
                return null;
            }
        }

        var result = RepertoirePgnCleanup.Repair(pgn, truth);
        if (apply)
        {
            if (result.Actions.Count > 0)
            {
                file.PgnContent = result.Pgn;
                file.FileSize = Encoding.UTF8.GetByteCount(result.Pgn);
            }
            file.CleanupVersion = RepertoirePgnCleanup.CurrentVersion;
        }
        return result.Actions;
    }

    /// <summary>
    /// Alle Chessable-Repertoire-Dateien. <paramref name="apply"/>=false: reiner Bericht über ALLE Dateien, nichts wird
    /// geschrieben. <paramref name="apply"/>=true: nur Dateien mit veraltetem Regelstand, Datei für Datei gespeichert.
    /// </summary>
    public async Task<Report> CleanupAllAsync(bool apply, CancellationToken ct = default)
    {
        var ids = await _db.RepertoireFiles
            .Where(f => (f.FileName.StartsWith("chessable-") || f.Repertoire.ChessableCourseId != null)
                        && (!apply || f.CleanupVersion < RepertoirePgnCleanup.CurrentVersion))
            .OrderBy(f => f.Id)
            .Select(f => f.Id)
            .ToListAsync(ct);

        var changed = new List<FileReport>();
        var waiting = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var file = await _db.RepertoireFiles.Include(f => f.Repertoire).FirstOrDefaultAsync(f => f.Id == id, ct);
            if (file == null) continue;
            var actions = await CleanupFileAsync(file, _proxy, _logger, apply, ct);
            if (actions == null)
            {
                waiting++;
            }
            else
            {
                if (actions.Count > 0)
                {
                    changed.Add(new FileReport(file.Id, file.RepertoireId, file.Repertoire.UserId, file.Repertoire.Name, actions));
                    if (apply) file.Repertoire.UpdatedAt = DateTime.UtcNow;
                }
                if (apply) await _db.SaveChangesAsync(ct);
            }
            _db.ChangeTracker.Clear();   // Prod trägt ~250 MB Chessable-PGN — nicht im Tracker ansammeln
        }
        return new Report(ids.Count, changed.Count, waiting, changed);
    }
}

/// <summary>
/// Einmal nach dem Start: bereinigt alle Chessable-Repertoire-Dateien, deren Regelstand veraltet ist. Idempotent und
/// über <see cref="RepertoireFile.CleanupVersion"/> gemerkt — spätere Starts lesen nur neue oder übersprungene Dateien.
/// Wartet kurz, damit Start und piratechess nicht gleichzeitig unter Last stehen.
/// </summary>
public class RepertoireCleanupBackfillService : BackgroundService
{
    internal static TimeSpan StartDelay = TimeSpan.FromSeconds(90);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RepertoireCleanupBackfillService> _logger;

    public RepertoireCleanupBackfillService(IServiceScopeFactory scopeFactory, ILogger<RepertoireCleanupBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            using var scope = _scopeFactory.CreateScope();
            var report = await scope.ServiceProvider.GetRequiredService<RepertoireCleanupService>().CleanupAllAsync(apply: true, stoppingToken);
            if (report.Changed > 0 || report.WaitingForPiratechess > 0)
                _logger.LogInformation("Repertoire-Bereinigung: {Changed} von {Files} Dateien geändert, {Waiting} warten auf piratechess",
                    report.Changed, report.Files, report.WaitingForPiratechess);
            foreach (var f in report.ChangedFiles)
                foreach (var a in f.Actions)
                    _logger.LogInformation("Repertoire-Bereinigung „{Repertoire}“ (Datei {FileId}): Partie {Game} „{Line}“ {Action} {Oid} — {Detail}",
                        f.Repertoire, f.FileId, a.Game, a.Line, a.Action, a.Oid, a.Detail);
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren: der nächste Start macht weiter (Regelstand wird je Datei gemerkt).
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Repertoire-Bereinigung fehlgeschlagen — der nächste Start versucht es erneut.");
        }
    }
}
