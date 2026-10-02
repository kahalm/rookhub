using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    private static IEnumerable<string> Split(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<int> ParseInts(string? csv) =>
        Split(csv).Select(x => int.TryParse(x, out var n) ? n : throw new DomainValidationException("Unbekannte Elo-Stufe."));
}
