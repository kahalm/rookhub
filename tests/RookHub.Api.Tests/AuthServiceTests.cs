using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AuthServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuthService _authService;
    private readonly IConfiguration _config;
    private readonly CapturingLogger<AuthService> _logger = new();

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "TestSecretKeyThatIsAtLeast32Characters!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience"
            })
            .Build();

        _authService = new AuthService(_db, _config, _logger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Register_CreatesUserAndReturnsToken()
    {
        var dto = new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" };

        var result = await _authService.RegisterAsync(dto);

        Assert.Equal("testuser", result.Username);
        Assert.NotEmpty(result.Token);
        Assert.True(result.UserId > 0);
        Assert.Single(_db.AppUsers);
    }

    [Fact]
    public async Task Register_NotifiesAllAdmins_WithUsername()
    {
        // Zwei Admins + ein Nicht-Admin -> nur die Admins sollen benachrichtigt werden.
        _db.AppUsers.AddRange(
            new RookHub.Api.Models.AppUser { Username = "admin1", PasswordHash = "x", IsAdmin = true },
            new RookHub.Api.Models.AppUser { Username = "admin2", PasswordHash = "x", IsAdmin = true },
            new RookHub.Api.Models.AppUser { Username = "plebs", PasswordHash = "x", IsAdmin = false });
        await _db.SaveChangesAsync();
        var adminIds = _db.AppUsers.Where(u => u.IsAdmin).Select(u => u.Id).ToList();

        var sut = new AuthService(_db, _config, _logger, new NotificationService(_db));
        await sut.RegisterAsync(new RegisterDto { Username = "newbie", Password = "password123" });

        var notifs = _db.Notifications
            .Where(n => n.Type == RookHub.Api.Models.NotificationType.NewUserRegistered).ToList();
        Assert.Equal(2, notifs.Count);
        Assert.All(notifs, n => Assert.Contains(n.UserId, adminIds));
        Assert.All(notifs, n => Assert.Equal("/admin", n.Link));
        Assert.All(notifs, n => Assert.Contains("newbie", n.DataJson));
    }

    [Fact]
    public async Task Register_WithoutNotificationService_StillSucceeds()
    {
        // Best-effort: ohne NotificationService (optionaler Ctor-Param) bleibt Registrierung intakt.
        var result = await _authService.RegisterAsync(
            new RegisterDto { Username = "solo", Password = "password123" });
        Assert.True(result.UserId > 0);
    }

    [Fact]
    public async Task Register_DuplicateUsername_Throws()
    {
        var dto = new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" };
        await _authService.RegisterAsync(dto);

        var dto2 = new RegisterDto { Username = "testuser", Email = "test2@example.com", Password = "password123" };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.RegisterAsync(dto2));
        Assert.Equal("Username or email already in use.", ex.Message); // generisch, kein Enumeration-Oracle
    }

    [Fact]
    public async Task Register_DuplicateEmail_Throws()
    {
        var dto = new RegisterDto { Username = "user1", Email = "test@example.com", Password = "password123" };
        await _authService.RegisterAsync(dto);

        var dto2 = new RegisterDto { Username = "user2", Email = "test@example.com", Password = "password123" };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.RegisterAsync(dto2));
        Assert.Equal("Username or email already in use.", ex.Message); // identisch zum Username-Fall -> kein Oracle
    }

    [Fact]
    public async Task Register_WithoutEmail_Succeeds()
    {
        var dto = new RegisterDto { Username = "noemail", Email = null, Password = "password123" };

        var result = await _authService.RegisterAsync(dto);

        Assert.Equal("noemail", result.Username);
        Assert.NotEmpty(result.Token);
        Assert.Null(_db.AppUsers.Single().Email); // leer -> null gespeichert
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Register_BlankEmail_StoresNull(string? email)
    {
        var dto = new RegisterDto { Username = "blank", Email = email, Password = "password123" };

        await _authService.RegisterAsync(dto);

        Assert.Null(_db.AppUsers.Single().Email);
    }

    [Fact]
    public async Task Register_MultipleUsersWithoutEmail_AllSucceed()
    {
        // Ohne Email darf es keine Dublettenpruefung geben (mehrere NULLs erlaubt).
        await _authService.RegisterAsync(new RegisterDto { Username = "a", Email = null, Password = "password123" });
        await _authService.RegisterAsync(new RegisterDto { Username = "b", Email = "", Password = "password123" });

        Assert.Equal(2, _db.AppUsers.Count());
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" });

        var result = await _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "password123" });

        Assert.Equal("testuser", result.Username);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task Login_RememberMe_IssuesLongerLivedToken()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" });

        var normal = await _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "password123", RememberMe = false });
        var remember = await _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "password123", RememberMe = true });

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var normalExp = handler.ReadJwtToken(normal.Token).ValidTo;
        var rememberExp = handler.ReadJwtToken(remember.Token).ValidTo;

        // Ohne „Eingeloggt bleiben": ~30 Tage; mit: ~90 Tage.
        Assert.True(normalExp > DateTime.UtcNow.AddDays(29), "normales Token sollte mindestens 29 Tage gültig sein");
        Assert.True(normalExp < DateTime.UtcNow.AddDays(31), "normales Token sollte höchstens 31 Tage gültig sein");
        Assert.True(rememberExp > DateTime.UtcNow.AddDays(89), "Remember-Me-Token sollte mindestens 89 Tage gültig sein");
        Assert.True(rememberExp < DateTime.UtcNow.AddDays(91), "Remember-Me-Token sollte höchstens 91 Tage gültig sein");
    }

    [Fact]
    public async Task Impersonate_ReturnsTargetIdentity_WithImpClaim_AndShortLifetime()
    {
        var target = await _authService.RegisterAsync(new RegisterDto { Username = "victim", Email = "v@example.com", Password = "password123" });
        const int adminId = 999;

        var res = await _authService.ImpersonateAsync(adminId, "rootadmin", target.UserId);

        Assert.True(res.Impersonating);
        Assert.Equal("rootadmin", res.ImpersonatorUsername);
        Assert.Equal(target.UserId, res.UserId);
        Assert.Equal("victim", res.Username);
        Assert.False(res.IsAdmin);

        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(res.Token);
        // Token trägt die Zielidentität ...
        Assert.Equal(target.UserId.ToString(), jwt.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        // ... + den imp-Claim (Admin-Id) zur Nachvollziehbarkeit.
        Assert.Equal(adminId.ToString(), jwt.Claims.First(c => c.Type == "imp").Value);
        // Kurzlebig (~2 h), nicht 30 Tage.
        Assert.True(jwt.ValidTo < DateTime.UtcNow.AddHours(3), "Impersonation-Token sollte kurzlebig sein");

        // Audit-Log wird geschrieben, aber auf Information (nicht Warning) — sonst verfälscht
        // der legitime Admin-Vorgang die Warn-Rate (log-watcher warn_spike).
        var audit = Assert.Single(_logger.Events, e => e.Message.Contains("Impersonation"));
        Assert.Equal(LogLevel.Information, audit.Level);
    }

    [Fact]
    public async Task Impersonate_Self_Throws()
    {
        var u = await _authService.RegisterAsync(new RegisterDto { Username = "admin", Email = "a@example.com", Password = "password123" });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _authService.ImpersonateAsync(u.UserId, "admin", u.UserId));
    }

    [Fact]
    public async Task Impersonate_UnknownUser_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _authService.ImpersonateAsync(1, "admin", 4242));
    }

    [Fact]
    public async Task Login_InvalidPassword_Throws()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "wrongpassword" }));
    }

    [Fact]
    public async Task Login_NonexistentUser_Throws()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginDto { Username = "nobody", Password = "password123" }));
    }

    [Fact]
    public async Task Login_UsernameIsCaseInsensitive()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "TestUser", Email = "t@example.com", Password = "password123" });

        var result = await _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "password123" });

        Assert.Equal("TestUser", result.Username);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task Register_UsernameCollisionIsCaseInsensitive_Throws()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "Admin", Email = "a@example.com", Password = "password123" });

        var dup = new RegisterDto { Username = "admin", Email = "other@example.com", Password = "password123" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.RegisterAsync(dup));
    }

    [Fact]
    public async Task Login_ValidCredentials_LogsUserLoginWithUserIdAndName()
    {
        var reg = await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" });

        await _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "password123" });

        // Kibana zaehlt Logins ueber messageTemplate "UserLogin" und Unique Logins
        // ueber fields.UserId -> beide Properties muessen im Log-Event stecken.
        var ev = Assert.Single(_logger.Events, e => e.Message.Contains("UserLogin"));
        Assert.Equal(reg.UserId, Assert.IsType<int>(ev.State["UserId"]));
        Assert.Equal("testuser", ev.State["UserName"]);
    }

    [Fact]
    public async Task Login_InvalidPassword_DoesNotLogUserLogin()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "wrongpassword" }));

        Assert.DoesNotContain(_logger.Events, e => e.Message.Contains("UserLogin"));
    }

    [Fact]
    public async Task ChangePassword_ValidCurrentPassword_UpdatesHash()
    {
        var reg = await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "t@example.com", Password = "oldpass" });
        var hashBefore = _db.AppUsers.Single().PasswordHash;

        await _authService.ChangePasswordAsync(reg.UserId, new ChangePasswordDto { CurrentPassword = "oldpass", NewPassword = "newpass99" });

        var hashAfter = _db.AppUsers.Single().PasswordHash;
        Assert.NotEqual(hashBefore, hashAfter);
        Assert.True(BCrypt.Net.BCrypt.Verify("newpass99", hashAfter));
    }

    [Fact]
    public async Task Register_SetsSecurityStamp()
    {
        var reg = await _authService.RegisterAsync(new RegisterDto { Username = "stamped", Email = "s@example.com", Password = "password123" });
        var stamp = _db.AppUsers.Single(u => u.Id == reg.UserId).SecurityStamp;
        Assert.False(string.IsNullOrEmpty(stamp));
    }

    [Fact]
    public async Task ChangePassword_RotatesSecurityStamp()
    {
        var reg = await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "t@example.com", Password = "oldpass" });
        var before = _db.AppUsers.Single().SecurityStamp;

        await _authService.ChangePasswordAsync(reg.UserId, new ChangePasswordDto { CurrentPassword = "oldpass", NewPassword = "newpass99" });

        var after = _db.AppUsers.Single().SecurityStamp;
        Assert.False(string.IsNullOrEmpty(after));
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task Login_BackfillsMissingSecurityStamp()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "legacy", Email = "l@example.com", Password = "password123" });
        // Alt-Bestand simulieren: Stempel entfernen.
        var u = _db.AppUsers.Single();
        u.SecurityStamp = null;
        await _db.SaveChangesAsync();

        await _authService.LoginAsync(new LoginDto { Username = "legacy", Password = "password123" });

        Assert.False(string.IsNullOrEmpty(_db.AppUsers.Single().SecurityStamp));
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Throws()
    {
        var reg = await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "t@example.com", Password = "oldpass" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.ChangePasswordAsync(reg.UserId, new ChangePasswordDto { CurrentPassword = "wrongpass", NewPassword = "newpass99" }));
    }

    [Fact]
    public async Task ChangePassword_AfterChange_OldPasswordRejected()
    {
        var reg = await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "t@example.com", Password = "oldpass" });
        await _authService.ChangePasswordAsync(reg.UserId, new ChangePasswordDto { CurrentPassword = "oldpass", NewPassword = "newpass99" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "oldpass" }));
    }

    [Fact]
    public async Task ChangePassword_AfterChange_NewPasswordAccepted()
    {
        var reg = await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "t@example.com", Password = "oldpass" });
        await _authService.ChangePasswordAsync(reg.UserId, new ChangePasswordDto { CurrentPassword = "oldpass", NewPassword = "newpass99" });

        var result = await _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "newpass99" });
        Assert.NotEmpty(result.Token);
    }

    // Minimaler ILogger, der Events samt strukturierter Properties mitschreibt.
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public sealed record Entry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State);

        public List<Entry> Events { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var dict = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
                foreach (var kv in kvps)
                    dict[kv.Key] = kv.Value;
            Events.Add(new Entry(logLevel, formatter(state, exception), dict));
        }
    }
}
