using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Hintergrund-Analyseaufträge des Users: „diese Stellung bis Tiefe N mit K Linien rechnen, sobald die
/// Hintergrund-Engine frei ist". Abgearbeitet vom <see cref="AnalysisJobWorker"/> (pausiert, während der
/// User live extern rechnet). Ergebnis = letzte Broker-Zeile als opakes JSON (Frontend mappt wie live).
/// </summary>
[ApiController]
[Route("api/analysis-jobs")]
[Authorize]
public class AnalysisJobsController : BaseApiController
{
    private readonly AnalysisJobService _jobs;

    public AnalysisJobsController(AnalysisJobService jobs) { _jobs = jobs; }

    [HttpGet]
    public async Task<ActionResult<List<AnalysisJobDto>>> List(CancellationToken ct)
        => Ok(await _jobs.ListAsync(GetUserId(), ct));

    [HttpPost]
    public async Task<ActionResult<AnalysisJobDto>> Create([FromBody] CreateAnalysisJobRequest request, CancellationToken ct)
    {
        try
        {
            var dto = await _jobs.CreateAsync(GetUserId(), request, ct);
            return CreatedAtAction(nameof(List), null, dto);
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Mehrere Stellungen auf einmal (Mehrfachauswahl) — Übersprungene kommen mit Grund zurück, kein 4xx dafür.</summary>
    [HttpPost("batch")]
    public async Task<ActionResult<AnalysisJobBatchResult>> CreateBatch([FromBody] CreateAnalysisJobsBatchRequest request, CancellationToken ct)
    {
        try { return Ok(await _jobs.CreateManyAsync(GetUserId(), request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AnalysisJobDto>> Update(int id, [FromBody] UpdateAnalysisJobRequest request, CancellationToken ct)
    {
        try
        {
            var dto = await _jobs.UpdateAsync(GetUserId(), id, request, ct);
            return dto is null ? NotFound() : Ok(dto);
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Auftrag neu anstoßen (nach „gescheitert" oder gefühltem Stillstand) — Ergebnis bleibt erhalten.</summary>
    [HttpPost("{id:int}/restart")]
    public async Task<ActionResult<AnalysisJobDto>> Restart(int id, CancellationToken ct)
    {
        var dto = await _jobs.RestartAsync(GetUserId(), id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => await _jobs.DeleteAsync(GetUserId(), id, ct) ? NoContent() : NotFound();
}
