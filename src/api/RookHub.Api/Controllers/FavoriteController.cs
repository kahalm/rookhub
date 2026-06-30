using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/favorites")]
[Authorize]
public class FavoriteController : BaseApiController
{
    private readonly FavoriteService _favorites;

    public FavoriteController(FavoriteService favorites) => _favorites = favorites;

    /// <summary>Alle geliebten Puzzles des Users (neueste zuerst) inkl. Metadaten zum Nachspielen/Analysieren.</summary>
    [HttpGet]
    public async Task<ActionResult<List<FavoritePuzzleDto>>> List([FromQuery] int take = 200)
        => Ok(await _favorites.ListAsync(GetUserId(), take));

    /// <summary>Anzahl geliebter Puzzles (Dashboard-Kachel).</summary>
    [HttpGet("count")]
    public async Task<IActionResult> Count()
        => Ok(new { count = await _favorites.CountAsync(GetUserId()) });

    /// <summary>Ist ein konkretes Puzzle favorisiert? (Herz-Status im Solver).</summary>
    [HttpGet("contains")]
    public async Task<IActionResult> Contains([FromQuery] PuzzleSource source, [FromQuery] int puzzleId)
        => Ok(new { favorited = await _favorites.ContainsAsync(GetUserId(), source, puzzleId) });

    /// <summary>Puzzle favorisieren (idempotent). 404, wenn das Puzzle nicht existiert.</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] ToggleFavoriteDto dto)
    {
        try
        {
            var favorited = await _favorites.AddAsync(GetUserId(), dto.Source, dto.PuzzleId);
            return Ok(new { favorited });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Puzzle aus den Favoriten entfernen (idempotent).</summary>
    [HttpDelete]
    public async Task<IActionResult> Remove([FromQuery] PuzzleSource source, [FromQuery] int puzzleId)
    {
        var favorited = await _favorites.RemoveAsync(GetUserId(), source, puzzleId);
        return Ok(new { favorited });
    }
}
