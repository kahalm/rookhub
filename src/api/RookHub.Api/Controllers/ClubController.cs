using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Club;

namespace RookHub.Api.Controllers;

/// <summary>
/// ClubHub — Kartei der Kinder und Jugendlichen des Vereins (Wunsch 2026-09-30). Zwei Rechte: die Leitung
/// (<see cref="Permissions.ClubManage"/>, Admin eingeschlossen) sieht und darf alles, ein Trainer
/// (<see cref="Permissions.ClubTrainer"/>) die Gruppen, denen er zugeteilt ist. Weil JEDE Action eines von beiden annimmt,
/// hängt hier kein <c>[HasPermission]</c> — die Rechte kommen LIVE aus dem <see cref="PermissionResolver"/> (wie dort), und
/// die Regeln samt Sichtbarkeit stehen in <see cref="ClubService"/>: ohne Recht 403, fremdes Kind/fremde Gruppe 404.
/// Ausnahme sind die drei <c>link</c>-Actions: sie gehören dem KONTO, das sich mit einem Karteiblatt verknüpft.
/// </summary>
[ApiController]
[Route("api/club")]
[Authorize]
public class ClubController : BaseApiController
{
    private readonly ClubService _club;
    private readonly PermissionResolver? _permissions;

    public ClubController(ClubService club, PermissionResolver? permissions = null)
    {
        _club = club;
        _permissions = permissions;
    }

    private async Task<ClubActor> ActorAsync()
    {
        var userId = GetUserId();
        if (User.IsInRole("Admin")) return new ClubActor(userId, true, true);
        if (_permissions != null)
        {
            var live = await _permissions.GetAsync(userId);
            return new ClubActor(userId, live.Has(Permissions.ClubManage), live.Has(Permissions.ClubTrainer));
        }
        return new ClubActor(userId,
            User.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, Permissions.ClubManage),
            User.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, Permissions.ClubTrainer));
    }

    // ---- Kinder -------------------------------------------------------------------------------

    [HttpGet("members")]
    public async Task<ActionResult<List<ClubMemberListDto>>> Members([FromQuery] int? groupId,
        [FromQuery] bool archived = false, CancellationToken ct = default) =>
        Ok(await _club.ListMembersAsync(await ActorAsync(), groupId, archived, ct));

    [HttpGet("members/{id:int}")]
    public async Task<ActionResult<ClubMemberDto>> Member(int id, CancellationToken ct) =>
        Ok(await _club.GetMemberAsync(await ActorAsync(), id, ct));

    [HttpPost("members")]
    public async Task<ActionResult<ClubMemberDto>> CreateMember([FromBody] ClubMemberInputDto input, CancellationToken ct) =>
        Ok(await _club.CreateMemberAsync(await ActorAsync(), input, ct));

    [HttpPut("members/{id:int}")]
    public async Task<ActionResult<ClubMemberDto>> UpdateMember(int id, [FromBody] ClubMemberInputDto input, CancellationToken ct) =>
        Ok(await _club.UpdateMemberAsync(await ActorAsync(), id, input, ct));

    /// <summary>Das ganze Blatt löschen (Kontakte, Notizen, Anwesenheit) — nur die Leitung.</summary>
    [HttpDelete("members/{id:int}")]
    public async Task<IActionResult> DeleteMember(int id, CancellationToken ct)
    {
        await _club.DeleteMemberAsync(await ActorAsync(), id, ct);
        return NoContent();
    }

    // ---- Bild am Blatt ------------------------------------------------------------------------

    /// <summary>Das Bild zum Blatt setzen oder ersetzen (ein Bild je Blatt; der nginx lässt 15 MB je Anfrage durch).</summary>
    [HttpPost("members/{id:int}/photo")]
    [RequestSizeLimit(ClubService.MaxPhotoUploadBytes)]
    public async Task<ActionResult<ClubMemberPhotoDto>> SetMemberPhoto(int id, IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "Kein Bild mitgeschickt." });
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return Ok(await _club.SetMemberPhotoAsync(await ActorAsync(), id, ms.ToArray(), ct));
    }

    /// <summary>Das Bild als JPEG; <c>?thumb=true</c> das Vorschaubild. Privat: kein Cache über den Browser des Betrachters
    /// hinaus. Die Oberfläche hängt die Marke des Bilds (<c>photoVersion</c>) als <c>v</c> an — hier ohne Bedeutung, sie
    /// sorgt nur dafür, dass ein ersetztes Bild eine neue Adresse hat.</summary>
    [HttpGet("members/{id:int}/photo")]
    public async Task<IActionResult> MemberPhoto(int id, [FromQuery] bool thumb = false, CancellationToken ct = default)
    {
        var bytes = await _club.GetMemberPhotoAsync(await ActorAsync(), id, thumb, ct);
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(bytes, "image/jpeg");
    }

    [HttpDelete("members/{id:int}/photo")]
    public async Task<IActionResult> DeleteMemberPhoto(int id, CancellationToken ct)
    {
        await _club.DeleteMemberPhotoAsync(await ActorAsync(), id, ct);
        return NoContent();
    }

    [HttpPost("members/{id:int}/notes")]
    public async Task<ActionResult<ClubMemberDto>> AddNote(int id, [FromBody] ClubNoteInputDto input, CancellationToken ct) =>
        Ok(await _club.AddNoteAsync(await ActorAsync(), id, input.Text, ct));

    [HttpDelete("members/{id:int}/notes/{noteId:int}")]
    public async Task<ActionResult<ClubMemberDto>> DeleteNote(int id, int noteId, CancellationToken ct) =>
        Ok(await _club.DeleteNoteAsync(await ActorAsync(), id, noteId, ct));

    /// <summary>Einmal-Code ausgeben, mit dem das Konto des Kindes sich verknüpft.</summary>
    [HttpPost("members/{id:int}/link-code")]
    public async Task<ActionResult<ClubLinkCodeDto>> CreateLinkCode(int id, CancellationToken ct) =>
        Ok(await _club.CreateLinkCodeAsync(await ActorAsync(), id, ct));

    [HttpDelete("members/{id:int}/link")]
    public async Task<ActionResult<ClubMemberDto>> Unlink(int id, CancellationToken ct) =>
        Ok(await _club.UnlinkAsync(await ActorAsync(), id, ct));

    /// <summary>Lernstand aus dem verknüpften Konto; 204 ohne Verknüpfung.</summary>
    [HttpGet("members/{id:int}/progress")]
    public async Task<ActionResult<ClubProgressDto>> Progress(int id, [FromServices] ClubProgressService progress, CancellationToken ct) =>
        await _club.LinkedAccountAsync(await ActorAsync(), id, ct) is { } account
            ? Ok(await progress.GetAsync(account.UserId, account.Username, ct))
            : NoContent();

    // ---- Gruppen ------------------------------------------------------------------------------

    [HttpGet("groups")]
    public async Task<ActionResult<List<ClubGroupListDto>>> Groups(CancellationToken ct) =>
        Ok(await _club.ListGroupsAsync(await ActorAsync(), ct));

    [HttpGet("groups/{id:int}")]
    public async Task<ActionResult<ClubGroupDto>> Group(int id, [FromQuery] int take = ClubService.DefaultSessionWindow,
        CancellationToken ct = default) =>
        Ok(await _club.GetGroupAsync(await ActorAsync(), id, take, ct));

    [HttpPost("groups")]
    public async Task<ActionResult<ClubGroupDto>> CreateGroup([FromBody] ClubGroupInputDto input, CancellationToken ct) =>
        Ok(await _club.CreateGroupAsync(await ActorAsync(), input, ct));

    [HttpPut("groups/{id:int}")]
    public async Task<ActionResult<ClubGroupDto>> UpdateGroup(int id, [FromBody] ClubGroupInputDto input, CancellationToken ct) =>
        Ok(await _club.UpdateGroupAsync(await ActorAsync(), id, input, ct));

    [HttpDelete("groups/{id:int}")]
    public async Task<IActionResult> DeleteGroup(int id, CancellationToken ct)
    {
        await _club.DeleteGroupAsync(await ActorAsync(), id, ct);
        return NoContent();
    }

    [HttpPost("groups/{id:int}/trainers")]
    public async Task<ActionResult<ClubGroupDto>> AddTrainer(int id, [FromBody] ClubTrainerInputDto input, CancellationToken ct) =>
        Ok(await _club.AddTrainerAsync(await ActorAsync(), id, input.Username, ct));

    [HttpDelete("groups/{id:int}/trainers/{userId:int}")]
    public async Task<ActionResult<ClubGroupDto>> RemoveTrainer(int id, int userId, CancellationToken ct) =>
        Ok(await _club.RemoveTrainerAsync(await ActorAsync(), id, userId, ct));

    [HttpPost("groups/{id:int}/members/{memberId:int}")]
    public async Task<ActionResult<ClubGroupDto>> AddGroupMember(int id, int memberId, CancellationToken ct) =>
        Ok(await _club.AddGroupMemberAsync(await ActorAsync(), id, memberId, ct));

    [HttpDelete("groups/{id:int}/members/{memberId:int}")]
    public async Task<ActionResult<ClubGroupDto>> RemoveGroupMember(int id, int memberId, CancellationToken ct) =>
        Ok(await _club.RemoveGroupMemberAsync(await ActorAsync(), id, memberId, ct));

    // ---- Einheiten ----------------------------------------------------------------------------

    /// <summary>Einheit des Tages anlegen oder ersetzen (je Gruppe und Tag eine).</summary>
    [HttpPost("groups/{id:int}/sessions")]
    public async Task<ActionResult<ClubSessionDetailDto>> SaveSession(int id, [FromBody] ClubSessionInputDto input, CancellationToken ct) =>
        Ok(await _club.SaveSessionAsync(await ActorAsync(), id, input, null, ct));

    /// <summary>Die Tage aller Einheiten der Gruppe (yyyy-MM-dd, älteste zuerst) — zum Blättern in der Anwesenheitsliste.</summary>
    [HttpGet("groups/{id:int}/sessions/dates")]
    public async Task<ActionResult<List<string>>> SessionDates(int id, CancellationToken ct) =>
        Ok(await _club.ListSessionDatesAsync(await ActorAsync(), id, ct));

    /// <summary>Die Einheit eines Tages (yyyy-MM-dd); 204, wenn es an dem Tag keine gibt.</summary>
    [HttpGet("groups/{id:int}/sessions/by-date/{date}")]
    public async Task<ActionResult<ClubSessionDetailDto>> SessionByDate(int id, string date, CancellationToken ct) =>
        await _club.GetSessionByDateAsync(await ActorAsync(), id, date, ct) is { } session ? Ok(session) : NoContent();

    [HttpPut("groups/{id:int}/sessions/{sessionId:int}")]
    public async Task<ActionResult<ClubSessionDetailDto>> UpdateSession(int id, int sessionId, [FromBody] ClubSessionInputDto input,
        CancellationToken ct) =>
        Ok(await _club.SaveSessionAsync(await ActorAsync(), id, input, sessionId, ct));

    [HttpGet("sessions/{sessionId:int}")]
    public async Task<ActionResult<ClubSessionDetailDto>> Session(int sessionId, CancellationToken ct) =>
        Ok(await _club.GetSessionAsync(await ActorAsync(), sessionId, ct));

    [HttpDelete("sessions/{sessionId:int}")]
    public async Task<IActionResult> DeleteSession(int sessionId, CancellationToken ct)
    {
        await _club.DeleteSessionAsync(await ActorAsync(), sessionId, ct);
        return NoContent();
    }

    // ---- Fotos zur Einheit --------------------------------------------------------------------

    /// <summary>Ein Foto hochladen (ein Bild je Anfrage — der nginx lässt 15 MB je Anfrage durch, Handyfotos haben 3–6).</summary>
    [HttpPost("sessions/{sessionId:int}/photos")]
    [RequestSizeLimit(ClubService.MaxPhotoUploadBytes)]
    public async Task<ActionResult<ClubPhotoDto>> AddPhoto(int sessionId, IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "Kein Bild mitgeschickt." });
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return Ok(await _club.AddPhotoAsync(await ActorAsync(), sessionId, ms.ToArray(), ct));
    }

    /// <summary>Das Bild als JPEG; <c>?thumb=1</c> das Vorschaubild. Privat: kein Cache über den Browser des Betrachters hinaus.</summary>
    [HttpGet("sessions/{sessionId:int}/photos/{photoId:int}")]
    public async Task<IActionResult> Photo(int sessionId, int photoId, [FromQuery] bool thumb = false, CancellationToken ct = default)
    {
        var bytes = await _club.GetPhotoAsync(await ActorAsync(), sessionId, photoId, thumb, ct);
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(bytes, "image/jpeg");
    }

    [HttpDelete("sessions/{sessionId:int}/photos/{photoId:int}")]
    public async Task<IActionResult> DeletePhoto(int sessionId, int photoId, CancellationToken ct)
    {
        await _club.DeletePhotoAsync(await ActorAsync(), sessionId, photoId, ct);
        return NoContent();
    }

    // ---- Verknüpfung, vom KONTO aus (kein Club-Recht nötig) ------------------------------------

    [HttpGet("link")]
    public async Task<ActionResult<ClubLinkStateDto>> LinkState(CancellationToken ct) =>
        Ok(await _club.LinkStateAsync(GetUserId(), ct));

    /// <summary>Code einlösen. Am „auth"-Limiter (10/min je IP) — der Code ist kurz genug zum Abtippen, also nicht zum Raten.</summary>
    [HttpPost("link")]
    [EnableRateLimiting("auth")]
    [DenyWhileImpersonating]
    public async Task<ActionResult<ClubLinkStateDto>> Redeem([FromBody] ClubLinkRedeemDto input, CancellationToken ct) =>
        Ok(await _club.RedeemAsync(GetUserId(), input.Code, ct));

    [HttpDelete("link")]
    [DenyWhileImpersonating]
    public async Task<IActionResult> SelfUnlink(CancellationToken ct)
    {
        await _club.SelfUnlinkAsync(GetUserId(), ct);
        return NoContent();
    }
}
