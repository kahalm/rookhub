using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Filters;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Validation;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/tournament-monitors")]
[Authorize]
[TypeFilter(typeof(CrawlerExceptionFilter))]
public class TournamentMonitorController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly CrawlerProxyService _proxy;
    private readonly ILogger<TournamentMonitorController> _logger;

    public TournamentMonitorController(AppDbContext db, CrawlerProxyService proxy, ILogger<TournamentMonitorController> logger)
    {
        _db = db;
        _proxy = proxy;
        _logger = logger;
    }

    [HttpPost("{tournamentId}")]
    public async Task<IActionResult> Activate(string tournamentId)
    {
        if (!TournamentIdValidator.IsValid(tournamentId))
            return BadRequest(new { message = "Invalid tournament ID." });

        var userId = GetUserId();

        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == tournamentId && m.UserId == userId);

        if (monitor is not null)
        {
            monitor.ActiveUntil = DateTime.UtcNow.AddHours(1);
            await _db.SaveChangesAsync();
            return Ok(new
            {
                active = true,
                activeUntil = monitor.ActiveUntil,
                lastCheckedAt = monitor.LastCheckedAt,
                lastKnownRounds = monitor.LastKnownRounds
            });
        }

        // Fetch current round count from crawler
        int knownRounds = 0;
        int dbId = 0;
        var result = await _proxy.GetAsync($"/api/tournaments/{tournamentId}");
        // TryGetInt32 statt GetInt32: liefert der Crawler das Feld als String/Null/anderen Typ, würde
        // GetInt32 werfen → unbehandelter 500 statt sauberem Fallback (knownRounds bleibt 0, dbId-Check greift).
        if (result.TryGetProperty("totalRounds", out var totalRoundsProp) && totalRoundsProp.TryGetInt32(out var tr))
            knownRounds = tr;
        if (result.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var did))
            dbId = did;

        // Ohne aufloesbare Crawler-DB-Id wuerde der Hintergrund-Monitor dauerhaft
        // /api/tournaments/0/rounds/check pollen -> erst gar nicht aktivieren.
        if (dbId <= 0)
        {
            _logger.LogWarning(
                "Could not resolve crawler DB id for tournament {TournamentId}; not activating monitor.",
                tournamentId);
            return StatusCode(502, new { message = "Turnier konnte beim Crawler nicht aufgeloest werden; Monitor nicht aktiviert." });
        }

        // Get actual known rounds from rounds/check
        try
        {
            var checkResult = await _proxy.GetAsync($"/api/tournaments/{tournamentId}/rounds/check");
            if (checkResult.TryGetProperty("knownRounds", out var kr) && kr.TryGetInt32(out var krv))
                knownRounds = krv;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check rounds for {TournamentId}, falling back to totalRounds", tournamentId);
        }

        monitor = new TournamentMonitor
        {
            UserId = userId,
            CrawlerTournamentId = tournamentId,
            CrawlerTournamentDbId = dbId,
            ActiveUntil = DateTime.UtcNow.AddHours(1),
            LastKnownRounds = knownRounds
        };

        _db.TournamentMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            active = true,
            activeUntil = monitor.ActiveUntil,
            lastCheckedAt = monitor.LastCheckedAt,
            lastKnownRounds = monitor.LastKnownRounds
        });
    }

    [HttpGet("{tournamentId}")]
    public async Task<IActionResult> GetStatus(string tournamentId)
    {
        if (!TournamentIdValidator.IsValid(tournamentId))
            return BadRequest(new { message = "Invalid tournament ID." });

        var userId = GetUserId();

        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == tournamentId && m.UserId == userId);

        if (monitor is null || monitor.ActiveUntil < DateTime.UtcNow)
        {
            return Ok(new
            {
                active = false,
                activeUntil = (DateTime?)null,
                lastCheckedAt = (DateTime?)null,
                lastKnownRounds = 0
            });
        }

        return Ok(new
        {
            active = true,
            activeUntil = monitor.ActiveUntil,
            lastCheckedAt = monitor.LastCheckedAt,
            lastKnownRounds = monitor.LastKnownRounds
        });
    }

    [HttpDelete("{tournamentId}")]
    public async Task<IActionResult> Deactivate(string tournamentId)
    {
        if (!TournamentIdValidator.IsValid(tournamentId))
            return BadRequest(new { message = "Invalid tournament ID." });

        var userId = GetUserId();

        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == tournamentId && m.UserId == userId);

        if (monitor is not null)
        {
            _db.TournamentMonitors.Remove(monitor);
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }
}
