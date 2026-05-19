using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

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

    private TournamentFavoriteController CreateController(int userId)
    {
        var controller = new TournamentFavoriteController(_db);
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
}
