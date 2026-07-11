using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/book-puzzles")]
[Authorize]   // secure by default; öffentliche Endpoints sind explizit mit [AllowAnonymous] markiert
public class BookPuzzleController : BaseApiController
{
    private static readonly Regex SessionIdPattern = new(ValidationConstants.SessionIdPattern, RegexOptions.Compiled);

    private readonly BookPuzzleService _service;
    private readonly DailyLeaderboardService _leaderboard;
    private readonly HintGenerationService _hints;
    private readonly IBackgroundTaskQueue _bgQueue;
    private readonly AppDbContext _db;
    private readonly ILogger<BookPuzzleController> _logger;

    // logger optional, damit bestehende Test-Konstruktionen ohne Änderung kompilieren.
    public BookPuzzleController(BookPuzzleService service, DailyLeaderboardService leaderboard,
        HintGenerationService hints, IBackgroundTaskQueue bgQueue, AppDbContext db,
        ILogger<BookPuzzleController>? logger = null)
    {
        _service = service;
        _leaderboard = leaderboard;
        _hints = hints;
        _bgQueue = bgQueue;
        _db = db;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BookPuzzleController>.Instance;
    }

    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var dto = await _service.GetByIdAsync(id);
        return dto == null ? NotFound(new { message = "Book puzzle not found." }) : Ok(dto);
    }

    [AllowAnonymous]
    [HttpGet("{id:int}/next")]
    public async Task<IActionResult> GetNextInBook(int id)
    {
        try { return Ok(await _service.GetNextInBookAsync(id)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpGet("{id:int}/random")]
    public async Task<IActionResult> GetRandomInBook(int id)
    {
        try { return Ok(await _service.GetRandomInBookAsync(id)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [Authorize]
    [HttpPost("{id:int}/attempt")]
    public async Task<IActionResult> RecordAttempt(int id, [FromBody] RecordBookAttemptDto dto)
    {
        try { await _service.RecordAttemptAsync(id, GetUserId(), dto); return Ok(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpPost("{id:int}/attempt/anonymous")]
    public async Task<IActionResult> RecordAnonymousAttempt(int id, [FromBody] RecordAnonymousBookAttemptDto dto)
    {
        try { await _service.RecordAnonymousAttemptAsync(id, dto); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpGet("{id:int}/results")]
    public async Task<ActionResult<BookPuzzleResultsDto>> GetResults(int id, [FromQuery] string? since = null)
        => Ok(await _service.GetResultsAsync(id, since));

    /// <summary>„Track solves" eines per Link geteilten Puzzles: erfasst den Erstversuch des Besuchers
    /// (eingeloggt via Token, sonst via anonymer SessionId) und liefert die aktuellen Zähler.
    /// <c>solved=false</c> deckt Fehlzug/Aufgeben/Reset ab. Pro Besucher zählt nur der erste Versuch.</summary>
    [AllowAnonymous]
    [HttpPost("{id:int}/track")]
    public async Task<ActionResult<SharedPuzzleCountsDto>> Track(int id, [FromBody] RecordSharedAttemptDto dto)
    {
        var userId = GetUserIdOrNull();
        string identityKey;
        if (userId is int uid)
        {
            identityKey = $"u:{uid}";
        }
        else
        {
            if (!SessionIdPattern.IsMatch(dto.SessionId ?? ""))
                return BadRequest(new { message = "Invalid sessionId." });
            identityKey = $"s:{dto.SessionId}";
        }
        try { return Ok(await _service.RecordSharedAttemptAsync(id, identityKey, dto.Solved, dto.HintsUsed)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Aktuelle „Track solves"-Zähler (solved/failed, Erstversuch je Besucher) eines geteilten Puzzles.</summary>
    [AllowAnonymous]
    [HttpGet("{id:int}/track-counts")]
    public async Task<ActionResult<SharedPuzzleCountsDto>> TrackCounts(int id)
        => Ok(await _service.GetSharedCountsAsync(id));

    /// <summary>
    /// Monats-Wertung des Tagespuzzles. <paramref name="month"/> als <c>yyyy-MM</c> (Default:
    /// laufender UTC-Monat). Punkte = 10 je Erstversuch-Lösung + Tages-Rang-Bonus (5/3/1).
    /// Literal-Route — steht bewusst vor <c>daily/{date}</c>.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("daily/leaderboard")]
    public async Task<IActionResult> GetDailyLeaderboard([FromQuery] string? month = null)
    {
        int year, mon;
        if (string.IsNullOrWhiteSpace(month))
        {
            var now = DateTime.UtcNow;
            year = now.Year;
            mon = now.Month;
        }
        else if (!TryParseMonth(month, out year, out mon))
        {
            return BadRequest(new { message = "month must be yyyy-MM." });
        }

        try { return Ok(await _leaderboard.GetDailyLadderAsync(year, mon)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// All-time Hall of Fame des Tagespuzzles (meiste Lösungen, meiste 🥇, schnellste Lösung).
    /// <paramref name="top"/> begrenzt die Listenlänge (1–25). Literal-Route vor <c>daily/{date}</c>.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("daily/hall-of-fame")]
    public async Task<IActionResult> GetDailyHallOfFame([FromQuery] int top = 5)
        => Ok(await _leaderboard.GetDailyHallOfFameAsync(Math.Clamp(top, 1, 25)));

    /// <summary>Parst <c>yyyy-MM</c> (Jahr 2000–9999, Monat 1–12).</summary>
    private static bool TryParseMonth(string s, out int year, out int month)
    {
        year = 0;
        month = 0;
        var parts = s.Split('-');
        return parts.Length == 2
            && int.TryParse(parts[0], out year) && year is >= 2000 and <= 9999
            && int.TryParse(parts[1], out month) && month is >= 1 and <= 12;
    }

    [Authorize]
    [HttpPost("claim-session")]
    public async Task<IActionResult> ClaimSession([FromBody] ClaimBookSessionDto dto)
    {
        var transferred = await _service.ClaimSessionAsync(GetUserId(), dto.SessionId);
        return Ok(new { transferred });
    }

    [AllowAnonymous]
    [HttpGet("random")]
    public async Task<IActionResult> GetRandom([FromQuery] string pool = "random", [FromQuery] string? exclude = null, [FromQuery] int? bookId = null)
    {
        try { return Ok(await _service.GetRandomAsync(pool, exclude, bookId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>
    /// Tagespuzzle fuer ein bestimmtes UTC-Datum. <paramref name="date"/> als <c>yyyyMMdd</c>
    /// oder das Literal <c>today</c>. Zukuenftige Daten geben 400 zurueck; fuer heute und
    /// vergangene Tage wird ggf. on-demand eine Zuordnung angelegt und persistiert.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("daily/{date}")]
    public async Task<IActionResult> GetDaily(string date)
    {
        DateOnly parsed;
        if (string.Equals(date, "today", StringComparison.OrdinalIgnoreCase))
        {
            parsed = DateOnly.FromDateTime(DateTime.UtcNow);
        }
        else if (!DateOnly.TryParseExact(date, "yyyyMMdd", null,
                     System.Globalization.DateTimeStyles.None, out parsed))
        {
            return BadRequest(new { message = "date must be yyyyMMdd or 'today'." });
        }

        try { return Ok(await _service.GetOrAssignDailyAsync(parsed)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpGet("by-line-id")]
    public async Task<IActionResult> GetByLineId([FromQuery] string lineId)
    {
        try { return Ok(new { id = await _service.GetIdByLineIdAsync(lineId) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpGet("books")]
    public async Task<IActionResult> GetBooks() => Ok(await _service.GetBooksAsync());

    [HttpPost("/api/admin/book-puzzles/import")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromBody] List<BookPuzzleImportDto> puzzles)
    {
        try
        {
            var (imported, skipped) = await _service.ImportAsync(puzzles);
            return Ok(new { imported, skipped });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// Admin: Tagespuzzle eines UTC-Datums neu generieren. Datum/Link bleiben gleich, nur das
    /// dahinterliegende Puzzle wechselt; das bisherige wird ausgemustert (nie wieder Daily/Random/Blind).
    /// <paramref name="date"/> als <c>yyyyMMdd</c> oder das Literal <c>today</c>.
    /// </summary>
    [HttpPost("/api/admin/book-puzzles/daily/{date}/regenerate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RegenerateDaily(string date)
    {
        DateOnly parsed;
        if (string.Equals(date, "today", StringComparison.OrdinalIgnoreCase))
        {
            parsed = DateOnly.FromDateTime(DateTime.UtcNow);
        }
        else if (!DateOnly.TryParseExact(date, "yyyyMMdd", null,
                     System.Globalization.DateTimeStyles.None, out parsed))
        {
            return BadRequest(new { message = "date must be yyyyMMdd or 'today'." });
        }

        try { return Ok(await _service.RegenerateDailyAsync(parsed)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Admin: Tipps eines einzelnen Buch-Puzzles (neu) generieren — synchron, fürs Testen.
    /// 400 wenn kein API-Key konfiguriert ist; 404 wenn das Puzzle fehlt; sonst die generierten Tipps.</summary>
    [HttpPost("/api/admin/book-puzzles/{id:int}/regenerate-hints")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RegenerateHints(int id)
    {
        if (!_hints.IsAvailable) return BadRequest(new { message = "Anthropic API key not configured." });
        var ok = await _hints.GenerateForPuzzleAsync(id, force: true, HttpContext.RequestAborted);
        if (!ok) return NotFound(new { message = "Puzzle not found or no hints generated." });
        var dto = await _service.GetByIdAsync(id);
        return Ok(new { hints = dto?.Hints });
    }

    /// <summary>Markiert die Tipps eines Buch-Puzzles als „dumm/schlecht" (oder hebt das auf) —
    /// Review-Flag fürs spätere gezielte Neu-Generieren. Darf jeder eingeloggte User setzen —
    /// pro User gedrosselt („user-flag") und mit Audit-Log (das Flag ist global, ein Missbrauch
    /// muss dem Verursacher zuordenbar sein). 404 wenn das Puzzle fehlt.</summary>
    [HttpPost("{id:int}/flag-hints")]
    [Authorize]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("user-flag")]
    public async Task<IActionResult> FlagHints(int id, [FromBody] FlagHintsDto body)
    {
        var bp = await _db.BookPuzzles.FindAsync(id);
        if (bp == null) return NotFound(new { message = "Puzzle not found." });
        bp.HintsFlagged = body.Flagged;
        await _db.SaveChangesAsync();
        _logger.LogInformation("HintsFlagged: BookPuzzle {PuzzleId} → {Flagged} durch User {UserId}",
            bp.Id, body.Flagged, GetUserId());
        return Ok(new { id = bp.Id, hintsFlagged = bp.HintsFlagged });
    }

    /// <summary>Admin: Tipps für ein ganzes Buch im Hintergrund (neu) generieren. <paramref name="force"/>
    /// regeneriert auch bereits vorhandene; sonst nur fehlende/veraltete.</summary>
    [HttpPost("/api/admin/books/{bookId}/generate-hints")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GenerateBookHints(int bookId, [FromQuery] bool force = false)
    {
        if (!_hints.IsAvailable) return BadRequest(new { message = "Anthropic API key not configured." });
        var ids = await _db.BookPuzzles.Where(bp => bp.BookId == bookId).Select(bp => bp.Id).ToListAsync();
        if (ids.Count == 0) return NotFound(new { message = "No puzzles for this book." });
        await _bgQueue.EnqueueAsync(async (sp, ct) =>
            await sp.GetRequiredService<HintGenerationService>().GenerateForPuzzlesAsync(ids, force, ct));
        return Ok(new { queued = ids.Count });
    }
}
