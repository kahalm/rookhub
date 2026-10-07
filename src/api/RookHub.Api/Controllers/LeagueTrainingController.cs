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
        var q = new TrainingLinesService.Query(repertoire, color, TrainingLinesService.ParseOverrides(chapterColors), take);
        var r = await lines.LinesAsync(GetUserId(), q, async () => (await lines.LeagueGamesAsync(fide, filter, ct)).Games, ct,
            () => lines.LeagueEloAsync(fide, ct));
        return r is null ? NotFound(new { reason = "repertoire" }) : Ok(r);
    }

    /// <summary>„Show me lines to train" für einen Ligaspieler — wie <c>POST /api/prep/player/{id}/training-repertoire</c>; Name aus
    /// seiner Karte (sonst die FIDE-ID). Nur angemeldet, keine Fassung über einen Teilen-Link.</summary>
    [HttpPost("player/{fide}/training-repertoire")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> TrainingRepertoire(string fide, [FromBody] TrainingLinesService.CreateRequest? req,
        [FromServices] TrainingLinesService lines, CancellationToken ct)
    {
        req ??= new(null, null, null, null, null, null, null, null, null);
        var filter = LeagueProfileStore.TreeFilter.Parse(req.Source ?? "both", req.Speeds, req.Years, onlySure: req.Unsure != true);
        // Name (für „Prep: <Name> <Jahr>" und den Namensschutz) und Partien in einem — der Name steht erst damit fest
        var (n, games) = await lines.LeagueGamesAsync(fide, filter, ct);
        TrainingLinesService.Created? r;
        try
        {
            r = await lines.CreateRepertoireAsync(GetUserId(), string.IsNullOrWhiteSpace(n) ? fide : n, req.ToQuery(),
                () => Task.FromResult(games), ct, () => lines.LeagueEloAsync(fide, ct));
        }
        catch (TrainingLinesService.SameRepertoireException e) { return BadRequest(new { reason = "sameRepertoire", message = e.Message }); }
        return r is null ? NotFound(new { reason = "repertoire" }) : Ok(new { id = r.Id, name = r.Name, lines = r.Lines, replaced = r.Replaced });
    }
}
