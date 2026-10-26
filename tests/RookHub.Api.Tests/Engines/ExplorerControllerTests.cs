using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Controllers.Engines;
using RookHub.Api.DTOs.Repertoires;

namespace RookHub.Api.Tests.Engines;

/// <summary>Explorer auf dem Analysebrett: nur die Übersetzung der Abfrage-Parameter — die
/// Datenstrecke prüft <see cref="RepertoireExplorerServiceTests"/>.</summary>
public class ExplorerControllerTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private ExplorerController Controller(ExplorerOnlineQuota? quota = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" })
            .Build();
        var gate = new LichessExplorerGate(refillInterval: TimeSpan.Zero);
        var service = new RepertoireExplorerService(_db, TestServices.Repertoire(_db),
            new LichessExplorerClient(new HttpClient(), gate, NullLogger<LichessExplorerClient>.Instance),
            new LocalExplorerClient(new HttpClient(), NullLogger<LocalExplorerClient>.Instance),
            new MemoryCache(new MemoryCacheOptions()), gate, new EncryptionService(config), config,
            NullLogger<RepertoireExplorerService>.Instance);
        var services = new ServiceCollection()
            .AddSingleton(new RateLimitRejectionHandler(NullLogger<RateLimitRejectionHandler>.Instance))
            .BuildServiceProvider();
        return new ExplorerController(service, quota)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Test")),
                    RequestServices = services,
                },
            },
        };
    }

    [Theory]
    [InlineData("1800,abc", "blitz")]
    [InlineData("1750", "blitz")]
    [InlineData("1800", "hyper")]
    public async Task Position_BadSelection_Is400(string ratings, string speeds)
    {
        var r = await DomainHttp.ResultAsync(async () => (await Controller().Position(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            null, "lichess", ratings, speeds, CancellationToken.None)).Result);
        DomainHttp.AssertError(r, 400);
    }

    [Fact]
    public async Task Position_LocalWithoutLocalExplorer_Is400()
    {
        var r = await DomainHttp.ResultAsync(async () => (await Controller().Position(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "local", "masters", null, null, CancellationToken.None)).Result);
        DomainHttp.AssertError(r, 400, "Der lokale Explorer ist auf diesem Server nicht eingerichtet.");
    }

    [Fact]
    public async Task Position_NoToken_IsOkWithStatus()
    {
        var r = await Controller().Position("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            null, "lichess", " 1800 , 2000 ", "blitz,rapid", CancellationToken.None);
        var dto = Assert.IsType<ExplorerPositionResultDto>(Assert.IsType<OkObjectResult>(r.Result).Value);
        Assert.Equal("tokenMissing", dto.Status);
    }

    [Fact]
    public async Task Games_BadSelection_Is400_AndNoToken_IsOkWithStatus()
    {
        const string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        DomainHttp.AssertError(await DomainHttp.ResultAsync(async () =>
            (await Controller().Games(fen, null, "lichess", "1750", "blitz", CancellationToken.None)).Result), 400, "Unbekannte Elo-Stufe.");
        var dto = Assert.IsType<ExplorerGamesResultDto>(Assert.IsType<OkObjectResult>(
            (await Controller().Games(fen, null, "masters", null, null, CancellationToken.None)).Result).Value);
        Assert.Equal("tokenMissing", dto.Status);
    }

    // ---- N7-003: Deckel je Konto für die Online-Quelle ----

    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static ExplorerOnlineQuota Quota(int perMinute) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Explorer:OnlinePerAccountPerMinute"] = perMinute.ToString() })
        .Build());

    [Fact]
    public async Task Position_Online_IsCappedPerAccount_With429()
    {
        var quota = Quota(2);
        var c = Controller(quota);
        Assert.IsType<OkObjectResult>((await c.Position(Start, null, "lichess", "1800", "blitz", CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await c.Position(Start, "online", "lichess", "1800", "blitz", CancellationToken.None)).Result);

        var third = await c.Games(Start, null, "lichess", "1800", "blitz", CancellationToken.None);

        Assert.IsNotType<OkObjectResult>(third.Result);
        Assert.Equal(429, c.HttpContext.Response.StatusCode);
        Assert.True(c.HttpContext.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public void Quota_LocalSourceIsNeverCapped_AndAccountsAreSeparate()
    {
        var quota = Quota(1);
        Assert.True(quota.TryAcquire(7, null, out _));
        Assert.False(quota.TryAcquire(7, "online", out var retryAfter));
        Assert.NotNull(retryAfter);
        Assert.True(quota.TryAcquire(7, "local", out _));     // lokale Quelle unberührt
        Assert.True(quota.TryAcquire(8, "online", out _));    // anderes Konto, eigener Deckel
    }
}
