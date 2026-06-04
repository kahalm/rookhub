using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Trainingsziele des eingeloggten Users: effektives Tagesziel (persönlich oder aus Gruppen-Vorlage)
/// lesen/anpassen/zurücksetzen, heutiger Fortschritt und der Ziele-Tracker (Tagesreihe für die Heatmap).
/// </summary>
[ApiController]
[Route("api/training-goals")]
[Authorize]
public class TrainingGoalController : BaseApiController
{
    private readonly TrainingGoalService _service;

    public TrainingGoalController(TrainingGoalService service) => _service = service;

    /// <summary>Effektives Ziel des Users (persönlich &gt; Gruppen-Vorlage &gt; keins).</summary>
    [HttpGet]
    public async Task<ActionResult<TrainingGoalDto>> Get()
        => Ok(await _service.GetEffectiveGoalAsync(GetUserId()));

    /// <summary>Persönlichen Override setzen/aktualisieren; gibt das neue effektive Ziel zurück.</summary>
    [HttpPut]
    public async Task<ActionResult<TrainingGoalDto>> Set([FromBody] TrainingGoalInputDto dto)
        => Ok(await _service.SetPersonalGoalAsync(GetUserId(), dto));

    /// <summary>Persönlichen Override entfernen → Rückfall auf die Gruppen-Vorlage (falls vorhanden).</summary>
    [HttpDelete]
    public async Task<ActionResult<TrainingGoalDto>> DeleteOverride()
        => Ok(await _service.DeletePersonalGoalAsync(GetUserId()));

    /// <summary>Heutiger Fortschritt je Kategorie + Wochenstand.</summary>
    [HttpGet("today")]
    public async Task<ActionResult<TodayProgressDto>> Today()
        => Ok(await _service.GetTodayAsync(GetUserId()));

    /// <summary>Tagesreihe der letzten <paramref name="weeks"/> Wochen für die Tracker-Heatmap.</summary>
    [HttpGet("tracker")]
    public async Task<ActionResult<TrackerResponseDto>> Tracker([FromQuery] int weeks = 27)
        => Ok(await _service.GetTrackerAsync(GetUserId(), weeks));
}
