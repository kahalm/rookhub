using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.TrainingVideos;
using RookHub.Api.Services;
using RookHub.Api.Services.TrainingVideos;

namespace RookHub.Api.Controllers.TrainingVideos;

/// <summary>
/// Trainingsvideos (privat): aufgezeichnete Trainingsstunden des eigenen Kontos — Liste, Stundenseite, Stellungs- und
/// Volltextsuche, Trainerkommentare an einer eigenen Partie und das Video. Nur das Konto der Stunde und Admins; nichts
/// davon ist öffentlich. Das Video ist <c>[AllowAnonymous]</c> NUR, weil <c>&lt;video src&gt;</c> keinen Header schickt —
/// es braucht dann den Ausweis <c>?t=</c> aus <c>GET access-token</c>.
/// </summary>
[ApiController]
[Route("api/training-videos")]
[Authorize]
public class TrainingVideosController : BaseApiController
{
    private readonly TrainingVideoService _service;
    private readonly TrainingVideoAccessTokens _tokens;
    private readonly TrainingNotebookService _notebook;
    private readonly AppDbContext _db;
    private readonly TrainingLessonShares _shares;

    public TrainingVideosController(TrainingVideoService service, TrainingVideoAccessTokens tokens, TrainingNotebookService notebook,
        AppDbContext db, TrainingLessonShares shares)
    {
        _service = service;
        _tokens = tokens;
        _notebook = notebook;
        _db = db;
        _shares = shares;
    }

    [HttpGet]
    public async Task<ActionResult<List<TrainingLessonListItemDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(GetUserId(), IsAdmin, ct));

    /// <summary>Ausweis für das Video (<c>?t=</c>), 12 h gültig.</summary>
    [HttpGet("access-token")]
    public ActionResult<TrainingAccessTokenDto> AccessToken()
    {
        var (token, expires) = _tokens.Issue(GetUserId());
        Response.Headers.CacheControl = "no-store";
        return Ok(new TrainingAccessTokenDto(token, expires));
    }

    /// <summary>„Im Training besprochen?“ — <c>?fen=</c> (volle FEN oder nur das Figurenfeld).</summary>
    [HttpGet("search/position")]
    public async Task<ActionResult<TrainingSearchResultDto>> SearchPosition([FromQuery] string? fen, CancellationToken ct)
        => Ok(await _service.SearchPositionAsync(GetUserId(), IsAdmin, fen, ct));

    /// <summary>Volltext über Trainerkommentare, Zitate und Notizen — <c>?q=</c>.</summary>
    [HttpGet("search/text")]
    public async Task<ActionResult<TrainingSearchResultDto>> SearchText([FromQuery] string? q, CancellationToken ct)
        => Ok(await _service.SearchTextAsync(GetUserId(), IsAdmin, q, ct));

    /// <summary>Trainerkommentare an einer eigenen RookHub-Partie, je Stellung zugeordnet (leer, wenn keine Stunde sie bespricht).</summary>
    [HttpGet("games/{gameId:int}/comments")]
    public async Task<ActionResult<TrainingGameCommentsDto>> GameComments(int gameId, CancellationToken ct)
        => Ok(await _service.GameCommentsAsync(GetUserId(), IsAdmin, gameId, ct));

    /// <summary>Trainer-Notizbuch: alle Notizen, wiederkehrende Themen, Eröffnungsempfehlungen mit Repertoire-Abgleich.</summary>
    [HttpGet("notebook")]
    public async Task<ActionResult<TrainingNotebookDto>> Notebook(CancellationToken ct)
        => Ok(await _notebook.GetAsync(GetUserId(), IsAdmin, ct));

    /// <summary>Stundenübergreifender Fragen-Satz (alle Segmente mit Frage); <c>?onlyWrong=true</c> = nur zuletzt falsche.</summary>
    [HttpGet("questions")]
    public async Task<ActionResult<List<TrainingDrillItemDto>>> Questions([FromQuery] bool onlyWrong = false, CancellationToken ct = default)
        => Ok(await _service.QuestionSetAsync(GetUserId(), IsAdmin, onlyWrong, ct));

    /// <summary>Eigenen Stand an einer Stellung speichern (Nachspielen / Fragen-Satz).</summary>
    [HttpPut("{id:int}/segments/{segmentId:int}/progress")]
    public async Task<ActionResult<TrainingProgressDto>> SetProgress(int id, int segmentId, [FromBody] TrainingProgressUpdateDto dto,
        CancellationToken ct)
        => Ok(await _service.SetProgressAsync(GetUserId(), IsAdmin, id, segmentId, dto, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TrainingLessonDto>> Get(int id, CancellationToken ct)
        => Ok(await _service.GetAsync(GetUserId(), IsAdmin, id, ct));

    /// <summary>Das Video (mp4) mit Bereichsanfragen. Bearer ODER <c>?t=</c>; die Stunde muss dem Konto gehören (Admin: jede).</summary>
    [HttpGet("{id:int}/video")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPartitions.EbookFilePolicy)]
    public async Task<IActionResult> Video(int id, [FromQuery(Name = "t")] string? token, CancellationToken ct)
    {
        int userId;
        bool isAdmin;
        if (User?.Identity?.IsAuthenticated == true)
        {
            userId = GetUserId();
            isAdmin = IsAdmin;
        }
        else
        {
            if (!_tokens.TryRead(token, out userId)) return Unauthorized();
            var user = await _db.AppUsers.Where(u => u.Id == userId && u.DeletedAt == null).Select(u => new { u.IsAdmin }).FirstOrDefaultAsync(ct);
            if (user == null) return Unauthorized();
            isAdmin = user.IsAdmin;
        }
        var path = await _service.ResolveVideoAsync(userId, isAdmin, id, ct);
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        return PhysicalFile(path, "video/mp4", enableRangeProcessing: true);
    }

    // ---------------------------------------------------------------- Teilen-Links (Wunsch 2026-11-17)

    /// <summary>Aktive Teilen-Links des EIGENEN Eintrags (neueste zuerst); 404 fremd/unbekannt.</summary>
    [HttpGet("{id:int}/shares")]
    public async Task<ActionResult<List<TrainingShareDto>>> Shares(int id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return await _shares.ListAsync(GetUserId(), id, ct) is { } list ? Ok(list) : NotFound();
    }

    /// <summary>Neuer Teilen-Link <c>{ days: 1|7|30 }</c> (Vorgabe 7) → der Link; 404 fremd/unbekannt, 400 andere Frist oder
    /// schon zu viele aktive.</summary>
    [HttpPost("{id:int}/shares")]
    public async Task<ActionResult<TrainingShareDto>> CreateShare(int id, [FromBody] TrainingShareCreateDto? dto, CancellationToken ct)
        => await _shares.CreateAsync(GetUserId(), id, dto?.Days, ct) is { } share ? Ok(share) : NotFound();

    /// <summary>Teilen-Link widerrufen; danach antwortet er mit 410. 404 fremd/unbekannt/schon widerrufen.</summary>
    [HttpDelete("{id:int}/shares/{shareId:int}")]
    public async Task<IActionResult> RevokeShare(int id, int shareId, CancellationToken ct)
        => await _shares.RevokeAsync(GetUserId(), id, shareId, ct) ? NoContent() : NotFound();

    /// <summary>Gast über den Teilen-Link <c>/tl/{token}</c>, ohne Anmeldung, nur lesend: der Eintrag ohne Notizen,
    /// Übungsstand, andere Teile, RookHub-Partien und Besitzernamen (<see cref="TrainingVideoService.GetForGuestAsync"/>).
    /// 404 unbekannter Link, 410 abgelaufen oder widerrufen — beide ohne weitere Angaben. Kein eigener Rate-Limiter: der
    /// globale je IP wie bei den übrigen <c>shared/{token}</c>-Endpunkten.</summary>
    [HttpGet("shared/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<TrainingLessonDto>> Shared(string token, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var (state, lessonId) = await _shares.ResolveAsync(token, ct);
        if (state == TrainingShareState.Gone) return StatusCode(StatusCodes.Status410Gone, new { message = "Link abgelaufen.", code = "share_expired" });
        if (state != TrainingShareState.Valid) return NotFound();
        return await _service.GetForGuestAsync(lessonId, ct) is { } dto ? Ok(dto) : NotFound();
    }

    /// <summary>Das Video hinter dem Teilen-Link, mit Bereichsanfragen. JEDE Anfrage prüft das Token neu (widerrufen wirkt
    /// sofort, auch mitten im Abspielen); der Pfad kommt nur aus der Zeile des Eintrags. Rate-Limiter wie das Video oben.</summary>
    [HttpGet("shared/{token}/video")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPartitions.EbookFilePolicy)]
    public async Task<IActionResult> SharedVideo(string token, CancellationToken ct)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var (state, lessonId) = await _shares.ResolveAsync(token, ct);
        if (state == TrainingShareState.Gone) return StatusCode(StatusCodes.Status410Gone, new { message = "Link abgelaufen.", code = "share_expired" });
        if (state != TrainingShareState.Valid) return NotFound();
        var path = await _service.ResolveVideoForGuestAsync(lessonId, ct);
        if (path == null) return NotFound();
        // Kurz und nur im Browser des Gasts: nach dem Widerruf soll nicht lange ein Zwischenspeicher weiter ausliefern.
        Response.Headers.CacheControl = "private, max-age=300";
        return PhysicalFile(path, "video/mp4", enableRangeProcessing: true);
    }
}
