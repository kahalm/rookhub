using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
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
        catch (LoginThrottledException ex)
        {
            // Konto-Bremse (nicht der IP-Limiter): für dieses Konto läuft schon eine gebremste Prüfung.
            Response.Headers.RetryAfter = "5";
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = ex.Message });
        }
        catch (AccountLockedException ex)
        {
            // Nur mit richtigem Passwort erreichbar (siehe AuthService.LoginAsync) — kein Konto-Orakel.
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message, lockedUntil = ex.LockedUntilForClient });
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
    /// <remarks>Nicht waehrend einer Impersonation: eingeloest wird der Code zu einer GEWOEHNLICHEN
    /// Anmeldung des Zielkontos (30 Tage, ohne <c>imp</c>-Claim, samt geteiltem Cookie) — damit fielen
    /// alle Impersonations-Sperren (E-Mail aendern, API-Token anlegen) und der Admin-Bezug im Log weg.</remarks>
    [Authorize]
    [HttpPost("handoff")]
    [DenyWhileImpersonating]
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
    /// IST. 204 heisst schlicht „keine geteilte Anmeldung", ohne Unterscheidung: kein Cookie,
    /// abgelaufen, Konto geloescht oder die Funktion gar nicht eingerichtet.
    /// </summary>
    /// <remarks>
    /// Bewusst 204 und kein 401: JEDER App-Start ohne Anmeldung fragt hier, das Nein ist also der
    /// Normalfall und keine abgelehnte Anmeldung. Als 401 zaehlte die Ueberwachung jeden anonymen
    /// Besucher als fehlgeschlagenen Auth-Versuch (log-watcher `auth_bruteforce`, HIGH am 2026-09-15
    /// durch 25 frische Browser-Sitzungen eines Tests; auf Prod kamen 56 von 57 Auth-401 von hier).
    /// <para>Eigener Pfad <c>rh-session</c> (Codereview N6-001): das Cookie traegt genau diesen Pfad und geht damit
    /// nicht mehr an die anderen Anwendungen der Elterndomaene, die unter <c>/api/auth</c> ihre Anmeldung haben.
    /// Ein altes Cookie (Pfad <c>/api/auth</c>) schickt der Browser hierher ebenfalls mit — es wird eingetauscht
    /// und dabei durch das neue ersetzt.</para>
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting("auth-session")]
    [HttpPost("rh-session")]
    public async Task<ActionResult<AuthResponseDto>> SharedSession(CancellationToken ct)
    {
        var cookies = SharedSessionCookieValues();
        AuthResponseDto? res = null;
        foreach (var cookie in cookies)
            if ((res = await _sharedSession.RedeemAsync(cookie, ct)) != null) break;
        if (res is null)
        {
            // Ein Cookie, das nicht (mehr) taugt, gehoert weg — sonst fragt jede Seite bei jedem
            // Start erneut danach und bekommt bis in 30 Tagen dieselbe Absage.
            if (cookies.Count > 0) DeleteSharedSessionCookie();
            return NoContent();
        }
        await WriteSharedSessionAsync(res, ct);
        return Ok(res);
    }

    /// <summary>
    /// ALLE Werte des geteilten Cookies in der Reihenfolge des Browsers (laengerer Pfad zuerst, RFC 6265 5.4).
    /// <c>Request.Cookies</c> behaelt bei gleichem Namen nur den LETZTEN — liegen das neue und das alte Cookie
    /// (Pfad <c>/api/auth</c>, vor N6-001) nebeneinander oder hat ein anderer Host der Elterndomaene eins mit
    /// kuerzerem Pfad gesetzt, waere das ausgerechnet dieses und kippte die gueltige Anmeldung.
    /// </summary>
    private List<string> SharedSessionCookieValues()
    {
        var values = new List<string>();
        foreach (var header in Request.Headers.Cookie)
        {
            if (header is null) continue;
            foreach (var pair in header.Split(';'))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0 || !pair.AsSpan(0, eq).Trim().SequenceEqual(_sharedSession.CookieName)) continue;
                var value = pair[(eq + 1)..].Trim().Trim('"');
                if (!values.Contains(value)) values.Add(value);
            }
        }
        return values;
    }

    /// <summary>
    /// Beendet die geteilte Anmeldung (Abmelden). Bewusst offen und immer 204: das Cookie zu
    /// loeschen ist nichts, wofuer man angemeldet sein muesste, und ein 401 beim Abmelden waere
    /// die verkehrte Antwort.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth-session")]
    [HttpPost("rh-session/end")]
    public IActionResult EndSharedSession()
    {
        DeleteSharedSessionCookie();
        return NoContent();
    }

    /// <summary>Uebergang (eine Version, danach entfernen): alter Pfad von <see cref="SharedSession"/> fuer
    /// Oberflaechen, die noch aus dem Browser-Cache laufen. Hierher kommt nur noch das ALTE Cookie
    /// (Pfad <c>/api/auth</c>); es wird eingetauscht und durch das neue ersetzt.</summary>
    /// <remarks>
    /// Das NEUE Cookie kommt hier nie an — „kein Cookie" heisst an diesem Pfad also auch „schon umgezogen".
    /// Mit 204 meldete sich eine alte Fassung mit uebernommener Sitzung beim zweiten Abgleich ab (Offline-Inhalte
    /// weg) und raeumte ueber <c>session/end</c> auch das neue Cookie ab — alle anderen uebernommenen Sitzungen
    /// gleich mit. Deshalb entscheidet ohne Cookie das Merk-Cookie (<see cref="SharedSessionService.MovedMarkerName"/>):
    /// <list type="bullet">
    ///   <item>keins: abgemeldet oder nie angemeldet → wie bisher 204, die alte Fassung meldet sich ab.</item>
    ///   <item>vorhanden und es gehoert dem Konto des mitgeschickten Tokens (oder es kommt gar keins mit, eine
    ///         Uebernahme beim Start) → 410: hier ist nichts zu holen, die Anmeldung lebt am neuen Pfad. Die
    ///         alte Fassung wertet das als „keine Antwort" und laesst die Sitzung stehen. Bewusst nicht 401/403
    ///         (log-watcher <c>auth_bruteforce</c>) und nicht 502–504 (Wiederholung im retry.interceptor).</item>
    ///   <item>vorhanden, aber fuer ein ANDERES Konto (am Geraet hat sich inzwischen jemand anderes angemeldet)
    ///         → 204: die alte Fassung meldet sich ab statt im vorigen Konto weiterzulaufen. Uebernehmen kann
    ///         sie das andere Konto von hier aus nicht, der Wert ist kein Nachweis.</item>
    /// </list>
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting("auth-session")]
    [HttpPost("session")]
    public async Task<ActionResult<AuthResponseDto>> LegacySharedSession(CancellationToken ct)
    {
        if (SharedSessionCookieValues().Count == 0 && MovedSessionBelongsToCaller())
            return StatusCode(StatusCodes.Status410Gone);
        return await SharedSession(ct);
    }

    /// <summary>Liegt ein Merk-Cookie, das zum Konto des mitgeschickten Tokens passt (oder kommt kein Token mit)?</summary>
    private bool MovedSessionBelongsToCaller()
    {
        var marker = Request.Cookies[_sharedSession.MovedMarkerName];
        if (string.IsNullOrEmpty(marker)) return false;
        var userId = GetUserIdOrNull();
        if (userId is null) return true;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(marker),
            System.Text.Encoding.ASCII.GetBytes(_sharedSession.MovedMarkerFor(userId.Value)));
    }

    /// <summary>Uebergang (eine Version, danach entfernen): alter Pfad von <see cref="EndSharedSession"/>.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth-session")]
    [HttpPost("session/end")]
    public IActionResult LegacyEndSharedSession() => EndSharedSession();

    /// <summary>Legt das Cookie neu an. Tut nichts, solange keine Elterndomaene eingerichtet ist.</summary>
    private async Task WriteSharedSessionAsync(AuthResponseDto res, CancellationToken ct = default)
    {
        var value = await _sharedSession.IssueAsync(res.UserId, ct);
        if (value is null) return;
        var expires = DateTimeOffset.UtcNow.Add(SharedSessionService.Lifetime);
        Response.Cookies.Append(_sharedSession.CookieName, value, SharedSessionCookieOptions(expires));
        // Uebergang (N6-001): Merker fuer die Alt-Route, siehe LegacySharedSession. Gleiche Laufzeit wie das Cookie.
        Response.Cookies.Append(_sharedSession.MovedMarkerName, _sharedSession.MovedMarkerFor(res.UserId),
            SharedSessionCookieOptions(expires, SharedSessionService.MovedMarkerPath));
        DeleteLegacySharedSessionCookie();
    }

    private void DeleteSharedSessionCookie()
    {
        if (_sharedSession.CookieDomain is null) return;
        // Loeschen heisst: dasselbe Cookie mit abgelaufenem Datum. Domaene und Pfad MUESSEN dabei
        // uebereinstimmen, sonst legt der Browser ein zweites an und das alte bleibt liegen.
        Response.Cookies.Append(_sharedSession.CookieName, "",
            SharedSessionCookieOptions(DateTimeOffset.UnixEpoch));
        // Der Merker geht mit: ohne ihn antwortet die Alt-Route 204, und eine alte Fassung meldet sich ebenfalls ab.
        Response.Cookies.Append(_sharedSession.MovedMarkerName, "",
            SharedSessionCookieOptions(DateTimeOffset.UnixEpoch, SharedSessionService.MovedMarkerPath));
        DeleteLegacySharedSessionCookie();
    }

    /// <summary>Uebergang (N6-001): das Cookie mit dem alten Pfad <c>/api/auth</c> ging an jeden Host der
    /// Elterndomaene mit einer eigenen Anmeldung — bei jedem Schreiben und Loeschen mit wegraeumen, statt es bis
    /// zu 30 Tage liegen zu lassen. Mit den Alt-Routen entfernen.</summary>
    private void DeleteLegacySharedSessionCookie()
    {
        if (_sharedSession.CookieDomain is null) return;
        Response.Cookies.Append(_sharedSession.CookieName, "",
            SharedSessionCookieOptions(DateTimeOffset.UnixEpoch, SharedSessionService.LegacyCookiePath));
    }

    private CookieOptions SharedSessionCookieOptions(DateTimeOffset expires, string path = SharedSessionService.CookiePath) => new()
    {
        Domain = _sharedSession.CookieDomain,
        Path = path,
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
        await _passwordReset.RequestResetAsync(dto.Email, dto.Site, dto.Lang);
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

    /// <summary>Die Rechte, die JETZT gelten (0.589.0): eigene Rollen + Rollen der Gruppen, live aufgelöst. Die Oberflächen
    /// holen sie beim Start, beim Zurückkehren in den Tab und alle paar Minuten — die Claims im Token sind nur noch ein
    /// Startwert. <c>isAdmin</c> ist die Admin-Rolle des Tokens, dieselbe, die der Server bei jeder Prüfung zuerst ansieht.</summary>
    [Authorize]
    [EnableRateLimiting("auth-permissions")]
    [HttpGet("permissions")]
    public async Task<ActionResult<AuthPermissionsDto>> GetPermissions([FromServices] PermissionResolver resolver, CancellationToken ct)
    {
        var userId = GetUserId();
        await MoveLegacySharedSessionCookieAsync(userId, ct);
        var live = await resolver.GetAsync(userId, ct);
        return Ok(new AuthPermissionsDto { IsAdmin = User.IsInRole("Admin"), Permissions = live.Permissions.OrderBy(p => p).ToList() });
    }

    /// <summary>
    /// Uebergang (N6-001, Nacharbeit): zieht ein altes Cookie (Pfad <c>/api/auth</c>) um, das der Browser hierher
    /// noch mitschickt. Wer nur selbst angemeldet ist, ruft den Tausch nie auf — das alte Cookie ginge sonst bis zu
    /// 30 Tage weiter an jeden Host der Elterndomaene mit einer Anmeldung unter <c>/api/auth</c>. Die Rechte holt
    /// jede angemeldete Oberflaeche beim Start und alle paar Minuten, hier ist es also schnell weg.
    /// <para>Das NEUE Cookie kommt hier nie an (sein Pfad ist kein Praefix von <c>/api/auth/permissions</c>), jeder
    /// Wert ist also ein altes. Umgezogen wird nur die Anmeldung DIESES Kontos; ein taugliches Cookie eines anderen
    /// Kontos (Impersonation, anderer Nutzer am Geraet) bleibt unangetastet, bis der Tausch es umzieht. Ein
    /// untaugliches wird geloescht wie beim Tausch.</para>
    /// </summary>
    private async Task MoveLegacySharedSessionCookieAsync(int userId, CancellationToken ct)
    {
        if (!_sharedSession.IsEnabled) return;
        var cookies = SharedSessionCookieValues();
        if (cookies.Count == 0) return;
        AuthResponseDto? res = null;
        foreach (var cookie in cookies)
            if ((res = await _sharedSession.RedeemAsync(cookie, ct)) != null) break;
        if (res is null) DeleteLegacySharedSessionCookie();
        else if (res.UserId == userId) await WriteSharedSessionAsync(res, ct);
    }

    [HttpPut("change-password")]
    [Authorize]
    [DenyWhileImpersonating]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
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
