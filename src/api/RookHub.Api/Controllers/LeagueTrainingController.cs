using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Controllers;

/// <summary>
/// Trainingslinien auf der LeagueHub-Spielerkarte (Wunsch 2026-10-07) — der dünne Liga-Endpunkt zum gemeinsamen
/// <see cref="TrainingLinesService"/>. Nur angemeldet mit <see cref="Permissions.LeagueView"/>; es gibt bewusst KEINE Fassung unter
/// <c>/api/league/s/{token}</c> (Teilen-Links haben kein Konto und damit kein Repertoire). Eigene Datei, damit
/// <c>LeagueController</c> unberührt bleibt.
/// </summary>
[ApiController]
[Route("api/league")]
[Authorize]
public class LeagueTrainingController : BaseApiController
{
    /// <summary>Wie <c>/api/prep/player/{id}/training-lines</c>, für einen Ligaspieler: Partien wie seine Karte (Brett + Verein), dazu
    /// die Online-Partien (<c>source</c> Vorgabe <c>both</c>; ohne <c>unsure=true</c> nur gesicherte Konten — wie Baum und Profil).</summary>
    [HttpGet("player/{fide}/training-lines")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> TrainingLines(string fide, [FromQuery] int? repertoire, [FromQuery] string? color,
        [FromQuery] string? chapterColors, [FromQuery] int? take, [FromQuery] string? source, [FromQuery] string? speeds,
        [FromQuery] int? years, [FromQuery] bool? unsure, [FromServices] TrainingLinesService lines, CancellationToken ct)
    {
        var filter = LeagueProfileStore.TreeFilter.Parse(source ?? "both", speeds, years, onlySure: unsure != true);
        var r = await lines.LinesAsync(GetUserId(), repertoire, color, chapterColors, take,
            () => lines.LeagueGamesAsync(fide, filter, ct), ct);
        return r is null ? NotFound(new { reason = "repertoire" }) : Ok(r);
    }
}
