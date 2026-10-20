using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Admin-Schalter „Hintergrundjobs pausieren" (<see cref="BackgroundPause"/>): Stand lesen und umlegen, dazu die
/// Liste der Hintergrunddienste mit „pausierbar ja/nein" und letztem Takt. Wirkt zur Laufzeit, ohne Neustart.
/// Nur Admins (bewusst kein eigenes Recht: hält die Arbeit des ganzen Systems an).
/// </summary>
[ApiController]
[Route("api/admin/background-jobs")]
[Authorize(Roles = "Admin")]
public class BackgroundJobsController : BaseApiController
{
    private readonly BackgroundPause _pause;
    private readonly IEnumerable<IHostedService> _hosted;

    public BackgroundJobsController(BackgroundPause pause, IEnumerable<IHostedService> hosted)
    {
        _pause = pause;
        _hosted = hosted;
    }

    /// <summary>Stand des Schalters + Dienste.</summary>
    [HttpGet("pause")]
    public async Task<ActionResult<BackgroundJobsPauseDto>> Get(CancellationToken ct)
        => Ok(Build(await _pause.GetAsync(ct)));

    /// <summary>Schalter umlegen; gilt sofort.</summary>
    [HttpPut("pause")]
    public async Task<ActionResult<BackgroundJobsPauseDto>> Set([FromBody] SetBackgroundJobsPauseDto dto, CancellationToken ct)
    {
        var name = User?.FindFirstValue(ClaimTypes.Name);
        return Ok(Build(await _pause.SetAsync(dto.Paused, GetUserIdOrNull(), name, ct)));
    }

    private BackgroundJobsPauseDto Build(BackgroundPauseState s)
        => new(s.Paused, s.Since, s.By, s.ForcedByConfig, s.PausedInDb, ListServices(_hosted));

    /// <summary>Die eigenen Hintergrunddienste (Framework-Dienste wie der Webhost bleiben draußen), sortiert:
    /// pausierbare zuerst, dann nach Namen.</summary>
    internal static List<BackgroundJobServiceDto> ListServices(IEnumerable<IHostedService> hosted)
        => hosted
            .Where(h => h.GetType().Namespace?.StartsWith("RookHub.", StringComparison.Ordinal) == true)
            .Select(h => h is IBackgroundJobInfo info
                ? new BackgroundJobServiceDto(h.GetType().Name, info.PausableByAdmin, info.LastTickUtc)
                : new BackgroundJobServiceDto(h.GetType().Name, false, null))
            .DistinctBy(d => d.Name)
            .OrderByDescending(d => d.Pausable).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();
}
