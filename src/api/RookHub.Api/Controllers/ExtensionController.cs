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

    public ExtensionController(RepertoireService repertoireService, RepertoireAnalyzeService analyzeService,
        TrainingGoalService trainingGoalService, RememberedPositionService rememberedPositionService,
        SavedGameService savedGameService, SharedLineService sharedLineService,
        ChessableProxyService chessableProxy, ChessableImportService chessableImport)
    {
        _repertoireService = repertoireService;
        _analyzeService = analyzeService;
        _trainingGoalService = trainingGoalService;
        _rememberedPositionService = rememberedPositionService;
        _savedGameService = savedGameService;
        _sharedLineService = sharedLineService;
        _chessableProxy = chessableProxy;
        _chessableImport = chessableImport;
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
    public async Task<ActionResult<ChessableIngestResultDto>> ChessableIngest([FromBody] ChessableIngestRequest dto, CancellationToken ct)
    {
        if (ScopeGuard() is { } forbid) return forbid;
        if (dto == null || string.IsNullOrWhiteSpace(dto.Bid))
            return BadRequest(new { message = "bid required." });
        if (dto.Bid.Length > 12 || !dto.Bid.All(char.IsAsciiDigit))
            return BadRequest(new { message = "Invalid bid." });

        var chapters = dto.Chapters ?? new List<ChessableIngestChapter>();
        if (chapters.Count == 0 || chapters.All(c => (c.Lines?.Count ?? 0) == 0))
            return BadRequest(new { message = "No captured lines." });

        var target = dto.Target == "book" ? "book" : "repertoire";
        var mode = target == "book" ? "FirstKeyMove" : "None";

        ChessableCourseDataDto parsed;
        try
        {
            parsed = await _chessableProxy.ParseCourseAsync(dto.Bid, mode, chapters, ct);
        }
        catch (ChessableProxyException ex)
        {
            // 400 = ungültige/kaputte Roh-Daten (Client-Fehler) durchreichen; sonst upstream (502).
            var code = ex.Status == System.Net.HttpStatusCode.BadRequest ? 400 : 502;
            return StatusCode(code, new { message = ex.Message });
        }

        if (string.IsNullOrWhiteSpace(parsed.Pgn))
            return BadRequest(new { message = "Parser produced no importable lines." });

        var courseName = !string.IsNullOrWhiteSpace(dto.CourseName) ? dto.CourseName! : parsed.Name;
        var import = await _chessableImport.ImportPgnDirectAsync(
            GetUserId(), dto.Bid, parsed.Pgn, courseName, target, parsed.LineCount, ct);

        return Ok(new ChessableIngestResultDto(
            import.Id, import.Target, import.ResultId, import.CourseName,
            import.Imported, import.Skipped, import.Invalid, parsed.LineCount));
    }
}
