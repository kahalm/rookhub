using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using Serilog.Context;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Repertoires;
using RookHub.Api.DTOs.Chessable;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Führt einen asynchronen Chessable-Kurs-Import durch: holt den Kurs (tief) von piratechess
/// und legt ihn entweder als persönliches Repertoire (PGN, Mode "None") oder als persönliches
/// Buch (Puzzles, Mode "FirstKeyMove" → erster Key-Zug trainierbar) für den User an.
///
/// Robust gegen Neustarts: der Fortschritt wird in der DB gecheckpointet (Phase + bereits
/// geholtes PGN + ResultId). Ein nach einem Crash/Deploy resumter Job überspringt den teuren
/// Chessable-Fetch (PGN ist persistiert) und legt nichts doppelt an (idempotente Schritte).
/// </summary>
public class ChessableImportService : ICourseReimporter
{
    public const int MaxAttempts = 3;

    // Poll-Tuning (intern überschreibbar für Tests). piratechess holt bewusst LANGSAM
    // (Inter-Request-Delay 2–5 s + Linien-Retries + VPN-Rotation) → ein großer Kurs dauert
    // leicht >15 min. Der Abruf darf daher NICHT an einem festen Zeit-Limit scheitern, solange
    // er Fortschritt macht. Nur echter Stillstand (FetchStallPolls Polls ohne Fortschritt) bzw.
    // der großzügige Absolut-Backstop beenden das Polling.
    internal int PollDelayMs = 2500;
    internal int FetchStallPolls = 240;   // aufeinanderfolgende Polls OHNE Fortschritt → Stillstand (≈10 min)
    internal int FetchMaxPolls = 2160;    // Absolut-Backstop (≈90 min) gegen echte Endlosschleifen

    /// <summary>Prozessweites Gate für die DOWNLOAD-Lane: höchstens EIN gleichzeitiger Download-Import.
    /// Die Lane wird von zwei unabhängigen Treibern bedient — dem einzelnen <c>BackgroundTaskWorker</c>
    /// (Queue-Tickets) UND dem <see cref="ChessableImportWatchdogService"/> (ruft <see cref="RunNextAsync"/>
    /// an der bounded Queue VORBEI direkt auf). Der atomare Claim (<see cref="TryClaimAsync"/>) verhindert
    /// nur, dass zwei Treiber DENSELBEN Job greifen — NICHT, dass jeder einen ANDEREN wartenden Job claimt
    /// und parallel herunterlädt (beobachtet 2026-06-29: Watchdog-Drive + Queue-Worker zogen zwei Kurse
    /// gleichzeitig). Dieses Gate erzwingt die „seriell"-Annahme der Download-Lane prozessweit. Statisch,
    /// weil <see cref="ChessableImportService"/> scoped ist (eine Instanz je Scope/Request).
    /// Die Fast-Lane (netzfrei, eigener serieller Loop) bleibt bewusst ungated und läuft NEBENLÄUFIG.</summary>
    private static readonly SemaphoreSlim _downloadLaneGate = new(1, 1);

    /// <summary>Siehe <see cref="ChessableImportProgress.IsDrivenLocally"/> (prozessweite Treiber-Liste).</summary>
    internal static bool IsDrivenLocally(int importId) => ChessableImportProgress.IsDrivenLocally(importId);

    /// <summary>Siehe <see cref="ChessableImportProgress.TrackInflight"/>.</summary>
    internal static IDisposable TrackInflight(int importId) => ChessableImportProgress.TrackInflight(importId);

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _proxy;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ChessableRateLimiter _rateLimiter;
    private readonly ILogger<ChessableImportService> _logger;

    // Teildienste (Zerlegung Z3): je Fassaden-Instanz gebaut, teilen deren scoped DbContext.
    private readonly ChessableImportOutcome _outcome;
    private readonly ChessableTargetWriter _writer;
    private readonly ChessableLiveAppend _liveAppend;

    public ChessableImportService(
        AppDbContext db,
        EncryptionService encryption,
        ChessableProxyService proxy,
        RepertoireService repertoires,
        PgnImportService pgnImport,
        IBackgroundTaskQueue taskQueue,
        NotificationService notifications,
        ChessableBearerBreaker breaker,
        ChessableRateLimiter rateLimiter,
        ILogger<ChessableImportService> logger)
    {
        _db = db;
        _encryption = encryption;
        _proxy = proxy;
        _taskQueue = taskQueue;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _outcome = new ChessableImportOutcome(db, breaker, notifications, logger);
        _writer = new ChessableTargetWriter(db, repertoires, pgnImport);
        _liveAppend = new ChessableLiveAppend(db, pgnImport, repertoires, proxy, logger);
    }

    /// <summary>Reiht ein Ticket ein, das den fair als Nächstes dran befindlichen wartenden Import
    /// verarbeitet (nicht einen festen). So bestimmt die faire Reihenfolge — nicht die FIFO-Position
    /// in der geteilten Background-Queue —, welcher Import als Nächstes läuft.</summary>
    private async Task EnqueueNextAsync()
    {
        await _taskQueue.EnqueueAsync(async (sp, ct) =>
        {
            var svc = sp.GetRequiredService<ChessableImportService>();
            await svc.RunNextAsync(ct);
        });
    }

    /// <summary>
    /// Reiht einen Re-Import eines bereits importierten Chessable-Kurses ein (für die Neu-Aufbereitung
    /// veralteter Bücher OHNE gespeichertes Roh-PGN). Holt den Kurs erneut (oder aus dem Rohdaten-Cache)
    /// und lässt ihn durch die aktuelle Pipeline laufen — <see cref="ImportFileAsync"/> aktualisiert
    /// veraltete Bücher dabei in-place. Liefert die Import-Id, oder <c>null</c>, wenn für
    /// <paramref name="ownerUserId"/> kein Chessable-Bearer hinterlegt ist (kein Re-Fetch möglich).
    /// </summary>
    /// <summary>Alle im piratechess-DB-Cache vorliegenden Kurs-Bids (Batch, 1 Aufruf) — für den
    /// Massen-Reprocess, damit nicht je Kurs der teure Einzel-Cache-Check laufen muss.</summary>
    public Task<HashSet<string>> GetCachedBidsAsync(CancellationToken ct = default)
        => _proxy.GetCachedBidsAsync(ct);

    /// <summary>
    /// Steht <paramref name="bid"/> in der von Chessable bestätigten Kursliste des Nutzers
    /// (<see cref="ChessableCredential.CachedCoursesJson"/> — geschrieben von „Testen“, der Kursliste und dem
    /// Nacht-Refresh, immer mit seinem eigenen Bearer)? Schranke für alles, was Inhalte aus dem GETEILTEN
    /// Linien-Cache herausgibt: den Extension-Ingest mit Linien ohne Inhalt und die cached-lines-Auskunft. Ohne sie
    /// holte sich jedes Konto die Linien fremder, bezahlter Kurse über ihre oid. Bewusst ohne Live-Abruf: die
    /// Extension-Wege laufen auch mit <c>Chessable:Enabled=false</c>, und ein Chessable-Abruf je Chunk wäre genau das,
    /// was der Browser-Import vermeiden soll.
    /// </summary>
    public async Task<bool> HasVerifiedCourseAsync(int userId, string bid, CancellationToken ct = default)
    {
        var json = await _db.ChessableCredentials.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.CachedCoursesJson)
            .FirstOrDefaultAsync(ct);
        return ChessableReviewLineService.OwnedBids(json).Contains(bid);
    }

    public async Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName, int? targetRepertoireId = null, bool? knownCached = null, bool trustOwnership = false, CancellationToken ct = default)
    {
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == ownerUserId, ct);
        // Cache-Status vorab (Batch-Wert bevorzugt) — bestimmt auch, ob ein Owner OHNE Bearer bedient
        // werden kann: ein voll-gecachter Kurs kommt aus dem piratechess-Rohdaten-Cache, ganz ohne
        // Chessable-Kontakt/Bearer.
        var fullyCached = knownCached ?? await _proxy.IsCourseCachedAsync(bid, ct);
        // Kein Bearer des Owners: nur zulässig, wenn der Kurs gecacht ist (Re-Fetch aus dem Cache
        // ohne Bearer) UND der Aufrufer vertrauenswürdig ist (Admin-Massen-Reprocess). Sonst kein
        // Weg, den Kurs zu holen → null (der Reprocess markiert das Repertoire dann nur als aktuell).
        if (cred is null && !(trustOwnership && fullyCached)) return null;
        // SICHERHEIT: nur Kurse re-fetchen, die WIRKLICH in der Chessable-Bibliothek des Owners liegen.
        // Sonst ließe sich der Eigentums-Check von StartImport umgehen — ein User kann ein eigenes
        // Repertoire mit beliebigem ChessableCourseId bzw. Dateinamen `chessable-{bid}.pgn` anlegen
        // (ImportVersion 0 = sofort „stale") und es per /reprocess re-fetchen; für gecachte Kurse liefert
        // piratechess den Inhalt ohne Eigentumsprüfung. Der Check hier schließt diese zweite Tür.
        // Ausnahme: trustOwnership (Admin-Massen-Reprocess) — Chessables getHomeData listet nur einen Teil
        // der Bibliothek, daher würde der Check eigene, längst importierte Kurse fälschlich abweisen; Admins
        // dürfen ohnehin jeden Kurs holen.
        // Dieselbe Prüfung samt Drossel wie StartImport (CourseListRefreshCooldown) — ohne sie holte „Aktualisieren"
        // über N Repertoires mit unbekannter bid die Kursliste N-mal live von Chessable (A3-014).
        if (!trustOwnership && !await ChessableImportQueueService.OwnsCourseAsync(_db, _proxy, _encryption, cred!, bid, ct)) return null;
        // Dedup: läuft/pausiert für diesen (Owner, bid) bereits ein Import, KEINEN zweiten anlegen.
        // Verhindert, dass ein erneuter „Update all"-Klick (oder ein Resume/Retry) denselben Kurs ein
        // zweites Mal komplett von Chessable holt — genau die beobachtete N-fache Flut (bid 116242 4×).
        if (await _db.ChessableImports.AnyAsync(
                x => x.UserId == ownerUserId && x.Bid == bid && (x.Status == ChessableImportStatus.Running || x.Status == ChessableImportStatus.Paused), ct))
            return null;
        var queueRound = await _db.ChessableImports.CountAsync(x => x.UserId == ownerUserId && x.Status == ChessableImportStatus.Running, ct);
        var import = new ChessableImport
        {
            UserId = ownerUserId,
            Bid = bid,
            CourseName = courseName ?? string.Empty,
            Target = target,
            TargetRepertoireId = targetRepertoireId,
            Status = ChessableImportStatus.Running,
            QueueRound = queueRound,
            CreatedAt = DateTime.UtcNow,
            // Lane-Klassifikation: voll-gecachte Kurse → schnelle, netzfreie Lane (kein Warten hinter
            // den seriellen Downloads). Cache-Check ist piratechess-DB-lokal (kein Chessable-Abruf).
            // Beim Massen-Reprocess kommt der Status vorab aus einem Batch-Abruf (knownCached) → spart
            // je Kurs einen teuren Einzel-Check, der den ganzen Cache-Blob lädt. (Oben bereits ermittelt.)
            FullyCached = fullyCached,
        };
        _db.ChessableImports.Add(import);
        await _db.SaveChangesAsync(ct);
        // Nur die Download-Lane braucht ein Queue-Ticket; die Fast-Lane treibt ihr eigener Drain-Service.
        if (import.FullyCached != true)
            await EnqueueNextAsync();
        return import.Id;
    }

    /// <summary>
    /// Faire Reihenfolge wartender Importe: nach der bei Anlage eingefrorenen
    /// <see cref="ChessableImport.QueueRound"/> (Round-Robin über die User), dann nach Einreih-Zeit.
    /// Folge: der erste Job eines neu hinzukommenden Users (Runde 0) rückt vor die noch wartenden
    /// Folge-Jobs (Runde ≥ 1) des Erst-Users — also auf die 2. Stelle, danach wird abgewechselt.
    /// Da <see cref="ChessableImport.QueueRound"/> eingefroren ist, bleibt die Reihenfolge auch dann
    /// stabil, wenn frühere Jobs fertig werden. Rein/testbar.
    /// </summary>
    public static List<ChessableImport> FairOrder(IEnumerable<ChessableImport> queued) =>
        queued
            .OrderBy(i => i.QueueRound).ThenBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .ToList();

    /// <summary>Verarbeitet den fair als Nächstes dran befindlichen wartenden Import (Phase "queued").
    /// No-op, wenn keiner wartet (z. B. Ticket-Überschuss nach einem Abbruch).
    ///
    /// Der gewählte Job wird ATOMAR übernommen (Claim): per <c>ExecuteUpdateAsync</c> wird die Phase
    /// "queued" → "claimed" gesetzt, GEFILTERT auf die noch unveränderte Phase. Nur der Worker, dessen
    /// Update tatsächlich eine Zeile trifft (Rückgabe 1), bearbeitet den Job — bei einem Resume-Sturm /
    /// mehreren parallelen Tickets kann so kein zweiter Worker denselben wartenden Job greifen
    /// (verhindert Doppelverarbeitung). Verliert der Claim (0 Zeilen), wird der nächste Kandidat
    /// versucht.</summary>
    /// <param name="fastLane">true = nur voll-gecachte Importe (<c>FullyCached==true</c>, netzfrei) der
    /// schnellen Lane greifen; false (Default) = die Download-Lane, also alles ANDERE
    /// (<c>FullyCached==false</c> ODER noch null/unklassifiziert → läuft sicher als Download statt zu
    /// hängen).</param>
    public async Task RunNextAsync(CancellationToken ct = default, bool fastLane = false)
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
            using var tracked = TrackInflight(next.Id);

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
                _db.ChangeTracker.Clear();
                var stale = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == next.Id, ct);
                if (stale is not null && stale.Status == ChessableImportStatus.Running)
                {
                    stale.FullyCached = false;
                    stale.Phase = ChessableImportPhase.Queued;
                    await _db.SaveChangesAsync(ct);
                    _logger.LogWarning(
                        "Chessable-Import {Id} (bid {Bid}) ist nicht mehr voll gecacht — zurückgestuft in die Download-Lane",
                        stale.Id, stale.Bid);
                }
                continue; // nächsten Fast-Lane-Kandidaten probieren; der Job läuft künftig gedrosselt als Download
            }

            // Der lokal getrackte Entity-Stand ist nach dem Claim veraltet → frisch laden lassen.
            _db.ChangeTracker.Clear();
            await RunAsync(next.Id, ct);
            return;
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

    /// <summary>Verarbeitet (oder resumt) den Import-Job <paramref name="importId"/>.</summary>
    public async Task RunAsync(int importId, CancellationToken ct = default)
    {
        // Auch hier (nicht nur im Drain) registrieren, damit ein DIREKTER Aufruf nicht vom Watchdog als
        // verwaister Inflight-Job zurückgeholt wird. Verschachtelung ist über den Zähler abgedeckt.
        using var tracked = TrackInflight(importId);

        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is null)
        {
            _logger.LogWarning("ChessableImport {Id} nicht gefunden", importId);
            return;
        }
        if (import.Status != ChessableImportStatus.Running)
            return; // bereits abgeschlossen oder fehlgeschlagen — nichts zu tun
        if (import.FromBrowser)
        {
            // Zweite Schranke hinter dem Drain-Filter: ein Browser-Import wird nie serverseitig geholt.
            _logger.LogWarning("Chessable-Import {Id} (bid {Bid}) ist ein Browser-Import — kein Server-Abruf", import.Id, import.Bid);
            return;
        }

        // Hol-Beginn festhalten (erste Bearbeitung aus der Queue) — trennt Wartezeit von Holzeit.
        import.StartedAt ??= DateTime.UtcNow;
        import.Attempts++;
        if (import.Attempts > MaxAttempts)
        {
            await FailAsync(import, $"Abgebrochen nach {MaxAttempts} Versuchen");
            return;
        }
        await _db.SaveChangesAsync(ct);

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
                // Bearer-Quelle: i.d.R. der Besitzer; beim Admin-Download „im Namen eines Users" der Ziel-User.
                var bearerUserId = import.BearerUserId ?? import.UserId;
                var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == bearerUserId, ct);
                string bearer;
                if (cred is null)
                {
                    // Kein Bearer: NUR möglich, wenn der Kurs voll gecacht ist — dann liefert piratechess
                    // den Kurs ohne Chessable-Kontakt/Bearer aus dem Rohdaten-Cache (leerer Bearer-String).
                    // Sonst gibt es keinen Weg, den Kurs zu holen → echter Fehler.
                    if (import.FullyCached != true)
                    {
                        await FailAsync(import, "Kein Chessable-Bearer gespeichert");
                        return;
                    }
                    bearer = string.Empty;
                }
                else
                {
                    // Circuit-Breaker: ist der Bearer als unbrauchbar markiert (Account gesperrt/gelöscht
                    // bzw. Token tot), KEINE weitere Chessable-Anfrage damit machen. Statt zu scheitern
                    // pausieren (Phase "bearer-blocked") — ein erfolgreicher „Testen“-Klick nimmt den
                    // Import via ChessableBearerBreaker.ClearAndResumeAsync automatisch wieder auf.
                    // AUSNAHME: voll-gecachte Kurse (FullyCached) kommen ohne Chessable-Abruf aus dem
                    // piratechess-DB-Cache → ein toter Bearer blockt sie nicht, der Import läuft durch.
                    if (cred.BlockedAt is not null && import.FullyCached != true)
                    {
                        import.Status = ChessableImportStatus.Paused;
                        import.Phase = ChessableImportPhase.BearerBlocked;
                        import.Attempts = 0;
                        await _db.SaveChangesAsync(ct);
                        await CancelFetchJobAsync(import, ct);
                        _logger.LogInformation(
                            "Chessable-Import {Id} pausiert: Bearer von User {UserId} gesperrt (Circuit-Breaker) — wartet auf „Testen“",
                            import.Id, bearerUserId);
                        return;
                    }

                    // Tageslimit (Standard 2000 Zeilen/24h, Chessable:DailyLineLimitPerUser): wie der
                    // Bearer-Breaker AUSGENOMMEN bei voll-gecachten Kursen (kostet Chessable nichts).
                    // Bei Überschreitung 24h pausieren (Phase "rate-limited") — der
                    // ChessableImportWatchdogService gibt den Import danach automatisch wieder frei
                    // und diese Prüfung läuft dann erneut (das Fenster ist bis dahin ohnehin abgelaufen).
                    if (import.FullyCached != true)
                    {
                        var now = DateTime.UtcNow;
                        _rateLimiter.EnsureFreshWindow(cred, now);
                        if (_rateLimiter.IsOverLimit(cred))
                        {
                            import.Status = ChessableImportStatus.Paused;
                            import.Phase = ChessableImportPhase.RateLimited;
                            import.Attempts = 0;
                            import.RateLimitedAt = now;
                            await _db.SaveChangesAsync(ct);
                            await CancelFetchJobAsync(import, ct);
                            _logger.LogInformation(
                                "Chessable-Import {Id} pausiert: Tageslimit ({Limit} Zeilen/24h) für User {UserId} erreicht — Re-Check in 24h",
                                import.Id, _rateLimiter.DailyLimit, bearerUserId);
                            return;
                        }
                        await _db.SaveChangesAsync(ct); // ggf. frisch zurückgesetztes Fenster persistieren
                    }

                    var decrypted = _encryption.TryDecrypt(cred.EncryptedBearer);
                    if (decrypted is null)
                    {
                        await FailAsync(import, "Chessable-Bearer konnte nicht entschlüsselt werden (bitte neu hinterlegen)");
                        return;
                    }
                    bearer = decrypted;
                }
                var mode = import.Target == "book" ? "FirstKeyMove" : "None";

                import.Phase = ChessableImportPhase.Fetching;
                await _db.SaveChangesAsync(ct);

                // Async Job bei piratechess starten (oder bei Resume den gemerkten weiterpollen).
                if (string.IsNullOrEmpty(import.FetchJobId))
                {
                    var start = await WithConnectionRetryAsync(
                        () => _proxy.StartCourseFetchAsync(bearer, import.Bid, mode, ct), import.Id, ct);
                    import.FetchJobId = start.JobId;
                    await _db.SaveChangesAsync(ct);
                }

                // Poll-Schleife — FORTSCHRITTS-bewusst statt festem Zeit-Limit (der frühere
                // 360×2,5s≈15min-Deckel killte langsame, aber gesunde Abrufe großer Kurse).
                // Solange ChaptersDone/LinesDone steigen, läuft der Abruf weiter; nur echter
                // Stillstand (FetchStallPolls) bzw. der Absolut-Backstop (FetchMaxPolls) beendet ihn.
                int lastMarker = import.ChaptersDone + import.LinesDone;
                int noProgressPolls = 0;
                int totalPolls = 0;

                while (string.IsNullOrEmpty(pgn))
                {
                    // Externen Abbruch/Pause erkennen (anderer Request setzt Status) — Checkpoint bleibt erhalten.
                    await _db.Entry(import).ReloadAsync(ct);
                    if (import.Status != ChessableImportStatus.Running)
                    {
                        _logger.LogInformation("Chessable-Import {Id} während Hol-Phase {Status}", import.Id, import.Status);
                        // Abgebrochen/pausiert: piratechess mit anhalten (S2-008). Das macht die Schleife selbst und
                        // nicht der Controller — dessen Abbruch kreuzte sich sonst mit dem nächsten Poll (Job weg →
                        // null → hier startete ein neuer Job).
                        await CancelFetchJobAsync(import, ct);
                        return;
                    }

                    var prog = await WithConnectionRetryAsync(
                        () => _proxy.GetCourseProgressAsync(import.FetchJobId!, ct), import.Id, ct);
                    // Abgebrochen, während dieser Import weiterläuft (Wettlauf mit dem eigenen Abbruch nach Stillstand,
                    // oder ein piratechess, der abgebrochene Jobs aufbewahrt): dasselbe wie ein verschwundener Job.
                    if (prog?.Status == ChessableCourseJobStatus.Cancelled)
                        prog = null;
                    if (prog is null)
                    {
                        // Job weg (piratechess-Neustart) → neu starten und weiter pollen.
                        var start = await WithConnectionRetryAsync(
                            () => _proxy.StartCourseFetchAsync(bearer, import.Bid, mode, ct), import.Id, ct);
                        import.FetchJobId = start.JobId;
                        await _db.SaveChangesAsync(ct);
                        await Task.Delay(PollDelayMs, ct);
                        continue;
                    }

                    import.ChaptersDone = prog.ChaptersDone;
                    import.ChaptersTotal = prog.ChaptersTotal;
                    import.LinesDone = prog.LinesDone;
                    if (prog.LinesTotal > 0) import.LinesTotal = prog.LinesTotal;

                    if (prog.Status == ChessableCourseJobStatus.Completed)
                    {
                        pgn = prog.Pgn ?? "";
                        import.LineCount = prog.LineCount;
                        if (string.IsNullOrWhiteSpace(import.CourseName))
                            import.CourseName = !string.IsNullOrWhiteSpace(prog.CourseName) ? prog.CourseName! : $"Chessable {import.Bid}";
                        import.FetchedPgn = pgn;
                        await _db.SaveChangesAsync(ct); // Checkpoint: ab hier kein erneuter Fetch nötig
                        if (import.FullyCached != true)
                        {
                            var linesFetched = import.LineCount > 0 ? import.LineCount : import.LinesDone;
                            await _rateLimiter.RecordUsageAsync(bearerUserId, linesFetched, ct);
                        }
                        break;
                    }
                    if (prog.Status == ChessableCourseJobStatus.Failed)
                        throw new InvalidOperationException(prog.Error ?? "Kurs-Abruf fehlgeschlagen");
                    if (prog.Status != ChessableCourseJobStatus.Running)
                    {
                        // Ein Status, den dieser Stand nicht kennt (piratechess neuer als rookhub): sofort scheitern,
                        // statt FetchStallPolls lang (≈ 10 min je Versuch, bis MaxAttempts) einen Stillstand abzuwarten.
                        // Den Job vorher anhalten — er könnte bei piratechess noch über die VPN-IP holen.
                        await CancelFetchJobAsync(import, ct);
                        throw new InvalidOperationException($"Unbekannter Status des Kurs-Abrufs von piratechess: \"{prog.Status}\"");
                    }

                    // Stillstand erkennen: Stall-Zähler NUR bei echtem Fortschritt zurücksetzen.
                    int marker = prog.ChaptersDone + prog.LinesDone;
                    if (marker > lastMarker)
                    {
                        lastMarker = marker;
                        noProgressPolls = 0;
                    }
                    else
                    {
                        noProgressPolls++;
                    }

                    await _db.SaveChangesAsync(ct); // Fortschritt fürs Frontend sichtbar machen

                    if (noProgressPolls >= FetchStallPolls || ++totalPolls >= FetchMaxPolls)
                        break; // Stillstand / Absolut-Grenze → unten Resume oder (nach MaxAttempts) Fehler

                    await Task.Delay(PollDelayMs, ct);
                }

                if (string.IsNullOrEmpty(pgn))
                {
                    // Abruf kam nicht durch (Stillstand/Absolut-Grenze). Resume-fähig → bis MaxAttempts
                    // AUTOMATISCH neu einreihen statt hart zu scheitern: FetchJobId/FetchedPgn-Checkpoint
                    // bleibt erhalten. Nach der Absolut-Grenze (Job macht noch Fortschritt) läuft der
                    // piratechess-Job weiter und der nächste Versuch pollt ihn wieder; bei echtem Stillstand
                    // wird er abgebrochen (S2-008), der nächste Versuch bekommt 404 und startet einen neuen.
                    // Ein älterer piratechess ohne Abbruch lässt ihn laufen — dann wie bisher weiterpollen.
                    if (noProgressPolls >= FetchStallPolls)
                        await CancelFetchJobAsync(import, ct);
                    if (import.Attempts < MaxAttempts)
                    {
                        import.Phase = ChessableImportPhase.Queued;
                        await _db.SaveChangesAsync(ct);
                        _logger.LogWarning(
                            "Chessable-Import {Id} Hol-Phase ohne Fortschritt (Versuch {Attempt}/{Max}) — wird automatisch neu eingereiht",
                            import.Id, import.Attempts, MaxAttempts);
                        await EnqueueNextAsync();
                        return;
                    }
                    throw new TimeoutException("Zeitüberschreitung beim Kurs-Abruf (kein Fortschritt)");
                }
            }

            var courseName = !string.IsNullOrWhiteSpace(import.CourseName) ? import.CourseName : $"Chessable {import.Bid}";

            // --- Phase 2: Import (idempotent) ---
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
            var queueTime = FormatDuration(import.StartedAt - import.CreatedAt);
            var fetchTime = FormatDuration(import.CompletedAt - (import.StartedAt ?? import.CreatedAt));
            await _outcome.NotifyCompletedAsync(import, courseName, queueTime, fetchTime);
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
            await FailAsync(import, ex.Message);
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("LogTags", "import,chessable"))
                _logger.LogWarning(ex, "Chessable-Import {Id} fehlgeschlagen (Versuch {Attempt})", import.Id, import.Attempts);
            await FailAsync(import, ex.Message);
        }
    }

    /// <summary>
    /// Importiert ein bereits FERTIGES Kurs-PGN (vom Browser über die RepCheck-Extension geholt und von
    /// piratechess fetch-frei geparst) direkt als Repertoire/Buch — OHNE piratechess-Fetch, Queue oder VPN.
    /// Nutzt exakt dieselben Import-Primitive (<see cref="ChessableTargetWriter"/>)
    /// wie der Server-Fetch-Pfad und legt einen abgeschlossenen <see cref="ChessableImport"/>-Satz an
    /// (Verlauf/Anzeige, Benachrichtigung). Idempotent bei erneutem Senden desselben Kurses: Buch dedupliziert
    /// per LineId, Repertoire wird IN-PLACE ersetzt (Trainings-Fortschritt bleibt), wenn schon eins aus diesem
    /// Kurs (bid) existiert.
    /// </summary>
    public async Task<ChessableImport> ImportPgnDirectAsync(
        int userId, string bid, string pgn, string courseName, string target, int lineCount, CancellationToken ct = default)
    {
        target = target == "book" ? "book" : "repertoire";
        var name = Trunc(string.IsNullOrWhiteSpace(courseName) ? $"Chessable {bid}" : courseName, 200);
        var import = new ChessableImport
        {
            UserId = userId,
            Bid = bid,
            CourseName = name,
            Target = target,
            Status = ChessableImportStatus.Running,
            Phase = ChessableImportPhase.Importing,
            LineCount = lineCount,
            FullyCached = true,   // Browser lieferte die Daten → kein Download nötig
            FromBrowser = true,   // nie von den Server-Lanes abrufen
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
        };

        // Idempotenz Repertoire: erneutes „Über meinen Browser holen" desselben Kurses landet im VORHANDENEN
        // Repertoire (Id/Fortschritt bleiben) statt ein Duplikat anzulegen — und hängt dort nur an. Match wie
        // bei BumpSiblingRepertoiresAsync über ChessableCourseId ODER den stabilen Dateinamen (das PGN des
        // Parsers trägt kein [Site]-Tag → ChessableCourseId wird beim Neuanlegen unten explizit gesetzt).
        if (target == "repertoire")
        {
            var fileName = $"chessable-{bid}.pgn";
            var existing = await _db.Repertoires
                .Where(r => r.UserId == userId && (r.ChessableCourseId == bid || r.Files.Any(f => f.FileName == fileName)))
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync(ct);
            if (existing is int repId) import.TargetRepertoireId = repId;
        }

        _db.ChessableImports.Add(import);
        await _db.SaveChangesAsync(ct);

        // Dieser Satz ist ab sofort „inflight" (running/importing), wird aber INLINE im Request
        // getrieben — nicht vom Drain. Ohne Registrierung hielte ihn der Watchdog für eine
        // verwaiste Zombie-Zeile, setzte ihn nach der Karenz auf „queued" zurück, und die
        // Fast-Lane liefe parallel zum noch laufenden Direktimport (doppelter Import, im
        // schlimmsten Fall sogar ein echter Chessable-Abruf für einen reinen Browser-Import).
        using var tracked = TrackInflight(import.Id);

        try
        {
            if (target == "book")
                await _writer.ImportAsBookAsync(import, pgn, name, ct);
            else if (import.TargetRepertoireId is not null)
            {
                // Vorhandenes Repertoire: ANHÄNGEN, nie ersetzen. Der Browser liefert oft nur einen Teil des Kurses
                // („Mitschnitt importieren" = die gerade durchgeklickten Kapitel), und das Ersetzen löschte alle übrigen
                // Partien — auf Prod stand Repertoire 232 am 2026-08-12 dreimal hintereinander auf EINER Linie. Derselbe
                // Weg wie der Live-Append: bekannte oids übersprungen, oid-lose Bestandslinien nachgetragen, Bereinigung
                // inline. Ersetzt wird nur noch beim serverseitigen Neuabruf (RunAsync), der immer den ganzen Kurs liefert.
                var (added, repId, _) = await AppendLiveAsync(userId, bid, pgn, name, target, ct);
                import.ResultId = repId;
                import.Imported = added;
                import.Skipped = Math.Max(0, lineCount - added);
                import.Invalid = 0;
            }
            else
                await _writer.ImportAsRepertoireAsync(import, pgn, name, ct);

            // Kurs-Zuordnung (bid) explizit setzen, falls neu angelegt und noch leer (das Parser-PGN trägt
            // kein [Site]-Tag) → macht künftige Browser-Importe desselben Kurses idempotent (in-place).
            if (target == "repertoire" && import.ResultId is int newRepId)
            {
                var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == newRepId, ct);
                if (rep is not null && string.IsNullOrEmpty(rep.ChessableCourseId))
                {
                    rep.ChessableCourseId = bid;
                    await _db.SaveChangesAsync(ct);
                }
            }

            import.Status = ChessableImportStatus.Completed;
            import.Phase = ChessableImportPhase.Done;
            import.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            using (LogContext.PushProperty("LogTags", "import,chessable,browser"))
                _logger.LogInformation(
                    "Browser-Import {Id} fertig: {Target} '{Name}' (bid {Bid}), imported={Imported}",
                    import.Id, import.Target, name, import.Bid, import.Imported);

            await _outcome.NotifyCompletedAsync(import, name, "0s", "0s");
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("LogTags", "import,chessable,browser"))
                _logger.LogWarning(ex, "Browser-Import {Id} fehlgeschlagen (bid {Bid})", import.Id, import.Bid);
            await FailAsync(import, ex.Message);
            throw;
        }
        return import;
    }

    /// <summary>
    /// LIVE-Append (V1 „beim Durchklicken"): hängt die frisch erfassten Linien SOFORT an das Ziel an,
    /// ohne je Aufruf einen <see cref="ChessableImport"/>-Satz oder eine Benachrichtigung zu erzeugen
    /// (sonst Flut bei per-Linie-Aufrufen). Buch: <see cref="PgnImportService.ImportFileAsync"/> (dedup per
    /// LineId). Repertoire: bestehendes chessable-{bid}-Repertoire finden (bzw. bei der ERSTEN Linie
    /// anlegen) und die neuen Linien an die PGN-Datei anhängen — dedupliziert per Zugtext, sodass erneutes
    /// Durchklicken derselben Linie (auch sitzungsübergreifend) keine Dubletten erzeugt. Liefert die Zahl der
    /// tatsächlich NEU hinzugefügten Linien + die Ziel-Id, dazu wie viele vorhandene Linien ihre oid nachgetragen
    /// bekamen (<see cref="LiveAppendResult.Linked"/>).
    /// </summary>
    public Task<LiveAppendResult> AppendLiveAsync(
        int userId, string bid, string pgn, string courseName, string target, CancellationToken ct = default,
        bool partial = false)
        => _liveAppend.AppendAsync(userId, bid, pgn, courseName, target, ct, partial);

    /// <summary>Siehe <see cref="ChessableLiveAppend.WithAppendLockAsync"/> (prozessweite Schlösser je (User, bid)).</summary>
    internal static Task<T> WithAppendLockAsync<T>(string key, Func<Task<T>> body, CancellationToken ct = default)
        => ChessableLiveAppend.WithAppendLockAsync(key, body, ct);

    /// <summary>
    /// Ergebnis eines Live-Append. <c>Linked</c> zählt vorhandene Linien, die ihre Chessable-oid nachgetragen bekamen —
    /// ohne diese Zahl las sich ein „Kurs holen", das hunderte Alt-Linien verknüpfte, als „0 neue Linien angehängt"
    /// (Kurs 207313, 2026-09-15). Zerlegt sich weiterhin in (imported, resultId, target).
    /// </summary>
    public readonly record struct LiveAppendResult(int Imported, int? ResultId, string Target, int Linked)
    {
        public void Deconstruct(out int imported, out int? resultId, out string target)
        {
            imported = Imported;
            resultId = ResultId;
            target = Target;
        }
    }

    /// <summary>
    /// Import-Fortschritt eines Chessable-Kurses für die Overlays: liefert die Menge der Chessable-oids,
    /// die der User bereits importiert hat — als Buch (<c>BookPuzzle.ChessableOid</c> des Buchs
    /// <c>chessable-u{userId}-{bid}.pgn</c>) und/oder als Repertoire (aus dem <c>[ChessableOid]</c>-Header
    /// im gespeicherten Repertoire-PGN, Datei <c>chessable-{bid}.pgn</c> bzw. <c>ChessableCourseId==bid</c>).
    /// <c>book</c>/<c>repertoire</c> zeigen, ob das jeweilige Ziel überhaupt existiert (auch bei 0 oids,
    /// z. B. Alt-Import ohne oid).
    /// </summary>
    public Task<(HashSet<string> Oids, bool Book, bool Repertoire)> GetImportedOidsAsync(
        int userId, string bid, CancellationToken ct = default)
        => _liveAppend.GetImportedOidsAsync(userId, bid, ct);

    // ===== Laufender Browser-Import (Kapitel für Kapitel) =====
    // Der Browser streamt einen Kurs in Chunks; jeder wird SOFORT geparst und angehängt, statt bis zum
    // Schluss im Speicher zu liegen (siehe ChessableIngestSessionStore). Drei Schritte, damit die
    // Sitzung trotzdem EINEN Import-Datensatz und EINE Benachrichtigung ergibt.

    /// <summary>
    /// Schritt 1: Import-Datensatz der Sitzung anlegen. Der zurückgegebene Token hält ihn als „lokal
    /// getrieben" markiert — er gehört in die Sitzung, nicht in den Request, sonst hält der Watchdog den
    /// Import zwischen zwei Chunks für verwaist und reiht ihn neu ein.
    /// </summary>
    public async Task<(ChessableImport Import, IDisposable Inflight)> StartBrowserImportAsync(
        int userId, string bid, string? courseName, string target, CancellationToken ct = default)
    {
        target = target == "book" ? "book" : "repertoire";
        var import = new ChessableImport
        {
            UserId = userId,
            Bid = bid,
            CourseName = Trunc(string.IsNullOrWhiteSpace(courseName) ? $"Chessable {bid}" : courseName!, 200),
            Target = target,
            Status = ChessableImportStatus.Running,
            Phase = ChessableImportPhase.Importing,
            FullyCached = true,   // Browser liefert die Daten → kein Chessable-Abruf
            FromBrowser = true,   // nie von den Server-Lanes abrufen
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
        };
        _db.ChessableImports.Add(import);
        await _db.SaveChangesAsync(ct);
        return (import, TrackInflight(import.Id));
    }

    /// <summary>
    /// Kapitel-Versatz für den ERSTEN Chunk einer Sitzung, die der Server nicht kennt (Codereview 2026-09-29, A3-006).
    /// Der Versatz lebt nur im Arbeitsspeicher (<see cref="ChessableIngestSessionStore"/>). Startet die API mitten im
    /// kapitelweisen Import neu (Watchtower nachts, Deploy), legt der nächste Chunk derselben Sitzung eine NEUE mit
    /// Versatz 0 an: er trägt wieder „002.xxx" und trifft auf die LineIds der schon importierten Kapitel — unbekannte oid,
    /// kein Teil-Import, also fiel jede dieser Linien auf „übersprungen", und die restlichen Kapitel fehlten still.
    /// <para>Liefert die höchste Kapitelnummer des Buchs, wenn der Chunk mit Versatz 0 auf belegte LineIds träfe UND
    /// keine seiner oids im Buch vorkommt: dann ist er keine Wiederholung schon importierter Linien, sondern neuer
    /// Inhalt, und mit Versatz 0 ginge er verloren. Sonst 0 (bisheriges Verhalten) — ein erneutes Holen desselben Kurses
    /// trägt bekannte oids und behält seine Nummern. Ohne oids lässt sich das nicht unterscheiden: dann auch 0. Belegt
    /// zählt nur eine LineId mit oid — ein Alt-Buch ohne oids behält Versatz 0, damit der oid-Nachtrag greift.</para>
    /// </summary>
    public async Task<int> ResumeChapterOffsetAsync(int userId, string bid, string chunkPgn, CancellationToken ct = default)
    {
        var fileName = $"chessable-u{userId}-{bid}.pgn";
        var parsed = PgnImportService.ParsePgn(fileName, chunkPgn, keepCommentOnlyAsInfo: true).Puzzles;
        var oids = parsed.Where(p => !string.IsNullOrEmpty(p.ChessableOid)).Select(p => p.ChessableOid!)
            .Distinct(StringComparer.Ordinal).ToList();
        if (oids.Count == 0) return 0;
        if (await _db.BookPuzzles.AnyAsync(bp => bp.BookFileName == fileName && bp.ChessableOid != null
                && oids.Contains(bp.ChessableOid), ct))
            return 0;
        // Nur eine Kollision mit Linien, die eine oid TRAGEN, zeigt einen Neustart mitten im Import an (die Kapitel dieser
        // Sitzung sind mit oid gespeichert). Trifft der Chunk nur auf oid-lose Altlinien (Import vor piratechess v1.29.0),
        // ist das erneute Holen eines Alt-Buchs: der oid-Nachtrag (gleiche LineId + Züge + StartPly, v0.476.1) braucht
        // Versatz 0 — hinter dem Buch stünde der ganze Kurs doppelt da.
        var lineIds = parsed.Select(p => p.LineId).Distinct(StringComparer.Ordinal).ToList();
        if (!await _db.BookPuzzles.AnyAsync(bp => bp.BookFileName == fileName && bp.ChessableOid != null
                && lineIds.Contains(bp.LineId), ct))
            return 0;

        var rounds = await _db.BookPuzzles.Where(bp => bp.BookFileName == fileName).Select(bp => bp.Round).ToListAsync(ct);
        var maxChapter = rounds.Count == 0 ? 0 : rounds.Max(ChessableRoundOffset.ChapterOf);
        if (maxChapter > 0)
            using (LogContext.PushProperty("LogTags", "import,chessable,browser"))
                _logger.LogInformation(
                    "Browser-Import: unbekannte Sitzung trifft auf belegte Kapitelnummern — setzt hinter Kapitel {MaxChapter} fort (User {UserId}, bid {Bid})",
                    maxChapter, userId, bid);
        return maxChapter;
    }

    /// <summary>
    /// Schritt 2: einen Kapitel-Chunk anhängen. Geht denselben Weg wie der Live-Append (dedupliziert,
    /// je (User, bid) serialisiert) und schreibt die Zähler des Import-Datensatzes fort, damit die
    /// Warteschlangen-Anzeige mitwächst.
    /// </summary>
    public async Task<LiveAppendResult> AppendBrowserChunkAsync(
        int importId, string pgn, int linesInChunk, bool partial = false, CancellationToken ct = default)
    {
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct)
            ?? throw new InvalidOperationException($"Chessable import {importId} not found.");

        var res = await AppendLiveAsync(import.UserId, import.Bid, pgn, import.CourseName, import.Target, ct, partial);

        // Der Live-Append setzt den Anzeigenamen eines Buchs nur, wenn er LEER ist — er ist fuer vorhandene
        // Buecher gedacht, deren Namen der Nutzer gepflegt haben kann. PgnImportService legt ein neues Buch aber
        // schon mit dem Dateinamen als Anzeigename an (chessable-u5-55720), und genau so hiess das Buch nach dem
        // ersten laufenden Import (2026-09-19). Ein Buch, das noch seinen Dateinamen traegt, bekommt hier den
        // Kursnamen; ein vom Nutzer umbenanntes bleibt unangetastet.
        if (import.Target == "book" && res.ResultId is int bookId)
        {
            var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct);
            if (book is not null && IsFileNameDerived(book.DisplayName, book.FileName))
                book.DisplayName = import.CourseName;
        }

        import.Imported += res.Imported;
        import.Skipped += Math.Max(0, linesInChunk - res.Imported);
        import.LineCount += linesInChunk;
        import.LinesDone = import.LineCount;
        import.ChaptersDone += 1;
        import.ChaptersTotal = Math.Max(import.ChaptersTotal, import.ChaptersDone);
        if (res.ResultId is not null) import.ResultId = res.ResultId;
        await _db.SaveChangesAsync(ct);
        return res;
    }

    /// <summary>
    /// Schritt 3: Sitzung abschließen — Status, Kurs-Zuordnung, Log, Benachrichtigung. <c>null</c>, wenn
    /// der Datensatz nicht (mehr) existiert.
    /// </summary>
    public async Task<ChessableImport?> FinishBrowserImportAsync(int importId, CancellationToken ct = default)
    {
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is null) return null;

        // Kurs-Zuordnung (bid) nachtragen: das Parser-PGN trägt kein [Site]-Tag, und ohne sie fände ein
        // späterer Browser-Import das Repertoire nicht wieder und legte ein zweites an.
        if (import.Target == "repertoire" && import.ResultId is int repId)
        {
            var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == repId, ct);
            if (rep is not null && string.IsNullOrEmpty(rep.ChessableCourseId)) rep.ChessableCourseId = import.Bid;
        }

        import.Status = ChessableImportStatus.Completed;
        import.Phase = ChessableImportPhase.Done;
        import.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        using (LogContext.PushProperty("LogTags", "import,chessable,browser"))
            _logger.LogInformation(
                "Browser-Import {Id} fertig (laufend): {Target} '{Name}' (bid {Bid}), Kapitel={Chapters}, imported={Imported}",
                import.Id, import.Target, import.CourseName, import.Bid, import.ChaptersDone, import.Imported);

        await _outcome.NotifyCompletedAsync(import, import.CourseName, "0s", "0s");
        return import;
    }

    /// <summary>Siehe <see cref="ChessableImportTargets.IsFileNameDerived"/>.</summary>
    internal static bool IsFileNameDerived(string? displayName, string? fileName)
        => ChessableImportTargets.IsFileNameDerived(displayName, fileName);

    /// <summary>Bricht den Import-Datensatz einer Sitzung ab. Das bereits Importierte BLEIBT — es liegt
    /// in der Datenbank und ist genau der Gewinn des laufenden Imports.</summary>
    public async Task FailBrowserImportAsync(int importId, string message, CancellationToken ct = default)
    {
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is not null) await FailAsync(import, message);
    }

    // Reine Verbindungsfehler zu piratechess (Container-Neustart/kurzer Ausfall) dürfen einen Import
    // NICHT sofort scheitern lassen — sonst killt ein 5-Sekunden-Recreate die ganze Queue kaskadierend.
    // Daher mit Backoff erneut versuchen (summiert ~2,5 min), bis piratechess wieder erreichbar ist;
    // erst danach propagiert der Fehler und der Import scheitert. Echte Antwort-Fehler von piratechess
    // (ChessableProxyException, kein Transportfehler) werden NICHT erneut versucht.
    private static readonly int[] ConnRetryBackoffMs = { 3000, 5000, 10000, 15000, 20000, 30000, 30000, 30000 };

    /// <summary>Codereview S2-008: den piratechess-Kurs-Job des Imports best effort abbrechen (Abbruch, Pause,
    /// erkannter Stillstand), damit piratechess den Kurs nicht trotzdem ganz über die VPN-IP holt. Blockiert nie:
    /// kurzer Timeout, 404/405 (älterer piratechess) still, andere Fehler nur geloggt. Die JobId bleibt stehen — ein
    /// späteres Pollen eines abgebrochenen Jobs liefert 404, dann startet der Import einen neuen.</summary>
    private async Task CancelFetchJobAsync(ChessableImport import, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(import.FetchJobId))
            await _proxy.CancelCourseJobAsync(import.FetchJobId, ct);
    }

    private async Task<T> WithConnectionRetryAsync<T>(Func<Task<T>> op, int importId, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return await op(); }
            // Bei Shutdown (ct abgebrochen) NICHT erneut versuchen → Abbruch sauber durchreichen.
            catch (Exception ex) when (attempt < ConnRetryBackoffMs.Length && !ct.IsCancellationRequested && IsTransientConnectionError(ex))
            {
                var delay = ConnRetryBackoffMs[attempt];
                _logger.LogWarning(
                    "Chessable-Import {Id}: piratechess nicht erreichbar ({Msg}) — Verbindungs-Retry {Attempt}/{Max} in {Delay}s",
                    importId, ex.Message, attempt + 1, ConnRetryBackoffMs.Length, delay / 1000);
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>True für reine Transport-/Verbindungsfehler (piratechess down/Neustart): SocketException,
    /// HttpRequestException oder typische „connection refused"/„timed out"-Meldungen. NICHT für
    /// <see cref="ChessableProxyException"/> (= piratechess hat geantwortet, aber mit Fehler).</summary>
    internal static bool IsTransientConnectionError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is ChessableProxyException) return false;
            if (e is System.Net.Sockets.SocketException or HttpRequestException or TaskCanceledException) return true;
            var m = e.Message;
            if (m.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
                || m.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
                || m.Contains("No route to host", StringComparison.OrdinalIgnoreCase)
                || m.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)
                || m.Contains("timed out", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Internal statt private: die Zombie-Haertung (Fangnetze unten) war der Ausloeser echter
    // Prod-Haenger und braucht direkte Tests — sie ist ueber die oeffentliche API nicht erreichbar,
    // ohne eine halbe Chessable-Session zu simulieren.
    internal Task FailAsync(ChessableImport import, string error) => _outcome.FailAsync(import, error);

    private static string Trunc(string s, int max) => ChessableImportTargets.Trunc(s, max);

    /// <summary>Siehe <see cref="ChessableImportProgress.FormatDuration"/>.</summary>
    internal static string FormatDuration(TimeSpan? span) => ChessableImportProgress.FormatDuration(span);
}
