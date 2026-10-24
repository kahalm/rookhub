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

    // Poll-Tuning der Abruf-Lane (intern überschreibbar für Tests), siehe ChessableCourseFetch.
    internal int PollDelayMs { get => _courseFetch.PollDelayMs; set => _courseFetch.PollDelayMs = value; }
    internal int FetchStallPolls { get => _courseFetch.FetchStallPolls; set => _courseFetch.FetchStallPolls = value; }
    internal int FetchMaxPolls { get => _courseFetch.FetchMaxPolls; set => _courseFetch.FetchMaxPolls = value; }

    /// <summary>Siehe <see cref="ChessableImportProgress.IsDrivenLocally"/> (prozessweite Treiber-Liste).</summary>
    internal static bool IsDrivenLocally(int importId) => ChessableImportProgress.IsDrivenLocally(importId);

    /// <summary>Siehe <see cref="ChessableImportProgress.TrackInflight"/>.</summary>
    internal static IDisposable TrackInflight(int importId) => ChessableImportProgress.TrackInflight(importId);

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _proxy;
    private readonly ILogger<ChessableImportService> _logger;

    // Teildienste (Zerlegung Z3): je Fassaden-Instanz gebaut, teilen deren scoped DbContext.
    private readonly ChessableImportOutcome _outcome;
    private readonly ChessableTargetWriter _writer;
    private readonly ChessableLiveAppend _liveAppend;
    private readonly ChessableReimportQueue _reimportQueue;
    private readonly ChessableCourseFetch _courseFetch;
    private readonly ChessableFetchLane _fetchLane;

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
        _logger = logger;
        _outcome = new ChessableImportOutcome(db, breaker, notifications, logger);
        _writer = new ChessableTargetWriter(db, repertoires, pgnImport);
        _liveAppend = new ChessableLiveAppend(db, pgnImport, repertoires, proxy, logger);
        _reimportQueue = new ChessableReimportQueue(db, proxy, encryption, taskQueue);
        _courseFetch = new ChessableCourseFetch(db, encryption, proxy, rateLimiter, _outcome, _reimportQueue, logger);
        _fetchLane = new ChessableFetchLane(db, proxy, _courseFetch, _writer, _outcome, logger);
    }

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

    /// <summary>
    /// Reiht einen Re-Import eines bereits importierten Chessable-Kurses ein (für die Neu-Aufbereitung
    /// veralteter Bücher OHNE gespeichertes Roh-PGN). Holt den Kurs erneut (oder aus dem Rohdaten-Cache)
    /// und lässt ihn durch die aktuelle Pipeline laufen — <see cref="PgnImportService.ImportFileAsync"/> aktualisiert
    /// veraltete Bücher dabei in-place. Liefert die Import-Id, oder <c>null</c>, wenn für
    /// <paramref name="ownerUserId"/> kein Chessable-Bearer hinterlegt ist (kein Re-Fetch möglich).
    /// </summary>
    public Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName, int? targetRepertoireId = null, bool? knownCached = null, bool trustOwnership = false, CancellationToken ct = default)
        => _reimportQueue.EnqueueReimportAsync(ownerUserId, bid, target, courseName, targetRepertoireId, knownCached, trustOwnership, ct);

    /// <summary>
    /// Faire Reihenfolge wartender Importe: nach der bei Anlage eingefrorenen
    /// <see cref="ChessableImport.QueueRound"/> (Round-Robin über die User), dann nach Einreih-Zeit.
    /// Folge: der erste Job eines neu hinzukommenden Users (Runde 0) rückt vor die noch wartenden
    /// Folge-Jobs (Runde ≥ 1) des Erst-Users — also auf die 2. Stelle, danach wird abgewechselt.
    /// Da <see cref="ChessableImport.QueueRound"/> eingefroren ist, bleibt die Reihenfolge auch dann
    /// stabil, wenn frühere Jobs fertig werden. Rein/testbar.
    /// </summary>
    public static List<ChessableImport> FairOrder(IEnumerable<ChessableImport> queued) => ChessableFetchLane.FairOrder(queued);

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
    public Task RunNextAsync(CancellationToken ct = default, bool fastLane = false) => _fetchLane.RunNextAsync(ct, fastLane);

    /// <summary>Verarbeitet (oder resumt) den Import-Job <paramref name="importId"/>.</summary>
    public Task RunAsync(int importId, CancellationToken ct = default) => _fetchLane.RunAsync(importId, ct);

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

    /// <summary>True für reine Transport-/Verbindungsfehler (piratechess down/Neustart): SocketException,
    /// HttpRequestException oder typische „connection refused"/„timed out"-Meldungen. NICHT für
    /// <see cref="ChessableProxyException"/> (= piratechess hat geantwortet, aber mit Fehler).</summary>
    internal static bool IsTransientConnectionError(Exception ex) => ChessableCourseFetch.IsTransientConnectionError(ex);

    // Internal statt private: die Zombie-Haertung (Fangnetze unten) war der Ausloeser echter
    // Prod-Haenger und braucht direkte Tests — sie ist ueber die oeffentliche API nicht erreichbar,
    // ohne eine halbe Chessable-Session zu simulieren.
    internal Task FailAsync(ChessableImport import, string error) => _outcome.FailAsync(import, error);

    private static string Trunc(string s, int max) => ChessableImportTargets.Trunc(s, max);

    /// <summary>Siehe <see cref="ChessableImportProgress.FormatDuration"/>.</summary>
    internal static string FormatDuration(TimeSpan? span) => ChessableImportProgress.FormatDuration(span);
}
