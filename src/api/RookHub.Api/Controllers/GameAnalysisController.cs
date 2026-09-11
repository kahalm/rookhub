using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Partie-Analysen: ein PGN einwerfen und jede Stellung von der Hintergrund-Engine durchrechnen
/// lassen. Vorstufe der Punktepartie und für sich nützlich — bisher ging nur Stellung für Stellung
/// (<see cref="AnalysisJobController"/>).
/// </summary>
[ApiController]
[Route("api/game-analyses")]
[Authorize]
public class GameAnalysisController : BaseApiController
{
    private readonly GameAnalysisService _service;

    public GameAnalysisController(GameAnalysisService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<GameAnalysisDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(GetUserId(), ct));

    /// <summary>
    /// Der kuratierte Bestand: Partien, die JEDER als Punktepartie spielen darf — auch ohne
    /// Anmeldung. Bewusst OHNE die Zugliste (die steckt nur im Detail-Abruf, und der bleibt auf
    /// eigene Analysen beschraenkt): hier gibt es Kopfdaten und Fortschritt, nicht die Partie.
    /// Literal-Route VOR <c>{id:int}</c>.
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<List<GameAnalysisDto>>> ListPublic(CancellationToken ct)
        => Ok(await _service.ListPublicAsync(ct));

    /// <summary>Partie in den kuratierten Bestand aufnehmen bzw. herausnehmen — Besitzer der
    /// Analyse oder Admin. Aendert NICHTS an der Analyse selbst, nur daran, wer sie spielen
    /// darf.</summary>
    [HttpPut("{id:int}/public")]
    public async Task<ActionResult<object>> SetPublic(int id, [FromBody] SetGameAnalysisPublicRequest req,
        CancellationToken ct)
    {
        var result = await _service.SetPublicAsync(GetUserId(), IsAdmin, id, req.IsPublic, ct);
        return result is null
            ? NotFound(new { message = "Analysis not found." })
            : Ok(new { isPublic = result.Value });
    }

    /// <summary>Tempo und Restdauer der eigenen Analysen, aus den Zeitstempeln der gerechneten
    /// Stellungen. Literal-Route VOR <c>{id:int}</c>.</summary>
    [HttpGet("throughput")]
    public async Task<ActionResult<AnalysisThroughputDto>> Throughput(CancellationToken ct)
        => Ok(await _service.ThroughputAsync(GetUserId(), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GameAnalysisDto>> Get(int id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(GetUserId(), id, ct);
        return dto is null ? NotFound(new { message = "Analysis not found." }) : Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<GameAnalysisDto>> Create([FromBody] CreateGameAnalysisRequest req, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.CreateAsync(GetUserId(), req, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // z. B. keine Hintergrund-Engine hinterlegt — der Nutzer soll das lesen, nicht raten.
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Eine Partie auf der PUNKTEPARTIE-Seite einwerfen. Derselbe Weg wie <see cref="Create"/>, nur
    /// ohne Regler: Tiefe, Linienzahl und Engine bestimmt der Server. Literal-Route, also VOR
    /// <c>{id:int}</c> — bei POST gibt es dort zwar nichts zu verwechseln, aber die Reihenfolge
    /// bleibt so, wie die Datei sie sonst haelt.
    ///
    /// <para>Antwortet bei einer Absage 400 mit einem <c>reason</c> aus
    /// <see cref="GuessUploadReason"/>; die Seite formuliert daraus den Satz in der Sprache des
    /// Nutzers.</para>
    /// </summary>
    [HttpPost("guess")]
    public async Task<ActionResult<GameAnalysisDto>> CreateForGuess([FromBody] CreateGuessGameRequest req,
        CancellationToken ct)
    {
        var result = await _service.CreateForGuessAsync(GetUserId(), req, ct);
        return result.Analysis is null
            ? BadRequest(new { reason = result.Reason, message = "Game could not be accepted." })
            : Ok(result.Analysis);
    }

    /// <summary>Ob der Nutzer einwerfen darf und wie viele Partien noch frei sind — die Seite fragt
    /// das, BEVOR jemand ein PGN hineinkopiert.</summary>
    [HttpGet("guess/status")]
    public async Task<ActionResult<GuessUploadStatusDto>> GuessUploadStatus(CancellationToken ct)
        => Ok(await _service.GuessUploadStatusAsync(GetUserId(), ct));

    /// <summary>
    /// Die Partie noch einmal anstossen — alles, was nicht gerechnet ist, kommt frisch in die
    /// Warteschlange, und zwar auf einer neu gewaehlten Engine. Der Weg aus der Sackgasse
    /// „Auftrag haengt an einer Engine, die aus ist": ein Auftrag wechselt von sich aus nie.
    /// </summary>
    [HttpPost("{id:int}/restart")]
    public async Task<ActionResult<GameAnalysisDto>> Restart(int id, CancellationToken ct)
    {
        var dto = await _service.RestartAsync(GetUserId(), id, ct);
        return dto is null ? NotFound(new { message = "Analysis not found." }) : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => await _service.DeleteAsync(GetUserId(), id, ct)
            ? NoContent()
            : NotFound(new { message = "Analysis not found." });
}
