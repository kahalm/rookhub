using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Eröffnungs-Explorer für eine einzelne Stellung (Analysebrett). Rechnung und Datenstrecke liegen in
/// <see cref="RepertoireExplorerService"/> — dieselbe wie beim Lochfinder, damit Speicher, Token und
/// Drossel nur EINMAL existieren. Nur angemeldet: die Online-Quelle verbraucht das gemeinsame
/// Kontingent des Server-Tokens.
/// </summary>
[ApiController]
[Route("api/explorer")]
[Authorize]
public class ExplorerController : BaseApiController
{
    private readonly RepertoireExplorerService _explorer;

    public ExplorerController(RepertoireExplorerService explorer) => _explorer = explorer;

    /// <summary>Zugstatistik der Stellung. <c>ratings</c>/<c>speeds</c> als Komma-Liste (nur Lichess).</summary>
    [HttpGet("position")]
    public async Task<ActionResult<ExplorerPositionResultDto>> Position(
        [FromQuery] string? fen, [FromQuery] string? source, [FromQuery] string? database,
        [FromQuery] string? ratings, [FromQuery] string? speeds, CancellationToken ct)
    {
        try
        {
            var query = ExplorerQuery.Create(database, ParseInts(ratings), Split(speeds));
            return Ok(await _explorer.PositionAsync(GetUserId(), fen, source, query, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Eine Handvoll Partien, die die Stellung erreicht haben (Parameter wie oben).</summary>
    [HttpGet("games")]
    public async Task<ActionResult<ExplorerGamesResultDto>> Games(
        [FromQuery] string? fen, [FromQuery] string? source, [FromQuery] string? database,
        [FromQuery] string? ratings, [FromQuery] string? speeds, CancellationToken ct)
    {
        try
        {
            var query = ExplorerQuery.Create(database, ParseInts(ratings), Split(speeds));
            return Ok(await _explorer.GamesAsync(GetUserId(), fen, source, query, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Welche Quellen es gibt (dieselbe Antwort wie am Repertoire-Endpunkt).</summary>
    [HttpGet("sources")]
    public ActionResult<ExplorerSourcesDto> Sources() => Ok(_explorer.Sources());

    private static IEnumerable<string> Split(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<int> ParseInts(string? csv) =>
        Split(csv).Select(x => int.TryParse(x, out var n) ? n : throw new ArgumentException("Unbekannte Elo-Stufe."));
}
