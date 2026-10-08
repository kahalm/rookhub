using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Eröffnungs-Explorer für eine einzelne Stellung (Analysebrett). Rechnung und Datenstrecke liegen in
/// <see cref="RepertoireExplorerService"/> — dieselbe wie beim Lochfinder, damit Speicher, Token und
/// Drossel nur EINMAL existieren. Nur angemeldet: die Online-Quelle verbraucht das gemeinsame
/// Kontingent des Server-Tokens.
///
/// <para>Fängt nichts selbst (Codereview A7-011): eine ungültige Auswahl ist eine
/// <c>DomainValidationException</c>, der globale <c>DomainExceptionFilter</c> macht daraus 400 <c>{ message }</c>.</para>
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
        var query = ExplorerQuery.Create(database, ParseInts(ratings), Split(speeds));
        return Ok(await _explorer.PositionAsync(GetUserId(), fen, source, query, ct));
    }

    /// <summary>Eine Handvoll Partien, die die Stellung erreicht haben (Parameter wie oben).</summary>
    [HttpGet("games")]
    public async Task<ActionResult<ExplorerGamesResultDto>> Games(
        [FromQuery] string? fen, [FromQuery] string? source, [FromQuery] string? database,
        [FromQuery] string? ratings, [FromQuery] string? speeds, CancellationToken ct)
    {
        var query = ExplorerQuery.Create(database, ParseInts(ratings), Split(speeds));
        return Ok(await _explorer.GamesAsync(GetUserId(), fen, source, query, ct));
    }

    /// <summary>Die häufigsten Zugfolgen von der Grundstellung zu <paramref name="fen"/> — NUR aus dem lokalen Explorer
    /// (<see cref="ExplorerPathFinder"/>). Ohne lokalen Explorer 400 <c>noLocalExplorer</c>, ungültige Stellung 400
    /// <c>invalidFen</c>, andere Quelle als <c>local</c> 400 <c>onlyLocal</c>.</summary>
    [HttpGet("paths")]
    [EnableRateLimiting(RateLimitPartitions.ExplorerPathsPolicy)]
    public async Task<ActionResult<ExplorerPathsResultDto>> Paths(
        [FromQuery] string? fen, [FromQuery] int? maxPlies, [FromQuery] string? source,
        [FromServices] ExplorerPathFinder finder, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(source) && source != "local")
            return BadRequest(new { reason = "onlyLocal", message = "Die Zugfolgen-Suche gibt es nur mit dem lokalen Explorer." });
        if (!finder.IsAvailable)
            return BadRequest(new { reason = "noLocalExplorer", message = "Der lokale Explorer ist auf diesem Server nicht eingerichtet." });
        try
        {
            return Ok(await finder.FindAsync(fen ?? "", maxPlies, ct));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { reason = "invalidFen", message = "Keine gültige Stellung." });
        }
    }

    private static IEnumerable<string> Split(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<int> ParseInts(string? csv) =>
        Split(csv).Select(x => int.TryParse(x, out var n) ? n : throw new DomainValidationException("Unbekannte Elo-Stufe."));
}
