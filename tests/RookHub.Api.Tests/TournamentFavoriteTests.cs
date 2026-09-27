using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace RookHub.Api.Tests;

public class TournamentFavoriteTests : IDisposable
{
    private readonly AppDbContext _db;

    public TournamentFavoriteTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string username = "testuser")
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Profile = new UserProfile()
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    /// <param name="playersJson">Was der Crawler als Teilnehmerliste liefert — fuer den automatischen
    /// Abgleich beim Laden eines Turniers.</param>
    private TournamentFavoriteController CreateController(int userId, string playersJson = "[]")
    {
        var crawler = new CrawlerProxyService(new HttpClient(new PlayersHandler(playersJson))
        {
            BaseAddress = new Uri("http://localhost:8080"),
        });
        var autoFavorites = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        var controller = new TournamentFavoriteController(_db, autoFavorites, crawler);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                }, "test"))
            }
        };
        return controller;
    }

    #region GetAll

    [Fact]
    public async Task GetAll_ReturnsFavorites()
    {
        var userId = await CreateUserAsync();
        _db.TournamentFavorites.AddRange(
            new TournamentFavorite { UserId = userId, CrawlerTournamentId = "100", PlayerSnr = 1 },
            new TournamentFavorite { UserId = userId, CrawlerTournamentId = "100", PlayerSnr = 2 }
        );
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.GetAll();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var favs = Assert.IsType<List<TournamentFavoriteDto>>(okResult.Value);
        Assert.Equal(2, favs.Count);
    }

    [Fact]
    public async Task GetAll_FilterByTournament()
    {
        var userId = await CreateUserAsync();
        _db.TournamentFavorites.AddRange(
            new TournamentFavorite { UserId = userId, CrawlerTournamentId = "100", PlayerSnr = 1 },
            new TournamentFavorite { UserId = userId, CrawlerTournamentId = "200", PlayerSnr = 1 }
        );
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.GetAll(tournamentId: "100");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var favs = Assert.IsType<List<TournamentFavoriteDto>>(okResult.Value);
        Assert.Single(favs);
        Assert.Equal("100", favs[0].CrawlerTournamentId);
    }

    #endregion

    #region Player Favorites

    [Fact]
    public async Task CreatePlayerFavorite_AddsFavorite()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.Create(new CreateTournamentFavoriteDto
        {
            CrawlerTournamentId = "100", PlayerSnr = 5
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TournamentFavoriteDto>(okResult.Value);
        Assert.Equal(5, dto.PlayerSnr);
        Assert.Single(await _db.TournamentFavorites.ToListAsync());
    }

    [Fact]
    public async Task CreatePlayerFavorite_Duplicate_ReturnsConflict()
    {
        var userId = await CreateUserAsync();
        _db.TournamentFavorites.Add(new TournamentFavorite
        {
            UserId = userId, CrawlerTournamentId = "100", PlayerSnr = 5
        });
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.Create(new CreateTournamentFavoriteDto
        {
            CrawlerTournamentId = "100", PlayerSnr = 5
        });

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeleteFavorite_RemovesFavorite()
    {
        var userId = await CreateUserAsync();
        var fav = new TournamentFavorite { UserId = userId, CrawlerTournamentId = "100", PlayerSnr = 5 };
        _db.TournamentFavorites.Add(fav);
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.Delete(fav.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _db.TournamentFavorites.ToListAsync());
    }

    [Fact]
    public async Task DeleteFavorite_NotFound()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.Delete(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteByPlayer_RemovesFavorite()
    {
        var userId = await CreateUserAsync();
        _db.TournamentFavorites.Add(new TournamentFavorite
        {
            UserId = userId, CrawlerTournamentId = "100", PlayerSnr = 5
        });
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.DeleteByPlayer("100", 5);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _db.TournamentFavorites.ToListAsync());
    }

    [Fact]
    public async Task DeleteByPlayer_NotFound()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.DeleteByPlayer("100", 99);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    #endregion

    #region Team Favorites

    [Fact]
    public async Task CreateTeamFavorite_AddsFavorite()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.CreateTeamFavorite(new CreateTeamFavoriteDto
        {
            CrawlerTournamentId = "100", TeamSnr = 3
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TournamentFavoriteDto>(okResult.Value);
        Assert.Equal(3, dto.TeamSnr);
    }

    [Fact]
    public async Task CreateTeamFavorite_Duplicate_ReturnsConflict()
    {
        var userId = await CreateUserAsync();
        _db.TournamentFavorites.Add(new TournamentFavorite
        {
            UserId = userId, CrawlerTournamentId = "100", TeamSnr = 3
        });
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.CreateTeamFavorite(new CreateTeamFavoriteDto
        {
            CrawlerTournamentId = "100", TeamSnr = 3
        });

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeleteByTeam_RemovesFavorite()
    {
        var userId = await CreateUserAsync();
        _db.TournamentFavorites.Add(new TournamentFavorite
        {
            UserId = userId, CrawlerTournamentId = "100", TeamSnr = 3
        });
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.DeleteByTeam("100", 3);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _db.TournamentFavorites.ToListAsync());
    }

    [Fact]
    public async Task DeleteByTeam_NotFound()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.DeleteByTeam("100", 99);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    #endregion

    #region Settings

    [Fact]
    public async Task GetSettings_DefaultFalse()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.GetSettings("100");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        Assert.Contains("\"showFavoritesOnly\":false", json);
    }

    [Fact]
    public async Task SaveSettings_CreatesNew()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.SaveSettings("100", new TournamentSettingsDto { ShowFavoritesOnly = true });

        Assert.IsType<OkObjectResult>(result);
        var setting = await _db.TournamentUserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
        Assert.NotNull(setting);
        Assert.True(setting.ShowFavoritesOnly);
    }

    [Fact]
    public async Task SaveSettings_UpdatesExisting()
    {
        var userId = await CreateUserAsync();
        _db.TournamentUserSettings.Add(new TournamentUserSetting
        {
            UserId = userId, CrawlerTournamentId = "100", ShowFavoritesOnly = true
        });
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        await controller.SaveSettings("100", new TournamentSettingsDto { ShowFavoritesOnly = false });

        var setting = await _db.TournamentUserSettings.FirstAsync(s => s.UserId == userId);
        Assert.False(setting.ShowFavoritesOnly);
    }

    #endregion

    [Fact]
    public async Task SaveSettings_OverlongTournamentId_IsBadRequest_NotServerError()
    {
        // Der Route-Parameter kam ungeprüft in eine Spalte mit MaxLength(50): MariaDB warf beim
        // Speichern „Data too long", der Aufrufer bekam 500 und der log-watcher einen Fehlalarm.
        var userId = await CreateUserAsync("settings-user");
        var controller = CreateController(userId);

        var result = await controller.SaveSettings(new string('9', 60), new TournamentSettingsDto { ShowFavoritesOnly = true });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(_db.TournamentUserSettings);
    }

    [Fact]
    public async Task GetSettings_InvalidTournamentId_IsBadRequest()
    {
        var userId = await CreateUserAsync("settings-user2");
        var controller = CreateController(userId);

        Assert.IsType<BadRequestObjectResult>(await controller.GetSettings("../../etc/passwd"));
    }

    #region Automatische Favoriten

    private sealed class PlayersHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private const string OlympiadPlayers =
        """[{"snr":11,"name":"Martinovic, Sasa","fideId":"14502828"},{"snr":12,"name":"Anders, Jemand","fideId":"1"}]""";

    private async Task TrackMartinovicAsync(int userId)
    {
        _db.TrackedPlayers.Add(new TrackedPlayer
        {
            UserId = userId, PlayerKey = "fide:14502828", DisplayName = "Martinovic, Sasa",
            LastName = "Martinovic", FirstName = "Sasa", FideId = "14502828",
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Ein verfolgter Spieler ist in jedem Turnier, in dem er spielt, schon Favorit, sobald man das
    /// Turnier oeffnet — auch ohne Abo und ohne naechtlichen Lauf.
    /// </summary>
    [Fact]
    public async Task GetAll_ForTournament_FavoritesTrackedPlayerAutomatically()
    {
        var userId = await CreateUserAsync();
        await TrackMartinovicAsync(userId);

        var result = await CreateController(userId, OlympiadPlayers).GetAll("1469895");

        var favs = Assert.IsType<List<TournamentFavoriteDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(11, Assert.Single(favs).PlayerSnr);
    }

    /// <summary>
    /// Wer den automatischen Stern entfernt, will ihn nicht beim naechsten Oeffnen zurueck — und
    /// wer ihn wieder setzt, nimmt das Entfernen zurueck.
    /// </summary>
    [Fact]
    public async Task RemovedAutoFavorite_StaysRemoved_UntilSetAgain()
    {
        var userId = await CreateUserAsync();
        await TrackMartinovicAsync(userId);
        await CreateController(userId, OlympiadPlayers).GetAll("1469895");

        Assert.IsType<NoContentResult>(await CreateController(userId).DeleteByPlayer("1469895", 11));
        var again = await CreateController(userId, OlympiadPlayers).GetAll("1469895");
        Assert.Empty(Assert.IsType<List<TournamentFavoriteDto>>(Assert.IsType<OkObjectResult>(again.Result).Value));
        Assert.Single(await _db.TournamentFavoriteDismissals.ToListAsync());

        await CreateController(userId).Create(new CreateTournamentFavoriteDto { CrawlerTournamentId = "1469895", PlayerSnr = 11 });
        Assert.Empty(await _db.TournamentFavoriteDismissals.ToListAsync());
        Assert.Single(await _db.TournamentFavorites.ToListAsync());
    }

    /// <summary>Ohne Turnier (die Liste ueber alle) wird nichts abgeglichen — und der Crawler nicht gefragt.</summary>
    [Fact]
    public async Task GetAll_WithoutTournament_DoesNotAutoFavorite()
    {
        var userId = await CreateUserAsync();
        await TrackMartinovicAsync(userId);

        await CreateController(userId, OlympiadPlayers).GetAll();

        Assert.Empty(await _db.TournamentFavorites.ToListAsync());
    }

    #endregion
}
