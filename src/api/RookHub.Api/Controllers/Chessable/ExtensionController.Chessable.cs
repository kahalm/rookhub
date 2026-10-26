using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.DTOs;
using RookHub.Api.Filters;
using RookHub.Api.Models;
using RookHub.Api.Authorization;
using RookHub.Api.Services;
using RookHub.Api.Services.Chessable;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Chessable;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.Repertoires;
using RookHub.Api.DTOs.Social;

namespace RookHub.Api.Controllers.Chessable;

/// <summary>
/// Die Chessable-Flaeche der Extension-API (zweite Zerlegungsrunde W7b): Browser-Import (ganz, kapitelweise, live),
/// Trainings-Mitschnitte (Linie trainiert, Problemzuege, Sitzungszuege, Review-Linien), Fortschritt, Linien-Cache und
/// die Fehlermeldungen von RepCheck.
///
/// <para>Teil von <see cref="ExtensionController"/> (Partial): Route-Praefix <c>api/extension</c>, <c>[Authorize]</c>,
/// CORS-Policy und Token-Scope stehen an der Hauptdatei und gelten fuer jeden Endpunkt hier unveraendert; die
/// Ausnahmen je Endpunkt (<c>[AllowAnonymous]</c>, Rate-Limits, Rumpf-Deckel) stehen wie vorher an der Methode.</para>
/// </summary>
public partial class ExtensionController
{
    private static bool IsValidBid(string? bid) => ChessableIds.IsValidBid(bid);

    /// <summary>Obergrenze einer Linien-Cache-Abfrage — höchstens die von piratechess
    /// (<c>BrowserCourseAssembler.MaxOidsPerLookup</c>), sonst beantwortet piratechess eine hier erlaubte Anfrage mit 400
    /// und die Extension holt still alle Linien selbst. Kette mit literalem Spiegeltest
    /// (<c>ChessableCourseJobContractTests</c>): RepCheck <c>SHARED_CACHE_BATCH</c> ≤ dies ≤ piratechess.</summary>
    internal const int MaxCachedLineLookup = 10000;

    private static bool IsValidOid(string? oid) => ChessableIds.TryParseOid(oid, out _);

    // Formregeln der Ingest-Anfragen: ChessableIngestRules (auch vom Chunk-Import im Dienst genutzt, A3-012).
    private static string? ValidateLineOids(IEnumerable<ChessableIngestChapter> chapters)
        => ChessableIngestRules.ValidateLineOids(chapters);

    private static bool WantsCacheFill(IEnumerable<ChessableIngestChapter> chapters)
        => ChessableIngestRules.WantsCacheFill(chapters);

    /// <summary>Meldung, wenn Linien aus dem geteilten Cache für einen Kurs verlangt werden, der nicht in der
    /// bestätigten Kursliste des Nutzers steht (siehe <see cref="ChessableImportService.HasVerifiedCourseAsync"/>).</summary>
    private const string CacheFillNotVerifiedMessage =
        "Lines from the shared cache are only filled for courses in your verified Chessable course list. Send the line content instead.";

    private ObjectResult CacheFillForbidden(int userId, string bid)
    {
        _logger.LogWarning("Browser-Import: Linien aus dem geteilten Cache für einen nicht bestätigten Kurs verlangt — abgelehnt (User {UserId}, bid {Bid})",
            userId, bid);
        return StatusCode(StatusCodes.Status403Forbidden, new { message = CacheFillNotVerifiedMessage });
    }

    private async Task<IActionResult> ParseAndImportAsync(
        int userId, string bid, string target, string? courseName, List<ChessableIngestChapter> chapters, CancellationToken ct,
        string? courseJson = null, bool complete = false)
    {
        var mode = target == "book" ? "FirstKeyMove" : "None";
        ChessableCourseDataDto parsed;
        try
        {
            parsed = await _chessableProxy.ParseCourseAsync(bid, mode, chapters, courseJson, complete, ct);
        }
        catch (ChessableProxyException ex)
        {
            return ChessableProxyExceptionFilter.ResultFor(ex);
        }

        if (string.IsNullOrWhiteSpace(parsed.Pgn))
            return BadRequest(new { message = "Parser produced no importable lines." });

        var name = !string.IsNullOrWhiteSpace(courseName) ? courseName! : parsed.Name;
        var import = await _chessableImport.ImportPgnDirectAsync(userId, bid, parsed.Pgn, name, target, parsed.LineCount, ct);
        return Ok(new ChessableIngestResultDto(
            import.Id, import.Target, import.ResultId, import.CourseName,
            import.Imported, import.Skipped, import.Invalid, parsed.LineCount));
    }

    /// <summary>
    /// Browser-Import „Über meinen Browser holen": die RepCheck-Extension hat die rohen Chessable-
    /// Antworten als eigene eingeloggte Session geholt (V2 aktiv) bzw. beim Training mitgeschnitten (V1
    /// passiv) und schickt sie hier je Kapitel. RookHub lässt sie vom fetch-freien piratechess-Parser in
    /// PGN wandeln und importiert das Ergebnis als Repertoire bzw. Buch/Kurs — ganz ohne serverseitigen
    /// Chessable-Abruf/VPN (der Browser passiert Cloudflare als echte Session). <c>Target</c> "repertoire"
    /// (Default) oder "book". Bei erneutem Senden desselben Kurses idempotent; in ein vorhandenes Repertoire wird
    /// nur angehängt, nie ersetzt (ein Mitschnitt trägt meist nur einzelne Kapitel).
    /// </summary>
    [HttpPost("chessable/ingest")]
    [RequestSizeLimit(64_000_000)]
    [TypeFilter(typeof(ChessableProxyExceptionFilter))]   // piratechess nicht erreichbar → 502 (A3-015)
    public async Task<IActionResult> ChessableIngest([FromBody] ChessableIngestRequest dto, CancellationToken ct)
    {
        if (dto == null || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "Invalid or missing bid." });

        var chapters = dto.Chapters ?? new List<ChessableIngestChapter>();
        if (chapters.Count == 0 || chapters.All(c => (c.Lines?.Count ?? 0) == 0))
            return BadRequest(new { message = "No captured lines." });
        if (ValidateLineOids(chapters) is { } shapeError)
            return BadRequest(new { message = shapeError });
        var userId = GetUserId();
        if (WantsCacheFill(chapters) && !await _chessableImport.HasVerifiedCourseAsync(userId, dto.Bid, ct))
            return CacheFillForbidden(userId, dto.Bid);

        var target = dto.Target == "book" ? "book" : "repertoire";
        // Kursname aus den Linien selbst (game.name) vor dem der Extension — siehe ChessableLineJson.
        var courseName = ChessableLineJson.ResolveCourseName(dto.CourseName, chapters.SelectMany(c => c.Lines ?? new List<string>()));
        return await ParseAndImportAsync(userId, dto.Bid, target, courseName, chapters, ct);
    }

    /// <summary>
    /// Import-Fortschritt eines Chessable-Kurses (für die Overlays auf chessable.com): liefert die
    /// Chessable-oids, die der User bereits importiert hat (Buch und/oder Repertoire). Die Extension
    /// matcht sie gegen die Linien-oids der Kursstruktur und zeigt Kurs-/Kapitel-/Linien-Fortschritt.
    /// </summary>
    /// <summary>
    /// „Linie auf Chessable trainiert": markiert die Linie (Kurs-bid + Varianten-oid) in den
    /// RookHub-Gegenstücken — Kurs-Linie gilt als gelöst, im Repertoire-Trainer wird eine neue
    /// Linie „gelernt" bzw. eine fällige eine SR-Stufe vorgerückt (Chessable ersetzt das Review).
    /// Idempotent; unbekannte bid/oid sind kein Fehler (leeres Ergebnis). Eine oid ausserhalb der piratechess-Regel
    /// (<see cref="ChessableIds.TryParseOid"/>, z. B. „0" oder mehr als <c>int.MaxValue</c>) ist dagegen 400.
    /// </summary>
    [HttpPost("chessable/line-trained")]
    public async Task<ActionResult<ChessableLineTrainedResultDto>> ChessableLineTrained(
        [FromBody] ChessableLineTrainedInputDto dto, CancellationToken ct)
    {
        // oid in kanonischer Form (wie im PGN-Header [ChessableOid]); vorher reichten bis zu 32 Ziffern (A3-013).
        var oid = dto == null ? null : ChessableIds.CanonicalOid(dto.Oid);
        if (dto == null || !IsValidBid(dto.Bid) || oid is null)
            return BadRequest(new { message = "Valid bid and oid required." });
        return Ok(await _trainedLines.MarkTrainedAsync(GetUserId(), dto.Bid, oid, ct));
    }

    /// <summary>Rumpf-Deckel der drei Chessable-Roh-Senken (problem-moves, session-moves, review-lines), wie beim
    /// anonymen Pfad. Vorher galt Kestrels Vorgabe (30 MB) bzw. 64 MB bei review-lines. RepCheck schickt je Senke und
    /// Kurs höchstens 400 schwierige Züge, 200 Sitzungszüge bzw. 50 getReview-Linien; die größte je gemessene
    /// getReview-Linie hat 77 KB (Prod, 29.09.2026), 50 davon sind rund 4 MB.</summary>
    internal const long ChessableSinkRequestLimit = 16_000_000;

    /// <summary>
    /// „Schwierige Züge" ablegen: Batch-Upsert je (User, Kurs-bid, Linien-oid) — nHard aus den
    /// Kapitel-Listen, Zug-Details (problemMoves.thisUser) + lastReviewed aus den Linien-Antworten,
    /// die die Extension beim Training/Kurs-Holen ohnehin mitschneidet. Idempotent.
    /// </summary>
    [HttpPost("chessable/problem-moves")]
    [EnableRateLimiting("extension-sink")]
    [RequestSizeLimit(ChessableSinkRequestLimit)]
    public async Task<IActionResult> ChessableProblemMoves([FromBody] ChessableProblemMovesInputDto dto,
        CancellationToken ct)
    {
        if (dto == null || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "Valid bid required." });
        var written = await _problemMoves.UpsertBatchAsync(GetUserId(), dto.Bid, dto.Entries, ct);
        return Ok(new { written });
    }

    /// <summary>
    /// „Sitzungszüge" ablegen (append-only, KEIN Upsert): je trainierter Linie der rohe moves-Block aus
    /// Chessables eigenem Session-Report (saveProgressAndReturnNewProgressInfo) — enthält je Halbzug
    /// u. a. die falsch gespielten Züge (wrong[]), Overstudy-/Alternative-Flags, Level und Punkte.
    /// Quelle: RepCheck-Capture beim Training. Als Roh-Log gesammelt, Auswertung offen.
    /// </summary>
    [HttpPost("chessable/session-moves")]
    [EnableRateLimiting("extension-sink")]
    [RequestSizeLimit(ChessableSinkRequestLimit)]
    public async Task<IActionResult> ChessableSessionMoves([FromBody] ChessableSessionMovesInputDto dto,
        CancellationToken ct)
    {
        if (dto == null || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "Valid bid required." });
        var stored = await _sessionMoves.AppendBatchAsync(GetUserId(), dto.Bid, dto.Entries, ct);
        return Ok(new { stored });
    }

    /// <summary>
    /// „getReview-Linien" ablegen (zweite Linien-Quelle neben getGame): Batch-Upsert je (User, Kurs-bid,
    /// Linien-oid) der ROHEN getReview-Antwort. Wird beim Empfang NICHT geparst — nur roh gespeichert; der
    /// Aufbau zum Kurs (Fallback, wenn kein getGame vorliegt) passiert erst beim Kurs-Aufbau. Idempotent.
    /// </summary>
    [HttpPost("chessable/review-lines")]
    [EnableRateLimiting("extension-sink")]
    [RequestSizeLimit(ChessableSinkRequestLimit)]
    public async Task<IActionResult> ChessableReviewLines([FromBody] ChessableReviewLinesInputDto dto,
        CancellationToken ct)
    {
        if (dto == null || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "Valid bid required." });
        var userId = GetUserId();
        var stored = await _reviewLines.UpsertBatchAsync(userId, dto.Bid, dto.Entries, ct);

        // Kurs beim Training fortlaufend mit den Review-Linien ergänzen (getGame gewinnt, Review füllt
        // nur Lücken). Best-effort: ein Merge-Fehler darf den erfolgreichen Speicher-200 nicht kippen.
        var merged = 0;
        try { merged = await _reviewLines.MergeIntoCourseAsync(userId, dto.Bid, ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "getReview-Merge in den Kurs (User {UserId}, bid {Bid}) fehlgeschlagen — Speichern bleibt erfolgreich",
                userId, dto.Bid);
        }
        return Ok(new { stored, merged });
    }

    /// <summary>
    /// Token-lose Ablage von getReview-Linien — für Nutzer OHNE hinterlegten RookHub-Token. Statt eines
    /// RookHub-Accounts identifiziert die Chessable-<c>uid</c> (client-seitig aus dem Chessable-JWT
    /// decodiert) die Linien; sie landen in der Anon-Senke und werden geclaimt, sobald diese uid einem
    /// RookHub-Account zugeordnet wird (der User verknüpft seinen Chessable-Bearer). <c>AllowAnonymous</c>
    /// + per-IP-Ratelimit; KEIN Kurs-Merge hier (es gibt kein Zielkonto). Der Client sendet erst nach
    /// expliziter Zustimmung (einmaliger Hinweis in der Extension).
    /// </summary>
    [HttpPost("chessable/review-lines/anon")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    [RequestSizeLimit(16_000_000)]   // der Client batcht ≤50 Linien (offener Endpoint → DoS-Schranke)
    public async Task<IActionResult> ChessableReviewLinesAnon([FromBody] AnonymousChessableReviewLinesInputDto dto,
        CancellationToken ct)
    {
        if (dto == null || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "Valid bid required." });
        if (string.IsNullOrWhiteSpace(dto.Uid) || dto.Uid.Length > 32 || !dto.Uid.All(char.IsAsciiDigit))
            return BadRequest(new { message = "Valid uid required." });
        var stored = await _reviewLines.UpsertAnonBatchAsync(dto.Uid, dto.Bid, dto.Entries, ct);
        return Ok(new { stored });
    }

    [HttpGet("chessable/progress")]
    public async Task<IActionResult> ChessableProgress([FromQuery] string bid, CancellationToken ct)
    {
        if (!IsValidBid(bid)) return BadRequest(new { message = "Invalid bid." });
        var (oids, book, repertoire) = await _chessableImport.GetImportedOidsAsync(GetUserId(), bid, ct);
        return Ok(new ChessableProgressDto(book, repertoire, oids.ToList()));
    }

    /// <summary>
    /// Welche Linien (Chessable-oids) liegen schon im geteilten piratechess-Rohdaten-Cache? Die Extension
    /// überspringt beim „Kurs holen" für diese Linien den Chessable-Abruf und schickt beim Import nur die oid —
    /// den Inhalt setzt piratechess ein. Nur die Existenz, nie der Inhalt. piratechess nicht erreichbar →
    /// leere Liste (die Extension holt dann eben alle Linien selbst).
    /// <para>Treffer gibt es nur für einen Kurs (<c>Bid</c>) aus der bestätigten Kursliste des Nutzers, und nur
    /// Linien, mit denen piratechess diesen Kurs auch füllt — dieselbe Schranke wie beim Ingest. Ohne <c>Bid</c> oder
    /// für einen anderen Kurs: leere Liste, die Extension schickt dann alle Linien selbst (statt Linien ohne Inhalt,
    /// die der Ingest ablehnen würde).</para>
    /// </summary>
    [HttpPost("chessable/cached-lines")]
    public async Task<IActionResult> ChessableCachedLines([FromBody] ChessableCachedLinesRequest dto, CancellationToken ct)
    {
        var oids = dto?.Oids ?? new List<string>();
        if (oids.Count > MaxCachedLineLookup)
            return BadRequest(new { message = $"At most {MaxCachedLineLookup} oids per request." });
        if (!oids.All(IsValidOid))
            return BadRequest(new { message = "Invalid oid." });
        var bid = dto?.Bid;
        if (bid is not null && !IsValidBid(bid))
            return BadRequest(new { message = "Invalid bid." });
        var distinct = oids.Distinct().ToList();
        if (bid is null || distinct.Count == 0 || !await _chessableImport.HasVerifiedCourseAsync(GetUserId(), bid, ct))
            return Ok(new ChessableCachedLinesDto(new List<string>()));
        var cached = await _chessableProxy.GetCachedLineOidsAsync(bid, distinct, ct);
        return Ok(new ChessableCachedLinesDto(distinct.Where(cached.Contains).ToList()));
    }

    private static readonly HashSet<string> UnexpectedResponseEndpoints =
        new(StringComparer.Ordinal) { "getCourse", "getList", "getGame" };

    /// <summary>
    /// RepCheck: „Kurs holen“ ist mit einem Fehler abgebrochen, der KEINE unerwartete Chessable-Antwort war (Fehler in der
    /// Erweiterung, beim Senden an RookHub …). Log + Admin-Nachricht (<see cref="ChessableResponseAlertService.ReportCrawlErrorAsync"/>).
    /// </summary>
    [HttpPost("chessable/crawl-error")]
    public async Task<IActionResult> ChessableCrawlError([FromBody] ChessableCrawlErrorInputDto dto,
        [FromServices] ChessableResponseAlertService alerts, CancellationToken ct)
    {
        if (dto is null || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "Invalid bid." });
        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(new { message = "Message required." });
        return Ok(await alerts.ReportCrawlErrorAsync(GetUserId(), dto, ct));
    }

    /// <summary>
    /// RepCheck hat beim „Kurs holen“ eine unerwartete Chessable-Antwort bekommen und den Abruf gestoppt. Die Antwort
    /// wird zum Durchsehen protokolliert; sieht sie nach einer Sperre aus, geht eine Admin-Nachricht im Namen des
    /// Nutzers raus (<see cref="ChessableResponseAlertService"/>). Der Dienst kommt per <c>[FromServices]</c>, damit
    /// der ohnehin lange Konstruktor nicht wächst.
    /// </summary>
    [HttpPost("chessable/unexpected-response")]
    public async Task<IActionResult> ChessableUnexpectedResponse([FromBody] ChessableUnexpectedResponseInputDto dto,
        [FromServices] ChessableResponseAlertService alerts, CancellationToken ct)
    {
        if (dto is null || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "Invalid bid." });
        if (!UnexpectedResponseEndpoints.Contains(dto.Endpoint ?? ""))
            return BadRequest(new { message = "Unknown endpoint." });
        if ((dto.Lid != null && !IsValidOid(dto.Lid)) || (dto.Oid != null && !IsValidOid(dto.Oid)))
            return BadRequest(new { message = "Invalid lid/oid." });
        return Ok(await alerts.ReportAsync(GetUserId(), dto, ct));
    }

    /// <summary>
    /// Kapitelweiser Browser-Import: die Extension streamt einen großen Kurs Kapitel für Kapitel (bounded
    /// pro Request) statt in einem einzigen (potenziell riesigen) Ingest-Body. Der Server sammelt die rohen
    /// Kapitel je <c>SessionId</c> (<see cref="ChessableIngestSessionStore"/>) und parst/importiert erst
    /// beim Chunk mit <c>Final=true</c> den GANZEN Kurs — so bleibt die Kapitel-/Round-Reihenfolge über
    /// Kapitel hinweg korrekt (ein einzelnes Kapitel parsen würde die Round-Nummerierung je Chunk auf 1
    /// zurücksetzen). <c>Bid</c>/<c>Target</c>/<c>CourseName</c> stammen vom ersten Chunk.
    /// </summary>
    [HttpPost("chessable/ingest/chunk")]
    [RequestSizeLimit(48_000_000)]
    [TypeFilter(typeof(ChessableProxyExceptionFilter))]   // piratechess nicht erreichbar → 502 (A3-015)
    public async Task<IActionResult> ChessableIngestChunk([FromBody] ChessableIngestChunkRequest dto, CancellationToken ct)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.SessionId) || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "sessionId and valid bid required." });

        var userId = GetUserId();
        var outcome = await _chessableImport.HandleIngestChunkAsync(_ingestSessions, userId, dto, _logger, ct);
        return outcome.Kind switch
        {
            ChessableChunkOutcomeKind.Ack => Ok(outcome.Ack),
            ChessableChunkOutcomeKind.Done => Ok(outcome.Result),
            ChessableChunkOutcomeKind.CacheFillForbidden => CacheFillForbidden(userId, dto.Bid),
            ChessableChunkOutcomeKind.ProxyError => ChessableProxyExceptionFilter.ResultFor(outcome.ProxyError!),
            _ => BadRequest(new { message = outcome.Message }),
        };
    }

    /// <summary>
    /// Live-Append „beim Durchklicken" (V1): die Extension schickt die soeben passiv erfassten Linien SOFORT
    /// hierher (nicht erst am Ende). Sie werden über den fetch-freien Parser zu PGN gewandelt und DIREKT ans
    /// bestehende Repertoire angehängt (bzw. legen es bei der ersten Linie an) → das Repertoire wächst live
    /// mit. Anders als /ingest wird pro Aufruf KEIN Import-Datensatz und KEINE Benachrichtigung erzeugt; der
    /// Server dedupliziert Linien per Zugtext. <c>Chapters</c> trägt nur die NEUEN Linien.
    /// </summary>
    [HttpPost("chessable/ingest/live")]
    [RequestSizeLimit(16_000_000)]
    [TypeFilter(typeof(ChessableProxyExceptionFilter))]   // piratechess nicht erreichbar → 502 (A3-015)
    public async Task<IActionResult> ChessableIngestLive([FromBody] ChessableLiveIngestRequest dto, CancellationToken ct)
    {
        if (dto == null || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "Invalid or missing bid." });

        var chapters = dto.Chapters ?? new List<ChessableIngestChapter>();
        if (chapters.Count == 0 || chapters.All(c => (c.Lines?.Count ?? 0) == 0))
            return BadRequest(new { message = "No lines." });
        if (ValidateLineOids(chapters) is { } shapeError)
            return BadRequest(new { message = shapeError });
        var userId = GetUserId();
        if (WantsCacheFill(chapters) && !await _chessableImport.HasVerifiedCourseAsync(userId, dto.Bid, ct))
            return CacheFillForbidden(userId, dto.Bid);

        var target = dto.Target == "book" ? "book" : "repertoire";
        var mode = target == "book" ? "FirstKeyMove" : "None";

        ChessableCourseDataDto parsed;
        try
        {
            parsed = await _chessableProxy.ParseCourseAsync(dto.Bid, mode, chapters, ct: ct);
        }
        catch (ChessableProxyException ex)
        {
            return ChessableProxyExceptionFilter.ResultFor(ex);
        }

        // Reine Info-/Erklärlinien liefern kein PGN → nichts anzuhängen, aber kein Fehler (Client macht weiter).
        if (string.IsNullOrWhiteSpace(parsed.Pgn))
            return Ok(new ChessableLiveIngestResultDto(0, null, target, 0));

        // Kursname aus den Linien selbst (game.name) vor dem der Extension — siehe ChessableLineJson.
        var name = ChessableLineJson.ResolveCourseName(dto.CourseName, chapters.SelectMany(c => c.Lines ?? new List<string>()), parsed.Name) ?? parsed.Name;
        var live = await _chessableImport.AppendLiveAsync(userId, dto.Bid, parsed.Pgn, name, target, ct);
        return Ok(new ChessableLiveIngestResultDto(live.Imported, live.ResultId, live.Target, parsed.LineCount, live.Linked));
    }
}
