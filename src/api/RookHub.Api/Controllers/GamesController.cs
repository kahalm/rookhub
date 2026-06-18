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
    public GamesController(SavedGameService service) => _service = service;

    /// <summary>Eigene gespeicherte Partien (neueste zuerst, ohne PGN).</summary>
    [HttpGet]
    public async Task<ActionResult<List<SavedGameDto>>> List([FromQuery] int take = 200)
        => Ok(await _service.ListAsync(GetUserId(), take));

    /// <summary>Öffentliche Sicht auf eine geteilte Partie (kein Login nötig). Literal-Route vor {id}.</summary>
    [HttpGet("shared/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<SharedGameDto>> GetShared(string token)
    {
        var game = await _service.GetSharedAsync(token);
        return game == null ? NotFound() : Ok(game);
    }

    /// <summary>Detail einer eigenen Partie inkl. PGN (zum Nachspielen/Analysieren).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<SavedGameDetailDto>> Get(int id)
    {
        var game = await _service.GetAsync(GetUserId(), id);
        return game == null ? NotFound() : Ok(game);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _service.DeleteAsync(GetUserId(), id) ? NoContent() : NotFound();
}
