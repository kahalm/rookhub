using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/extension")]
[Authorize]
[EnableCors("ExtensionPolicy")]
public class ExtensionController : BaseApiController
{
    private readonly RepertoireService _repertoireService;
    private readonly RepertoireAnalyzeService _analyzeService;
    private readonly TrainingGoalService _trainingGoalService;
    private readonly RememberedPositionService _rememberedPositionService;
    private readonly SavedGameService _savedGameService;
    private readonly SharedLineService _sharedLineService;
    private readonly ChessableProxyService _chessableProxy;
    private readonly ChessableImportService _chessableImport;
    private readonly ChessableIngestSessionStore _ingestSessions;

    public ExtensionController(RepertoireService repertoireService, RepertoireAnalyzeService analyzeService,
        TrainingGoalService trainingGoalService, RememberedPositionService rememberedPositionService,
        SavedGameService savedGameService, SharedLineService sharedLineService,
        ChessableProxyService chessableProxy, ChessableImportService chessableImport,
        ChessableIngestSessionStore ingestSessions)
    {
        _repertoireService = repertoireService;
        _analyzeService = analyzeService;
        _trainingGoalService = trainingGoalService;
        _rememberedPositionService = rememberedPositionService;
        _savedGameService = savedGameService;
        _sharedLineService = sharedLineService;
        _chessableProxy = chessableProxy;
        _chessableImport = chessableImport;
        _ingestSessions = ingestSessions;
    }

    private static bool IsValidBid(string? bid) => !string.IsNullOrEmpty(bid) && bid.Length <= 12 && bid.All(char.IsAsciiDigit);

    private async Task<IActionResult> ParseAndImportAsync(
        int userId, string bid, string target, string? courseName, List<ChessableIngestChapter> chapters, CancellationToken ct)
    {
        var mode = target == "book" ? "FirstKeyMove" : "None";
        ChessableCourseDataDto parsed;
        try
        {
            parsed = await _chessableProxy.ParseCourseAsync(bid, mode, chapters, ct);
        }
        catch (ChessableProxyException ex)
        {
            var code = ex.Status == System.Net.HttpStatusCode.BadRequest ? 400 : 502;
            return StatusCode(code, new { message = ex.Message });
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
    /// Wenn ein API-Token genutzt wird (User-Identity hat scope-Claim), muss dieser
    /// <c>extension</c> sein. JWT-User (kein scope-Claim) duerfen immer. Schuetzt davor,
    /// dass spaeter dazukommende Token-Scopes versehentlich Extension-Daten lesen.
    /// </summary>
    private ActionResult? ScopeGuard()
    {
        var scope = User.FindFirst("scope")?.Value;
        if (scope == null) return null; // JWT
        if (scope == "extension") return null;
        return Forbid();
    }

    /// <summary>
    /// Repertoire-Liste fuer Extension-Clients. <paramref name="kind"/> akzeptiert die
    /// String-Repraesentation von <see cref="RepertoireKind"/> (z. B. <c>opening</c>,
    /// case-insensitive). Ohne Filter werden alle Kinds zurueckgegeben.
    /// </summary>
    [HttpGet("repertoires")]
    public async Task<ActionResult<List<ExtensionRepertoireDto>>> GetRepertoires([FromQuery] string? kind = null)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        RepertoireKind? kindFilter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<RepertoireKind>(kind, ignoreCase: true, out var parsed))
                return BadRequest(new { message = "Invalid kind. Allowed: None, Opening, Middlegame, Endgame." });
            kindFilter = parsed;
        }
        return Ok(await _repertoireService.GetExtensionListAsync(GetUserId(), kindFilter));
    }

    [HttpGet("repertoires/{id}/pgn")]
    public async Task<IActionResult> GetPgn(int id)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        try
        {
            var pgn = await _repertoireService.GetCombinedPgnAsync(id, GetUserId());
            return Content(pgn, "text/plain");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Server-seitige Partie-Analyse: Client schickt die SAN-Zugliste der aktuellen Partie,
    /// Server vergleicht ply-weise gegen das (gecachte) Positions-Set des Users und liefert
    /// Abweichungs-Index, Zugumstellungen und FEN-vor-Abweichung zurueck. Vermeidet, dass das
    /// ganze Repertoire-PGN zur Extension wandern muss.
    /// </summary>
    [HttpPost("analyze-game")]
    public async Task<ActionResult<AnalyzeGameResponseDto>> AnalyzeGame([FromBody] AnalyzeGameRequestDto dto)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        if (dto == null) return BadRequest(new { message = "Body required." });
        if (dto.Moves == null) dto.Moves = new();
        if (dto.Moves.Count > 600)
            return BadRequest(new { message = "Too many moves (max 600 plies)." });
        return Ok(await _analyzeService.AnalyzeAsync(GetUserId(), dto));
    }

    /// <summary>
    /// Meldet ein Häppchen AKTIVER Chessable-Trainingszeit (von der RepCheck-Extension/dem Userscript auf
    /// chessable.com gemessen). Append-only; fließt in die eigene Kategorie „Chessable" des Trainingsziele-
    /// Trackers. Zeitstempel wird serverseitig gesetzt; <see cref="ChessableActivityInputDto.SecondsActive"/>
    /// ist auf 1–3600 s je Aufruf begrenzt (die Extension flusht in kleinen Intervallen).
    /// </summary>
    [HttpPost("training-activity")]
    public async Task<IActionResult> RecordTrainingActivity([FromBody] ChessableActivityInputDto dto)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        if (dto == null || dto.SecondsActive <= 0)
            return BadRequest(new { message = "secondsActive required and > 0." });
        await _trainingGoalService.RecordChessableActivityAsync(GetUserId(), dto);
        return Ok(new { recorded = true });
    }

    /// <summary>
    /// Merkt eine auf chessable.com angezeigte Stellung (Button „Remember line"): FEN + Kontext
    /// (Kurs-ID, Seiten-URL) werden append-only gespeichert. Verwendungszweck noch offen.
    /// </summary>
    [HttpPost("remember-line")]
    public async Task<ActionResult<RememberedPositionDto>> RememberLine([FromBody] RememberLineInputDto dto)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        if (dto == null || !RememberedPositionService.LooksLikeFen(dto.Fen))
            return BadRequest(new { message = "Valid fen required." });
        return Ok(await _rememberedPositionService.SaveAsync(GetUserId(), dto));
    }

    /// <summary>Listet die gemerkten Stellungen des Users (neueste zuerst).</summary>
    [HttpGet("remembered-lines")]
    public async Task<ActionResult<List<RememberedPositionDto>>> GetRememberedLines([FromQuery] int take = 200)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        return Ok(await _rememberedPositionService.ListAsync(GetUserId(), take));
    }

    /// <summary>Löscht eine gemerkte Stellung des Users (idempotent).</summary>
    [HttpDelete("remembered-lines/{id:int}")]
    public async Task<IActionResult> DeleteRememberedLine(int id)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        return await _rememberedPositionService.DeleteAsync(GetUserId(), id) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Speichert die aktuell auf chess.com/lichess angeschaute Partie (Button „Partie speichern").
    /// Der Client schickt die SAN-Zugliste + Best-Effort-Metadaten; der Server baut das PGN und
    /// vergibt ein ShareToken. Dedup über (User, Source, ExternalId). Sichtbar im Bereich „Partien".
    /// </summary>
    [HttpPost("games")]
    public async Task<ActionResult<SavedGameDetailDto>> SaveGame([FromBody] SaveGameInputDto dto)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        if (dto == null) return BadRequest(new { message = "Body required." });
        try
        {
            return Ok(await _savedGameService.SaveAsync(GetUserId(), dto));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Teilt die aktuell auf chess.com/lichess gespielte Zugfolge als öffentliche Nur-Ansehen-Line
    /// (<c>/l/{token}</c>) — für die „Sharebar" im Extension-Popup. Der Server baut aus der SAN-Liste
    /// ein PGN; dieselbe Zugfolge desselben Users liefert denselben Link (Dedup). 400 bei leerer Liste.
    /// </summary>
    [HttpPost("share-line")]
    public async Task<ActionResult<ShareLineResultDto>> ShareLine([FromBody] ShareExtensionLineInputDto dto, CancellationToken ct)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        if (dto == null) return BadRequest(new { message = "Body required." });
        var res = await _sharedLineService.CreateStandaloneAsync(GetUserId(), dto.Moves, dto.Title, ct);
        return res == null ? BadRequest(new { message = "No moves." }) : Ok(res);
    }

    /// <summary>
    /// Browser-Import „Über meinen Browser holen": die RepCheck-Extension hat die rohen Chessable-
    /// Antworten als eigene eingeloggte Session geholt (V2 aktiv) bzw. beim Training mitgeschnitten (V1
    /// passiv) und schickt sie hier je Kapitel. RookHub lässt sie vom fetch-freien piratechess-Parser in
    /// PGN wandeln und importiert das Ergebnis als Repertoire bzw. Buch/Kurs — ganz ohne serverseitigen
    /// Chessable-Abruf/VPN (der Browser passiert Cloudflare als echte Session). <c>Target</c> "repertoire"
    /// (Default) oder "book". Bei erneutem Senden desselben Kurses idempotent.
    /// </summary>
    [HttpPost("chessable/ingest")]
    [RequestSizeLimit(64_000_000)]
    public async Task<IActionResult> ChessableIngest([FromBody] ChessableIngestRequest dto, CancellationToken ct)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        if (dto == null || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "Invalid or missing bid." });

        var chapters = dto.Chapters ?? new List<ChessableIngestChapter>();
        if (chapters.Count == 0 || chapters.All(c => (c.Lines?.Count ?? 0) == 0))
            return BadRequest(new { message = "No captured lines." });

        var target = dto.Target == "book" ? "book" : "repertoire";
        return await ParseAndImportAsync(GetUserId(), dto.Bid, target, dto.CourseName, chapters, ct);
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
    public async Task<IActionResult> ChessableIngestChunk([FromBody] ChessableIngestChunkRequest dto, CancellationToken ct)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        if (dto == null || string.IsNullOrWhiteSpace(dto.SessionId) || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "sessionId and valid bid required." });

        var userId = GetUserId();
        var target = dto.Target == "book" ? "book" : "repertoire";

        // Kapitel anhängen (ein finaler Chunk darf leer sein, wenn das letzte Kapitel schon zuvor kam).
        ChessableIngestSessionStore.Session? session = null;
        if (dto.Chapter is { } chapter && (chapter.Lines?.Count ?? 0) > 0)
        {
            var (s, error) = _ingestSessions.AddChapter(userId, dto.SessionId, dto.Bid, target, dto.CourseName, chapter);
            if (error != null)
            {
                _ingestSessions.Discard(userId, dto.SessionId);
                return BadRequest(new { message = error });
            }
            session = s;
        }

        if (!dto.Final)
        {
            var lines = session?.Chapters.Sum(c => c.Lines?.Count ?? 0) ?? 0;
            return Ok(new ChessableIngestChunkAck(false, session?.Chapters.Count ?? 0, lines));
        }

        // Final: gesamte Session entnehmen und als ganzen Kurs parsen + importieren.
        var taken = _ingestSessions.Take(userId, dto.SessionId);
        if (taken == null || taken.Chapters.Count == 0)
            return BadRequest(new { message = "No captured lines in session." });

        return await ParseAndImportAsync(userId, taken.Bid, taken.Target, taken.CourseName, taken.Chapters, ct);
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
    public async Task<IActionResult> ChessableIngestLive([FromBody] ChessableLiveIngestRequest dto, CancellationToken ct)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        if (dto == null || !IsValidBid(dto.Bid))
            return BadRequest(new { message = "Invalid or missing bid." });

        var chapters = dto.Chapters ?? new List<ChessableIngestChapter>();
        if (chapters.Count == 0 || chapters.All(c => (c.Lines?.Count ?? 0) == 0))
            return BadRequest(new { message = "No lines." });

        var target = dto.Target == "book" ? "book" : "repertoire";
        var mode = target == "book" ? "FirstKeyMove" : "None";

        ChessableCourseDataDto parsed;
        try
        {
            parsed = await _chessableProxy.ParseCourseAsync(dto.Bid, mode, chapters, ct);
        }
        catch (ChessableProxyException ex)
        {
            var code = ex.Status == System.Net.HttpStatusCode.BadRequest ? 400 : 502;
            return StatusCode(code, new { message = ex.Message });
        }

        // Reine Info-/Erklärlinien liefern kein PGN → nichts anzuhängen, aber kein Fehler (Client macht weiter).
        if (string.IsNullOrWhiteSpace(parsed.Pgn))
            return Ok(new ChessableLiveIngestResultDto(0, null, target, 0));

        var name = !string.IsNullOrWhiteSpace(dto.CourseName) ? dto.CourseName! : parsed.Name;
        var (imported, resultId, t) = await _chessableImport.AppendLiveAsync(GetUserId(), dto.Bid, parsed.Pgn, name, target, ct);
        return Ok(new ChessableLiveIngestResultDto(imported, resultId, t, parsed.LineCount));
    }
}
