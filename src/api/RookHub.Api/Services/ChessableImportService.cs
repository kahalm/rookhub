using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using Serilog.Context;

namespace RookHub.Api.Services;

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

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _proxy;
    private readonly RepertoireService _repertoires;
    private readonly PgnImportService _pgnImport;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly NotificationService _notifications;
    private readonly ChessableBearerBreaker _breaker;
    private readonly ILogger<ChessableImportService> _logger;

    public ChessableImportService(
        AppDbContext db,
        EncryptionService encryption,
        ChessableProxyService proxy,
        RepertoireService repertoires,
        PgnImportService pgnImport,
        IBackgroundTaskQueue taskQueue,
        NotificationService notifications,
        ChessableBearerBreaker breaker,
        ILogger<ChessableImportService> logger)
    {
        _db = db;
        _encryption = encryption;
        _proxy = proxy;
        _repertoires = repertoires;
        _pgnImport = pgnImport;
        _taskQueue = taskQueue;
        _notifications = notifications;
        _breaker = breaker;
        _logger = logger;
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
        if (!trustOwnership && !await OwnerHasCourseAsync(cred!, bid, ct)) return null;
        // Dedup: läuft/pausiert für diesen (Owner, bid) bereits ein Import, KEINEN zweiten anlegen.
        // Verhindert, dass ein erneuter „Update all"-Klick (oder ein Resume/Retry) denselben Kurs ein
        // zweites Mal komplett von Chessable holt — genau die beobachtete N-fache Flut (bid 116242 4×).
        if (await _db.ChessableImports.AnyAsync(
                x => x.UserId == ownerUserId && x.Bid == bid && (x.Status == "running" || x.Status == "paused"), ct))
            return null;
        var queueRound = await _db.ChessableImports.CountAsync(x => x.UserId == ownerUserId && x.Status == "running", ct);
        var import = new ChessableImport
        {
            UserId = ownerUserId,
            Bid = bid,
            CourseName = courseName ?? string.Empty,
            Target = target,
            TargetRepertoireId = targetRepertoireId,
            Status = "running",
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

    /// <summary>Prüft, ob <paramref name="bid"/> in der Chessable-Bibliothek zum Bearer von
    /// <paramref name="cred"/> liegt (gecachte Kursliste zuerst, sonst einmal frisch laden + Cache
    /// aktualisieren). Nicht verifizierbar (Bearer kaputt / Chessable-Fehler) ⇒ false (fail-closed).
    /// Gegenstück zu <c>ChessableController.UserOwnsCourseAsync</c> für die Re-Fetch-Pfade.</summary>
    private async Task<bool> OwnerHasCourseAsync(ChessableCredential cred, string bid, CancellationToken ct)
    {
        bool Has(string? json) =>
            !string.IsNullOrEmpty(json)
            && (System.Text.Json.JsonSerializer.Deserialize<List<DTOs.ChessableCourseDto>>(
                    json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? new())
               .Any(c => c.Bid == bid);

        if (Has(cred.CachedCoursesJson)) return true;

        var bearer = _encryption.TryDecrypt(cred.EncryptedBearer);
        if (bearer is null) return false;
        try
        {
            var courses = await _proxy.GetCoursesAsync(bearer, ct);
            cred.CachedCoursesJson = System.Text.Json.JsonSerializer.Serialize(
                courses, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            cred.CoursesCachedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return courses.Any(c => c.Bid == bid);
        }
        catch (ChessableProxyException)
        {
            return false;
        }
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
        var queued = await _db.ChessableImports
            .Where(i => i.Status == "running" && i.Phase == "queued"
                && (fastLane ? i.FullyCached == true : i.FullyCached != true))
            .ToListAsync(ct);

        foreach (var next in FairOrder(queued))
        {
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
                if (stale is not null && stale.Status == "running")
                {
                    stale.FullyCached = false;
                    stale.Phase = "queued";
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
                .Where(i => i.Id == importId && i.Status == "running" && i.Phase == "queued")
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Phase, "claimed"), ct);
            return rows == 1;
        }

        // InMemory-Fallback: nur claimen, wenn die Phase noch "queued" ist.
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is null || import.Status != "running" || import.Phase != "queued")
            return false;
        import.Phase = "claimed";
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Verarbeitet (oder resumt) den Import-Job <paramref name="importId"/>.</summary>
    public async Task RunAsync(int importId, CancellationToken ct = default)
    {
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is null)
        {
            _logger.LogWarning("ChessableImport {Id} nicht gefunden", importId);
            return;
        }
        if (import.Status != "running")
            return; // bereits abgeschlossen oder fehlgeschlagen — nichts zu tun

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
                        import.Status = "paused";
                        import.Phase = "bearer-blocked";
                        import.Attempts = 0;
                        await _db.SaveChangesAsync(ct);
                        _logger.LogInformation(
                            "Chessable-Import {Id} pausiert: Bearer von User {UserId} gesperrt (Circuit-Breaker) — wartet auf „Testen“",
                            import.Id, bearerUserId);
                        return;
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

                import.Phase = "fetching";
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
                    if (import.Status != "running")
                    {
                        _logger.LogInformation("Chessable-Import {Id} während Hol-Phase {Status}", import.Id, import.Status);
                        return;
                    }

                    var prog = await WithConnectionRetryAsync(
                        () => _proxy.GetCourseProgressAsync(import.FetchJobId!, ct), import.Id, ct);
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

                    if (prog.Status == "completed")
                    {
                        pgn = prog.Pgn ?? "";
                        import.LineCount = prog.LineCount;
                        if (string.IsNullOrWhiteSpace(import.CourseName))
                            import.CourseName = !string.IsNullOrWhiteSpace(prog.CourseName) ? prog.CourseName! : $"Chessable {import.Bid}";
                        import.FetchedPgn = pgn;
                        await _db.SaveChangesAsync(ct); // Checkpoint: ab hier kein erneuter Fetch nötig
                        break;
                    }
                    if (prog.Status == "failed")
                        throw new InvalidOperationException(prog.Error ?? "Kurs-Abruf fehlgeschlagen");

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
                    // bleibt erhalten, der piratechess-Job läuft weiter bzw. die Rohdaten sind dann gecacht.
                    if (import.Attempts < MaxAttempts)
                    {
                        import.Phase = "queued";
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
            import.Phase = "importing";
            await _db.SaveChangesAsync(ct);

            if (import.Target == "repertoire")
                await ImportAsRepertoireAsync(import, pgn, courseName, ct);
            else
                await ImportAsBookAsync(import, pgn, courseName, ct);

            import.Status = "completed";
            import.Phase = "done";
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
            await _notifications.CreateAsync(import.UserId, NotificationType.ChessableImportCompleted,
                new Dictionary<string, string>
                {
                    ["courseName"] = courseName,
                    ["target"] = import.Target,
                    ["queueTime"] = queueTime,
                    ["fetchTime"] = fetchTime,
                },
                import.Target == "book" ? "/courses" : "/repertoires");
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

    private async Task ImportAsRepertoireAsync(ChessableImport import, string pgn, string courseName, CancellationToken ct)
    {
        // In-place-Re-Import (Reprocess-Re-Fetch): das frische PGN ersetzt ein BESTEHENDES Repertoire,
        // damit dessen Id und damit der Trainings-Fortschritt erhalten bleiben.
        if (import.TargetRepertoireId is int target
            && await _db.Repertoires.AnyAsync(r => r.Id == target && r.UserId == import.UserId, ct))
        {
            // Erst die neue Datei hochladen, dann die alten löschen → nie ein Zeitfenster mit 0 Dateien.
            var oldIds = await _db.RepertoireFiles.Where(f => f.RepertoireId == target).Select(f => f.Id).ToListAsync(ct);
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(pgn)))
                await _repertoires.UploadFileAsync(target, import.UserId, $"chessable-{import.Bid}.pgn", ms); // setzt ImportVersion + ChessableCourseId neu
            if (oldIds.Count > 0)
            {
                var olds = await _db.RepertoireFiles.Where(f => oldIds.Contains(f.Id)).ToListAsync(ct);
                _db.RepertoireFiles.RemoveRange(olds);
                await _db.SaveChangesAsync(ct);
            }
            import.ResultId = target;
            import.Imported = import.LineCount;
            import.Skipped = 0;
            import.Invalid = 0;
            // Mehrere Repertoires desselben Users können auf demselben Chessable-Kurs (bid) beruhen. Der
            // (Owner,bid)-Dedup holt den Kurs aber nur EINMAL → die Geschwister blieben sonst veraltet.
            // Darum das frische PGN auch in alle Geschwister-Repertoires desselben bid schreiben.
            await BumpSiblingRepertoiresAsync(import, pgn, target, ct);
            return;
        }

        // Resume: Repertoire evtl. in einem vorigen Versuch schon angelegt → nicht doppeln.
        int repId;
        if (import.ResultId is int existing
            && await _db.Repertoires.AnyAsync(r => r.Id == existing && r.UserId == import.UserId, ct))
        {
            repId = existing;
        }
        else
        {
            var rep = await _repertoires.CreateAsync(import.UserId, new CreateRepertoireDto
            {
                Name = Trunc(courseName, 200),
                Description = $"Aus Chessable importiert (bid {import.Bid})",
                Kind = RepertoireKind.Opening,
                IsPublic = false,
                // Importierte Kurse standardmäßig NICHT von der RepCheck-Extension verwenden.
                UseForExtension = false
            });
            repId = rep.Id;
            import.ResultId = repId;
            await _db.SaveChangesAsync(ct); // Checkpoint: Repertoire-Anlage
        }

        // Datei nur hochladen, wenn noch keine da ist (idempotent bei Resume).
        if (!await _db.RepertoireFiles.AnyAsync(f => f.RepertoireId == repId, ct))
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(pgn));
            await _repertoires.UploadFileAsync(repId, import.UserId, $"chessable-{import.Bid}.pgn", ms);
        }

        import.Imported = import.LineCount;
        import.Skipped = 0;
        import.Invalid = 0;
    }

    /// <summary>Schreibt das frisch geholte PGN zusätzlich in alle WEITEREN Repertoires desselben Users, die
    /// auf demselben Chessable-Kurs (bid) beruhen (per ChessableCourseId oder Dateiname), außer dem bereits
    /// aktualisierten <paramref name="target"/>. In-place (Id/Fortschritt bleiben) → UploadFileAsync hebt die
    /// ImportVersion. Ohne das blieben Kurs-Duplikate nach „Alle aktualisieren" veraltet (Dedup holt 1×).</summary>
    private async Task BumpSiblingRepertoiresAsync(ChessableImport import, string pgn, int target, CancellationToken ct)
    {
        var fileName = $"chessable-{import.Bid}.pgn";
        var siblingIds = await _db.Repertoires
            .Where(r => r.UserId == import.UserId && r.Id != target
                && (r.ChessableCourseId == import.Bid || r.Files.Any(f => f.FileName == fileName)))
            .Select(r => r.Id).ToListAsync(ct);

        foreach (var repId in siblingIds)
        {
            var oldIds = await _db.RepertoireFiles.Where(f => f.RepertoireId == repId).Select(f => f.Id).ToListAsync(ct);
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(pgn)))
                await _repertoires.UploadFileAsync(repId, import.UserId, fileName, ms); // hebt ImportVersion + ChessableCourseId
            if (oldIds.Count > 0)
            {
                var olds = await _db.RepertoireFiles.Where(f => oldIds.Contains(f.Id)).ToListAsync(ct);
                _db.RepertoireFiles.RemoveRange(olds);
                await _db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task ImportAsBookAsync(ChessableImport import, string pgn, string courseName, CancellationToken ct)
    {
        // Pro-User-eindeutiger Dateiname; PgnImportService dedupliziert per LineId → von Natur aus
        // idempotent, ein Resume legt dieselben Puzzles nicht doppelt an.
        var fileName = $"chessable-u{import.UserId}-{import.Bid}.pgn";
        var res = await _pgnImport.ImportFileAsync(fileName, pgn, ct);

        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == res.BookId, ct);
        if (book is not null)
        {
            book.OwnerUserId = import.UserId;
            book.DisplayName = Trunc(courseName, 200);
            book.Tags = "chessable";
            book.UpdatedAt = DateTime.UtcNow;
        }

        import.ResultId = res.BookId;
        // Gesamtzahl der Puzzles im Buch (korrekt auch beim Resume, wo res.Imported nur die neuen zählt).
        import.Imported = await _db.BookPuzzles.CountAsync(bp => bp.BookId == res.BookId, ct);
        import.Skipped = res.Skipped;
        import.Invalid = res.Invalid;
    }

    // Reine Verbindungsfehler zu piratechess (Container-Neustart/kurzer Ausfall) dürfen einen Import
    // NICHT sofort scheitern lassen — sonst killt ein 5-Sekunden-Recreate die ganze Queue kaskadierend.
    // Daher mit Backoff erneut versuchen (summiert ~2,5 min), bis piratechess wieder erreichbar ist;
    // erst danach propagiert der Fehler und der Import scheitert. Echte Antwort-Fehler von piratechess
    // (ChessableProxyException, kein Transportfehler) werden NICHT erneut versucht.
    private static readonly int[] ConnRetryBackoffMs = { 3000, 5000, 10000, 15000, 20000, 30000, 30000, 30000 };

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

    private async Task FailAsync(ChessableImport import, string error)
    {
        import.Status = "failed";
        import.Error = Trunc(error, 1000);
        import.CompletedAt = DateTime.UtcNow;
        // Terminalen Status mit CancellationToken.None festschreiben: scheitert ein Import WEGEN einer
        // (Timeout-)Cancellation, muss er trotzdem als "failed" persistiert werden — sonst bliebe er als
        // Zombie auf "running" haengen und wuerde beim Neustart endlos resumed.
        await _db.SaveChangesAsync(CancellationToken.None);

        // Circuit-Breaker: war der Fehlschlag fatal für den Bearer selbst (Account gesperrt/gelöscht
        // bzw. Token tot), den Bearer sperren — danach pausieren weitere Importe sofort, statt mit dem
        // toten Bearer immer wieder fehlzuschlagen (siehe ChessableBearerBreaker). IP-/VPN-Blocks lösen
        // das bewusst NICHT aus.
        if (ChessableBearerBreaker.IsBearerFatal(error))
            await _breaker.TripAsync(import.BearerUserId ?? import.UserId, error, CancellationToken.None);

        // User benachrichtigen: Import fehlgeschlagen.
        var name = !string.IsNullOrWhiteSpace(import.CourseName) ? import.CourseName : $"Chessable {import.Bid}";
        await _notifications.CreateAsync(import.UserId, NotificationType.ChessableImportFailed,
            new Dictionary<string, string> { ["courseName"] = name }, "/chessable");
    }

    private static string Trunc(string s, int max) => s.Length > max ? s[..max] : s;

    /// <summary>Kompakte, sprachneutrale Dauer für Meldungen: "1 h 5 min", "12 min", "45 s"; "—" wenn unbekannt.</summary>
    internal static string FormatDuration(TimeSpan? span)
    {
        if (span is null || span.Value < TimeSpan.Zero) return "—";
        var t = span.Value;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} min";
        return $"{(int)t.TotalSeconds} s";
    }
}
