using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.League;

namespace RookHub.Api.Controllers.Courses;

/// <summary>
/// „Taktiken trainieren“ (Wunsch 2026-11-14): die Empfohlenen Übungen einer Partie als Kapitel im Kurs „Persönliche
/// Taktiken“ des Kontos (<see cref="PersonalTacticsCourse"/>). Nur angemeldet — auch für Partien, die man über einen
/// Teilen-Link sieht.
/// </summary>
[ApiController]
[Route("api/courses/personal-tactics")]
[Authorize]
public class PersonalTacticsController(PersonalTacticsCourse course, LeagueClubResolver clubs, PermissionResolver? permissions = null)
    : BaseApiController
{
    /// <summary>
    /// Kapitel der Partie öffnen bzw. anlegen → <c>{ bookId, chapterIndex, chapter, created, lines }</c>. Der Zugriff auf die
    /// Partie wird je <c>kind</c> geprüft wie beim Lesen ihrer Übungen (<c>GET …/summary/exercises</c>): <c>club</c> =
    /// <see cref="Permissions.LeagueView"/> + Verein der Partie (403/404), <c>club-share</c> = Partie der Begegnung des
    /// Liga-Links, <c>club-link</c>/<c>own-share</c> = gültiger Link, <c>own</c> = Besitzer; sonst 404. 400 bei unbekanntem
    /// <c>kind</c> oder einer Partie ohne lösbare Aufgabe.
    /// </summary>
    [HttpPost("from-game")]
    public async Task<IActionResult> FromGame([FromBody] PersonalTacticsFromGameDto? dto, [FromServices] LeagueService? league,
        [FromServices] LeagueFixtureGames? fixtures, CancellationToken ct)
    {
        var userId = GetUserId();
        PersonalTacticsCourse.Game? game;
        switch (dto?.Kind)
        {
            case "own" when dto.Id is int id:
                game = await course.OwnGameAsync(userId, id, ct);
                break;
            case "own-share":
                game = await course.SharedOwnGameAsync(userId, dto.Token, ct);
                break;
            case "club-link":
                game = await course.ClubLinkGameAsync(dto.Token, ct);
                break;
            case "club" when dto.Id is int id:
                if (!await HasPermissionAsync(permissions, Permissions.LeagueView)) return Forbid();
                var (club, error) = await LeagueClubAsync(clubs, ct, await course.ClubOfGameAsync(id, ct));
                if (club is null) return error!;
                game = await course.ClubGameAsync(club.Id, id, linkable: true, ct);
                break;
            case "club-share" when dto.Id is int id:
                // Wie LeagueShareClubController: nur eine Partie, die an einem Brett der Begegnung DES LINKS hängt.
                game = league is not null && fixtures is not null && !string.IsNullOrWhiteSpace(dto.Token)
                    && await league.ShareFixtureAsync(dto.Token, ct) is { } s && await league.ShareContextAsync(dto.Token, ct) is { } link
                    && await fixtures.FixtureHasClubGameAsync(link.Club, s.Tnr, s.Round, s.Team, id, ct)
                    ? await course.ClubGameAsync(link.Club.Id, id, linkable: false, ct) : null;
                break;
            default:
                return BadRequest(new { message = "Unknown game reference." });
        }
        if (game is null) return NotFound();
        return Ok(await course.OpenAsync(userId, game, await EbookViewerAsync(), dto!.Lang, ct));
    }
}
