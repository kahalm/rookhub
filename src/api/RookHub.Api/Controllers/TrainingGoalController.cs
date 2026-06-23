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
    private readonly PlayTimeService _playTime;

    public TrainingGoalController(TrainingGoalService service, PlayTimeService playTime)
    {
        _service = service;
        _playTime = playTime;
    }

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

    /// <summary>Externe Spielzeit (Lichess/chess.com) des eigenen Users jetzt synchronisieren.</summary>
    [HttpPost("sync-play")]
    public async Task<IActionResult> SyncPlay(CancellationToken ct)
    {
        await _playTime.SyncUserAsync(GetUserId(), ct);
        return Ok(new { synced = true });
    }

    // ----- Manuelle Offline-Aktivitäten ------------------------------------

    /// <summary>Eigene manuell eingetragene Offline-Aktivitäten (neueste zuerst).</summary>
    [HttpGet("manual")]
    public async Task<ActionResult<List<ManualActivityDto>>> ListManual([FromQuery] int take = 200)
        => Ok(await _service.ListManualAsync(GetUserId(), take));

    /// <summary>Manuelle Offline-Aktivität anlegen (OTB-Partie / Offline-Studium / -Puzzle / Trainerstunde).</summary>
    [HttpPost("manual")]
    public async Task<ActionResult<ManualActivityDto>> AddManual([FromBody] ManualActivityInputDto dto)
    {
        try { return Ok(await _service.AddManualAsync(GetUserId(), dto)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Eigene manuelle Aktivität ändern (404, wenn nicht vorhanden/nicht eigene).</summary>
    [HttpPut("manual/{id:int}")]
    public async Task<ActionResult<ManualActivityDto>> UpdateManual(int id, [FromBody] ManualActivityInputDto dto)
    {
        try
        {
            var updated = await _service.UpdateManualAsync(GetUserId(), id, dto);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Eigene manuelle Aktivität löschen (404, wenn nicht vorhanden/nicht eigene).</summary>
    [HttpDelete("manual/{id:int}")]
    public async Task<IActionResult> DeleteManual(int id)
        => await _service.DeleteManualAsync(GetUserId(), id) ? NoContent() : NotFound();

    /// <summary>Chessable-Kurs-History (nach Kurs gruppiert) inkl. ermitteltem Thema; mit
    /// <paramref name="unassignedOnly"/> nur Kurse ohne feststehendes Thema.</summary>
    [HttpGet("chessable-courses")]
    public async Task<ActionResult<List<ChessableCourseSummaryDto>>> ChessableCourses([FromQuery] bool unassignedOnly = false)
        => Ok(await _service.GetChessableCoursesAsync(GetUserId(), unassignedOnly));

    /// <summary>Einem Chessable-Kurs manuell ein Thema zuordnen (Upsert). 400 bei leerer Kurs-ID.</summary>
    [HttpPut("chessable-courses/{courseId}")]
    public async Task<IActionResult> SetChessableCourseTheme(string courseId, [FromBody] ChessableCourseThemeInputDto dto)
        => await _service.SetChessableCourseThemeAsync(GetUserId(), courseId, dto.Theme) ? NoContent() : BadRequest();

    /// <summary>Manuelle Themen-Zuordnung eines Kurses entfernen (404, wenn keine vorhanden war).</summary>
    [HttpDelete("chessable-courses/{courseId}")]
    public async Task<IActionResult> ClearChessableCourseTheme(string courseId)
        => await _service.ClearChessableCourseThemeAsync(GetUserId(), courseId) ? NoContent() : NotFound();
}
