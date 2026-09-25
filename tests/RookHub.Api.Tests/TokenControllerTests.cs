using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// <c>POST /api/token/test</c> in der Lichess-Form — die Vorabprüfung des Providers
/// (<c>preflight.py</c>) liest daraus <c>payload[token].scopes</c> und verlangt
/// <c>engine:read</c> + <c>engine:write</c>. Nur <c>rkh_</c>-Tokens mit Scope <c>engine</c> gelten.
/// </summary>
public class TokenControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ApiTokenService _tokens;
    private readonly TokenController _controller;

    public TokenControllerTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _tokens = new ApiTokenService(_db, NullLogger<ApiTokenService>.Instance);
        _controller = new TokenController(_tokens, _db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> UserAsync(string name = "kahalm", DateTime? deletedAt = null)
    {
        var u = new AppUser { Username = name, PasswordHash = "x", DeletedAt = deletedAt };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<Dictionary<string, TokenTestInfo?>> PostAsync(string body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.ContentType = "text/plain";
        _controller.ControllerContext = new ControllerContext { HttpContext = ctx };
        var ok = Assert.IsType<OkObjectResult>(await _controller.Test(CancellationToken.None));
        return Assert.IsType<Dictionary<string, TokenTestInfo?>>(ok.Value);
    }

    [Fact]
    public async Task EngineToken_HasTheScopesThePreflightDemands()
    {
        var uid = await UserAsync();
        var raw = (await _tokens.CreateAsync(uid, "Provider", ApiTokenService.EngineScope, null)).RawToken;

        var result = await PostAsync(raw);
        var info = Assert.IsType<TokenTestInfo>(result[raw]);
        Assert.Equal("kahalm", info.UserId);
        // preflight.py: REQUIRED_SCOPES = ("engine:read", "engine:write"), kommasepariert
        Assert.Equal(["engine:read", "engine:write"], info.Scopes.Split(','));
        Assert.Null(info.Expires);
    }

    [Fact]
    public async Task ExpiringToken_ReportsMillisecondsSinceEpoch()
    {
        var uid = await UserAsync();
        var raw = (await _tokens.CreateAsync(uid, "Provider", ApiTokenService.EngineScope, 30)).RawToken;
        var info = (await PostAsync(raw))[raw]!;
        var expected = new DateTimeOffset(DateTime.SpecifyKind(_db.UserApiTokens.Single().ExpiresAt!.Value, DateTimeKind.Utc))
            .ToUnixTimeMilliseconds();
        Assert.Equal(expected, info.Expires);
    }

    [Fact]
    public async Task ForeignScopeUnknownOrDeleted_AreNull_AndTheKeyIsTheExactToken()
    {
        var uid = await UserAsync();
        var gone = await UserAsync("gone", DateTime.UtcNow);
        var extension = (await _tokens.CreateAsync(uid, "Ext", ApiTokenService.DefaultScope, null)).RawToken;
        var orphan = (await _tokens.CreateAsync(gone, "Provider", ApiTokenService.EngineScope, null)).RawToken;
        const string unknown = "rkh_doesnotexist000000000000000000000000000000";
        const string lichess = "lip_notours";

        var result = await PostAsync($"{extension}, {unknown},{orphan},{lichess}");
        Assert.Equal(4, result.Count);
        Assert.Null(result[extension]);
        Assert.Null(result[unknown]);
        Assert.Null(result[orphan]);
        Assert.Null(result[lichess]);
    }

    [Fact]
    public async Task Check_DoesNotCountAsUse()
    {
        var uid = await UserAsync();
        var raw = (await _tokens.CreateAsync(uid, "Provider", ApiTokenService.EngineScope, null)).RawToken;
        await PostAsync(raw);
        Assert.Null(_db.UserApiTokens.Single().LastUsedAt);
    }

    [Fact]
    public async Task EmptyBody_IsAnEmptyObject()
    {
        Assert.Empty(await PostAsync(""));
    }
}
