using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs.Engines;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Controllers.Engines;

/// <summary>„Ohne Engine analysieren" (0.855.0): eigener Zugbaum je Partie, Bewertungen erst nach dem Einschalten der Engine.
/// Regeln: <see cref="LineStudyService"/>.</summary>
[ApiController]
[Route("api/line-studies")]
[Authorize]
public class LineStudiesController(LineStudyService studies) : BaseApiController
{
    /// <summary>Der Baum zu einer Partie (<c>?key=</c>) — 404, solange es keinen gibt.</summary>
    [HttpGet]
    public async Task<ActionResult<LineStudyDto>> Get([FromQuery] string? key, CancellationToken ct)
        => await studies.GetAsync(GetUserId(), key, ct) is { } s ? Ok(s) : NotFound();

    /// <summary>Anlegen oder ersetzen (je Nutzer und <c>gameKey</c> einer); 400 bei ungültigem Baum.</summary>
    [HttpPut]
    public async Task<ActionResult<LineStudyDto>> Save([FromBody] SaveLineStudyRequest req, CancellationToken ct)
    {
        try { return Ok(await studies.SaveAsync(GetUserId(), req, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Engine einschalten.</summary>
    [HttpPost("{id:int}/reveal")]
    public async Task<ActionResult<LineStudyDto>> Reveal(int id, CancellationToken ct)
        => await studies.RevealAsync(GetUserId(), id, ct) is { } s ? Ok(s) : NotFound();

    /// <summary>Fortschritt; die Bewertungen je Stellung erst nach dem Einschalten.</summary>
    [HttpGet("{id:int}/evals")]
    public async Task<ActionResult<LineStudyEvalsDto>> Evals(int id, CancellationToken ct)
        => await studies.EvalsAsync(GetUserId(), id, ct) is { } e ? Ok(e) : NotFound();
}
