using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Der Anzeige-Zustand einer Seite je NUTZER statt je Browser — heute die Filterleiste des
/// Turnierkalenders. Begruendung und Grenzen: <see cref="Models.UserViewState"/>.
/// </summary>
[ApiController]
[Route("api/view-state")]
[Authorize]
public class ViewStateController : BaseApiController
{
    private readonly ViewStateService _states;

    public ViewStateController(ViewStateService states) => _states = states;

    /// <summary>
    /// Der gespeicherte Zustand. **204**, wenn es keinen gibt — das ist der Normalfall beim ersten
    /// Aufruf und kein Fehler; die Oberflaeche bleibt dann bei ihren Vorgaben.
    /// </summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key, CancellationToken ct)
    {
        if (!ViewStateService.IsAllowedKey(key)) return NotFound();

        var json = await _states.GetAsync(GetUserId(), key, ct);
        return json == null ? NoContent() : Content(json, "application/json");
    }

    /// <summary>
    /// Zustand speichern. Der Rumpf ist der Zustand SELBST (ein JSON-Objekt), nicht ein DTO mit
    /// einem Feld darin: der Server liest ihn nicht, er legt ihn ab.
    /// </summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Put(string key, [FromBody] System.Text.Json.JsonElement body,
        CancellationToken ct)
    {
        if (!ViewStateService.IsAllowedKey(key)) return NotFound();

        var json = body.GetRawText();
        return await _states.SaveAsync(GetUserId(), key, json, ct)
            ? NoContent()
            : BadRequest(new
            {
                message = $"Body must be a JSON object of at most {ViewStateService.MaxJsonLength} characters.",
            });
    }

    /// <summary>Zustand verwerfen (idempotent).</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        if (!ViewStateService.IsAllowedKey(key)) return NotFound();

        await _states.DeleteAsync(GetUserId(), key, ct);
        return NoContent();
    }
}
