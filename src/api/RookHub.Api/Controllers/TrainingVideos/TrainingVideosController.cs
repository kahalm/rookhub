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
    private readonly AppDbContext _db;

    public TrainingVideosController(TrainingVideoService service, TrainingVideoAccessTokens tokens, AppDbContext db)
    {
        _service = service;
        _tokens = tokens;
        _db = db;
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
}
