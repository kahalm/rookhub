using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
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

[ApiController]
[Route("api/extension")]
[Authorize]
[EnableCors("ExtensionPolicy")]
// Eine Stelle statt 17: prüft je Anfrage, dass ein PAT den Scope „extension" trägt (JWT-Nutzer
// haben keinen scope-Claim und dürfen immer). Vorher stand dieselbe Zeile in JEDER Action.
[RequireTokenScope(ApiTokenService.DefaultScope)]
public partial class ExtensionController : BaseApiController
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
    private readonly ChessableTrainedLineService _trainedLines;
    private readonly ChessableProblemMoveService _problemMoves;
    private readonly ChessableReviewLineService _reviewLines;
    private readonly ChessableSessionMoveService _sessionMoves;
    private readonly ILogger<ExtensionController> _logger;

    public ExtensionController(RepertoireService repertoireService, RepertoireAnalyzeService analyzeService,
        TrainingGoalService trainingGoalService, RememberedPositionService rememberedPositionService,
        SavedGameService savedGameService, SharedLineService sharedLineService,
        ChessableProxyService chessableProxy, ChessableImportService chessableImport,
        ChessableIngestSessionStore ingestSessions, ChessableTrainedLineService trainedLines,
        ChessableProblemMoveService problemMoves, ChessableReviewLineService reviewLines,
        ChessableSessionMoveService sessionMoves,
        ILogger<ExtensionController> logger)
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
        _trainedLines = trainedLines;
        _problemMoves = problemMoves;
        _reviewLines = reviewLines;
        _sessionMoves = sessionMoves;
        _logger = logger;
    }

    /// <summary>
    /// Repertoire-Liste fuer Extension-Clients. <paramref name="kind"/> akzeptiert die
    /// String-Repraesentation von <see cref="RepertoireKind"/> (z. B. <c>opening</c>,
    /// case-insensitive). Ohne Filter werden alle Kinds zurueckgegeben.
    /// </summary>
    [HttpGet("repertoires")]
    public async Task<ActionResult<List<ExtensionRepertoireDto>>> GetRepertoires([FromQuery] string? kind = null)
    {
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
    /// ganze Repertoire-PGN zur Extension wandern muss. Je Konto gedrosselt (ein Neuaufbau des Sets ist teuer); laufen
    /// im Prozess schon zu viele Neuaufbauten, 429 mit <c>reason: busy</c> (siehe <see cref="PositionSetBuildGate"/>).
    /// </summary>
    [HttpPost("analyze-game")]
    [EnableRateLimiting(RateLimitPartitions.ExtensionAnalyzePolicy)]
    public async Task<ActionResult<AnalyzeGameResponseDto>> AnalyzeGame([FromBody] AnalyzeGameRequestDto dto)
    {
        if (dto == null) return BadRequest(new { message = "Body required." });
        if (dto.Moves == null) dto.Moves = new();
        if (dto.Moves.Count > 600)
            return BadRequest(new { message = "Too many moves (max 600 plies)." });
        try
        {
            return Ok(await _analyzeService.AnalyzeAsync(GetUserId(), dto));
        }
        catch (PositionSetBusyException)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { reason = "busy", message = "Too many repertoire checks are being prepared right now. Try again in a few seconds." });
        }
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
        if (dto == null || !RememberedPositionService.LooksLikeFen(dto.Fen))
            return BadRequest(new { message = "Valid fen required." });
        return Ok(await _rememberedPositionService.SaveAsync(GetUserId(), dto));
    }

    /// <summary>Listet die gemerkten Stellungen des Users (neueste zuerst).</summary>
    [HttpGet("remembered-lines")]
    public async Task<ActionResult<List<RememberedPositionDto>>> GetRememberedLines([FromQuery] int take = 200)
    {
        return Ok(await _rememberedPositionService.ListAsync(GetUserId(), take));
    }

    /// <summary>Löscht eine gemerkte Stellung des Users (idempotent).</summary>
    [HttpDelete("remembered-lines/{id:int}")]
    public async Task<IActionResult> DeleteRememberedLine(int id)
    {
        return await _rememberedPositionService.DeleteAsync(GetUserId(), id) ? NoContent() : NotFound();
    }

    /// <summary>
    /// „Trennen" in RepCheck: widerruft GENAU den API-Token, mit dem diese Anfrage kommt — ohne Id, ein
    /// Extension-Token kann nur sich selbst widerrufen (die übrigen verwaltet das Profil, das der Scope-Zaun
    /// für Tokens sperrt). 204 auch, wenn der Token inzwischen schon weg war; 400 ohne API-Token (JWT), da gibt
    /// es nichts „selbst" zu widerrufen. Der Dienst kommt per <c>[FromServices]</c>, damit der Konstruktor nicht wächst.
    /// </summary>
    [HttpDelete("token/self")]
    public async Task<IActionResult> RevokeOwnToken([FromServices] ApiTokenService tokens)
    {
        if (!int.TryParse(User.FindFirst(ApiTokenAuthenticationHandler.TokenIdClaim)?.Value,
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var tokenId))
            return BadRequest(new { message = "Only an API token can revoke itself." });
        try
        {
            await tokens.RevokeAsync(GetUserId(), tokenId);
        }
        catch (KeyNotFoundException)
        {
            // schon widerrufen (paralleles „Trennen", Profil) — das Ziel ist erreicht
        }
        return NoContent();
    }

    /// <summary>
    /// Speichert die aktuell auf chess.com/lichess angeschaute Partie (Button „Partie speichern").
    /// Der Client schickt die SAN-Zugliste + Best-Effort-Metadaten; der Server baut das PGN und
    /// vergibt ein ShareToken. Dedup über (User, Source, ExternalId). Sichtbar im Bereich „Partien".
    /// </summary>
    [HttpPost("games")]
    public async Task<ActionResult<SavedGameDetailDto>> SaveGame([FromBody] SaveGameInputDto dto, CancellationToken ct = default)
    {
        if (dto == null) return BadRequest(new { message = "Body required." });
        try
        {
            var saved = await _savedGameService.SaveAsync(GetUserId(), dto);
            // Jede über die Erweiterung gespeicherte Partie wird gleich analysiert (0.667.0, Wunsch 2026-10-05; vorher nur mit
            // <c>analyze</c> aus der Übersicht, das Flag bleibt der Verträglichkeit halber). Wiederholtes Speichern rechnet nichts
            // doppelt (<see cref="SavedGameService.AnalyzeAsync"/> nutzt eine vorhandene Analyse). Scheitert es (keine Engine,
            // Deckel), bleibt die Partie trotzdem gespeichert — die Seite zeigt dann den Analysieren-Knopf.
            try { await _savedGameService.AnalyzeAsync(GetUserId(), saved.Id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Analyse nach dem Speichern von Partie {Id} nicht gestartet", saved.Id);
            }
            return Ok(saved);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Teilt die aktuell auf chess.com/lichess gespielte Zugfolge als öffentliche Nur-Ansehen-Line
    /// (<c>/l/{token}</c>) — für die „Sharebar" im Extension-Popup. Der Server baut aus der SAN-Liste
    /// ein PGN; dieselbe Zugfolge desselben Users liefert denselben Link (Dedup). 400 bei leerer Liste,
    /// illegalem oder zu langem Zug und zu großem PGN; der Rumpf ist auf 64 KB gedeckelt (600 Züge sind ~6 KB).
    /// </summary>
    [HttpPost("share-line")]
    [RequestSizeLimit(ShareLineRequestLimit)]
    public async Task<ActionResult<ShareLineResultDto>> ShareLine([FromBody] ShareExtensionLineInputDto dto, CancellationToken ct)
    {
        if (dto == null) return BadRequest(new { message = "Body required." });
        try
        {
            var res = await _sharedLineService.CreateStandaloneAsync(GetUserId(), dto.Moves, dto.Title, ct);
            return res == null ? BadRequest(new { message = "No moves." }) : Ok(res);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Rumpf-Deckel von share-line (Codereview N8-003) — vorher galten Kestrels 30 MB bzw. nginx' 15 MB.</summary>
    internal const long ShareLineRequestLimit = 64 * 1024;

    /// <summary>
    /// Welche Partien der Uebersicht (chess.com/lichess) liegen schon bei RookHub? Die Erweiterung zeigt
    /// dort dann ein Haekchen statt des Sende-Knopfs — und, wenn eine Analyse laeuft, deren Fortschritt.
    /// </summary>
    [HttpPost("games/known")]
    public async Task<ActionResult<List<KnownGameDto>>> KnownGames([FromBody] KnownGamesInputDto dto, CancellationToken ct)
    {
        if (dto == null) return BadRequest(new { message = "Body required." });
        if (dto.ExternalIds.Count > SavedGameService.MaxKnownLookup)
            return BadRequest(new { message = $"At most {SavedGameService.MaxKnownLookup} ids per request." });
        return Ok(await _savedGameService.KnownAsync(GetUserId(), dto.Source, dto.ExternalIds, ct));
    }

    /// <summary>
    /// „In RookHub analysieren" aus der Uebersicht auf chess.com/lichess — fuer eine Partie, die schon bei
    /// RookHub liegt, aber nie gerechnet wurde (etwa ueber 💾 gespeichert, bevor es „gleich analysieren"
    /// gab). Derselbe Weg und dieselbe Antwort wie <c>POST /api/games/{id}/analyze</c>; eine eigene Route,
    /// weil das API-Token der Erweiterung nur die Extension-Flaeche erreicht (<c>PatScopeFenceMiddleware</c>).
    /// </summary>
    [HttpPost("games/{id:int}/analyze")]
    public async Task<ActionResult<GameAnalyzeResultDto>> AnalyzeSavedGame(int id, CancellationToken ct)
    {
        var result = await _savedGameService.AnalyzeAsync(GetUserId(), id, ct);
        if (result is null) return NotFound();
        if (result.Reason is not null)
            return BadRequest(new { reason = result.Reason, message = "Game could not be accepted." });
        return Ok(result);
    }
}
