using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Kurs-Uebersetzung anfordern, ansehen, zurueckziehen; Quellsprache korrigieren (Plan „Kurs-Kommentare mehrsprachig",
/// Abschnitt 5). Die Regeln stehen in <see cref="CourseTranslationJobService"/>; hier nur die Abbildung auf HTTP.
/// Gruende gehen als <c>reason</c> hinaus — den Satz formuliert die Seite.
/// </summary>
[ApiController]
[Route("api/courses/{bookId:int}")]
[Authorize]
public class CourseTranslationController : BaseApiController
{
    private readonly CourseTranslationJobService _jobs;

    public CourseTranslationController(CourseTranslationJobService jobs) => _jobs = jobs;

    /// <summary>Uebersetzungen des Kurses: Quellsprache, vorhandene Sprachen, offene Auftraege (mit Platz), Sperrzeit,
    /// eigener offener Auftrag. Lesbar wie der Kurs — ohne Anmeldung nur ein oeffentlicher.</summary>
    [AllowAnonymous]
    [HttpGet("translations")]
    public async Task<ActionResult<CourseTranslationsDto>> GetTranslations(int bookId, CancellationToken ct)
    {
        var userId = GetUserIdOrNull();
        var dto = await _jobs.GetOverviewAsync(bookId, userId, userId is not null && IsAdmin, ct);
        return dto is null ? NotFound(new { message = "Course not found." }) : Ok(dto);
    }

    /// <summary>Uebersetzung anfordern <c>{ language }</c>: 202 mit dem neuen Auftrag, 200 mit einem schon offenen fuer
    /// (Kurs, Sprache). 400 <c>unsupported-language</c>/<c>same-language</c>/<c>nothing-to-translate</c>, 409
    /// <c>user-limit</c> (mit dem offenen Auftrag), 503 <c>not-configured</c>, 404 ohne Kurs-Zugang. Waehrend der
    /// Sperrzeit wird der Auftrag angenommen und wartet.</summary>
    [HttpPost("translations")]
    public async Task<IActionResult> RequestTranslation(int bookId, [FromBody] RequestCourseTranslationDto dto,
        CancellationToken ct)
    {
        var result = await _jobs.RequestAsync(GetUserId(), IsAdmin, bookId, dto.Language, ct);
        return result.Status switch
        {
            CourseTranslationRequestStatus.Created => Accepted(result.Job),
            CourseTranslationRequestStatus.Existing => Ok(result.Job),
            CourseTranslationRequestStatus.NotFound => NotFound(new { message = "Course not found." }),
            CourseTranslationRequestStatus.UserLimit => Conflict(new
            {
                reason = result.Reason,
                message = "You already have an open translation request.",
                openJob = result.OpenJob,
            }),
            CourseTranslationRequestStatus.NotConfigured => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { reason = result.Reason, message = "No text model is configured." }),
            _ => BadRequest(new { reason = result.Reason, message = result.Reason }),
        };
    }

    /// <summary>Auftrag zurueckziehen: den eigenen wartenden (Admin: jeden offenen, ein laufender wird abgebrochen).
    /// 204; 404 unbekannt/kein Zugang; 403 fremder Auftrag; 409 <c>not-waiting</c>.</summary>
    [HttpDelete("translations/{jobId:int}")]
    public async Task<IActionResult> WithdrawTranslation(int bookId, int jobId, CancellationToken ct)
        => await _jobs.WithdrawAsync(GetUserId(), IsAdmin, bookId, jobId, ct) switch
        {
            CourseTranslationWithdrawStatus.Withdrawn => NoContent(),
            CourseTranslationWithdrawStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Only your own request can be withdrawn." }),
            CourseTranslationWithdrawStatus.NotWaiting => Conflict(new
            {
                reason = "not-waiting", message = "Only a waiting request can be withdrawn.",
            }),
            _ => NotFound(new { message = "Translation request not found." }),
        };

    /// <summary>Quellsprache der Kommentare korrigieren <c>{ language }</c> (Besitzer oder Admin; <c>und</c> = nicht
    /// bestimmbar). Macht vorhandene Uebersetzungen nicht ungueltig.</summary>
    [HttpPut("comment-language")]
    public async Task<IActionResult> SetCommentLanguage(int bookId, [FromBody] SetCourseCommentLanguageDto dto,
        CancellationToken ct)
        => await _jobs.SetCommentLanguageAsync(GetUserId(), IsAdmin, bookId, dto.Language, ct) switch
        {
            CourseCommentLanguageStatus.Set => Ok(new
            {
                sourceLanguage = CourseCommentLocalizer.NormalizeLanguage(dto.Language),
            }),
            CourseCommentLanguageStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Only the owner or an admin can change the course language." }),
            CourseCommentLanguageStatus.Invalid => BadRequest(new
            {
                reason = "invalid-language", message = "Not a language code.",
            }),
            _ => NotFound(new { message = "Course not found." }),
        };
}

/// <summary>Admin: Warteschlange der Kurs-Uebersetzungen + juengste erledigte Auftraege (nur Endpunkt, keine Oberflaeche).</summary>
[ApiController]
[Route("api/admin/course-translations")]
[HasPermission(Permissions.BooksManage)]
public class AdminCourseTranslationController : BaseApiController
{
    private readonly CourseTranslationJobService _jobs;

    public AdminCourseTranslationController(CourseTranslationJobService jobs) => _jobs = jobs;

    [HttpGet]
    public async Task<ActionResult<AdminCourseTranslationsDto>> Get(CancellationToken ct)
        => Ok(await _jobs.GetAdminOverviewAsync(ct));
}
