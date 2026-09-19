using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Sicherheitsnetz für den Chessable-Import-Drain. Der normale Antrieb ist ein Ticket je Import in der
/// IN-MEMORY <see cref="IBackgroundTaskQueue"/> — und der kann Jobs liegen lassen: die Queue ist
/// bounded (<c>BoundedChannelFullMode.DropOldest</c>), ein großer Schwung Importe auf einmal verwirft
/// also die ältesten Tickets; zudem reiht ein FERTIGER Job den nächsten nicht automatisch nach (nur
/// das Anlegen und ein Stillstand reihen nach). Folge: Importe bleiben auf <c>Status=ChessableImportStatus.Running</c> /
/// <c>Phase=ChessableImportPhase.Queued</c> liegen, obwohl gar nichts mehr läuft (Vorfall 2026-06-29: 82 wartende, kein
/// aktiver — Drain erst nach API-Neustart via <see cref="ChessableImportResumeService"/> wieder an).
///
/// Dieser Watchdog prüft periodisch: gibt es wartende Importe (Phase "queued") UND ist KEINER aktiv
/// (Phase "claimed"/"fetching"/"importing")? Dann stößt er den nächsten Job DIREKT an
/// (<see cref="ChessableImportService.RunNextAsync"/>) — bewusst OHNE die bounded Queue, damit das
/// Nachfüllen nicht selbst wieder verworfen werden kann und auch ein hängender Queue-Consumer den
/// Drain nicht blockiert. Solange etwas läuft, hält er sich raus (kein Über-Parallelisieren).
/// </summary>
public class ChessableImportWatchdogService : BackgroundService
{
    /// <summary>Phasen, in denen ein Import AKTIV bearbeitet wird (nicht bloß wartend). Solange einer
    /// davon belegt ist, läuft der Drain — der Watchdog greift dann nicht ein.</summary>
    internal static readonly ChessableImportPhase[] InflightPhases = ChessableImportStates.Inflight;

    // Intern überschreibbar für Tests. Startverzögerung lässt den ResumeService beim Hochfahren zuerst
    // greifen; danach im Ruhe-Takt prüfen, nach einem Anstoß zügig weiterdrainen.
    internal TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    internal TimeSpan IdleInterval = TimeSpan.FromMinutes(2);
    internal TimeSpan BusyDelay = TimeSpan.FromSeconds(2);
    /// <summary>Wie lange ein wegen Tageslimit pausierter Import (<c>Phase=ChessableImportPhase.RateLimited</c>) wartet,
    /// bevor er automatisch wieder freigegeben wird (siehe <see cref="ChessableRateLimiter"/>).</summary>
    internal TimeSpan RateLimitPause = TimeSpan.FromHours(24);
    /// <summary>Wie lange ein Import in einer Inflight-Phase ohne lokalen Treiber beobachtet werden muss,
    /// bevor er als verwaist gilt und zurück in die Warteschlange geht (siehe
    /// <see cref="ReclaimOrphanedInflightAsync"/>).</summary>
    internal TimeSpan OrphanGrace = TimeSpan.FromMinutes(10);

    /// <summary>Seit wann ein Inflight-Import ohne lokalen Treiber beobachtet wird (Id → erste Sichtung).
    /// Nur vom Watchdog-Loop benutzt (ein Thread).</summary>
    private readonly Dictionary<int, DateTime> _orphanSince = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChessableImportWatchdogService> _logger;

    /// <summary>
    /// Laufen die RookHub-EIGENEN Chessable-Lanes (<c>Chessable:Enabled</c>)? Auf PROD stehen sie seit
    /// 2026-09-09 auf <c>false</c> — alle sollen die RepCheck-Erweiterung benutzen. Der EXTENSION-Weg ist
    /// davon ausdrücklich UNBERÜHRT, und genau deshalb läuft dieser Dienst jetzt auch dann: seine
    /// Browser-Pflichten (Sitzungen schließen, verwaiste Browser-Importe beenden) gehören zum
    /// Extension-Weg. Ist der eigene Weg AUS, wird nichts angetrieben, was ohnehin niemand abarbeitet —
    /// und ein verwaister Import wird BEENDET statt zurückgestellt: zurückgestellt stünde er für immer
    /// auf „läuft" und blockierte über die Dedup-Regel in <c>EnqueueReimportAsync</c> jeden neuen Import
    /// desselben Kurses (2026-09-20 auf Prod: „1 Kurs kann aktualisiert werden" ließ sich nicht abräumen).
    /// </summary>
    internal bool LanesEnabled { get; init; } = true;

    public ChessableImportWatchdogService(
        IServiceScopeFactory scopeFactory,
        ILogger<ChessableImportWatchdogService> logger,
        IConfiguration? configuration = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        LanesEnabled = configuration?.GetValue("Chessable:Enabled", true) ?? true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            bool drove = false;
            try
            {
                drove = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chessable-Import-Watchdog: Tick fehlgeschlagen");
            }

            try { await Task.Delay(drove ? BusyDelay : IdleInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Ein Durchlauf: stößt den nächsten wartenden Import an, wenn der Drain steht.
    /// Liefert <c>true</c>, wenn angestoßen wurde (dann zügig erneut prüfen statt voll zu warten).</summary>
    internal async Task<bool> TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var imports = scope.ServiceProvider.GetRequiredService<ChessableImportService>();

        // Browser-Import-Sitzungen ohne Chunk seit 30 min: Import-Datensatz mit dem Erreichten schließen.
        // Steht VOR dem Verwaist-Check: ein so geschlossener Import ist danach terminal und taucht dort
        // gar nicht erst als „läuft ohne Treiber" auf.
        var closed = 0;
        if (scope.ServiceProvider.GetService<ChessableIngestSessionStore>() is { } sessions)
            closed = await CloseExpiredBrowserSessionsAsync(sessions, imports, ct);

        var resumedCount = LanesEnabled ? await ResumeExpiredRateLimitedAsync(db, ct) : 0;
        if (resumedCount > 0)
            _logger.LogInformation(
                "Chessable-Import-Watchdog: {Count} wegen Tageslimit pausierte Importe nach 24h automatisch freigegeben",
                resumedCount);

        var reclaimed = await ReclaimOrphanedInflightAsync(db, imports, ct);

        // Ohne die eigenen Lanes gibt es keinen Drain, den man anstoßen könnte.
        if (!LanesEnabled) return resumedCount > 0 || reclaimed > 0 || closed > 0;

        if (!await IsDrainStalledAsync(db, ct)) return resumedCount > 0 || reclaimed > 0 || closed > 0;

        var queued = await db.ChessableImports.CountAsync(
            i => i.Status == ChessableImportStatus.Running && i.Phase == ChessableImportPhase.Queued && i.FullyCached != true, ct);
        _logger.LogWarning(
            "Chessable-Import-Watchdog: {Queued} wartende Download-Importe, kein aktiver — stoße den Drain an", queued);

        var svc = scope.ServiceProvider.GetRequiredService<ChessableImportService>();
        await svc.RunNextAsync(ct);   // Download-Lane (fastLane=false default): claimt + verarbeitet einen Job
        return true;
    }

    /// <summary>Gibt Importe frei, die wegen des Tages-Zeilenlimits (<c>Phase=ChessableImportPhase.RateLimited</c>)
    /// seit mindestens <see cref="RateLimitPause"/> pausiert sind: zurück auf <c>Status=ChessableImportStatus.Running</c>/
    /// <c>Phase=ChessableImportPhase.Queued</c> (nimmt der normale Drain danach wieder auf). Die Rate-Limit-Prüfung in
    /// <see cref="ChessableImportService.RunAsync"/> läuft beim nächsten Anlauf erneut — das 24h-Fenster
    /// des Bearer-Users ist bis dahin ohnehin abgelaufen, pausiert also nur erneut, falls der User in
    /// der Zwischenzeit über einen ANDEREN Import schon wieder das Limit gerissen hat. Liefert die
    /// Anzahl freigegebener Importe.</summary>
    internal async Task<int> ResumeExpiredRateLimitedAsync(AppDbContext db, CancellationToken ct)
    {
        var threshold = DateTime.UtcNow - RateLimitPause;
        var expired = await db.ChessableImports
            .Where(i => i.Status == ChessableImportStatus.Paused && i.Phase == ChessableImportPhase.RateLimited
                && i.RateLimitedAt != null && i.RateLimitedAt <= threshold)
            .ToListAsync(ct);
        foreach (var import in expired)
        {
            import.Status = ChessableImportStatus.Running;
            import.Phase = ChessableImportPhase.Queued;
            import.Attempts = 0;
            import.RateLimitedAt = null;
        }
        if (expired.Count > 0) await db.SaveChangesAsync(ct);
        return expired.Count;
    }

    /// <summary>Holt VERWAISTE Inflight-Importe zurück in die Warteschlange: Sätze, die laut DB in einer
    /// Inflight-Phase stehen ("claimed"/"fetching"/"importing"), zu denen es in diesem Prozess aber KEINEN
    /// Treiber (mehr) gibt (<see cref="ChessableImportService.IsDrivenLocally"/>).
    ///
    /// FALLE, gegen die das schützt: verliert ein Job seinen treibenden Task, ohne einen Terminal-Status zu
    /// schreiben (z. B. DB-Ausfall &gt; 30 s — dann scheitert auch das Fehler-Update in <c>FailAsync</c>),
    /// bleibt er als Zombie inflight. <see cref="IsDrainStalledAsync"/> wertet ihn als „Drain läuft" und die
    /// Fast-Lane zählt ihn als belegten Slot (<c>FreeSlotsAsync</c>) — die Lane steht dann DAUERHAFT, bisher
    /// bis zum API-Neustart (<see cref="ChessableImportResumeService"/>). Genau die Vorfallsklasse 2026-06-29,
    /// nur eine Ebene tiefer.
    ///
    /// Zwei Sichtungen im Abstand von <see cref="OrphanGrace"/> sind nötig, damit ein gerade erst geclaimter
    /// Job (Registrierungs-Fenster) nicht fälschlich zurückgeholt wird. <see cref="ChessableImport.Attempts"/>
    /// bleibt stehen → der Job zählt weiter gegen <see cref="ChessableImportService.MaxAttempts"/> statt
    /// endlos zu kreisen. Setzt EINE API-Instanz voraus (Treiberliste ist prozesslokal).</summary>
    /// <summary>
    /// Schließt die Import-Datensätze abgelaufener Browser-Import-Sitzungen (kein Chunk seit der TTL des
    /// <see cref="ChessableIngestSessionStore"/>): Status „fehlgeschlagen" mit der Bilanz des Erreichten. Die
    /// Linien selbst sind längst importiert — nur der Datensatz sagte weiter „läuft". Liefert die Anzahl.
    /// </summary>
    internal static async Task<int> CloseExpiredBrowserSessionsAsync(
        ChessableIngestSessionStore sessions, ChessableImportService imports, CancellationToken ct = default)
    {
        var closed = 0;
        foreach (var s in sessions.TakeExpired())
        {
            if (s.ImportId is not int importId) continue;
            await imports.FailBrowserImportAsync(importId,
                $"Browser-Abruf ohne Abschluss (kein Kapitel seit {sessions.Ttl.TotalMinutes:0} min) — "
                + $"{s.ChaptersDone} Kapitel, {s.Imported} Linien übernommen.", ct);
            closed++;
        }
        return closed;
    }

    internal async Task<int> ReclaimOrphanedInflightAsync(AppDbContext db, ChessableImportService? imports = null, CancellationToken ct = default)
    {
        var inflight = await db.ChessableImports
            .Where(i => i.Status == ChessableImportStatus.Running && InflightPhases.Contains(i.Phase))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var seen = new HashSet<int>();
        var reclaimed = 0;
        foreach (var import in inflight)
        {
            if (ChessableImportService.IsDrivenLocally(import.Id))
            {
                _orphanSince.Remove(import.Id);
                continue;
            }
            seen.Add(import.Id);
            if (!_orphanSince.TryGetValue(import.Id, out var since))
            {
                _orphanSince[import.Id] = now;   // erste Sichtung → erst beim nächsten Tick entscheiden
                continue;
            }
            if (now - since < OrphanGrace) continue;

            if (!LanesEnabled)
            {
                // Eigener Chessable-Weg aus: ein zurückgestellter Import würde NIE wieder aufgegriffen.
                // Hier kann es sich nur um einen Browser-Import handeln, dessen Sitzung dieser Prozess nicht
                // mehr kennt (API-Neustart, Tab zu) — die Linien sind längst importiert, nur der Datensatz
                // sagte weiter „läuft" und blockierte jeden neuen Import desselben Kurses.
                _logger.LogWarning(
                    "Chessable-Import-Watchdog: Import {Id} (bid {Bid}) steht seit {Minutes:0} min in Phase {Phase} "
                    + "ohne Treiber, und der eigene Chessable-Weg ist abgeschaltet — Datensatz wird geschlossen",
                    import.Id, import.Bid, (now - since).TotalMinutes, import.Phase);
                if (imports is not null)
                    await imports.FailBrowserImportAsync(import.Id,
                        "Browser-Abruf ohne Abschluss — die Sitzung ist nicht mehr bekannt (Neustart oder geschlossener Tab).", ct);
                else
                    import.Phase = ChessableImportPhase.Queued;
                _orphanSince.Remove(import.Id);
                reclaimed++;
                continue;
            }

            _logger.LogWarning(
                "Chessable-Import-Watchdog: Import {Id} (bid {Bid}) steht seit {Minutes:0} min in Phase {Phase}, "
                + "wird aber von niemandem bearbeitet — zurück in die Warteschlange",
                import.Id, import.Bid, (now - since).TotalMinutes, import.Phase);
            import.Phase = ChessableImportPhase.Queued;
            _orphanSince.Remove(import.Id);
            reclaimed++;
        }

        // Nicht mehr inflight (fertig/abgebrochen/zurückgeholt) → Beobachtung verwerfen.
        foreach (var id in _orphanSince.Keys.Where(id => !seen.Contains(id)).ToList())
            _orphanSince.Remove(id);

        if (reclaimed > 0) await db.SaveChangesAsync(ct);
        return reclaimed;
    }

    /// <summary>Der Drain steht: mindestens ein Import wartet (Phase "queued") und KEINER ist gerade
    /// aktiv (Phase "claimed"/"fetching"/"importing"). Rein/testbar.</summary>
    internal static async Task<bool> IsDrainStalledAsync(AppDbContext db, CancellationToken ct = default)
    {
        // Nur die Download-Lane (alles außer voll-gecacht; null = unklassifiziert zählt als Download).
        // Die schnelle, netzfreie Lane treibt der ChessableImportFastLaneService.
        var hasQueued = await db.ChessableImports
            .AnyAsync(i => i.Status == ChessableImportStatus.Running && i.Phase == ChessableImportPhase.Queued && i.FullyCached != true, ct);
        if (!hasQueued) return false;

        var hasInflight = await db.ChessableImports
            .AnyAsync(i => i.Status == ChessableImportStatus.Running && i.FullyCached != true && InflightPhases.Contains(i.Phase), ct);
        return !hasInflight;
    }
}
