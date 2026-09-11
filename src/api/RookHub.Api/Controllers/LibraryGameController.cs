using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Der Rohbestand kommentierter Meisterpartien als Nachschlagewerk: suchen und einzelne Partien
/// zum Rechnen anfordern. Nur angemeldet — angefordert wird auf Rechenzeit, die jemandem gehoert.
/// </summary>
[ApiController]
[Route("api/library-games")]
[Authorize]
public class LibraryGameController : BaseApiController
{
    private readonly LibraryGameService _service;

    public LibraryGameController(LibraryGameService service) => _service = service;

    /// <summary>Eine Seite der Bestandssuche, nach Eignungsnote sortiert. OHNE die Zuege.</summary>
    [HttpGet]
    public async Task<ActionResult<LibraryGamePageDto>> Search([FromQuery] string? q, [FromQuery] string? language,
        [FromQuery] int? minCommentedPlies, [FromQuery] int page = 1,
        [FromQuery] int pageSize = LibraryGameService.DefaultPageSize, CancellationToken ct = default, [FromQuery] string? line = null)
        => Ok(await _service.SearchAsync(GetUserId(), q, language, minCommentedPlies, page, pageSize, ct, line));

    /// <summary>
    /// Diese Partie rechnen lassen. Antwortet mit der Analyse — der neuen oder, wenn sie schon
    /// spielbar ist, der vorhandenen (dann <c>alreadyPlayable</c>).
    ///
    /// <para>400 mit einem <c>reason</c> aus <see cref="LibraryRequestReason"/>; die Seite
    /// formuliert daraus den Satz in der Sprache des Nutzers.</para>
    /// </summary>
    [HttpPost("{id:int}/request")]
    public async Task<ActionResult<object>> Request(int id, CancellationToken ct)
    {
        var result = await _service.RequestAsync(GetUserId(), id, ct);
        if (result.Analysis is null)
            return result.Reason == LibraryRequestReason.NotFound
                ? NotFound(new { reason = result.Reason, message = "Game not found." })
                : BadRequest(new { reason = result.Reason, message = "Game could not be requested." });

        return Ok(new { analysis = result.Analysis, alreadyPlayable = result.AlreadyPlayable });
    }
}
