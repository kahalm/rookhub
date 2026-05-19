using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

public class SubscriptionServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public SubscriptionServiceTests()
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

    private SubscriptionController CreateController(int userId)
    {
        var controller = new SubscriptionController(_db);
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

    [Fact]
    public async Task GetAll_ReturnsUserSubscriptions()
    {
        var userId = await CreateUserAsync();
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = "100", TournamentName = "T1"
        });
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = "200", TournamentName = "T2"
        });
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.GetAll();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var subs = Assert.IsType<List<TournamentSubscriptionDto>>(okResult.Value);
        Assert.Equal(2, subs.Count);
    }

    [Fact]
    public async Task GetAll_ReturnsEmpty_WhenNoSubscriptions()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.GetAll();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var subs = Assert.IsType<List<TournamentSubscriptionDto>>(okResult.Value);
        Assert.Empty(subs);
    }

    [Fact]
    public async Task Create_AddsSubscription()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.Create(new CreateSubscriptionDto
        {
            CrawlerTournamentId = "100", TournamentName = "Test Tournament"
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TournamentSubscriptionDto>(okResult.Value);
        Assert.Equal("100", dto.CrawlerTournamentId);
        Assert.Equal("Test Tournament", dto.TournamentName);
        Assert.Single(await _db.TournamentSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Create_Duplicate_ReturnsConflict()
    {
        var userId = await CreateUserAsync();
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = "100", TournamentName = "T1"
        });
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.Create(new CreateSubscriptionDto
        {
            CrawlerTournamentId = "100", TournamentName = "T1"
        });

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_RemovesSubscription()
    {
        var userId = await CreateUserAsync();
        var sub = new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = "100", TournamentName = "T1"
        };
        _db.TournamentSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.Delete(sub.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _db.TournamentSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Delete_NotFound_WhenWrongUser()
    {
        var userId1 = await CreateUserAsync("user1");
        var userId2 = await CreateUserAsync("user2");
        var sub = new TournamentSubscription
        {
            UserId = userId1, CrawlerTournamentId = "100", TournamentName = "T1"
        };
        _db.TournamentSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        var controller = CreateController(userId2);
        var result = await controller.Delete(sub.Id);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Delete_NotFound_WhenInvalidId()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.Delete(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
