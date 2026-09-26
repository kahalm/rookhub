using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>„Ähnliche Meisterpartien" (0.543.0) — eigene Klasse unter derselben Route wie <see cref="GamesController"/>.
/// Der Teilen-Link zeigt sie auch ohne Anmeldung: es sind Kopfdaten des Rohbestands ohne Züge, derselbe Zuschnitt wie die
/// anonyme Bestandssuche.</summary>
[ApiController]
[Route("api/games")]
[Authorize]
public class SimilarGamesController : BaseApiController
{
    private readonly SimilarGamesService _service;

    public SimilarGamesController(SimilarGamesService service) => _service = service;

    [HttpGet("{id:int}/similar")]
    public async Task<ActionResult<SimilarGamesDto>> Own(int id, CancellationToken ct)
    {
        var dto = await _service.ForOwnAsync(GetUserId(), id, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpGet("shared/{token}/similar")]
    [AllowAnonymous]
    public async Task<ActionResult<SimilarGamesDto>> Shared(string token, CancellationToken ct)
    {
        var dto = await _service.ForSharedAsync(token, GetUserIdOrNull(), ct);
        return dto == null ? NotFound() : Ok(dto);
    }
}
