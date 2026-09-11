using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Der Eroeffnungsbaum der Punktepartie: „welche Zuege werden in dieser Stellung gespielt, und von
/// wie vielen Partien?"
///
/// <para>Offen wie die Bestandsliste daneben — die Punktepartie laesst sich ohne Anmeldung
/// spielen, und ein Filter, der eine Anmeldung verlangt, waere eine Tuer vor einem offenen
/// Raum.</para>
/// </summary>
[ApiController]
[Route("api/guess-tree")]
public class GuessTreeController : ControllerBase
{
    private readonly GuessOpeningTree _tree;

    public GuessTreeController(GuessOpeningTree tree) => _tree = tree;

    /// <param name="line">Die bisherigen Halbzuege („e4 e5"); leer = Grundstellung.</param>
    /// <param name="onlyPlayable">Nur zaehlen, was schon gerechnet und freigegeben ist. Sonst
    /// zaehlt der ganze Rohbestand — von dort laesst sich eine Partie anfordern.</param>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<OpeningTreeDto>> Branch(CancellationToken ct,
        [FromQuery] string? line = null, [FromQuery] bool onlyPlayable = true)
        => Ok(await _tree.BranchAsync(line, onlyPlayable, ct));
}
