using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Tests.Accounts;

public class PasswordResetServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CapturingEmailSender _email = new();
    private readonly PasswordResetService _service;

    public PasswordResetServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://rookhub.example",
                ["App:KidHubBaseUrl"] = "https://kidhub.example/",
                ["App:TurnierBaseUrl"] = "https://turnier.example",
                // App:LeagueHubBaseUrl bewusst NICHT gesetzt: Rueckfall auf App:BaseUrl.
            })
            .Build();

        _service = new PasswordResetService(_db, _email, config, NullLogger<PasswordResetService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> CreateUserAsync(string email = "user@test.com", string password = "OldPassword1!")
    {
        var user = new AppUser
        {
            Username = "resetuser",
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    // Extrahiert das Roh-Token aus dem Reset-Link in der versendeten Mail.
    private static string ExtractToken(string body)
    {
        var m = Regex.Match(body, @"reset-password\?token=([^\s""]+)");
        Assert.True(m.Success, "Reset link with token expected in email body.");
        return Uri.UnescapeDataString(m.Groups[1].Value);
    }

    [Fact]
    public async Task RequestReset_CreatesTokenAndSendsMail_ForKnownEmail()
    {
        var user = await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("known@test.com");

        Assert.Single(_db.PasswordResetTokens);
        var token = _db.PasswordResetTokens.Single();
        Assert.Equal(user.Id, token.UserId);
        Assert.Null(token.UsedAt);
        Assert.True(token.ExpiresAt > DateTime.UtcNow);
        Assert.NotNull(_email.LastTo);
        Assert.Equal("known@test.com", _email.LastTo);
        Assert.Contains("reset-password?token=", _email.LastText);
    }

    [Fact]
    public async Task RequestReset_MailNamesTheUsername()
    {
        // Der Anmeldename gehoert ausdruecklich in die Mail: die Reset-Strecke laeuft komplett
        // ueber die E-Mail, der Login verlangt aber den Benutzernamen — ohne den Hinweis
        // scheitern Nutzer nach erfolgreichem Reset endlos mit der E-Mail im Login-Feld.
        await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("known@test.com");

        Assert.Contains("Dein Benutzername für die Anmeldung lautet: resetuser", _email.LastText);
        Assert.Contains("<strong>resetuser</strong>", _email.LastHtml);
    }

    [Fact]
    public async Task RequestReset_IsCaseInsensitiveOnEmail()
    {
        await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("Known@Test.com");

        Assert.Single(_db.PasswordResetTokens);
    }

    [Fact]
    public async Task RequestReset_DoesNothing_ForUnknownEmail()
    {
        await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("unknown@test.com");

        Assert.Empty(_db.PasswordResetTokens);
        Assert.Null(_email.LastTo);
    }

    [Fact]
    public async Task RequestReset_DoesNothing_ForDeletedAccount()
    {
        var user = await CreateUserAsync("gone@test.com");
        user.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _service.RequestResetAsync("gone@test.com");

        Assert.Empty(_db.PasswordResetTokens);
    }

    [Fact]
    public async Task RequestReset_MailFailure_KeepsExistingTokensValid()
    {
        // Regression: alte Tokens wurden ENTWERTET und committet, BEVOR die Mail rausging —
        // ein SMTP-Ausfall beim zweiten Anfordern liess den User mit null gueltigen Links
        // zurueck (alter Link tot, neuer nie zugestellt). Jetzt wird erst nach erfolgreichem
        // Versand entwertet/persistiert.
        await CreateUserAsync("known@test.com");
        await _service.RequestResetAsync("known@test.com");            // Link 1 zugestellt
        var token1 = ExtractToken(_email.LastText!);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://rookhub.example" })
            .Build();
        var failingSvc = new PasswordResetService(_db, new FailingEmailSender(), config, NullLogger<PasswordResetService>.Instance);
        await failingSvc.RequestResetAsync("known@test.com");          // SMTP down

        Assert.Single(_db.PasswordResetTokens);                        // kein zweites Token persistiert
        Assert.Null(_db.PasswordResetTokens.Single().UsedAt);          // Link 1 weiterhin offen …
        await _service.ResetPasswordAsync(token1, "BrandNew1!");       // … und funktioniert noch
    }

    [Fact]
    public async Task RequestReset_InvalidatesPreviousOpenTokens()
    {
        await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("known@test.com");
        await _service.RequestResetAsync("known@test.com");

        var tokens = await _db.PasswordResetTokens.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens[0].UsedAt);   // erstes Token entwertet
        Assert.Null(tokens[1].UsedAt);      // nur das neueste gilt
    }

    [Fact]
    public async Task ResetPassword_SetsNewPasswordAndConsumesToken()
    {
        var user = await CreateUserAsync("known@test.com", "OldPassword1!");
        await _service.RequestResetAsync("known@test.com");
        var rawToken = ExtractToken(_email.LastText!);

        await _service.ResetPasswordAsync(rawToken, "BrandNewPass2!");

        var updated = await _db.AppUsers.FindAsync(user.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("BrandNewPass2!", updated!.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("OldPassword1!", updated.PasswordHash));
        Assert.NotNull(_db.PasswordResetTokens.Single().UsedAt);
    }

    [Fact]
    public async Task ResetPassword_RejectsAlreadyUsedToken()
    {
        await CreateUserAsync("known@test.com");
        await _service.RequestResetAsync("known@test.com");
        var rawToken = ExtractToken(_email.LastText!);
        await _service.ResetPasswordAsync(rawToken, "FirstNew1!");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.ResetPasswordAsync(rawToken, "SecondNew1!"));
    }

    [Fact]
    public async Task ResetPassword_RejectsExpiredToken()
    {
        var user = await CreateUserAsync("known@test.com");
        await _service.RequestResetAsync("known@test.com");
        var rawToken = ExtractToken(_email.LastText!);
        // Token kuenstlich ablaufen lassen.
        var token = _db.PasswordResetTokens.Single();
        token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.ResetPasswordAsync(rawToken, "BrandNew1!"));
    }

    [Fact]
    public async Task ResetPassword_RejectsUnknownToken()
    {
        await CreateUserAsync("known@test.com");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.ResetPasswordAsync("totally-made-up", "BrandNew1!"));
    }

    // ---- Admin-Link (UX-002 / P-2): Konten ohne E-Mail -----------------------------------------

    private async Task<AppUser> CreateUserWithoutEmailAsync(bool isAdmin = false)
    {
        var user = new AppUser
        {
            Username = "nomail",
            Email = null,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword1!"),
            IsAdmin = isAdmin,
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task AdminLink_StoresOnlyTheHash_ExpiresIn24h_SendsNoMail()
    {
        var user = await CreateUserWithoutEmailAsync();

        var before = DateTime.UtcNow;
        var (link, expiresAt) = await _service.IssueAdminResetLinkAsync(user.Id, actorId: 1, actorIsAdmin: true);

        Assert.StartsWith("https://rookhub.example/reset-password?token=", link);
        var raw = ExtractToken(link);
        var token = Assert.Single(_db.PasswordResetTokens);
        Assert.Equal(user.Id, token.UserId);
        Assert.Null(token.UsedAt);
        Assert.Equal(SecretTokens.Sha256Hex(raw), token.TokenHash);
        Assert.NotEqual(raw, token.TokenHash);
        Assert.Equal(TimeSpan.FromHours(24), PasswordResetService.AdminLinkTtl);
        Assert.InRange(token.ExpiresAt, before.AddHours(24).AddSeconds(-1), DateTime.UtcNow.AddHours(24).AddSeconds(1));
        Assert.Equal(token.ExpiresAt, expiresAt);
        Assert.Equal(0, _email.Sends); // kein Versand
    }

    [Fact]
    public async Task AdminLink_InvalidatesOpenTokens()
    {
        var user = await CreateUserAsync("known@test.com");
        await _service.RequestResetAsync("known@test.com");
        var mailToken = ExtractToken(_email.LastText!);
        _email.Reset();

        await _service.IssueAdminResetLinkAsync(user.Id, actorId: 1, actorIsAdmin: true);

        Assert.Equal(2, _db.PasswordResetTokens.Count());
        Assert.Single(_db.PasswordResetTokens.Where(t => t.UsedAt == null));
        Assert.Equal(0, _email.Sends);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.ResetPasswordAsync(mailToken, "BrandNew1!"));
    }

    [Fact]
    public async Task AdminLink_CanBeRedeemedOnce()
    {
        var user = await CreateUserWithoutEmailAsync();
        _db.UserApiTokens.Add(new UserApiToken { UserId = user.Id, Name = "ext", TokenHash = "h", Prefix = "rkh_x" });
        await _db.SaveChangesAsync();
        var (link, _) = await _service.IssueAdminResetLinkAsync(user.Id, actorId: 1, actorIsAdmin: true);
        var raw = ExtractToken(link);

        await _service.ResetPasswordAsync(raw, "BrandNew1!");

        var reloaded = await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("BrandNew1!", reloaded.PasswordHash));
        Assert.Empty(_db.UserApiTokens.Where(t => t.UserId == user.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.ResetPasswordAsync(raw, "Another1!"));
    }

    [Fact]
    public async Task AdminLink_DeletedOrUnknownUser_Throws()
    {
        var user = await CreateUserWithoutEmailAsync();
        user.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.IssueAdminResetLinkAsync(user.Id, actorId: 1, actorIsAdmin: true));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.IssueAdminResetLinkAsync(9999, actorId: 1, actorIsAdmin: true));
        Assert.Empty(_db.PasswordResetTokens);
    }

    [Fact]
    public async Task AdminLink_ForAdminAccount_OnlyByAdmin()
    {
        var admin = await CreateUserWithoutEmailAsync(isAdmin: true);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.IssueAdminResetLinkAsync(admin.Id, actorId: 1, actorIsAdmin: false));
        Assert.Empty(_db.PasswordResetTokens);

        await _service.IssueAdminResetLinkAsync(admin.Id, actorId: 1, actorIsAdmin: true);
        Assert.Single(_db.PasswordResetTokens);
    }

    private sealed class FailingEmailSender : IEmailSender
    {
        public bool IsEnabled => true;
        public Task SendAsync(string to, string subject, string html, string text, CancellationToken ct = default)
            => throw new InvalidOperationException("SMTP down");
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public string? LastTo { get; private set; }
        public string? LastSubject { get; private set; }
        public string? LastHtml { get; private set; }
        public string? LastText { get; private set; }
        public string? LastFromName { get; private set; }
        public int Sends { get; private set; }
        public bool IsEnabled => true;

        public void Reset()
        {
            LastTo = LastSubject = LastHtml = LastText = LastFromName = null;
            Sends = 0;
        }

        public Task SendAsync(string to, string subject, string html, string text, CancellationToken ct = default)
            => SendAsync(to, subject, html, text, null, ct);

        public Task SendAsync(string to, string subject, string html, string text, string? fromName, CancellationToken ct = default)
        {
            LastTo = to;
            LastSubject = subject;
            LastHtml = html;
            LastText = text;
            LastFromName = fromName;
            Sends++;
            return Task.CompletedTask;
        }
    }

    // ---- UX-031: Mail je Seite und Sprache ----

    [Fact]
    public async Task RequestReset_WithoutSite_StaysRookHubAndGerman()
    {
        // Ohne site/lang bleibt alles wie bisher: RookHub-Link, deutscher RookHub-Betreff, konfigurierter Absender.
        await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("known@test.com");

        Assert.Equal("RookHub — Passwort zurücksetzen", _email.LastSubject);
        Assert.Contains("https://rookhub.example/reset-password?token=", _email.LastText);
        Assert.Contains("für dein RookHub-Konto wurde", _email.LastText);
        Assert.EndsWith("— RookHub", _email.LastText);
        Assert.Null(_email.LastFromName);
    }

    [Fact]
    public async Task RequestReset_FromKidHub_LinksToKidHub_NamesKidHub_InTheUiLanguage()
    {
        // Szenario UX-031: eine Mutter setzt auf kidhub.* (Magyar) das Passwort zurueck und sucht im Postfach
        // nach „KidHub" — bisher kam „RookHub — Passwort zurücksetzen" auf Deutsch mit Link auf rookhub.*.
        await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("known@test.com", site: "kidhub", lang: "hu");

        Assert.Equal("KidHub — Reset your password", _email.LastSubject);
        Assert.Equal("KidHub", _email.LastFromName);
        Assert.Contains("https://kidhub.example/reset-password?token=", _email.LastText);
        Assert.DoesNotContain("rookhub.example", _email.LastText);
        Assert.Contains("the same account as on RookHub", _email.LastText);
        Assert.Contains("https://kidhub.example/reset-password?token=", _email.LastHtml);
    }

    [Fact]
    public async Task RequestReset_FromKidHub_InGerman()
    {
        await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("known@test.com", site: "KidHub", lang: "de-AT");

        Assert.Equal("KidHub — Passwort zurücksetzen", _email.LastSubject);
        Assert.Equal("KidHub", _email.LastFromName);
        Assert.Contains("dein KidHub-Konto (dasselbe Konto wie bei RookHub)", _email.LastText);
    }

    [Fact]
    public async Task RequestReset_FromTurnier_LinksToTheTournamentSite()
    {
        await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("known@test.com", site: "turnier", lang: "en");

        Assert.Contains("https://turnier.example/reset-password?token=", _email.LastText);
        Assert.Equal("RookHub Tournaments — Reset your password", _email.LastSubject);
        Assert.Equal("RookHub Tournaments", _email.LastFromName);
    }

    [Fact]
    public async Task RequestReset_SiteWithoutOwnBaseUrl_FallsBackToAppBaseUrl()
    {
        // LeagueHub ohne App:LeagueHubBaseUrl: der Link bleibt auf RookHub (dasselbe Konto), die Mail nennt LeagueHub.
        await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("known@test.com", site: "leaguehub");

        Assert.Contains("https://rookhub.example/reset-password?token=", _email.LastText);
        Assert.Equal("LeagueHub — Passwort zurücksetzen", _email.LastSubject);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("clubhub")]
    [InlineData("")]
    public async Task RequestReset_UnknownSite_IsRookHub_NeverAForeignLink(string site)
    {
        // Kein freies URL-Feld: was nicht auf der festen Liste steht, ist RookHub.
        await CreateUserAsync("known@test.com");

        await _service.RequestResetAsync("known@test.com", site: site);

        Assert.Contains("https://rookhub.example/reset-password?token=", _email.LastText);
        Assert.DoesNotContain("evil.example", _email.LastText);
        Assert.DoesNotContain("evil.example", _email.LastHtml);
        Assert.Equal("RookHub — Passwort zurücksetzen", _email.LastSubject);
        Assert.Null(_email.LastFromName);
    }

    [Fact]
    public async Task RequestReset_SiteLink_TokenStillResetsThePassword()
    {
        var user = await CreateUserAsync("known@test.com");
        await _service.RequestResetAsync("known@test.com", site: "kidhub", lang: "en");

        await _service.ResetPasswordAsync(ExtractToken(_email.LastText!), "BrandNewPass4!");

        var reloaded = await _db.AppUsers.FindAsync(user.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("BrandNewPass4!", reloaded!.PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_RevokesApiTokens()
    {
        // Der Reset ist DER Weg nach einer Kontoübernahme. Ein vom Angreifer angelegtes, unbefristetes
        // Extension-Token kennt den Security-Stamp nicht und behielte sonst die volle Extension-Fläche
        // (Repertoire-PGNs lesen, Share-Links anlegen, schreiben).
        var user = await CreateUserAsync("pat@test.com");
        _db.UserApiTokens.Add(new UserApiToken
        {
            UserId = user.Id, Name = "attacker", TokenHash = "h", Prefix = "rkh_evil", Scope = "extension",
        });
        await _db.SaveChangesAsync();
        await _service.RequestResetAsync("pat@test.com");
        var rawToken = ExtractToken(_email.LastText!);

        await _service.ResetPasswordAsync(rawToken, "BrandNewPass3!");
        Assert.False(await _db.UserApiTokens.AnyAsync(t => t.UserId == user.Id));
    }

    /// <summary>A1-008: Zwei gleichzeitige Resets mit demselben Link — der zweite hat die Zeile noch offen geladen.
    /// Nur einer darf das Passwort setzen.</summary>
    [Fact]
    public async Task Reset_SecondRequestWithAStaleRow_IsRejected()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var dbA = new AppDbContext(options);
        await using var dbB = new AppDbContext(options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://rookhub.example" }).Build();
        var mailA = new CapturingEmailSender();
        var svcA = new PasswordResetService(dbA, mailA, config, NullLogger<PasswordResetService>.Instance);
        var svcB = new PasswordResetService(dbB, new CapturingEmailSender(), config, NullLogger<PasswordResetService>.Instance);
        dbA.AppUsers.Add(new AppUser { Username = "race", Email = "race@test.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword1!") });
        await dbA.SaveChangesAsync();
        await svcA.RequestResetAsync("race@test.com");
        var raw = ExtractToken(mailA.LastText!);
        Assert.Null((await dbA.PasswordResetTokens.Include(t => t.User).SingleAsync()).UsedAt);   // A hat die Zeile offen geladen

        await svcB.ResetPasswordAsync(raw, "FirstNewPassword1!");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svcA.ResetPasswordAsync(raw, "SecondNewPassword1!"));

        var hash = (await dbB.AppUsers.AsNoTracking().SingleAsync()).PasswordHash;
        Assert.True(BCrypt.Net.BCrypt.Verify("FirstNewPassword1!", hash));
    }
}
