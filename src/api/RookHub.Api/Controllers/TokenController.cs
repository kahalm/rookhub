using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// <c>POST /api/token/test</c> in der Form der Lichess-API — damit die Vorabprüfung des
/// Engine-Providers (<c>engine-provider/preflight.py</c>) einen RookHub-API-Token genauso prüfen kann
/// wie einen Lichess-Token: Rumpf <c>text/plain</c> = Token (kommagetrennt mehrere), Antwort je Token
/// <c>{ userId, scopes, expires }</c> oder <c>null</c>. Immer 200.
///
/// <para>Anerkannt werden NUR <c>rkh_</c>-Tokens mit Scope <c>engine</c> — sie gelten als
/// <c>engine:read,engine:write</c>, genau das, was die Vorabprüfung verlangt. Jeder andere Token
/// (unbekannt, abgelaufen, Scope <c>extension</c>) ist <c>null</c>: für den Provider taugt er nicht,
/// und eine differenziertere Antwort wäre nur Auskunft nach außen.</para>
///
/// <para>Anonym (der Token steht im Rumpf, nicht im Header — der Scope-Zaun sieht ihn nicht), aber
/// gedrosselt (<c>anonymous-puzzle</c>, 30/min je IP). Eine Prüfung ist keine Benutzung:
/// <c>LastUsedAt</c> bleibt unberührt.</para>
/// </summary>
[ApiController]
[Route("api/token")]
public class TokenController : ControllerBase
{
    /// <summary>So viele Tokens prüft EINE Anfrage höchstens (Lichess erlaubt mehrere).</summary>
    public const int MaxTokensPerRequest = 20;
    private const int MaxBodyChars = 8 * 1024;

    private readonly ApiTokenService _tokens;
    private readonly AppDbContext _db;

    public TokenController(ApiTokenService tokens, AppDbContext db)
    {
        _tokens = tokens;
        _db = db;
    }

    [HttpPost("test")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    [RequestSizeLimit(MaxBodyChars * 4)]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            var buffer = new char[MaxBodyChars + 1];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
            body = new string(buffer, 0, Math.Min(read, MaxBodyChars));
        }

        var result = new Dictionary<string, TokenTestInfo?>(StringComparer.Ordinal);
        foreach (var raw in body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Distinct(StringComparer.Ordinal).Take(MaxTokensPerRequest))
        {
            result[raw] = await InfoAsync(raw, ct);
        }
        return Ok(result);
    }

    private async Task<TokenTestInfo?> InfoAsync(string raw, CancellationToken ct)
    {
        var token = await _tokens.FindValidAsync(raw, ct);
        if (token is null || token.Scope != ApiTokenService.EngineScope) return null;
        var owner = await _db.AppUsers.AsNoTracking()
            .Where(u => u.Id == token.UserId)
            .Select(u => new { u.Username, u.DeletedAt })
            .FirstOrDefaultAsync(ct);
        if (owner is null || owner.DeletedAt is not null) return null;
        long? expires = token.ExpiresAt is { } e
            ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            : null;
        return new TokenTestInfo(owner.Username, "engine:read,engine:write", expires);
    }
}
