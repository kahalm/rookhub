using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class BotStatsControllerTests : IDisposable
{
    private const string Secret = "shared_bot_stats_secret_value";
    private readonly AppDbContext _db;
    private readonly BotStatsService _service;

    public BotStatsControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var puzzles = new PuzzleService(_db, new MemoryCache(new MemoryCacheOptions()), NullLogger<PuzzleService>.Instance);
        _service = new BotStatsService(_db, new TrainingGoalService(_db), puzzles);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> CreateLinkedUserAsync(string discordId, string username = "spieler")
    {
        var u = new AppUser
        {
            Username = username,
            Email = $"{username}@t.com",
            PasswordHash = "h",
            Profile = new UserProfile { DiscordId = discordId, DisplayName = "Anzeige" },
        };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    private BotStatsController BuildController(string? secret, string? signatureHeader)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SchachBot:StatsSecret"] = secret })
            .Build();
        var controller = new BotStatsController(_service, config, NullLogger<BotStatsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        if (signatureHeader != null)
            controller.ControllerContext.HttpContext.Request.Headers["X-Bot-Signature"] = signatureHeader;
        return controller;
    }

    private static string ValidHeader(string discordId)
        => "sha256=" + SchachBotWebhookService.ComputeHmacHex(Secret, discordId);

    [Fact]
    public async Task GetPlayerProgress_ValidSignatureLinkedUser_ReturnsProgress()
    {
        await CreateLinkedUserAsync("12345");
        var controller = BuildController(Secret, ValidHeader("12345"));

        var result = await controller.GetPlayerProgress("12345");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BotPlayerProgressDto>(ok.Value);
        Assert.Equal("spieler", dto.Username);
        Assert.Equal("Anzeige", dto.DisplayName);
        Assert.NotNull(dto.Today);
        Assert.NotNull(dto.Puzzles);
    }

    [Fact]
    public async Task GetPlayerProgress_WrongSignature_ReturnsUnauthorized()
    {
        await CreateLinkedUserAsync("12345");
        var controller = BuildController(Secret, "sha256=deadbeef");

        var result = await controller.GetPlayerProgress("12345");

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetPlayerProgress_MissingSignature_ReturnsUnauthorized()
    {
        await CreateLinkedUserAsync("12345");
        var controller = BuildController(Secret, null);

        var result = await controller.GetPlayerProgress("12345");

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetPlayerProgress_UnlinkedDiscordId_ReturnsNotFound()
    {
        // Signatur korrekt, aber kein verknüpftes Konto.
        var controller = BuildController(Secret, ValidHeader("99999"));

        var result = await controller.GetPlayerProgress("99999");

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetPlayerProgress_NoSecretConfigured_ReturnsNotFound()
    {
        await CreateLinkedUserAsync("12345");
        // Feature deaktiviert (kein Secret) → Endpoint verhält sich wie nicht vorhanden,
        // ohne die Signatur überhaupt zu prüfen.
        var controller = BuildController(secret: "", ValidHeader("12345"));

        var result = await controller.GetPlayerProgress("12345");

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
