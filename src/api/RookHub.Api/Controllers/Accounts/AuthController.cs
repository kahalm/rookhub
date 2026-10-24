using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.DTOs.Accounts;

namespace RookHub.Api.Controllers.Accounts;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : BaseApiController
{
    private readonly AuthService _authService;
    private readonly PasswordResetService _passwordReset;
    private readonly AuthHandoffService _handoff;
    private readonly SharedSessionService _sharedSession;
    /// <summary>Kurze Hintergrundarbeiten mit eigenem Consumer (wie Web-Push) — hier der Reset-Mailversand,
    /// damit „Passwort vergessen" nicht ueber die Antwortzeit verraet, ob es die Adresse gibt.</summary>
    private readonly IWebhookTaskQueue _shortTasks;

    public AuthController(AuthService authService, PasswordResetService passwordReset,
        AuthHandoffService handoff, SharedSessionService sharedSession, IWebhookTaskQueue shortTasks)
    {
        _authService = authService;
        _passwordReset = passwordReset;
        _handoff = handoff;
        _sharedSession = sharedSession;
        _shortTasks = shortTasks;
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
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = ex.Message, code = ApiErrorCodes.LoginThrottled });
        }
        catch (AccountLockedException ex)
        {
            // Nur mit richtigem Passwort erreichbar (siehe AuthService.LoginAsync) — kein Konto-Orakel.
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = ex.Message, code = ApiErrorCodes.AccountLocked, lockedUntil = ex.LockedUntilForClient });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Invalid username or password.", code = ApiErrorCodes.LoginInvalid });
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
        var userId = GetUserId();
        var code = await _handoff.IssueAsync(userId, ct);
        // Eingeloest wird der Code nur in einem Browser, der die geteilte Anmeldung DIESES Kontos traegt (siehe
        // HandoffExchange, Codereview F1-008). Der Sprung legt sie deshalb hier frisch an — auch wenn das Cookie
        // (30 Tage) aelter ist als das Token (bis 90 Tage mit „eingeloggt bleiben"). Bisher schrieb erst der
        // Tausch drueben das Cookie dieses Kontos; im Ergebnis aendert sich fuer den Sprung also nichts.
        await WriteSharedSessionAsync(userId, ct);
        return Ok(new { code, expiresInSeconds = (int)AuthHandoffService.Lifetime.TotalSeconds });
    }

    /// <summary>Loest einen Uebergabe-Code gegen eine eigene Anmeldung ein. Offen, weil der Aufrufer
    /// hier ja noch nicht angemeldet IST — der Code ist der Nachweis. 400 sagt bewusst nur, dass es
    /// nicht ging: unbekannt, abgelaufen und verbraucht sind von aussen nicht zu unterscheiden.</summary>
    /// <remarks>
    /// <para><b>An den Browser gebunden (Codereview F1-008):</b> Der Code allein ist kein Nachweis, DASS der
    /// einloesende Browser der springende ist. Ein Angreifer liesse sich von seinem Server laufend frische Codes
    /// fuer das EIGENE Konto holen und das Opfer auf <c>…?h=&lt;Code&gt;</c> leiten — still als Angreifer
    /// angemeldet, samt geteiltem Cookie fuer alle Oberflaechen (Login-CSRF). Bei eingerichteter Elterndomaene
    /// verlangt der Tausch deshalb zusaetzlich das geteilte Cookie DESSELBEN Kontos — der Sprung legt es beim
    /// Holen des Codes an (<see cref="Handoff"/>), im Browser des Opfers fehlt es oder gehoert dem Opfer. Ohne
    /// Elterndomaene (localhost, IP) gibt es kein Cookie, dann bleibt es beim Code allein.</para>
    /// <para>Neuer Pfad <c>rh-session/handoff</c>: nur dorthin schickt der Browser das Cookie mit (sein Pfad ist
    /// <c>/api/auth/rh-session</c>, N6-001). Der alte Pfad bleibt als Uebergang (<see cref="LegacyHandoffExchange"/>).</para>
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("rh-session/handoff")]
    public async Task<ActionResult<AuthResponseDto>> HandoffExchange([FromBody] HandoffExchangeDto dto, CancellationToken ct)
    {
        HashSet<int>? holders = null;
        if (_sharedSession.IsEnabled)
        {
            holders = new HashSet<int>();
            foreach (var cookie in SharedSessionCookieValues())
                if (await _sharedSession.RedeemAsync(cookie, ct) is { } shared) holders.Add(shared.UserId);
            // Ohne taugliches Cookie kann kein Code mehr passen — dann gar nicht erst einloesen.
            if (holders.Count == 0) return BadRequest(new { message = "Handoff code is not valid." });
        }
        var res = await _handoff.RedeemAsync(dto?.Code, ct);
        if (res is null || (holders != null && !holders.Contains(res.UserId)))
            return BadRequest(new { message = "Handoff code is not valid." });
        await WriteSharedSessionAsync(res, ct);
        return Ok(res);
    }

    /// <summary>Uebergang (eine Version, danach entfernen): alter Pfad von <see cref="HandoffExchange"/> fuer
    /// Oberflaechen aus dem Browser-Cache — mit derselben Pruefung. Hierher kommt nur noch ein altes Cookie (Pfad
    /// <c>/api/auth</c>); ohne eins endet der Sprung in der Anmeldemaske.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("handoff/exchange")]
    public Task<ActionResult<AuthResponseDto>> LegacyHandoffExchange([FromBody] HandoffExchangeDto dto, CancellationToken ct)
        => HandoffExchange(dto, ct);

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

    /// <summary>Legt das Cookie neu an. Tut nichts, solange keine Elterndomaene eingerichtet ist.</summary>
    private Task WriteSharedSessionAsync(AuthResponseDto res, CancellationToken ct = default) =>
        WriteSharedSessionAsync(res.UserId, ct);

    private async Task WriteSharedSessionAsync(int userId, CancellationToken ct = default)
    {
        var value = await _sharedSession.IssueAsync(userId, ct);
        if (value is null) return;
        var expires = DateTimeOffset.UtcNow.Add(SharedSessionService.Lifetime);
        Response.Cookies.Append(_sharedSession.CookieName, value, SharedSessionCookieOptions(expires));
        DeleteLegacySharedSessionCookie();
    }

    private void DeleteSharedSessionCookie()
    {
        if (_sharedSession.CookieDomain is null) return;
        // Loeschen heisst: dasselbe Cookie mit abgelaufenem Datum. Domaene und Pfad MUESSEN dabei
        // uebereinstimmen, sonst legt der Browser ein zweites an und das alte bleibt liegen.
        Response.Cookies.Append(_sharedSession.CookieName, "",
            SharedSessionCookieOptions(DateTimeOffset.UnixEpoch));
        DeleteLegacySharedSessionCookie();
    }

    /// <summary>Uebergang (N6-001): das Cookie mit dem alten Pfad <c>/api/auth</c> ging an jeden Host der
    /// Elterndomaene mit einer eigenen Anmeldung — bei jedem Schreiben und Loeschen mit wegraeumen, statt es bis
    /// zu 30 Tage liegen zu lassen. Die Alt-Routen, ihr Merk-Cookie und der Umzug in <c>permissions</c> sind seit
    /// 0.828.1 weg; dieses Aufraeumen bleibt, bis auch das letzte vor 0.624.0 ausgegebene Cookie (30 Tage) abgelaufen
    /// ist — danach samt <see cref="SharedSessionService.LegacyCookiePath"/> entfernen.</summary>
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
        // Suche, SMTP-Versand und Token-Persistenz laufen im Hintergrund (eigener DI-Scope): wartete die Antwort
        // darauf, kaeme sie bei unbekannter Adresse in Millisekunden, bei bekannter erst nach der SMTP-Runde —
        // ein Konten-Orakel trotz neutraler 200 (Codereview A1-009).
        var (email, site, lang) = (dto.Email, dto.Site, dto.Lang);
        await _shortTasks.EnqueueAsync((sp, ct) =>
            sp.GetRequiredService<PasswordResetService>().RequestResetAsync(email, site, lang, ct));
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
        var live = await resolver.GetAsync(GetUserId(), ct);
        return Ok(new AuthPermissionsDto { IsAdmin = User.IsInRole("Admin"), Permissions = live.Permissions.OrderBy(p => p).ToList() });
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
