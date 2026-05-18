using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/tournament-monitors")]
public class TournamentMonitorController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CrawlerProxyService _proxy;

    public TournamentMonitorController(AppDbContext db, CrawlerProxyService proxy)
    {
        _db = db;
        _proxy = proxy;
    }

    [HttpPost("{tournamentId}")]
    public async Task<IActionResult> Activate(string tournamentId)
    {
        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == tournamentId);

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
        try
        {
            var result = await _proxy.GetAsync($"/api/tournaments/{tournamentId}");
            if (result.TryGetProperty("totalRounds", out var totalRoundsProp))
                knownRounds = totalRoundsProp.GetInt32();
            if (result.TryGetProperty("id", out var idProp))
                dbId = idProp.GetInt32();
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }

        // Get actual known rounds from rounds/check
        try
        {
            var checkResult = await _proxy.GetAsync($"/api/tournaments/{tournamentId}/rounds/check");
            if (checkResult.TryGetProperty("knownRounds", out var kr))
                knownRounds = kr.GetInt32();
        }
        catch
        {
            // Fall back to totalRounds from tournament
        }

        monitor = new TournamentMonitor
        {
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
        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == tournamentId);

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
        var monitor = await _db.TournamentMonitors
            .FirstOrDefaultAsync(m => m.CrawlerTournamentId == tournamentId);

        if (monitor is not null)
        {
            _db.TournamentMonitors.Remove(monitor);
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }
}
