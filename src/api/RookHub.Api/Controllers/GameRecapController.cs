using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>„Kurz erzählt" (0.541.0) — die Nacherzählung der EIGENEN Partie. Eigene Klasse unter derselben Route wie
/// <see cref="GamesController"/>; die geteilte Ansicht bekommt den Text über <see cref="SharedGameDto.Recap"/>.</summary>
[ApiController]
[Route("api/games")]
[Authorize]
public class GameRecapController : BaseApiController
{
    private readonly GameRecapService _service;
    private readonly IGameReviewTextScheduler _scheduler;
    private readonly QuietHours? _quiet;

    public GameRecapController(GameRecapService service, IGameReviewTextScheduler scheduler, QuietHours? quiet = null)
    {
        _service = service;
        _scheduler = scheduler;
        _quiet = quiet;
    }

    /// <summary>Die Nacherzählung. Fehlt sie bei fertiger Analyse — eine Analyse von vor 0.541.0, oder der Lauf danach ist
    /// gescheitert —, wird sie im Hintergrund geschrieben und <c>pending</c> gesetzt; die Seite fragt später nach.</summary>
    [HttpGet("{id:int}/recap")]
    public async Task<ActionResult<GameRecapDto>> Get(int id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(GetUserId(), id, ct);
        if (dto == null) return NotFound();
        if (dto.Text == null && dto.Available && dto.HasAnalysis)
        {
            // In der Sperrzeit der Spark nicht anstoßen — beim nächsten Öffnen danach entsteht sie.
            dto.QuietUntil = _quiet?.QuietUntil();
            if (dto.QuietUntil == null)
            {
                _scheduler.ScheduleRecap(id);
                dto.Pending = true;
            }
        }
        return Ok(dto);
    }
}
