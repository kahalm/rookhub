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
    private readonly CrawlerProxyService _crawler;

    public SubscriptionController(AppDbContext db, CrawlerProxyService crawler)
    {
        _db = db;
        _crawler = crawler;
    }

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

    /// <summary>
    /// Abo anlegen — immer unter der chess-results-NUMMER des Turniers.
    ///
    /// <para>Die Turnierseite schickt ihren Routenwert, und der ist die Crawler-DB-Id; Kalender und
    /// Auto-Abo schicken die Nummer. Unter der DB-Id gespeichert crawlte der naechtliche Refresh ein
    /// fremdes Turnier, der Kalender zeigte das Turnier als ungemerkt, ein Klick dort legte ein
    /// zweites Abo an, und die Termin-Meldungen des Verzeichnisses (Abgleich ueber die Nummer)
    /// erreichten den Abonnenten nie (Codereview 2026-09-29, A5-001). Deshalb loest der Crawler die
    /// Kennung auf; kennt er sie nicht (noch nicht geholt) oder ist er nicht erreichbar, bleibt es bei
    /// der mitgebrachten Kennung. Ein Alt-Abo unter der DB-Id wird dabei auf die Nummer umgeschluesselt
    /// statt ein zweites anzulegen.</para>
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TournamentSubscriptionDto>> Create([FromBody] CreateSubscriptionDto dto)
    {
        var userId = GetUserId();
        var (key, allKeys) = await TournamentKeysAsync(dto.CrawlerTournamentId);
        var existing = await _db.TournamentSubscriptions
            .Where(s => s.UserId == userId && allKeys.Contains(s.CrawlerTournamentId))
            .ToListAsync();

        if (existing.Any(s => s.CrawlerTournamentId == key))
            return Conflict(new { message = "Already subscribed to this tournament." });

        var sub = existing.FirstOrDefault();
        if (sub is not null)
            sub.CrawlerTournamentId = key;
        else
        {
            sub = new TournamentSubscription
            {
                UserId = userId,
                CrawlerTournamentId = key,
                TournamentName = dto.TournamentName
            };
            _db.TournamentSubscriptions.Add(sub);
        }

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
    /// Die Kennung, unter der das Abo steht (chess-results-Nummer, sonst die mitgebrachte), und alle
    /// Kennungen desselben Turniers (dazu die DB-Id) fuer den Abgleich mit vorhandenen Abos.
    /// </summary>
    private async Task<(string Key, List<string> AllKeys)> TournamentKeysAsync(string tournamentId)
    {
        var ct = HttpContext?.RequestAborted ?? default;
        try
        {
            var t = await new CrawlQueueClient(_crawler).ResolveAsync(tournamentId, ct);
            if (t is not null)
            {
                var all = new List<string> { t.ChessResultsId, tournamentId };
                if (t.DbId > 0) all.Add(t.DbId.ToString());
                return (t.ChessResultsId, all.Distinct().ToList());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Crawler nicht erreichbar: das Abo trotzdem anlegen — der Refresh loest die Kennung
            // vor jedem Crawl ohnehin noch einmal auf.
        }
        return (tournamentId, [tournamentId]);
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
