using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Controllers;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : BaseApiController
{
    private readonly AuthService _authService;
    private readonly PasswordResetService _passwordReset;
    private readonly AuthHandoffService _handoff;
    private readonly SharedSessionService _sharedSession;

    public AuthController(AuthService authService, PasswordResetService passwordReset,
        AuthHandoffService handoff, SharedSessionService sharedSession)
    {
        _authService = authService;
        _passwordReset = passwordReset;
        _handoff = handoff;
        _sharedSession = sharedSession;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterDto dto)
    {
        try
        {
            var result = await _authService.RegisterAsync(dto);
            await WriteSharedSessionAsync(result);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        try
        {
            var result = await _authService.LoginAsync(dto);
            await WriteSharedSessionAsync(result);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Invalid username or password." });
        }
    }

    /// <summary>
    /// „Passwort vergessen", Schritt 1: schickt — falls die Adresse zu einem aktiven Konto
    /// gehoert — einen Reset-Link per Mail. Antwortet IMMER neutral mit 200 (keine
    /// User-Enumeration), unabhaengig davon, ob die Adresse existiert.
    /// </summary>
    // ===== Anmelde-Uebergabe zwischen den Oberflaechen (RookHub ↔ Turnierseite) =====

    /// <summary>Einmal-Code fuer den Sprung zur anderen Oberflaeche. Der Rohwert kommt NUR hier
    /// heraus und lebt Sekunden (<see cref="AuthHandoffService.Lifetime"/>).</summary>
    [Authorize]
    [HttpPost("handoff")]
    public async Task<IActionResult> Handoff(CancellationToken ct)
    {
        var code = await _handoff.IssueAsync(GetUserId(), ct);
        return Ok(new { code, expiresInSeconds = (int)AuthHandoffService.Lifetime.TotalSeconds });
    }

    /// <summary>Loest einen Uebergabe-Code gegen eine eigene Anmeldung ein. Offen, weil der Aufrufer
    /// hier ja noch nicht angemeldet IST — der Code ist der Nachweis. 400 sagt bewusst nur, dass es
    /// nicht ging: unbekannt, abgelaufen und verbraucht sind von aussen nicht zu unterscheiden.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("handoff/exchange")]
    public async Task<ActionResult<AuthResponseDto>> HandoffExchange([FromBody] HandoffExchangeDto dto, CancellationToken ct)
    {
        var res = await _handoff.RedeemAsync(dto?.Code, ct);
        if (res is null) return BadRequest(new { message = "Handoff code is not valid." });
        await WriteSharedSessionAsync(res, ct);
        return Ok(res);
    }

    // ===== Geteilte Anmeldung ueber beide Oberflaechen (siehe SharedSessionService) =====

    /// <summary>
    /// Holt sich die Anmeldung, die auf der Schwesterseite schon besteht — Nachweis ist das Cookie
    /// auf der gemeinsamen Elterndomaene. Offen, weil der Aufrufer hier ja noch nicht angemeldet
    /// IST. 401 heisst schlicht „keine geteilte Anmeldung", ohne Unterscheidung: kein Cookie,
    /// abgelaufen, Konto geloescht oder die Funktion gar nicht eingerichtet.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("session")]
    public async Task<ActionResult<AuthResponseDto>> SharedSession(CancellationToken ct)
    {
        var cookie = Request.Cookies[_sharedSession.CookieName];
        var res = await _sharedSession.RedeemAsync(cookie, ct);
        if (res is null)
        {
            // Ein Cookie, das nicht (mehr) taugt, gehoert weg — sonst fragt jede Seite bei jedem
            // Start erneut danach und bekommt bis in 30 Tagen dieselbe Absage.
            if (cookie != null) DeleteSharedSessionCookie();
            return Unauthorized(new { message = "No shared session." });
        }
        await WriteSharedSessionAsync(res, ct);
        return Ok(res);
    }

    /// <summary>
    /// Beendet die geteilte Anmeldung (Abmelden). Bewusst offen und immer 204: das Cookie zu
    /// loeschen ist nichts, wofuer man angemeldet sein muesste, und ein 401 beim Abmelden waere
    /// die verkehrte Antwort.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("session/end")]
    public IActionResult EndSharedSession()
    {
        DeleteSharedSessionCookie();
        return NoContent();
    }

    /// <summary>Legt das Cookie neu an. Tut nichts, solange keine Elterndomaene eingerichtet ist.</summary>
    private async Task WriteSharedSessionAsync(AuthResponseDto res, CancellationToken ct = default)
    {
        var value = await _sharedSession.IssueAsync(res.UserId, ct);
        if (value is null) return;
        Response.Cookies.Append(_sharedSession.CookieName, value, SharedSessionCookieOptions(
            DateTimeOffset.UtcNow.Add(SharedSessionService.Lifetime)));
    }

    private void DeleteSharedSessionCookie()
    {
        if (_sharedSession.CookieDomain is null) return;
        // Loeschen heisst: dasselbe Cookie mit abgelaufenem Datum. Domaene und Pfad MUESSEN dabei
        // uebereinstimmen, sonst legt der Browser ein zweites an und das alte bleibt liegen.
        Response.Cookies.Append(_sharedSession.CookieName, "",
            SharedSessionCookieOptions(DateTimeOffset.UnixEpoch));
    }

    private CookieOptions SharedSessionCookieOptions(DateTimeOffset expires) => new()
    {
        Domain = _sharedSession.CookieDomain,
        Path = SharedSessionService.CookiePath,
        HttpOnly = true,
        Secure = true,
        // Lax statt None: das Cookie soll bei einer fremd ausgeloesten Anfrage gar nicht erst
        // mitgehen. Beide Oberflaechen sind Subdomains derselben Domaene, also same-site — fuer
        // sie aendert Lax nichts.
        SameSite = SameSiteMode.Lax,
        Expires = expires,
        IsEssential = true,
    };

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        await _passwordReset.RequestResetAsync(dto.Email);
        return Ok(new { message = "If the address belongs to an account, a reset link has been sent." });
    }

    /// <summary>„Passwort vergessen", Schritt 2: neues Passwort mit dem Token aus der Mail setzen.</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        try
        {
            await _passwordReset.ResetPasswordAsync(dto.Token, dto.NewPassword);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { message = "Invalid or expired reset token." });
        }
    }

    [HttpPut("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        if (IsImpersonating())
            return StatusCode(403, new { message = "Not allowed while impersonating another user." });
        try
        {
            // Antwort trägt ein frisches Token: der rotierte Security-Stamp entwertet auch das Token
            // dieser Sitzung — das Frontend ersetzt seinen gespeicherten Stand damit und bleibt drin.
            return Ok(await _authService.ChangePasswordAsync(GetUserId(), dto));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Current password is incorrect." });
        }
    }
}
