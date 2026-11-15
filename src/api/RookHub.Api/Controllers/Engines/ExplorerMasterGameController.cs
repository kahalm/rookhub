using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.DTOs.Repertoires;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Controllers.Engines;

/// <summary>
/// Eine Meisterpartie des lokalen Explorers zum Nachspielen (0.980.0, Wunsch 2026-11-15: die Partie-Zusammenfassung nennt
/// Beispielpartien aus dem Explorer — „am besten mit einem Link“). Ohne Anmeldung: die Zusammenfassung wird auch über
/// Teilen-Links gelesen, und die Partien sind veröffentlichte Turnierpartien. Eigener Controller, weil
/// <see cref="ExplorerController"/> als Ganzes angemeldet ist (die Online-Quelle verbraucht dort das Server-Kontingent).
///
/// <para>Die Kennung geht in den Pfad der Anfrage an den Explorer — sie muss genau acht Zeichen aus Ziffern und
/// Buchstaben haben (<see cref="LocalExplorerClient.IsGameId"/>), sonst 400. Unbekannt = 404; Explorer nicht eingerichtet
/// oder nicht erreichbar = 503. Gedrosselt je Adresse wie die anderen anonymen Lese-Endpunkte.</para>
/// </summary>
[ApiController]
[Route("api/explorer/master-games")]
[AllowAnonymous]
[EnableRateLimiting("anonymous-tournament")]
public class ExplorerMasterGameController(LocalExplorerClient local, IMemoryCache memory) : BaseApiController
{
    /// <summary>Eine Meisterpartie ändert sich nicht; ein Tag im Arbeitsspeicher hält wiederholte Aufrufe vom Explorer fern.</summary>
    private static readonly TimeSpan MemoryTtl = TimeSpan.FromDays(1);

    [HttpGet("{id}")]
    public async Task<ActionResult<ExplorerMasterGameDto>> Get(string id, CancellationToken ct)
    {
        if (!LocalExplorerClient.IsGameId(id)) return BadRequest(new { message = "Keine gültige Partie-Kennung." });
        var key = "explorer:master-game:" + id;
        if (!memory.TryGetValue<string>(key, out var pgn) || pgn is null)
        {
            var result = await local.FetchMasterPgnAsync(id, ct);
            if (!result.Reachable) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Der Explorer ist gerade nicht erreichbar." });
            if (result.Pgn is null) return NotFound();
            pgn = result.Pgn;
            memory.Set(key, pgn, MemoryTtl);
        }
        return Ok(new ExplorerMasterGameDto { Id = id, Pgn = pgn });
    }
}
