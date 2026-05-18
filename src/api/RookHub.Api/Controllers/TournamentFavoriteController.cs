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
}
