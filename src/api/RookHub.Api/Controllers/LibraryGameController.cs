using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Der Rohbestand kommentierter Meisterpartien als Nachschlagewerk: suchen und einzelne Partien
/// zum Rechnen anfordern.
///
/// <para><b>Suchen darf jeder, anfordern nur angemeldet</b> (seit 0.475.5). Die Suche liefert
/// Kopfdaten — Spieler, Turnier, Jahr, Kommentator, Kommentardichte — und ausdruecklich KEINE
/// Zuege und keine Anmerkungen; das ist derselbe Zuschnitt, den der Eroeffnungsbaum
/// (<see cref="GuessTreeController"/>) ohnehin anonym ausliefert, und ohne ihn fuehrte der
/// Stellungsfilter fuer einen anonymen Besucher in eine leere Liste. Das ANFORDERN bleibt
/// angemeldet: es verbraucht Rechenzeit, die jemandem gehoert.</para>
/// </summary>
[ApiController]
[Route("api/library-games")]
[Authorize]
public class LibraryGameController : BaseApiController
{
    private readonly LibraryGameService _service;

    public LibraryGameController(LibraryGameService service) => _service = service;

    /// <summary>
    /// Eine Seite der Bestandssuche, nach Eignungsnote sortiert. OHNE die Zuege.
    ///
    /// <para>Auch ohne Anmeldung. Der angemeldete Nutzer bekommt zusaetzlich die Vermerke
    /// „schon angefordert" und „liegt im Bestand" — anonym bleibt nur der zweite uebrig, weil
    /// <c>MarkKnownAsync</c> die eigene Anforderung an der UserId erkennt und die gibt es hier
    /// nicht.</para>
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    [HttpGet]
    public async Task<ActionResult<LibraryGamePageDto>> Search([FromQuery] string? q, [FromQuery] string? language,
        [FromQuery] int? minCommentedPlies, [FromQuery] int page = 1,
        [FromQuery] int pageSize = LibraryGameService.DefaultPageSize, CancellationToken ct = default, [FromQuery] string? line = null)
        => Ok(await _service.SearchAsync(GetUserIdOrNull() ?? 0, q, language, minCommentedPlies, page, pageSize, ct, line));

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
