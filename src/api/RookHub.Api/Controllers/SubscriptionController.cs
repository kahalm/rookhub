using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/subscriptions")]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly AppDbContext _db;

    public SubscriptionController(AppDbContext db) => _db = db;

    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<List<TournamentSubscriptionDto>>> GetAll()
    {
        var subs = await _db.TournamentSubscriptions
            .Where(s => s.UserId == GetUserId())
            .Select(s => new TournamentSubscriptionDto
            {
                Id = s.Id,
                CrawlerTournamentId = s.CrawlerTournamentId,
                TournamentName = s.TournamentName,
                SubscribedAt = s.SubscribedAt
            })
            .ToListAsync();

        return Ok(subs);
    }

    [HttpPost]
    public async Task<ActionResult<TournamentSubscriptionDto>> Create([FromBody] CreateSubscriptionDto dto)
    {
        var userId = GetUserId();
        var exists = await _db.TournamentSubscriptions
            .AnyAsync(s => s.UserId == userId && s.CrawlerTournamentId == dto.CrawlerTournamentId);

        if (exists)
            return Conflict(new { message = "Already subscribed to this tournament." });

        var sub = new TournamentSubscription
        {
            UserId = userId,
            CrawlerTournamentId = dto.CrawlerTournamentId,
            TournamentName = dto.TournamentName
        };

        _db.TournamentSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        return Ok(new TournamentSubscriptionDto
        {
            Id = sub.Id,
            CrawlerTournamentId = sub.CrawlerTournamentId,
            TournamentName = sub.TournamentName,
            SubscribedAt = sub.SubscribedAt
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var sub = await _db.TournamentSubscriptions
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == GetUserId());

        if (sub == null)
            return NotFound(new { message = "Subscription not found." });

        _db.TournamentSubscriptions.Remove(sub);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
