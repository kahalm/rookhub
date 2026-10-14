using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Controllers.Courses;

/// <summary>
/// Woodpecker-Training über einen oder mehrere Kurse (feste Regeln aus dem Buch, <see cref="WoodpeckerRules"/>).
/// Pläne gehören dem Nutzer; ein fremder Plan ist 404. Fehlerfälle werfen die Dienste als Domänen-Ausnahme.
/// </summary>
[ApiController]
[Route("api/woodpecker")]
[Authorize]
public class WoodpeckerController : BaseApiController
{
    private readonly WoodpeckerService _service;

    public WoodpeckerController(WoodpeckerService service) => _service = service;

    /// <summary>Eigene Woodpecker-Trainings, neueste zuerst.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _service.ListAsync(GetUserId(), ct));

    /// <summary>Neues Training über die gewählten Kurse; Start optional rückdatiert.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWoodpeckerPlanDto dto, CancellationToken ct) =>
        Ok(await _service.CreateAsync(GetUserId(), IsAdmin, dto, ct));

    [HttpGet("{planId:int}")]
    public async Task<IActionResult> Get(int planId, CancellationToken ct) =>
        Ok(await _service.GetAsync(GetUserId(), planId, ct));

    [HttpDelete("{planId:int}")]
    public async Task<IActionResult> Delete(int planId, CancellationToken ct)
    {
        await _service.DeleteAsync(GetUserId(), planId, ct);
        return NoContent();
    }

    /// <summary>Beginn des laufenden Zyklus verschieben (rückdatieren).</summary>
    [HttpPut("{planId:int}/start")]
    public async Task<IActionResult> UpdateStart(int planId, [FromBody] UpdateWoodpeckerStartDto dto, CancellationToken ct) =>
        Ok(await _service.UpdateStartAsync(GetUserId(), planId, dto.StartedAt, ct));

    /// <summary>Zyklus 1 vor der Frist beenden (der Satz steht dann fest).</summary>
    [HttpPost("{planId:int}/finish-set")]
    public async Task<IActionResult> FinishSet(int planId, CancellationToken ct) =>
        Ok(await _service.FinishSetAsync(GetUserId(), planId, ct));

    /// <summary>Nächsten Zyklus beginnen (frühestens einen Tag nach dem Ende des vorigen).</summary>
    [HttpPost("{planId:int}/next-cycle")]
    public async Task<IActionResult> StartNextCycle(int planId, CancellationToken ct) =>
        Ok(await _service.StartNextCycleAsync(GetUserId(), planId, ct));

    /// <summary>Nächste Aufgabe: Zyklus 1 in Kursreihenfolge, danach zufällig aus dem Satz.</summary>
    [HttpGet("{planId:int}/next")]
    public async Task<IActionResult> Next(int planId, CancellationToken ct) =>
        Ok(await _service.GetNextAsync(GetUserId(), planId, ct));

    /// <summary>Versuch an einer Aufgabe im laufenden Zyklus (je Zyklus zählt der erste).</summary>
    [HttpPost("{planId:int}/attempts")]
    public async Task<IActionResult> RecordAttempt(int planId, [FromBody] RecordWoodpeckerAttemptDto dto, CancellationToken ct) =>
        Ok(await _service.RecordAttemptAsync(GetUserId(), planId, dto, ct));
}
