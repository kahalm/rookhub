using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Services;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/subscriptions")]
[Authorize]
public class SubscriptionController : BaseApiController
{
    private readonly AppDbContext _db;

    public SubscriptionController(AppDbContext db) => _db = db;

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
                SubscribedAt = s.SubscribedAt,
                EventDate = s.EventDate,
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
        // Race-Catch: zwischen der Prüfung oben und hier kann ein zweiter Request desselben Nutzers
        // (Doppelklick, zweiter Tab, Offline-Queue-Flush) dieselbe Zeile eingefügt haben. Der
        // Unique-Index schlägt dann zu — ohne diesen Fang wurde daraus ein 500 statt eines 409.
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            return Conflict(new { message = "Already subscribed to this tournament." });
        }

        return Ok(new TournamentSubscriptionDto
        {
            Id = sub.Id,
            CrawlerTournamentId = sub.CrawlerTournamentId,
            TournamentName = sub.TournamentName,
            SubscribedAt = sub.SubscribedAt
        });
    }

    /// <summary>
    /// Abo loesen ueber die TURNIER-Nummer statt ueber die Abo-Id.
    ///
    /// <para>Warum es das braucht: die Kurzansicht auf Karte, Liste und Kalender kennt das
    /// Turnier, nicht das Abo — der Merken-Knopf konnte deshalb nur ANlegen und tat beim zweiten
    /// Klick gar nichts. Ueber die Abo-Id zu gehen hiesse, erst die ganze Abo-Liste zu holen, um
    /// darin eine Id zu suchen, die der Server ohnehin kennt.</para>
    ///
    /// <para><b>Idempotent</b> (204 auch ohne Abo): der Knopf ist ein Umschalter, und „war schon
    /// nicht gemerkt" ist kein Fehlerfall, den ein Nutzer sehen muesste. Literal-Route VOR
    /// <c>{id:int}</c> — sonst liest der Router „by-tournament" als Id.</para>
    /// </summary>
    [HttpDelete("by-tournament/{crawlerTournamentId}")]
    public async Task<IActionResult> DeleteByTournament(string crawlerTournamentId)
    {
        var sub = await _db.TournamentSubscriptions
            .FirstOrDefaultAsync(s => s.CrawlerTournamentId == crawlerTournamentId
                                      && s.UserId == GetUserId());
        if (sub != null)
        {
            _db.TournamentSubscriptions.Remove(sub);
            await _db.SaveChangesAsync();
        }
        return NoContent();
    }

    [HttpDelete("{id:int}")]
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
