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

    public ExtensionController(RepertoireService repertoireService, RepertoireAnalyzeService analyzeService)
    {
        _repertoireService = repertoireService;
        _analyzeService = analyzeService;
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
}
