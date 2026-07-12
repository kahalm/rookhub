using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Auth-Boundary der RepCheck-Extension (`Authorization: Bearer rkh_…`): Header-Parsing +
/// die Sicherheits-Branches (kein/fremdes Schema → NoResult, ungültiges Token → Fail, gelöschter
/// Besitzer → Fail, Erfolg → Ticket mit scope-Claim). War komplett ungetestet.</summary>
public class ApiTokenAuthenticationHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ApiTokenService _tokens;
    public ApiTokenAuthenticationHandlerTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(opts);
        _tokens = new ApiTokenService(_db, NullLogger<ApiTokenService>.Instance);
    }
    public void Dispose() => _db.Dispose();

    private sealed class OptMon : IOptionsMonitor<ApiTokenAuthenticationOptions>
    {
        public ApiTokenAuthenticationOptions CurrentValue { get; } = new();
        public ApiTokenAuthenticationOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ApiTokenAuthenticationOptions, string?> listener) => null;
    }

    private async Task<AuthenticateResult> Authenticate(string? header)
    {
        var handler = new ApiTokenAuthenticationHandler(new OptMon(), NullLoggerFactory.Instance, UrlEncoder.Default, _tokens, _db);
        var scheme = new AuthenticationScheme(ApiTokenAuthenticationHandler.SchemeName, null, typeof(ApiTokenAuthenticationHandler));
        var ctx = new DefaultHttpContext();
        if (header != null) ctx.Request.Headers.Authorization = header;
        await handler.InitializeAsync(scheme, ctx);
        return await handler.AuthenticateAsync();
    }

    private async Task<(int userId, string raw)> MintToken(bool deleted = false)
    {
        var u = new AppUser { Username = "ext", Email = "ext@x.com", PasswordHash = "h", DeletedAt = deleted ? DateTime.UtcNow : null };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        var created = await _tokens.CreateAsync(u.Id, "RepCheck", "extension", null);
        return (u.Id, created.RawToken);
    }

    [Fact]
    public async Task NoHeader_ReturnsNoResult()
        => Assert.True((await Authenticate(null)).None);

    [Fact]
    public async Task NonBearerOrJwt_ReturnsNoResult()
    {
        Assert.True((await Authenticate("Basic abc")).None);
        Assert.True((await Authenticate("Bearer eyJhbGciOiJIUzI1NiJ9.jwt.sig")).None); // JWT → nicht unser Schema
    }

    [Fact]
    public async Task InvalidToken_Fails()
    {
        var res = await Authenticate("Bearer rkh_thisisnotarealtoken");
        Assert.False(res.None);
        Assert.False(res.Succeeded);
        Assert.NotNull(res.Failure);
    }

    [Fact]
    public async Task DeletedOwner_Fails()
    {
        var (_, raw) = await MintToken(deleted: true);
        var res = await Authenticate($"Bearer {raw}");
        Assert.False(res.Succeeded);
        Assert.NotNull(res.Failure);
    }

    [Fact]
    public async Task ValidToken_SucceedsWithUserIdAndScopeClaims()
    {
        var (userId, raw) = await MintToken();
        var res = await Authenticate($"Bearer {raw}");
        Assert.True(res.Succeeded);
        var p = res.Principal!;
        Assert.Equal(userId.ToString(), p.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("extension", p.FindFirstValue("scope"));
    }
}
