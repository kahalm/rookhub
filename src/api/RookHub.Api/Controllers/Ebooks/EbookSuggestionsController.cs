using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Controllers.Ebooks;

/// <summary>„Empfohlene Literatur“ unter Zusammenfassungen (Wunsch 2026-11-09), siehe <see cref="EbookSuggestions"/>.</summary>
[ApiController]
public class EbookSuggestionsController(EbookSuggestions suggestions, PermissionResolver? permissions = null) : BaseApiController
{
    /// <summary>Anzeige der Bücher <c>?ids=1,2</c> — offen, weil auch der Liga-Teilen-Link sie zeigt; nur Bücher der Vorschlagsliste.</summary>
    [HttpGet("api/ebook-suggestions")]
    [AllowAnonymous]
    public async Task<IActionResult> Info([FromQuery] string? ids, CancellationToken ct)
    {
        var signedIn = User?.Identity?.IsAuthenticated == true;
        EbookCaller caller = signedIn ? GetUserId() : EbookCaller.Anonymous(Request?.Headers["X-Visitor-Id"].FirstOrDefault());
        var manageAll = signedIn && await HasPermissionAsync(permissions, Permissions.EbooksManage);
        var list = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0);
        return Ok(await suggestions.InfoAsync(list, caller, manageAll, ct));
    }

    /// <summary>Buch nicht mehr vorschlagen.</summary>
    [HttpPut("api/ebook-suggestions/{id:int}/block")]
    [Authorize]
    public async Task<IActionResult> Block(int id, CancellationToken ct) =>
        await suggestions.SetBlockedAsync(GetUserId(), id, true, DateTime.UtcNow, ct) ? NoContent() : NotFound();

    /// <summary>„Nicht mehr vorschlagen“ zurücknehmen.</summary>
    [HttpDelete("api/ebook-suggestions/{id:int}/block")]
    [Authorize]
    public async Task<IActionResult> Unblock(int id, CancellationToken ct) =>
        await suggestions.SetBlockedAsync(GetUserId(), id, false, DateTime.UtcNow, ct) ? NoContent() : NotFound();

    /// <summary>Kandidaten für den Watcher: <c>?game=237</c> bzw. <c>o143</c> — ohne die Sperren des Anfordernden.</summary>
    [HttpGet("api/admin/ebook-suggestions/candidates")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Candidates([FromQuery] string? game, CancellationToken ct) =>
        Ok(await suggestions.CandidatesAsync(string.IsNullOrEmpty(game) ? null : await suggestions.RequesterAsync(game, ct), ct));
}
