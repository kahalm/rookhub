using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>Analyse-Verlauf des Analysebretts (0.603.0) — die letzten 20 Analysen je Nutzer samt Stern-Markierungen.</summary>
[ApiController]
[Route("api/analysis-history")]
[Authorize]
public class AnalysisHistoryController : BaseApiController
{
    private readonly AnalysisHistoryService _history;

    public AnalysisHistoryController(AnalysisHistoryService history) { _history = history; }

    [HttpGet]
    public async Task<ActionResult<List<AnalysisHistoryEntryDto>>> List(CancellationToken ct)
        => Ok(await _history.ListAsync(GetUserId(), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AnalysisHistoryEntryDto>> Get(int id, CancellationToken ct)
    {
        var dto = await _history.GetAsync(GetUserId(), id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>Stand speichern (anlegen oder fortschreiben) → der Eintrag samt Kennung; 400 bei unlesbarer Stellung/Zugfolge.</summary>
    [HttpPost]
    public async Task<ActionResult<AnalysisHistoryEntryDto>> Save([FromBody] SaveAnalysisHistoryRequest request, CancellationToken ct)
    {
        try { return Ok(await _history.SaveAsync(GetUserId(), request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => await _history.DeleteAsync(GetUserId(), id, ct) ? NoContent() : NotFound();
}
