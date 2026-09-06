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

        _authService = new AuthService(_db, config, NullLogger<AuthService>.Instance);
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
    public async Task Login_ReturnsUnauthorized_WithNonexistentUser()
    {
        var result = await _controller.Login(new LoginDto
        {
            Username = "nonexistent",
            Password = "Password1!"
        });

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    // ---- Forgot / Reset Password ----

    [Fact]
    public async Task ForgotPassword_ReturnsOk_EvenForUnknownEmail()
    {
        var result = await _controller.ForgotPassword(new ForgotPasswordDto { Email = "nobody@test.com" });

        Assert.IsType<OkObjectResult>(result);
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

    // ---- Geteilte Anmeldung ueber beide Oberflaechen ----

    [Fact]
    public async Task Login_LeavesASharedSessionCookieOnTheParentDomain()
    {
        // Ohne dieses Cookie muesste man sich auf der Turnierseite ein zweites Mal anmelden:
        // eigene Subdomain, eigener localStorage.
        await _controller.Register(new RegisterDto { Username = "u", Email = "u@t.com", Password = "Password1!" });
        _http.Response.Headers.Remove("Set-Cookie");

        await _controller.Login(new LoginDto { Username = "u", Password = "Password1!" });

        var cookie = SetCookieHeader("rh_session");
        Assert.NotNull(cookie);
        Assert.Contains("domain=.example.test", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
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
    public async Task SharedSession_WithoutACookieIsSimply401()
    {
        var result = await _controller.SharedSession(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
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

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
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
        var cookie = SetCookieHeader("rh_session");
        Assert.NotNull(cookie);
        Assert.Contains("domain=.example.test", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
