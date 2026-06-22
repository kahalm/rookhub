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
        var weekly = new WeeklyPostService(_db, NullLogger<WeeklyPostService>.Instance);
        _crawlerHandler = new RoutingHttpMessageHandler(defaultBody: "{}");
        var crawler = new CrawlerProxyService(new HttpClient(_crawlerHandler) { BaseAddress = new Uri("http://localhost:8080") });
        _service = new BotStatsService(_db, new TrainingGoalService(_db), puzzles, weekly, crawler);
    }

    private readonly RoutingHttpMessageHandler _crawlerHandler;

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
        Assert.Null(dto.WeeklyPost);   // kein Wochenpost vorhanden
    }

    // Trainings-PGN mit [%tqu] → 1 Puzzle.
    private const string TrainingPgn = "[Event \"WP\"]\n[Round \"1.1\"]\n" +
        "[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n\n" +
        "{ [%tqu \"En\",\"Finde den Zug\"] Pointe. } 2.Nf3 Nc6 3. Bb5 *";

    [Fact]
    public async Task GetPlayerProgress_IncludesWeeklyPostBlockWithUserProgress()
    {
        var user = await CreateLinkedUserAsync("12345");
        var post = new WeeklyPost
        {
            Title = "Woche 1", FileName = "w.pgn", PgnContent = TrainingPgn, FileSize = 10,
            ScheduledAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),   // fällig (Vergangenheit)
        };
        _db.WeeklyPosts.Add(post);
        await _db.SaveChangesAsync();
        // User hat das (einzige) Puzzle gespielt → erledigt.
        _db.WeeklyPostAttempts.Add(new WeeklyPostAttempt
        {
            WeeklyPostId = post.Id, UserId = user.Id, PuzzleIndex = 0, Solved = true, AttemptedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var controller = BuildController(Secret, ValidHeader("12345"));
        var ok = Assert.IsType<OkObjectResult>((await controller.GetPlayerProgress("12345")).Result);
        var dto = Assert.IsType<BotPlayerProgressDto>(ok.Value);

        Assert.NotNull(dto.WeeklyPost);
        Assert.Equal(post.Id, dto.WeeklyPost!.Id);
        Assert.Equal("Woche 1", dto.WeeklyPost.Title);
        Assert.Equal(1, dto.WeeklyPost.Total);
        Assert.Equal(1, dto.WeeklyPost.PlayedCount);
        Assert.Equal(1, dto.WeeklyPost.SolvedCount);
        Assert.True(dto.WeeklyPost.Completed);
    }

    [Fact]
    public async Task GetPlayerProgress_FinishedTournament_IncludesPlayerResult()
    {
        var user = await CreateLinkedUserAsync("12345");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = user.Id, CrawlerTournamentId = "T100", TournamentName = "Stadtmeisterschaft",
            EventDate = today.AddDays(-1),
        });
        _db.TournamentFavorites.Add(new TournamentFavorite
        {
            UserId = user.Id, CrawlerTournamentId = "T100", PlayerSnr = 3,
        });
        await _db.SaveChangesAsync();

        // Punkte = kumulativer Stand → finaler Wert ist der der höchsten Runde (2,5 / 3 Partien).
        const string results = """
        [
          {"roundNumber":1,"result":"1","points":"1"},
          {"roundNumber":2,"result":"½","points":"1.5"},
          {"roundNumber":3,"result":"1","points":"2,5"}
        ]
        """;
        _crawlerHandler
            .Map("/players/3/results", results)
            .Map("tournaments/T100", """{"location":"Innsbruck"}""");

        var controller = BuildController(Secret, ValidHeader("12345"));
        var ok = Assert.IsType<OkObjectResult>((await controller.GetPlayerProgress("12345")).Result);
        var dto = Assert.IsType<BotPlayerProgressDto>(ok.Value);

        var t = Assert.Single(dto.Tournaments);
        Assert.Equal("Stadtmeisterschaft", t.Name);
        Assert.Equal("finished", t.Status);
        Assert.Equal(-1, t.DaysUntil);
        Assert.Equal("Innsbruck", t.Location);
        Assert.Equal(3, t.ResultGames);
        Assert.Equal(2.5, t.ResultPoints!.Value);
    }

    [Fact]
    public async Task GetPlayerProgress_UpcomingTournament_NoResultLookup()
    {
        var user = await CreateLinkedUserAsync("12345");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = user.Id, CrawlerTournamentId = "T200", TournamentName = "Frühjahrs-Open",
            EventDate = today.AddDays(3),
        });
        await _db.SaveChangesAsync();
        _crawlerHandler
            .Map("/players/", "[]")
            .Map("tournaments/T200", """{"location":"Wien"}""");

        var controller = BuildController(Secret, ValidHeader("12345"));
        var ok = Assert.IsType<OkObjectResult>((await controller.GetPlayerProgress("12345")).Result);
        var dto = Assert.IsType<BotPlayerProgressDto>(ok.Value);

        var t = Assert.Single(dto.Tournaments);
        Assert.Equal("upcoming", t.Status);
        Assert.Equal(3, t.DaysUntil);
        Assert.Equal("Wien", t.Location);
        Assert.Equal(0, t.ResultGames);
        Assert.Null(t.ResultPoints);
        Assert.False(_crawlerHandler.Hits.ContainsKey("/players/")); // kein Ergebnis-Call für anstehend
    }

    [Fact]
    public async Task GetPlayerProgress_OutsideWindowTournament_NotIncluded()
    {
        var user = await CreateLinkedUserAsync("12345");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = user.Id, CrawlerTournamentId = "T300", TournamentName = "Längst vorbei",
            EventDate = today.AddDays(-30),
        });
        await _db.SaveChangesAsync();

        var controller = BuildController(Secret, ValidHeader("12345"));
        var ok = Assert.IsType<OkObjectResult>((await controller.GetPlayerProgress("12345")).Result);
        var dto = Assert.IsType<BotPlayerProgressDto>(ok.Value);

        Assert.Empty(dto.Tournaments);
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
