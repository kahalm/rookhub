using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

    /// <summary>
    /// Aktive Runden-Monitore je Konto. Jeder Monitor laesst den Crawler alle 30 s eine chess-results-Seite
    /// holen (rounds/check, 60 s gecacht) — ohne Deckel gehoerte der chess-results-Takt mit ein paar Dutzend
    /// Monitoren einem einzigen Konto (Codereview A5-004; vgl. MaxTracked im Turnierverlauf).
    /// </summary>
    internal const int MaxActiveMonitorsPerUser = 10;

    [HttpPost("{tournamentId}")]
    [EnableRateLimiting(RateLimitPartitions.CrawlerRequestPolicy)]
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

        // Verlaengern (oben) geht immer; ein NEUER Monitor nur unter dem Deckel — vor dem ersten Crawler-Aufruf.
        var now = DateTime.UtcNow;
        if (await _db.TournamentMonitors.CountAsync(m => m.UserId == userId && m.ActiveUntil >= now)
            >= MaxActiveMonitorsPerUser)
            return Conflict(new { message = $"Maximum of {MaxActiveMonitorsPerUser} active round monitors per user reached." });

        // Fetch current round count from crawler
        int knownRounds = 0;
        int dbId = 0;
        var result = await _proxy.GetAsync($"/api/tournaments/{tournamentId}");
        // TryGetInt32 statt GetInt32: liefert der Crawler das Feld als String/Null/anderen Typ, würde
        // GetInt32 werfen → unbehandelter 500 statt sauberem Fallback (knownRounds bleibt 0, dbId-Check greift).
        // Ausgangsstand der Entdopplung im RoundMonitorService (gemeldet wird nur ueber LastKnownRounds
        // hinaus): die schon geholten Runden ("knownRounds"), NICHT die geplante Rundenzahl "totalRounds" —
        // mit 9 als Stand haette der Monitor keine einzige Runde gemeldet.
        if (result.TryGetProperty("knownRounds", out var knownRoundsProp) && knownRoundsProp.TryGetInt32(out var kr0))
            knownRounds = kr0;
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
            _logger.LogWarning(ex, "Failed to check rounds for {TournamentId}, falling back to crawled rounds", tournamentId);
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
