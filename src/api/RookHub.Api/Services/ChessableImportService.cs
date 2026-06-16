using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

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
public class ChessableImportService
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

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _proxy;
    private readonly RepertoireService _repertoires;
    private readonly PgnImportService _pgnImport;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly NotificationService _notifications;
    private readonly ILogger<ChessableImportService> _logger;

    public ChessableImportService(
        AppDbContext db,
        EncryptionService encryption,
        ChessableProxyService proxy,
        RepertoireService repertoires,
        PgnImportService pgnImport,
        IBackgroundTaskQueue taskQueue,
        NotificationService notifications,
        ILogger<ChessableImportService> logger)
    {
        _db = db;
        _encryption = encryption;
        _proxy = proxy;
        _repertoires = repertoires;
        _pgnImport = pgnImport;
        _taskQueue = taskQueue;
        _notifications = notifications;
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
    /// No-op, wenn keiner wartet (z. B. Ticket-Überschuss nach einem Abbruch).</summary>
    public async Task RunNextAsync(CancellationToken ct = default)
    {
        var queued = await _db.ChessableImports
            .Where(i => i.Status == "running" && i.Phase == "queued")
            .ToListAsync(ct);
        var next = FairOrder(queued).FirstOrDefault();
        if (next is null) return;
        await RunAsync(next.Id, ct);
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
            await FailAsync(import, $"Abgebrochen nach {MaxAttempts} Versuchen", ct);
            return;
        }
        await _db.SaveChangesAsync(ct);

        try
        {
            // --- Phase 1: Kurs holen (Checkpoint: PGN wird persistiert) ---
            var pgn = import.FetchedPgn;
            if (string.IsNullOrEmpty(pgn))
            {
                // Bearer-Quelle: i.d.R. der Besitzer; beim Admin-Download „im Namen eines Users" der Ziel-User.
                var bearerUserId = import.BearerUserId ?? import.UserId;
                var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == bearerUserId, ct);
                if (cred is null)
                {
                    await FailAsync(import, "Kein Chessable-Bearer gespeichert", ct);
                    return;
                }
                var bearer = _encryption.Decrypt(cred.EncryptedBearer);
                var mode = import.Target == "book" ? "FirstKeyMove" : "None";

                import.Phase = "fetching";
                await _db.SaveChangesAsync(ct);

                // Async Job bei piratechess starten (oder bei Resume den gemerkten weiterpollen).
                if (string.IsNullOrEmpty(import.FetchJobId))
                {
                    var start = await _proxy.StartCourseFetchAsync(bearer, import.Bid, mode, ct);
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

                    var prog = await _proxy.GetCourseProgressAsync(import.FetchJobId!, ct);
                    if (prog is null)
                    {
                        // Job weg (piratechess-Neustart) → neu starten und weiter pollen.
                        var start = await _proxy.StartCourseFetchAsync(bearer, import.Bid, mode, ct);
                        import.FetchJobId = start.JobId;
                        await _db.SaveChangesAsync(ct);
                        await Task.Delay(PollDelayMs, ct);
                        continue;
                    }

                    import.ChaptersDone = prog.ChaptersDone;
                    import.ChaptersTotal = prog.ChaptersTotal;
                    import.LinesDone = prog.LinesDone;

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
        catch (ChessableProxyException ex)
        {
            await FailAsync(import, ex.Message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chessable-Import {Id} fehlgeschlagen (Versuch {Attempt})", import.Id, import.Attempts);
            await FailAsync(import, ex.Message, ct);
        }
    }

    private async Task ImportAsRepertoireAsync(ChessableImport import, string pgn, string courseName, CancellationToken ct)
    {
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
                IsPublic = false
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

    private async Task FailAsync(ChessableImport import, string error, CancellationToken ct)
    {
        import.Status = "failed";
        import.Error = Trunc(error, 1000);
        import.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

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
