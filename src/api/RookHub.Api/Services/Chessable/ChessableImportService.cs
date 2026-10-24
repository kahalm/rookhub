using RookHub.Api.Data;
using RookHub.Api.DTOs.Chessable;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;

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

    private readonly ChessableProxyService _proxy;

    // Teildienste (Zerlegung Z3): je Fassaden-Instanz gebaut, teilen deren scoped DbContext.
    private readonly ChessableImportOutcome _outcome;
    private readonly ChessableTargetWriter _writer;
    private readonly ChessableLiveAppend _liveAppend;
    private readonly ChessableReimportQueue _reimportQueue;
    private readonly ChessableCourseFetch _courseFetch;
    private readonly ChessableFetchLane _fetchLane;
    private readonly ChessableBrowserImport _browserImport;
    private readonly ChessableBrowserIngest _browserIngest;

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
        _proxy = proxy;
        _outcome = new ChessableImportOutcome(db, breaker, notifications, logger);
        _writer = new ChessableTargetWriter(db, repertoires, pgnImport);
        _liveAppend = new ChessableLiveAppend(db, pgnImport, repertoires, proxy, logger);
        _reimportQueue = new ChessableReimportQueue(db, proxy, encryption, taskQueue);
        _courseFetch = new ChessableCourseFetch(db, encryption, proxy, rateLimiter, _outcome, _reimportQueue, logger);
        _fetchLane = new ChessableFetchLane(db, proxy, _courseFetch, _writer, _outcome, logger);
        _browserImport = new ChessableBrowserImport(db, _writer, _liveAppend, _outcome, logger);
        _browserIngest = new ChessableBrowserIngest(_browserImport, proxy);
    }

    /// <summary>Alle im piratechess-DB-Cache vorliegenden Kurs-Bids (Batch, 1 Aufruf) — für den
    /// Massen-Reprocess, damit nicht je Kurs der teure Einzel-Cache-Check laufen muss.</summary>
    public Task<HashSet<string>> GetCachedBidsAsync(CancellationToken ct = default)
        => _proxy.GetCachedBidsAsync(ct);

    /// <summary>
    /// Steht <paramref name="bid"/> in der von Chessable bestätigten Kursliste des Nutzers? Schranke für alles, was
    /// Inhalte aus dem GETEILTEN Linien-Cache herausgibt — siehe <see cref="ChessableBrowserImport.HasVerifiedCourseAsync"/>.
    /// </summary>
    public Task<bool> HasVerifiedCourseAsync(int userId, string bid, CancellationToken ct = default)
        => _browserImport.HasVerifiedCourseAsync(userId, bid, ct);

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
    /// Importiert ein bereits FERTIGES Kurs-PGN aus dem Browser direkt als Repertoire/Buch — OHNE piratechess-Fetch,
    /// Queue oder VPN. Siehe <see cref="ChessableBrowserImport.ImportPgnDirectAsync"/>.
    /// </summary>
    public Task<ChessableImport> ImportPgnDirectAsync(
        int userId, string bid, string pgn, string courseName, string target, int lineCount, CancellationToken ct = default)
        => _browserImport.ImportPgnDirectAsync(userId, bid, pgn, courseName, target, lineCount, ct);

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

    // ===== Laufender Browser-Import (Kapitel für Kapitel), siehe ChessableBrowserImport =====

    /// <summary>Schritt 1: Import-Datensatz der Sitzung anlegen; der Token hält ihn als „lokal getrieben" markiert.</summary>
    public Task<(ChessableImport Import, IDisposable Inflight)> StartBrowserImportAsync(
        int userId, string bid, string? courseName, string target, CancellationToken ct = default)
        => _browserImport.StartBrowserImportAsync(userId, bid, courseName, target, ct);

    /// <summary>Kapitel-Versatz für den ERSTEN Chunk einer dem Server unbekannten Sitzung (A3-006).</summary>
    public Task<int> ResumeChapterOffsetAsync(int userId, string bid, string chunkPgn, CancellationToken ct = default)
        => _browserImport.ResumeChapterOffsetAsync(userId, bid, chunkPgn, ct);

    /// <summary>Schritt 2: einen Kapitel-Chunk anhängen und die Zähler fortschreiben.</summary>
    public Task<LiveAppendResult> AppendBrowserChunkAsync(
        int importId, string pgn, int linesInChunk, bool partial = false, CancellationToken ct = default)
        => _browserImport.AppendBrowserChunkAsync(importId, pgn, linesInChunk, partial, ct);

    /// <summary>Schritt 3: Sitzung abschließen. <c>null</c>, wenn der Datensatz nicht (mehr) existiert.</summary>
    public Task<ChessableImport?> FinishBrowserImportAsync(int importId, CancellationToken ct = default)
        => _browserImport.FinishBrowserImportAsync(importId, ct);

    /// <summary>Bricht den Import-Datensatz einer Sitzung ab; das bereits Importierte bleibt.</summary>
    public Task FailBrowserImportAsync(int importId, string message, CancellationToken ct = default)
        => _browserImport.FailBrowserImportAsync(importId, message, ct);

    /// <summary>
    /// Ein Chunk des kapitelweisen Browser-Imports (Codereview A3-012: Ablauf aus dem <c>ExtensionController</c>).
    /// <paramref name="sessions"/> (Singleton) und <paramref name="log"/> (Logquelle des Controllers) kommen vom
    /// Aufrufer — so bleiben DI-Registrierung, Konstruktor und Logquelle der Meldungen unverändert.
    /// </summary>
    public Task<ChessableChunkOutcome> HandleIngestChunkAsync(ChessableIngestSessionStore sessions, int userId,
        ChessableIngestChunkRequest dto, ILogger log, CancellationToken ct = default)
        => _browserIngest.HandleChunkAsync(sessions, userId, dto, log, ct);

    /// <summary>True für reine Transport-/Verbindungsfehler (piratechess down/Neustart): SocketException,
    /// HttpRequestException oder typische „connection refused"/„timed out"-Meldungen. NICHT für
    /// <see cref="ChessableProxyException"/> (= piratechess hat geantwortet, aber mit Fehler).</summary>
    internal static bool IsTransientConnectionError(Exception ex) => ChessableCourseFetch.IsTransientConnectionError(ex);

    // Internal statt private: die Zombie-Haertung (Fangnetze unten) war der Ausloeser echter
    // Prod-Haenger und braucht direkte Tests — sie ist ueber die oeffentliche API nicht erreichbar,
    // ohne eine halbe Chessable-Session zu simulieren.
    internal Task FailAsync(ChessableImport import, string error) => _outcome.FailAsync(import, error);

    /// <summary>Siehe <see cref="ChessableImportProgress.FormatDuration"/>.</summary>
    internal static string FormatDuration(TimeSpan? span) => ChessableImportProgress.FormatDuration(span);
}
