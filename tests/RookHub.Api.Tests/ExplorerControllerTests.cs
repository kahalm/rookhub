using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Explorer auf dem Analysebrett: nur die Übersetzung der Abfrage-Parameter — die
/// Datenstrecke prüft <see cref="RepertoireExplorerServiceTests"/>.</summary>
public class ExplorerControllerTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private ExplorerController Controller()
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
        return new ExplorerController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Test")),
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
        var r = await Controller().Position("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            null, "lichess", ratings, speeds, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(r.Result);
    }

    [Fact]
    public async Task Position_LocalWithoutLocalExplorer_Is400()
    {
        var r = await Controller().Position("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "local", "masters", null, null, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(r.Result);
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
    public void Sources_WithoutLocal()
    {
        var dto = Assert.IsType<ExplorerSourcesDto>(Assert.IsType<OkObjectResult>(Controller().Sources().Result).Value);
        Assert.False(dto.Local);
    }
}
