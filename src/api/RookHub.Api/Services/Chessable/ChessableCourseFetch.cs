using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Chessable;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Holt den Kurs EINES Server-Imports bei piratechess: Bearer auflösen (Breaker, Tageslimit, Entschlüsselung),
/// Kurs-Job starten und fortschrittsbewusst pollen, das PGN als Checkpoint sichern; bei Stillstand neu einreihen.
/// Phase 1 von <see cref="ChessableFetchLane.RunAsync"/>; Teil von <see cref="ChessableImportService"/>
/// (Zerlegung Z3), von der Fassade gebaut, loggt unter deren Quelle.
/// </summary>
internal sealed class ChessableCourseFetch
{
    private const int MaxAttempts = ChessableImportService.MaxAttempts;

    // Poll-Tuning (intern überschreibbar für Tests). piratechess holt bewusst LANGSAM
    // (Inter-Request-Delay 2–5 s + Linien-Retries + VPN-Rotation) → ein großer Kurs dauert
    // leicht >15 min. Der Abruf darf daher NICHT an einem festen Zeit-Limit scheitern, solange
    // er Fortschritt macht. Nur echter Stillstand (FetchStallPolls Polls ohne Fortschritt) bzw.
    // der großzügige Absolut-Backstop beenden das Polling.
    internal int PollDelayMs = 2500;
    internal int FetchStallPolls = 240;   // aufeinanderfolgende Polls OHNE Fortschritt → Stillstand (≈10 min)
    internal int FetchMaxPolls = 2160;    // Absolut-Backstop (≈90 min) gegen echte Endlosschleifen

    // Reine Verbindungsfehler zu piratechess (Container-Neustart/kurzer Ausfall) dürfen einen Import
    // NICHT sofort scheitern lassen — sonst killt ein 5-Sekunden-Recreate die ganze Queue kaskadierend.
    // Daher mit Backoff erneut versuchen (summiert ~2,5 min), bis piratechess wieder erreichbar ist;
    // erst danach propagiert der Fehler und der Import scheitert. Echte Antwort-Fehler von piratechess
    // (ChessableProxyException, kein Transportfehler) werden NICHT erneut versucht — außer 429 (Drosselung, I2-005).
    private static readonly int[] ConnRetryBackoffMs = { 3000, 5000, 10000, 15000, 20000, 30000, 30000, 30000 };

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _proxy;
    private readonly ChessableRateLimiter _rateLimiter;
    private readonly ChessableImportOutcome _outcome;
    private readonly ChessableReimportQueue _queue;
    private readonly ILogger<ChessableImportService> _logger;

    public ChessableCourseFetch(AppDbContext db, EncryptionService encryption, ChessableProxyService proxy,
        ChessableRateLimiter rateLimiter, ChessableImportOutcome outcome, ChessableReimportQueue queue,
        ILogger<ChessableImportService> logger)
    {
        _db = db;
        _encryption = encryption;
        _proxy = proxy;
        _rateLimiter = rateLimiter;
        _outcome = outcome;
        _queue = queue;
        _logger = logger;
    }

    /// <summary>Phase 1: Kurs bei piratechess holen. Liefert das PGN (als Checkpoint persistiert), oder <c>null</c>,
    /// wenn der Lauf hier endet (pausiert, abgebrochen, gescheitert, neu eingereiht).</summary>
    public async Task<string?> FetchPgnAsync(ChessableImport import, CancellationToken ct)
    {
        // Bearer-Quelle: i.d.R. der Besitzer; beim Admin-Download „im Namen eines Users" der Ziel-User.
        var bearerUserId = import.BearerUserId ?? import.UserId;
        var bearer = await ResolveBearerAsync(import, bearerUserId, ct);
        if (bearer is null) return null;
        var mode = ChessableImportTargets.ParseMode(import.Target);

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

        var poll = await PollUntilFetchedAsync(import, bearer, mode, bearerUserId, ct);
        if (poll.Stopped) return null;
        if (!string.IsNullOrEmpty(poll.Pgn)) return poll.Pgn;

        // Abruf kam nicht durch (Stillstand/Absolut-Grenze). Resume-fähig → bis MaxAttempts
        // AUTOMATISCH neu einreihen statt hart zu scheitern: FetchJobId/FetchedPgn-Checkpoint
        // bleibt erhalten. Nach der Absolut-Grenze (Job macht noch Fortschritt) läuft der
        // piratechess-Job weiter und der nächste Versuch pollt ihn wieder; bei echtem Stillstand
        // wird er abgebrochen (S2-008), der nächste Versuch bekommt 404 und startet einen neuen.
        // Ein älterer piratechess ohne Abbruch lässt ihn laufen — dann wie bisher weiterpollen.
        if (poll.NoProgressPolls >= FetchStallPolls)
            await CancelFetchJobAsync(import, ct);
        if (import.Attempts < MaxAttempts)
        {
            import.Phase = ChessableImportPhase.Queued;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Chessable-Import {Id} Hol-Phase ohne Fortschritt (Versuch {Attempt}/{Max}) — wird automatisch neu eingereiht",
                import.Id, import.Attempts, MaxAttempts);
            await _queue.EnqueueNextAsync();
            return null;
        }
        throw new TimeoutException("Zeitüberschreitung beim Kurs-Abruf (kein Fortschritt)");
    }

    /// <summary>Bearer für den Abruf: leer bei voll gecachtem Kurs ohne Bearer, sonst entschlüsselt. <c>null</c> =
    /// Lauf endet hier (kein Bearer → gescheitert; Breaker/Tageslimit → pausiert; nicht entschlüsselbar → gescheitert).</summary>
    private async Task<string?> ResolveBearerAsync(ChessableImport import, int bearerUserId, CancellationToken ct)
    {
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == bearerUserId, ct);
        if (cred is null)
        {
            // Kein Bearer: NUR möglich, wenn der Kurs voll gecacht ist — dann liefert piratechess
            // den Kurs ohne Chessable-Kontakt/Bearer aus dem Rohdaten-Cache (leerer Bearer-String).
            // Sonst gibt es keinen Weg, den Kurs zu holen → echter Fehler.
            if (import.FullyCached != true)
            {
                await _outcome.FailAsync(import, "Kein Chessable-Bearer gespeichert");
                return null;
            }
            return string.Empty;
        }

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
            return null;
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
                return null;
            }
            await _db.SaveChangesAsync(ct); // ggf. frisch zurückgesetztes Fenster persistieren
        }

        var decrypted = _encryption.TryDecrypt(cred.EncryptedBearer);
        if (decrypted is null)
        {
            await _outcome.FailAsync(import, "Chessable-Bearer konnte nicht entschlüsselt werden (bitte neu hinterlegen)");
            return null;
        }
        return decrypted;
    }

    /// <summary>Ergebnis der Poll-Schleife: <c>Stopped</c> = extern abgebrochen/pausiert (Lauf endet), sonst das
    /// PGN (leer = nicht durchgekommen) und die Zahl der Polls ohne Fortschritt am Ende.</summary>
    private readonly record struct PollOutcome(bool Stopped, string? Pgn, int NoProgressPolls);

    private async Task<PollOutcome> PollUntilFetchedAsync(
        ChessableImport import, string bearer, string mode, int bearerUserId, CancellationToken ct)
    {
        // Poll-Schleife — FORTSCHRITTS-bewusst statt festem Zeit-Limit (der frühere
        // 360×2,5s≈15min-Deckel killte langsame, aber gesunde Abrufe großer Kurse).
        // Solange ChaptersDone/LinesDone steigen, läuft der Abruf weiter; nur echter
        // Stillstand (FetchStallPolls) bzw. der Absolut-Backstop (FetchMaxPolls) beendet ihn.
        int lastMarker = import.ChaptersDone + import.LinesDone;
        int noProgressPolls = 0;
        int totalPolls = 0;
        string? pgn = null;

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
                return new PollOutcome(true, null, noProgressPolls);
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
        return new PollOutcome(false, pgn, noProgressPolls);
    }

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

    /// <summary>Siehe <see cref="ChessableImportService.IsTransientConnectionError"/>.</summary>
    internal static bool IsTransientConnectionError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            // 429: piratechess drosselt (EIN Fenster für rookhub) und erwartet Backoff — wiederholbar wie ein kurzer
            // Ausfall (I2-005). Jede andere Antwort von piratechess ist ein echter Fehler.
            if (e is ChessableProxyException proxyEx) return proxyEx.Status == System.Net.HttpStatusCode.TooManyRequests;
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
}
