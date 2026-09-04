using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Services;
using RookHub.Api.Validation;
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
        // Race-Catch wie bei den Abos: der Unique-Index ist die eigentliche Wahrheit, die Prüfung
        // oben nur die schnelle Antwort (Doppelklick/zweiter Tab landete sonst auf 500).
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            return Conflict(new { message = "Already favorited." });
        }

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
        if (!TournamentIdValidator.IsValid(tournamentId))
            return BadRequest(new { message = "Invalid tournament id." });
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
        try { await _db.SaveChangesAsync(); }   // Race-Catch, siehe Create()
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            return Conflict(new { message = "Already favorited." });
        }

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
        if (!TournamentIdValidator.IsValid(tournamentId))
            return BadRequest(new { message = "Invalid tournament id." });
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
        if (!TournamentIdValidator.IsValid(tournamentId))
            return BadRequest(new { message = "Invalid tournament id." });
        var setting = await _db.TournamentUserSettings
            .FirstOrDefaultAsync(s => s.UserId == GetUserId() && s.CrawlerTournamentId == tournamentId);

        return Ok(new { showFavoritesOnly = setting?.ShowFavoritesOnly ?? false });
    }

    /// <summary>Save settings (showFavoritesOnly) for a tournament.</summary>
    [HttpPut("settings/{tournamentId}")]
    public async Task<IActionResult> SaveSettings(string tournamentId, [FromBody] TournamentSettingsDto dto)
    {
        // Der Route-Parameter kam ungeprüft in eine Spalte mit MaxLength(50): beim ersten Speichern
        // einer längeren Id warf MariaDB „Data too long" und der Aufrufer bekam 500 statt 400 (die
        // DTO-Pfade erzwingen das Muster längst, der Monitor-Controller prüft hier ebenfalls).
        if (!TournamentIdValidator.IsValid(tournamentId))
            return BadRequest(new { message = "Invalid tournament id." });
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

        // Upsert-Race: legen zwei parallele Requests dieselbe Einstellungszeile an, schlägt der
        // Unique-Index beim zweiten zu. Hier ist die richtige Antwort NICHT 409 (der Aufrufer wollte
        // nur speichern), sondern: bestehende Zeile laden und den Wunsch darauf anwenden.
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            var existing = await _db.TournamentUserSettings
                .FirstOrDefaultAsync(x => x.UserId == userId && x.CrawlerTournamentId == tournamentId);
            if (existing is null) throw;
            existing.ShowFavoritesOnly = dto.ShowFavoritesOnly;
            await _db.SaveChangesAsync();
            return Ok(new { showFavoritesOnly = existing.ShowFavoritesOnly });
        }
        return Ok(new { showFavoritesOnly = setting.ShowFavoritesOnly });
    }
}
