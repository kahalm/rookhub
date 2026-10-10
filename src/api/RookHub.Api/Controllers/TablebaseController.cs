using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>Endspiel-Datenbank für die Live-Analyse (0.729.0) — offen wie das Analysebrett selbst.</summary>
[ApiController]
[Route("api/tablebase")]
[AllowAnonymous]
[EnableRateLimiting("anonymous-read")]
public class TablebaseController : ControllerBase
{
    private readonly TablebaseService _tablebase;

    public TablebaseController(TablebaseService tablebase) { _tablebase = tablebase; }

    /// <summary>Ergebnis der Stellung (bis 7 Steine) samt Bewertung jedes Zugs; immer 200, der Zustand steht in <c>status</c>.</summary>
    [HttpGet]
    public async Task<ActionResult<TablebaseResultDto>> Get([FromQuery] string? fen, CancellationToken ct)
        => Ok(await _tablebase.LookupAsync(fen, ct));
}
