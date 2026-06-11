using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

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
                ["App:BaseUrl"] = "https://rookhub.example"
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

    private sealed class CapturingEmailSender : IEmailSender
    {
        public string? LastTo { get; private set; }
        public string? LastText { get; private set; }
        public bool IsEnabled => true;

        public Task SendAsync(string to, string subject, string html, string text, CancellationToken ct = default)
        {
            LastTo = to;
            LastText = text;
            return Task.CompletedTask;
        }
    }
}
