using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AdminControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AdminController _controller;

    public AdminControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _controller = new AdminController(_db, new PuzzleService(_db));
        SetUser(99);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private async Task<AppUser> CreateUserAsync(string username, bool isAdmin = false)
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@test.com",
            PasswordHash = "hash",
            IsAdmin = isAdmin
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GetUsers_ReturnsAllUsers()
    {
        await CreateUserAsync("alice");
        await CreateUserAsync("bob");

        var result = await _controller.GetUsers(null, 1, 20) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var totalCount = (int)data.GetType().GetProperty("totalCount")!.GetValue(data)!;
        Assert.Equal(2, totalCount);
    }

    [Fact]
    public async Task GetUsers_SearchFilter_ReturnsMatching()
    {
        await CreateUserAsync("alice");
        await CreateUserAsync("bob");

        var result = await _controller.GetUsers("ali", 1, 20) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var totalCount = (int)data.GetType().GetProperty("totalCount")!.GetValue(data)!;
        Assert.Equal(1, totalCount);
    }

    [Fact]
    public async Task GetUsers_Pagination()
    {
        await CreateUserAsync("user1");
        await CreateUserAsync("user2");
        await CreateUserAsync("user3");

        var result = await _controller.GetUsers(null, 2, 2) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var items = data.GetType().GetProperty("items")!.GetValue(data) as System.Collections.IList;
        Assert.Single(items!);
    }

    [Fact]
    public async Task DeleteUser_RemovesUser()
    {
        var user = await CreateUserAsync("target");

        var result = await _controller.DeleteUser(user.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _db.AppUsers.FindAsync(user.Id));
    }

    [Fact]
    public async Task DeleteUser_Self_ReturnsBadRequest()
    {
        var self = await CreateUserAsync("self");
        SetUser(self.Id);

        var result = await _controller.DeleteUser(self.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteUser_NotFound()
    {
        var result = await _controller.DeleteUser(9999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ToggleAdmin_TogglesFlag()
    {
        var user = await CreateUserAsync("target", isAdmin: false);

        var result = await _controller.ToggleAdmin(user.Id) as OkObjectResult;

        Assert.NotNull(result);
        var updated = await _db.AppUsers.FindAsync(user.Id);
        Assert.True(updated!.IsAdmin);
    }

    [Fact]
    public async Task ToggleAdmin_Self_ReturnsBadRequest()
    {
        var self = await CreateUserAsync("self");
        SetUser(self.Id);

        var result = await _controller.ToggleAdmin(self.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ToggleAdmin_NotFound()
    {
        var result = await _controller.ToggleAdmin(9999);

        Assert.IsType<NotFoundResult>(result);
    }
}
