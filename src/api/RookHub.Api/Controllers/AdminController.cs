using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly PuzzleService _puzzleService;

    public AdminController(AppDbContext db, PuzzleService puzzleService)
    {
        _db = db;
        _puzzleService = puzzleService;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var query = _db.AppUsers.AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            if (search.Length > 100) search = search[..100];
            query = query.Where(u => u.Username.Contains(search) || u.Email.Contains(search));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsAdmin = u.IsAdmin,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(new { items, totalCount, page, pageSize });
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var currentUserId = GetUserId();
        if (id == currentUserId)
            return BadRequest(new { message = "Cannot delete yourself." });

        var user = await _db.AppUsers.FindAsync(id);
        if (user == null)
            return NotFound();

        // Remove friendships first (Restrict delete behavior)
        var friendships = await _db.Friendships
            .Where(f => f.RequesterId == id || f.AddresseeId == id)
            .ToListAsync();
        _db.Friendships.RemoveRange(friendships);

        _db.AppUsers.Remove(user);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("users/{id}/toggle-admin")]
    public async Task<IActionResult> ToggleAdmin(int id)
    {
        var currentUserId = GetUserId();
        if (id == currentUserId)
            return BadRequest(new { message = "Cannot toggle your own admin status." });

        var user = await _db.AppUsers.FindAsync(id);
        if (user == null)
            return NotFound();

        user.IsAdmin = !user.IsAdmin;
        await _db.SaveChangesAsync();

        return Ok(new AdminUserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            IsAdmin = user.IsAdmin,
            CreatedAt = user.CreatedAt
        });
    }

    [HttpPost("puzzles/import")]
    [RequestSizeLimit(500 * 1024 * 1024)]
    public async Task<IActionResult> ImportPuzzles(
        IFormFile file,
        [FromQuery] int? minRating,
        [FromQuery] int? maxRating,
        [FromQuery] int? maxCount,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        if (file.Length > 500 * 1024 * 1024)
            return BadRequest(new { message = "File exceeds 500 MB limit." });

        using var stream = file.OpenReadStream();
        var imported = await _puzzleService.ImportFromCsvAsync(stream, minRating, maxRating, maxCount, ct);
        return Ok(new { imported });
    }

    [HttpGet("puzzles/count")]
    public async Task<IActionResult> GetPuzzleCount()
    {
        var count = await _db.Puzzles.CountAsync();
        return Ok(new { count });
    }

    [HttpDelete("puzzles")]
    public async Task<IActionResult> ClearPuzzles()
    {
        await _db.PuzzleAttempts.ExecuteDeleteAsync();
        await _db.Puzzles.ExecuteDeleteAsync();
        return NoContent();
    }
}
