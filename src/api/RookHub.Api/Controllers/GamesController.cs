using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Bereich „Gespeicherte Partien": Liste/Detail/Löschen der vom User (über die RepCheck-Extension)
/// von chess.com/lichess gespeicherten Partien — plus der öffentliche Teilen-Link über das ShareToken.
/// Das Anlegen läuft über <c>POST /api/extension/games</c> (Extension-CORS/Token).
/// </summary>
[ApiController]
[Route("api/games")]
[Authorize]
public class GamesController : BaseApiController
{
    private readonly SavedGameService _service;
    private readonly GameMistakeProgressService _mistakes;

    public GamesController(SavedGameService service, GameMistakeProgressService mistakes)
    {
        _service = service;
        _mistakes = mistakes;
    }

    /// <summary>Eigene gespeicherte Partien (neueste zuerst, ohne PGN) samt Stand des Fehler-Trainings.</summary>
    [HttpGet]
    public async Task<ActionResult<List<SavedGameDto>>> List([FromQuery] int take = 200, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var games = await _service.ListAsync(userId, take);
        // EINE Abfrage fuer die ganze Liste statt einer je Partie.
        var stand = await _mistakes.ForGamesAsync(userId, games.Select(g => g.Id).ToList(), ct);
        foreach (var g in games)
            if (stand.TryGetValue(g.Id, out var m)) g.Mistakes = m;
        return Ok(games);
    }

    /// <summary>Stand des Fehler-Trainings einer eigenen Partie (der Trainer markiert damit Gefundenes).</summary>
    [HttpGet("{id:int}/mistakes")]
    public async Task<ActionResult<GameMistakeProgressDto>> Mistakes(int id, CancellationToken ct)
    {
        var stand = await _mistakes.GetAsync(GetUserId(), id, ct);
        return stand == null ? NotFound() : Ok(stand);
    }

    /// <summary>
    /// Fortschritt melden: Aufgabenzahl und die in diesem Durchlauf SELBST gefundenen Halbzuege. Additiv und
    /// idempotent — der Server vereinigt sie mit dem bisherigen Stand, ein zweiter Durchlauf nimmt nichts weg.
    /// </summary>
    [HttpPost("{id:int}/mistakes")]
    public async Task<ActionResult<GameMistakeProgressDto>> RecordMistakes(int id,
        [FromBody] MistakeProgressInputDto dto, CancellationToken ct)
    {
        if (dto is null) return BadRequest(new { message = "Body required." });
        var stand = await _mistakes.RecordAsync(GetUserId(), id, dto.Total, dto.Solved, ct);
        return stand == null ? NotFound() : Ok(stand);
    }

    /// <summary>Öffentliche Sicht auf eine geteilte Partie (kein Login nötig). Literal-Route vor {id}.</summary>
    [HttpGet("shared/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<SharedGameDto>> GetShared(string token)
    {
        var game = await _service.GetSharedAsync(token, GetUserIdOrNull());
        return game == null ? NotFound() : Ok(game);
    }

    /// <summary>
    /// Bewertungen der geteilten Partie fuer Kurve, Genauigkeit und Zug-Klassen — ohne Login. Anonym
    /// NUR die vom Besitzer verknuepfte Analyse; wer angemeldet ist und die Partie selbst hat rechnen
    /// lassen, bekommt ersatzweise seine eigene (deshalb die UserId, obwohl der Endpunkt anonym ist).
    /// Kein eigener Rate-Limiter: derselbe globale je IP wie <see cref="GetShared"/>. Literal-Route vor {id}.
    /// </summary>
    [HttpGet("shared/{token}/evals")]
    [AllowAnonymous]
    public async Task<ActionResult<GameEvalsDto>> SharedEvals(string token, CancellationToken ct)
    {
        var evals = await _service.GetSharedEvalsAsync(token, GetUserIdOrNull(), ct);
        return evals == null ? NotFound() : Ok(evals);
    }

    /// <summary>„Partie analysieren" auf der geteilten Partie — jeder Angemeldete; Absage 400 mit
    /// <c>reason</c> wie beim Einwurf auf der Punktepartie-Seite. Literal-Route vor {id}.</summary>
    [HttpPost("shared/{token}/analyze")]
    public async Task<ActionResult<GameAnalyzeResultDto>> AnalyzeShared(string token, CancellationToken ct)
        => AnalyzeResult(await _service.AnalyzeSharedAsync(GetUserId(), token, ct));

    /// <summary>Detail einer eigenen Partie inkl. PGN (zum Nachspielen/Analysieren).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<SavedGameDetailDto>> Get(int id)
    {
        var game = await _service.GetAsync(GetUserId(), id);
        return game == null ? NotFound() : Ok(game);
    }

    /// <summary>Bewertungen einer eigenen Partie (Nachspiel-Dialog in <c>/games</c>).</summary>
    [HttpGet("{id:int}/evals")]
    public async Task<ActionResult<GameEvalsDto>> Evals(int id, CancellationToken ct)
    {
        var evals = await _service.GetEvalsAsync(GetUserId(), id, ct);
        return evals == null ? NotFound() : Ok(evals);
    }

    /// <summary>„Partie analysieren" an einer eigenen Partie — rechnet nur, wenn es noch keine
    /// brauchbare Analyse gibt, und verknuepft sie mit der Partie.</summary>
    [HttpPost("{id:int}/analyze")]
    public async Task<ActionResult<GameAnalyzeResultDto>> Analyze(int id, CancellationToken ct)
        => AnalyzeResult(await _service.AnalyzeAsync(GetUserId(), id, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _service.DeleteAsync(GetUserId(), id) ? NoContent() : NotFound();

    /// <summary>Dieselbe Antwortform wie <c>POST /api/game-analyses/guess</c>: die Seite formuliert den
    /// Grund in der Sprache des Nutzers, der Server kennt sie nicht.</summary>
    private ActionResult<GameAnalyzeResultDto> AnalyzeResult(GameAnalyzeResultDto? result)
    {
        if (result is null) return NotFound();
        if (result.Reason is not null)
            return BadRequest(new { reason = result.Reason, message = "Game could not be accepted." });
        return Ok(result);
    }
}
