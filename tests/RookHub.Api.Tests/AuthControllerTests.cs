using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AuthControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuthService _authService;
    private readonly AuthController _controller;
    private readonly SharedSessionService _shared;
    private readonly DefaultHttpContext _http = new();

    public AuthControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "TestSecretKeyThatIsLongEnoughForHmacSha256!!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                // Die geteilte Anmeldung ueber beide Oberflaechen braucht eine Elterndomaene;
                // ohne sie ist sie aus (siehe SharedSessionServiceTests).
                ["Auth:SharedSessionDomain"] = ".example.test",
            })
            .Build();

        _authService = new AuthService(_db, config, NullLogger<AuthService>.Instance, null, TestServices.Cache());
        var resetService = new PasswordResetService(
            _db, new FakeEmailSender(), config, NullLogger<PasswordResetService>.Instance);
        var handoff = new AuthHandoffService(_db, _authService, NullLogger<AuthHandoffService>.Instance);
        _shared = new SharedSessionService(_db, _authService, config);
        _controller = new AuthController(_authService, resetService, handoff, _shared)
        {
            // Ohne HttpContext gibt es weder Request.Cookies noch Response.Cookies — und der
            // Controller schreibt beim Anmelden ein Cookie.
            ControllerContext = new ControllerContext { HttpContext = _http },
        };
    }

    /// <summary>Die Set-Cookie-Zeile, die der Controller geschrieben hat (oder null).</summary>
    private string? SetCookieHeader(string name) =>
        _http.Response.Headers.SetCookie.FirstOrDefault(h => h?.StartsWith(name + "=") == true);

    /// <summary>Die Set-Cookie-Zeile fuer genau diesen Pfad (Cookies sind je Name+Domaene+PFAD eigene Eintraege).</summary>
    private string? SetCookieHeader(string name, string path) =>
        _http.Response.Headers.SetCookie.FirstOrDefault(h => h?.StartsWith(name + "=") == true
            && h.Split(';').Any(part => string.Equals(part.Trim(), "path=" + path, StringComparison.OrdinalIgnoreCase)));

    private static bool IsDeletion(string? setCookie) =>
        setCookie != null && setCookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _db.Dispose();

    private sealed class FakeEmailSender : IEmailSender
    {
        public bool IsEnabled => true;
        public Task SendAsync(string to, string subject, string html, string text, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // ---- Register ----

    [Fact]
    public async Task Register_ReturnsOk_WithToken()
    {
        var dto = new RegisterDto { Username = "newuser", Email = "new@test.com", Password = "Password1!" };

        var result = await _controller.Register(dto);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = okResult.Value as AuthResponseDto;
        Assert.NotNull(response);
        Assert.Equal("newuser", response.Username);
        Assert.False(string.IsNullOrEmpty(response.Token));
    }

    [Fact]
    public async Task Register_ReturnsConflict_WhenUsernameExists()
    {
        _db.AppUsers.Add(new AppUser
        {
            Username = "existing",
            Email = "exist@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass")
        });
        await _db.SaveChangesAsync();

        var dto = new RegisterDto { Username = "existing", Email = "new@test.com", Password = "Password1!" };

        var result = await _controller.Register(dto);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Register_ReturnsConflict_WhenEmailExists()
    {
        _db.AppUsers.Add(new AppUser
        {
            Username = "user1",
            Email = "taken@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass")
        });
        await _db.SaveChangesAsync();

        var dto = new RegisterDto { Username = "user2", Email = "taken@test.com", Password = "Password1!" };

        var result = await _controller.Register(dto);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    // ---- Login ----

    [Fact]
    public async Task Login_ReturnsOk_WithValidCredentials()
    {
        // Register first
        await _controller.Register(new RegisterDto
        {
            Username = "loginuser",
            Email = "login@test.com",
            Password = "Password1!"
        });

        var result = await _controller.Login(new LoginDto
        {
            Username = "loginuser",
            Password = "Password1!"
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = okResult.Value as AuthResponseDto;
        Assert.NotNull(response);
        Assert.Equal("loginuser", response.Username);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WithWrongPassword()
    {
        await _controller.Register(new RegisterDto
        {
            Username = "loginuser",
            Email = "login@test.com",
            Password = "Password1!"
        });

        var result = await _controller.Login(new LoginDto
        {
            Username = "loginuser",
            Password = "WrongPassword1!"
        });

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_LockedAccount_Returns403_WithTheEnd()
    {
        // F5-011: nur mit richtigem Passwort — sonst 401 wie jedes falsche Passwort.
        await _controller.Register(new RegisterDto { Username = "gesperrt", Email = "g@test.com", Password = "Password1!" });
        var user = await _db.AppUsers.SingleAsync(u => u.Username == "gesperrt");
        var until = DateTime.UtcNow.AddDays(2);
        user.LockedUntil = until;
        await _db.SaveChangesAsync();

        var wrong = await _controller.Login(new LoginDto { Username = "gesperrt", Password = "WrongPassword1!" });
        Assert.IsType<UnauthorizedObjectResult>(wrong.Result);

        var result = await _controller.Login(new LoginDto { Username = "gesperrt", Password = "Password1!" });
        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, status.StatusCode);
        var body = System.Text.Json.JsonSerializer.Serialize(status.Value);
        Assert.Contains("\"lockedUntil\"", body);
        Assert.Contains(until.ToString("yyyy-MM-ddTHH:mm:ss"), body);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WithNonexistentUser()
    {
        var result = await _controller.Login(new LoginDto
        {
            Username = "nonexistent",
            Password = "Password1!"
        });

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_Returns429_WhileAThrottledCheckForTheSameAccountRuns()
    {
        // Konto-Bremse (W1 A1-004): läuft für ein gebremstes Konto schon eine Prüfung, wird ein
        // weiterer Versuch sofort abgewiesen — als 429 mit Retry-After, nicht als „falsches Passwort".
        await _controller.Register(new RegisterDto { Username = "busy", Password = "Password1!" });
        for (var i = 0; i < 6; i++)
            Assert.IsType<UnauthorizedObjectResult>(
                (await _controller.Login(new LoginDto { Username = "busy", Password = "falsch" })).Result);

        var running = _authService.LoginAsync(new LoginDto { Username = "busy", Password = "falsch" });  // hält das Tor
        var result = await _controller.Login(new LoginDto { Username = "busy", Password = "Password1!" });

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, status.StatusCode);
        Assert.Equal("5", _http.Response.Headers.RetryAfter.ToString());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => running);
    }

    // ---- Forgot / Reset Password ----

    [Fact]
    public async Task ForgotPassword_ReturnsOk_EvenForUnknownEmail()
    {
        var result = await _controller.ForgotPassword(new ForgotPasswordDto { Email = "nobody@test.com" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ForgotPassword_PassesSiteAndLanguageToTheMail()
    {
        // UX-031: KidHub schickt site/lang mit — der Controller muss sie bis zur Mail durchreichen.
        await _controller.Register(new RegisterDto { Username = "kid", Email = "kid@t.com", Password = "Password1!" });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://rookhub.example",
                ["App:KidHubBaseUrl"] = "https://kidhub.example",
            })
            .Build();
        var mails = new RecordingEmailSender();
        var controller = new AuthController(_authService,
            new PasswordResetService(_db, mails, config, NullLogger<PasswordResetService>.Instance),
            new AuthHandoffService(_db, _authService, NullLogger<AuthHandoffService>.Instance), _shared)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.ForgotPassword(new ForgotPasswordDto { Email = "kid@t.com", Site = "kidhub", Lang = "en" });

        Assert.IsType<OkObjectResult>(result);
        var mail = Assert.Single(mails.Sent);
        Assert.Equal("KidHub — Reset your password", mail.Subject);
        Assert.Contains("https://kidhub.example/reset-password?token=", mail.Text);
    }

    [Fact]
    public async Task ResetPassword_ReturnsBadRequest_WithInvalidToken()
    {
        var result = await _controller.ResetPassword(new ResetPasswordDto
        {
            Token = "does-not-exist",
            NewPassword = "BrandNew1!"
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Register_SetsIsAdmin_False()
    {
        var result = await _controller.Register(new RegisterDto
        {
            Username = "newuser",
            Email = "new@test.com",
            Password = "Password1!"
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = okResult.Value as AuthResponseDto;
        Assert.False(response!.IsAdmin);
    }

    // ---- Anmelde-Uebergabe ----

    /// <summary>Meldet den Controller als <paramref name="userId"/> an — mit <paramref name="impersonatedBy"/>
    /// traegt das Token den <c>imp</c>-Claim wie ein Einstieg ueber <c>POST /api/admin/users/{id}/impersonate</c>.</summary>
    private void SignIn(int userId, int? impersonatedBy = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (impersonatedBy is int adminId) claims.Add(new Claim("imp", adminId.ToString()));
        _http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public async Task Handoff_ReturnsACode_ForAnOwnLogin()
    {
        await _controller.Register(new RegisterDto { Username = "u", Email = "u@t.com", Password = "Password1!" });
        var user = await _db.AppUsers.FirstAsync();
        SignIn(user.Id);

        var result = await _controller.Handoff(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.AuthHandoffTokens.CountAsync(t => t.UserId == user.Id));
    }

    [Fact]
    public async Task Handoff_WhileImpersonating_Returns403_AndIssuesNoCode()
    {
        // Eingeloest wird der Code zu einer GEWOEHNLICHEN Anmeldung des Zielkontos (30 Tage, ohne imp-Claim,
        // samt rh_session) — damit fielen alle Impersonations-Sperren (E-Mail aendern → Passwort vergessen →
        // Konto dauerhaft uebernommen), und der Admin-Bezug im Log waere weg.
        await _controller.Register(new RegisterDto { Username = "u", Email = "u@t.com", Password = "Password1!" });
        var target = await _db.AppUsers.FirstAsync();
        SignIn(target.Id, impersonatedBy: 990001);

        var result = await _controller.Handoff(CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        Assert.False(await _db.AuthHandoffTokens.AnyAsync());
    }

    // ---- Geteilte Anmeldung ueber beide Oberflaechen ----

    [Fact]
    public async Task Login_LeavesASharedSessionCookieOnTheParentDomain()
    {
        // Ohne dieses Cookie muesste man sich auf der Turnierseite ein zweites Mal anmelden:
        // eigene Subdomain, eigener localStorage.
        await _controller.Register(new RegisterDto { Username = "u", Email = "u@t.com", Password = "Password1!" });
        _http.Response.Headers.Remove("Set-Cookie");

        await _controller.Login(new LoginDto { Username = "u", Password = "Password1!" });

        var cookie = SetCookieHeader("rh_session", "/api/auth/rh-session");
        Assert.NotNull(cookie);
        Assert.False(IsDeletion(cookie));
        Assert.Contains("domain=.example.test", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_ScopesTheSharedCookieToItsOwnPath_AndClearsTheOldOne()
    {
        // N6-001: mit Path=/api/auth schickte der Browser das 30-Tage-Cookie an JEDEN Host der Elterndomaene,
        // der unter /api/auth eine eigene Anmeldung hat (Dev-Stacks, RCT, Lernkompass, Cal.com). Gueltig
        // ausgegeben wird es nur noch fuer /api/auth/rh-session; ein altes Cookie mit /api/auth wird geloescht.
        await _controller.Register(new RegisterDto { Username = "u", Email = "u@t.com", Password = "Password1!" });
        _http.Response.Headers.Remove("Set-Cookie");

        await _controller.Login(new LoginDto { Username = "u", Password = "Password1!" });

        var live = _http.Response.Headers.SetCookie
            .Where(h => h?.StartsWith("rh_session=") == true && !IsDeletion(h)).ToList();
        var only = Assert.Single(live);
        Assert.Equal(only, SetCookieHeader("rh_session", "/api/auth/rh-session"));
        Assert.True(IsDeletion(SetCookieHeader("rh_session", "/api/auth")),
            "Das alte Cookie (Path=/api/auth) muss mit abgelaufenem Datum geloescht werden.");
        Assert.Contains("domain=.example.test", SetCookieHeader("rh_session", "/api/auth"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SharedSession_RedeemsAnOldPathCookie_AndMovesItToTheNewPath()
    {
        // Uebergang: wer sich vor N6-001 angemeldet hat, hat nur das alte Cookie. Der Browser schickt es an
        // /api/auth/rh-session mit (Pfad-Praefix) — der Tausch klappt, und danach liegt es am neuen Pfad.
        await _controller.Register(new RegisterDto { Username = "u", Email = "u@t.com", Password = "Password1!" });
        var user = await _db.AppUsers.FirstAsync();
        _http.Request.Headers.Cookie = $"rh_session={await _shared.IssueAsync(user.Id)}";
        _http.Response.Headers.Remove("Set-Cookie");

        var result = await _controller.LegacySharedSession(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(user.Id, Assert.IsType<AuthResponseDto>(ok.Value).UserId);
        Assert.False(IsDeletion(SetCookieHeader("rh_session", "/api/auth/rh-session")));
        Assert.NotNull(SetCookieHeader("rh_session", "/api/auth/rh-session"));
        Assert.True(IsDeletion(SetCookieHeader("rh_session", "/api/auth")));
    }

    [Fact]
    public async Task SharedSession_WithBothCookies_TakesTheNewPathOne()
    {
        // Browser listen Cookies mit laengerem Pfad zuerst (RFC 6265 5.4) — liegt noch ein altes, untaugliches
        // daneben, darf das die gueltige neue Anmeldung nicht kippen.
        await _controller.Register(new RegisterDto { Username = "u", Email = "u@t.com", Password = "Password1!" });
        var user = await _db.AppUsers.FirstAsync();
        _http.Request.Headers.Cookie = $"rh_session={await _shared.IssueAsync(user.Id)}; rh_session=alt.und.kaputt";

        var result = await _controller.SharedSession(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(user.Id, Assert.IsType<AuthResponseDto>(ok.Value).UserId);
    }

    [Fact]
    public async Task SharedSession_TurnsTheCookieIntoAnOwnLogin()
    {
        await _controller.Register(new RegisterDto { Username = "u", Email = "u@t.com", Password = "Password1!" });
        var user = await _db.AppUsers.FirstAsync();
        _http.Request.Headers.Cookie = $"rh_session={await _shared.IssueAsync(user.Id)}";

        var result = await _controller.SharedSession(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var res = Assert.IsType<AuthResponseDto>(ok.Value);
        Assert.Equal(user.Id, res.UserId);
        Assert.False(string.IsNullOrWhiteSpace(res.Token));
    }

    [Fact]
    public async Task SharedSession_WithoutACookieIsSimplyNoContent()
    {
        // Kein 401: jeder App-Start ohne Anmeldung fragt hier, und ein 401 zaehlt fuer die
        // Ueberwachung als abgelehnter Anmeldeversuch.
        var result = await _controller.SharedSession(CancellationToken.None);

        Assert.IsType<NoContentResult>(result.Result);
        // Und es wird auch nichts geloescht, was gar nicht da war.
        Assert.Null(SetCookieHeader("rh_session"));
    }

    [Fact]
    public async Task SharedSession_ThrowsAwayACookieThatNoLongerWorks()
    {
        // Sonst fragt jede Seite bei jedem Start erneut danach und bekommt 30 Tage lang dieselbe
        // Absage.
        _http.Request.Headers.Cookie = "rh_session=voelliger.unsinn";

        var result = await _controller.SharedSession(CancellationToken.None);

        Assert.IsType<NoContentResult>(result.Result);
        var cookie = SetCookieHeader("rh_session");
        Assert.NotNull(cookie);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndSharedSession_DeletesTheCookieOnTheSameDomainAndPath()
    {
        // Mit abweichender Domaene/Pfad legte der Browser ein ZWEITES an und das alte bliebe liegen.
        var result = _controller.EndSharedSession();

        Assert.IsType<NoContentResult>(result);
        var cookie = SetCookieHeader("rh_session", "/api/auth/rh-session");
        Assert.True(IsDeletion(cookie));
        Assert.Contains("domain=.example.test", cookie, StringComparison.OrdinalIgnoreCase);
        // ... und das alte Cookie (Pfad /api/auth, vor N6-001) gleich mit.
        Assert.True(IsDeletion(SetCookieHeader("rh_session", "/api/auth")));
    }

    [Fact]
    public void LegacyEndSharedSession_DeletesBothCookies()
    {
        // Eine Oberflaeche aus dem Browser-Cache meldet sich ueber den alten Pfad ab — auch das neue Cookie muss
        // dann weg, sonst meldete die naechste Seite den Nutzer gleich wieder an.
        var result = _controller.LegacyEndSharedSession();

        Assert.IsType<NoContentResult>(result);
        Assert.True(IsDeletion(SetCookieHeader("rh_session", "/api/auth/rh-session")));
        Assert.True(IsDeletion(SetCookieHeader("rh_session", "/api/auth")));
    }
}
