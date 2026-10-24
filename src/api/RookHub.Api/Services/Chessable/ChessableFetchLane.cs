using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using Serilog.Context;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Abruf-Lanes des Server-Imports: wartenden Import fair wählen und atomar übernehmen (Download-Lane prozessweit
/// seriell, Fast-Lane nebenläufig), dann den Kurs holen (<see cref="ChessableCourseFetch"/>) und ins Ziel schreiben.
/// Teil von <see cref="ChessableImportService"/> (Zerlegung Z3), von der Fassade gebaut; loggt unter deren Quelle. Das Download-Gate existiert genau einmal (statisch).
/// </summary>
internal sealed class ChessableFetchLane
{
    private const int MaxAttempts = ChessableImportService.MaxAttempts;

    /// <summary>Prozessweites Gate für die DOWNLOAD-Lane: höchstens EIN gleichzeitiger Download-Import.
    /// Die Lane wird von zwei unabhängigen Treibern bedient — dem einzelnen <c>BackgroundTaskWorker</c>
    /// (Queue-Tickets) UND dem <see cref="ChessableImportWatchdogService"/> (ruft <see cref="RunNextAsync"/>
    /// an der bounded Queue VORBEI direkt auf). Der atomare Claim (<see cref="TryClaimAsync"/>) verhindert
    /// nur, dass zwei Treiber DENSELBEN Job greifen — NICHT, dass jeder einen ANDEREN wartenden Job claimt
    /// und parallel herunterlädt (beobachtet 2026-06-29: Watchdog-Drive + Queue-Worker zogen zwei Kurse
    /// gleichzeitig). Dieses Gate erzwingt die „seriell"-Annahme der Download-Lane prozessweit. Statisch,
    /// weil <see cref="ChessableImportService"/> (und damit diese Lane) scoped ist (eine Instanz je Scope/Request).
    /// Die Fast-Lane (netzfrei, eigener serieller Loop) bleibt bewusst ungated und läuft NEBENLÄUFIG.</summary>
    private static readonly SemaphoreSlim _downloadLaneGate = new(1, 1);

    private readonly AppDbContext _db;
    private readonly ChessableProxyService _proxy;
    private readonly ChessableCourseFetch _fetch;
    private readonly ChessableTargetWriter _writer;
    private readonly ChessableImportOutcome _outcome;
    private readonly ILogger<ChessableImportService> _logger;

    public ChessableFetchLane(AppDbContext db, ChessableProxyService proxy, ChessableCourseFetch fetch,
        ChessableTargetWriter writer, ChessableImportOutcome outcome, ILogger<ChessableImportService> logger)
    {
        _db = db;
        _proxy = proxy;
        _fetch = fetch;
        _writer = writer;
        _outcome = outcome;
        _logger = logger;
    }

    /// <summary>Siehe <see cref="ChessableImportService.FairOrder"/>.</summary>
    internal static List<ChessableImport> FairOrder(IEnumerable<ChessableImport> queued) =>
        queued
            .OrderBy(i => i.QueueRound).ThenBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .ToList();

    /// <summary>Siehe <see cref="ChessableImportService.RunNextAsync"/>.</summary>
    public async Task RunNextAsync(CancellationToken ct, bool fastLane)
    {
        // Fast-Lane: ungated (eigener serieller Loop, netzfrei) → bewusst nebenläufig zur Download-Lane.
        if (fastLane)
        {
            await DrainNextAsync(ct, fastLane: true);
            return;
        }

        // Download-Lane: prozessweit auf höchstens EINEN gleichzeitigen Lauf begrenzen. Greift bereits
        // ein Download (Gate belegt), kehrt dieser Aufruf SOFORT zurück (WaitAsync(0)) statt einen
        // zweiten Kurs parallel zu ziehen — der laufende Treiber (Queue-Worker bzw. Watchdog) draint
        // den Rest weiter, der Watchdog holt verpasste Jobs ohnehin periodisch nach.
        if (!await _downloadLaneGate.WaitAsync(0, ct))
            return;
        try
        {
            await DrainNextAsync(ct, fastLane: false);
        }
        finally
        {
            _downloadLaneGate.Release();
        }
    }

    /// <summary>Claimt + verarbeitet GENAU einen fair als Nächstes dran befindlichen wartenden Import
    /// der gewählten Lane. Erwartet, dass der Aufrufer ggf. die Lane-Begrenzung (Download-Gate) hält.</summary>
    private async Task DrainNextAsync(CancellationToken ct, bool fastLane)
    {
        // Browser-Importe nie: ihre Daten kommen aus dem Browser, ein Server-Abruf holte den Kurs mit dem Bearer
        // bzw. aus dem Kurs-Cache — an der Eigentumsprüfung vorbei (siehe ChessableImport.FromBrowser).
        var queued = await _db.ChessableImports
            .Where(i => i.Status == ChessableImportStatus.Running && i.Phase == ChessableImportPhase.Queued
                && !i.FromBrowser
                && (fastLane ? i.FullyCached == true : i.FullyCached != true))
            .ToListAsync(ct);

        foreach (var next in FairOrder(queued))
        {
            // Schon VOR dem Claim als lokal getrieben markieren: sonst gäbe es ein Fenster, in dem die
            // DB-Phase bereits "claimed" ist, aber noch kein Treiber registriert — der Watchdog hielte
            // den Job für verwaist. Wird am Ende jedes Schleifendurchlaufs (auch bei continue/return)
            // wieder freigegeben.
            using var tracked = ChessableImportProgress.TrackInflight(next.Id);

            if (!await TryClaimAsync(next.Id, ct))
                continue; // jemand anderes war schneller → nächsten wartenden Job probieren

            // Fast-Lane-Klassifikation gilt nur zum ENQUEUE-Zeitpunkt — Jobs können tagelang liegen
            // (z. B. bearer-blocked) und der piratechess-Cache kann inzwischen invalidiert sein. Ohne
            // Re-Check würde der Job hier netzfrei-gedacht, aber tatsächlich mit echtem Chessable-Abruf
            // laufen: parallel (MaxParallel Fast-Lane-Runner), am _downloadLaneGate vorbei UND am
            // Bearer-Breaker vorbei (der FullyCached==true ausnimmt) → IP-Block-Risiko. Nur nötig,
            // solange noch kein FetchedPgn-Checkpoint existiert (danach ist der Job wirklich netzfrei).
            if (fastLane && string.IsNullOrEmpty(next.FetchedPgn)
                && !await _proxy.IsCourseCachedAsync(next.Bid, ct))
            {
                await DemoteToDownloadLaneAsync(next.Id, ct);
                continue; // nächsten Fast-Lane-Kandidaten probieren; der Job läuft künftig gedrosselt als Download
            }

            // Der lokal getrackte Entity-Stand ist nach dem Claim veraltet → frisch laden lassen.
            _db.ChangeTracker.Clear();
            await RunAsync(next.Id, ct);
            return;
        }
    }

    private async Task DemoteToDownloadLaneAsync(int importId, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        var stale = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (stale is not null && stale.Status == ChessableImportStatus.Running)
        {
            stale.FullyCached = false;
            stale.Phase = ChessableImportPhase.Queued;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Chessable-Import {Id} (bid {Bid}) ist nicht mehr voll gecacht — zurückgestuft in die Download-Lane",
                stale.Id, stale.Bid);
        }
    }

    /// <summary>Übernimmt den Job atomar, indem die Phase "queued" → "claimed" gesetzt wird —
    /// GEFILTERT auf die noch unveränderte Phase. Auf relationalen Providern via einzelnem
    /// <c>UPDATE … WHERE Phase='queued'</c> (echte Atomarität: nur ein Worker trifft die Zeile).
    /// Liefert <c>true</c>, wenn DIESER Aufruf den Job übernommen hat. Der InMemory-Test-Provider
    /// kann <c>ExecuteUpdate</c> nicht übersetzen → dort getrackter Re-Check-Fallback (gleiche Logik,
    /// nur ohne echte DB-Nebenläufigkeitsgarantie).</summary>
    private async Task<bool> TryClaimAsync(int importId, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            var rows = await _db.ChessableImports
                .Where(i => i.Id == importId && i.Status == ChessableImportStatus.Running && i.Phase == ChessableImportPhase.Queued)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Phase, ChessableImportPhase.Claimed), ct);
            return rows == 1;
        }

        // InMemory-Fallback: nur claimen, wenn die Phase noch "queued" ist.
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is null || import.Status != ChessableImportStatus.Running || import.Phase != ChessableImportPhase.Queued)
            return false;
        import.Phase = ChessableImportPhase.Claimed;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Siehe <see cref="ChessableImportService.RunAsync"/>.</summary>
    public async Task RunAsync(int importId, CancellationToken ct)
    {
        // Auch hier (nicht nur im Drain) registrieren, damit ein DIREKTER Aufruf nicht vom Watchdog als
        // verwaister Inflight-Job zurückgeholt wird. Verschachtelung ist über den Zähler abgedeckt.
        using var tracked = ChessableImportProgress.TrackInflight(importId);

        var import = await BeginAttemptAsync(importId, ct);
        if (import is null) return;

        // Lifecycle-Start: mit Domänen-Tags (ECS `tags`-Filter in Kibana) markieren.
        using (LogContext.PushProperty("LogTags", "import,chessable"))
            _logger.LogInformation(
                "Chessable-Import {Id} gestartet: {Target} (bid {Bid}, Versuch {Attempt}/{Max})",
                import.Id, import.Target, import.Bid, import.Attempts, MaxAttempts);

        try
        {
            // --- Phase 1: Kurs holen (Checkpoint: PGN wird persistiert) ---
            var pgn = import.FetchedPgn;
            if (string.IsNullOrEmpty(pgn))
            {
                pgn = await _fetch.FetchPgnAsync(import, ct);
                if (pgn is null) return;   // pausiert, abgebrochen, gescheitert oder neu eingereiht
            }

            // --- Phase 2: Import (idempotent) ---
            await ImportAndCompleteAsync(import, pgn, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // App faehrt herunter (BackgroundTaskWorker-stoppingToken) — das ist KEIN Import-Fehler.
            // Der Job bleibt auf "running" (Checkpoint FetchedPgn/FetchJobId bleibt erhalten) und wird
            // vom ChessableImportResumeService beim naechsten Start automatisch fortgesetzt. Nicht als
            // "failed" markieren und nicht als Fehler loggen (sonst Fehlalarm im log-watcher).
            _logger.LogInformation(
                "Chessable-Import {Id} durch Shutdown unterbrochen — wird beim Neustart fortgesetzt", import.Id);
        }
        catch (ChessableProxyException ex)
        {
            // Fehler aus dem piratechess-Proxy (z. B. „PGN generation failed …", „Course has no
            // chapters", Bearer/IP-Block). Bisher landete nur die knappe Message im DB-Error-Feld —
            // in Kibana war NICHTS sichtbar. Jetzt strukturiert mit Status + Kontext nach ES loggen,
            // damit Import-Ausfälle nachvollziehbar sind (der volle Parser-Stacktrace liegt auf der
            // piratechess-Seite, hier die rookhub-seitige Zuordnung bid/Import/Status).
            using (LogContext.PushProperty("LogTags", "import,chessable"))
                _logger.LogWarning(ex,
                    "Chessable-Import {Id} (bid {Bid}, Ziel {Target}) via Proxy fehlgeschlagen: HTTP {Status} — {Message}",
                    import.Id, import.Bid, import.Target, (int)ex.Status, ex.Message);
            await _outcome.FailAsync(import, ex.Message);
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("LogTags", "import,chessable"))
                _logger.LogWarning(ex, "Chessable-Import {Id} fehlgeschlagen (Versuch {Attempt})", import.Id, import.Attempts);
            await _outcome.FailAsync(import, ex.Message);
        }
    }

    /// <summary>Lädt den Job und zählt den Versuch. <c>null</c> = nichts zu tun (fehlt, nicht mehr laufend,
    /// Browser-Import oder nach <see cref="MaxAttempts"/> Versuchen gescheitert).</summary>
    private async Task<ChessableImport?> BeginAttemptAsync(int importId, CancellationToken ct)
    {
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is null)
        {
            _logger.LogWarning("ChessableImport {Id} nicht gefunden", importId);
            return null;
        }
        if (import.Status != ChessableImportStatus.Running)
            return null; // bereits abgeschlossen oder fehlgeschlagen — nichts zu tun
        if (import.FromBrowser)
        {
            // Zweite Schranke hinter dem Drain-Filter: ein Browser-Import wird nie serverseitig geholt.
            _logger.LogWarning("Chessable-Import {Id} (bid {Bid}) ist ein Browser-Import — kein Server-Abruf", import.Id, import.Bid);
            return null;
        }

        // Hol-Beginn festhalten (erste Bearbeitung aus der Queue) — trennt Wartezeit von Holzeit.
        import.StartedAt ??= DateTime.UtcNow;
        import.Attempts++;
        if (import.Attempts > MaxAttempts)
        {
            await _outcome.FailAsync(import, $"Abgebrochen nach {MaxAttempts} Versuchen");
            return null;
        }
        await _db.SaveChangesAsync(ct);
        return import;
    }

    /// <summary>Phase 2: ins Ziel schreiben (idempotent), abschließen, benachrichtigen.</summary>
    private async Task ImportAndCompleteAsync(ChessableImport import, string pgn, CancellationToken ct)
    {
        var courseName = ChessableImportTargets.NameOrFallback(import.CourseName, import.Bid);

        import.Phase = ChessableImportPhase.Importing;
        await _db.SaveChangesAsync(ct);

        if (import.Target == "repertoire")
            await _writer.ImportAsRepertoireAsync(import, pgn, courseName, ct);
        else
            await _writer.ImportAsBookAsync(import, pgn, courseName, ct);

        import.Status = ChessableImportStatus.Completed;
        import.Phase = ChessableImportPhase.Done;
        import.FetchedPgn = null; // Checkpoint nicht mehr nötig → Platz freigeben
        import.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        using (LogContext.PushProperty("LogTags", "import,chessable"))
            _logger.LogInformation(
                "Chessable-Import {Id} fertig: {Target} '{Name}' (bid {Bid}), imported={Imported}",
                import.Id, import.Target, courseName, import.Bid, import.Imported);

        // User benachrichtigen: Kurs ist fertig importiert → direkt zur passenden Ansicht.
        // Inkl. Dauer-Aufschlüsselung: Wartezeit (in der Queue) + reine Holzeit.
        var queueTime = ChessableImportProgress.FormatDuration(import.StartedAt - import.CreatedAt);
        var fetchTime = ChessableImportProgress.FormatDuration(import.CompletedAt - (import.StartedAt ?? import.CreatedAt));
        await _outcome.NotifyCompletedAsync(import, courseName, queueTime, fetchTime);
    }
}
