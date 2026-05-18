using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/tournament-favorites")]
[Authorize]
public class TournamentFavoriteController : BaseApiController
{
    private readonly AppDbContext _db;

    public TournamentFavoriteController(AppDbContext db) => _db = db;

    /// <summary>Get all favorites for the current user, optionally filtered by tournament.</summary>
    [HttpGet]
    public async Task<ActionResult<List<TournamentFavoriteDto>>> GetAll([FromQuery] string? tournamentId = null)
    {
        var userId = GetUserId();
        var query = _db.TournamentFavorites.Where(f => f.UserId == userId);

        if (!string.IsNullOrEmpty(tournamentId))
            query = query.Where(f => f.CrawlerTournamentId == tournamentId);

        var favs = await query
            .Select(f => new TournamentFavoriteDto
            {
                Id = f.Id,
                CrawlerTournamentId = f.CrawlerTournamentId,
                PlayerSnr = f.PlayerSnr,
                TeamSnr = f.TeamSnr,
                FavoritedAt = f.FavoritedAt
            })
            .ToListAsync();

        return Ok(favs);
    }

    /// <summary>Add a favorite.</summary>
    [HttpPost]
    public async Task<ActionResult<TournamentFavoriteDto>> Create([FromBody] CreateTournamentFavoriteDto dto)
    {
        var userId = GetUserId();
        var exists = await _db.TournamentFavorites
            .AnyAsync(f => f.UserId == userId
                        && f.CrawlerTournamentId == dto.CrawlerTournamentId
                        && f.PlayerSnr == dto.PlayerSnr);

        if (exists)
            return Conflict(new { message = "Already favorited." });

        var fav = new TournamentFavorite
        {
            UserId = userId,
            CrawlerTournamentId = dto.CrawlerTournamentId,
            PlayerSnr = dto.PlayerSnr
        };

        _db.TournamentFavorites.Add(fav);
        await _db.SaveChangesAsync();

        return Ok(new TournamentFavoriteDto
        {
            Id = fav.Id,
            CrawlerTournamentId = fav.CrawlerTournamentId,
            PlayerSnr = fav.PlayerSnr,
            TeamSnr = fav.TeamSnr,
            FavoritedAt = fav.FavoritedAt
        });
    }

    /// <summary>Remove a favorite by ID.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var fav = await _db.TournamentFavorites
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == GetUserId());

        if (fav == null)
            return NotFound(new { message = "Favorite not found." });

        _db.TournamentFavorites.Remove(fav);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Remove a favorite by tournament + player SNR.</summary>
    [HttpDelete("by-player/{tournamentId}/{playerSnr}")]
    public async Task<IActionResult> DeleteByPlayer(string tournamentId, int playerSnr)
    {
        var fav = await _db.TournamentFavorites
            .FirstOrDefaultAsync(f => f.UserId == GetUserId()
                                   && f.CrawlerTournamentId == tournamentId
                                   && f.PlayerSnr == playerSnr);

        if (fav == null)
            return NotFound(new { message = "Favorite not found." });

        _db.TournamentFavorites.Remove(fav);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Add a team favorite.</summary>
    [HttpPost("team")]
    public async Task<ActionResult<TournamentFavoriteDto>> CreateTeamFavorite([FromBody] CreateTeamFavoriteDto dto)
    {
        var userId = GetUserId();
        var exists = await _db.TournamentFavorites
            .AnyAsync(f => f.UserId == userId
                        && f.CrawlerTournamentId == dto.CrawlerTournamentId
                        && f.TeamSnr == dto.TeamSnr);

        if (exists)
            return Conflict(new { message = "Already favorited." });

        var fav = new TournamentFavorite
        {
            UserId = userId,
            CrawlerTournamentId = dto.CrawlerTournamentId,
            TeamSnr = dto.TeamSnr
        };

        _db.TournamentFavorites.Add(fav);
        await _db.SaveChangesAsync();

        return Ok(new TournamentFavoriteDto
        {
            Id = fav.Id,
            CrawlerTournamentId = fav.CrawlerTournamentId,
            TeamSnr = fav.TeamSnr,
            FavoritedAt = fav.FavoritedAt
        });
    }

    /// <summary>Remove a team favorite by tournament + team SNR.</summary>
    [HttpDelete("by-team/{tournamentId}/{teamSnr}")]
    public async Task<IActionResult> DeleteByTeam(string tournamentId, int teamSnr)
    {
        var fav = await _db.TournamentFavorites
            .FirstOrDefaultAsync(f => f.UserId == GetUserId()
                                   && f.CrawlerTournamentId == tournamentId
                                   && f.TeamSnr == teamSnr);

        if (fav == null)
            return NotFound(new { message = "Favorite not found." });

        _db.TournamentFavorites.Remove(fav);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Get settings (showFavoritesOnly) for a tournament.</summary>
    [HttpGet("settings/{tournamentId}")]
    public async Task<IActionResult> GetSettings(string tournamentId)
    {
        var setting = await _db.TournamentUserSettings
            .FirstOrDefaultAsync(s => s.UserId == GetUserId() && s.CrawlerTournamentId == tournamentId);

        return Ok(new { showFavoritesOnly = setting?.ShowFavoritesOnly ?? false });
    }

    /// <summary>Save settings (showFavoritesOnly) for a tournament.</summary>
    [HttpPut("settings/{tournamentId}")]
    public async Task<IActionResult> SaveSettings(string tournamentId, [FromBody] TournamentSettingsDto dto)
    {
        var userId = GetUserId();
        var setting = await _db.TournamentUserSettings
            .FirstOrDefaultAsync(s => s.UserId == userId && s.CrawlerTournamentId == tournamentId);

        if (setting == null)
        {
            setting = new TournamentUserSetting
            {
                UserId = userId,
                CrawlerTournamentId = tournamentId,
                ShowFavoritesOnly = dto.ShowFavoritesOnly
            };
            _db.TournamentUserSettings.Add(setting);
        }
        else
        {
            setting.ShowFavoritesOnly = dto.ShowFavoritesOnly;
        }

        await _db.SaveChangesAsync();
        return Ok(new { showFavoritesOnly = setting.ShowFavoritesOnly });
    }
}
