using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RevengeControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RevengeNotificationService _service;
    private readonly RevengeController _controller;

    public RevengeControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new RevengeNotificationService(_db, new FriendService(_db));
        _controller = new RevengeController(_service);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
    }

    private async Task<AppUser> CreateUserAsync(string username)
    {
        var user = new AppUser { Username = username, Email = $"{username}@test.com", PasswordHash = "hash", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task MakeFriendsAsync(int a, int b)
    {
        _db.Friendships.Add(new Friendship { RequesterId = a, AddresseeId = b, Status = FriendshipStatus.Accepted });
        await _db.SaveChangesAsync();
    }

    private async Task<Puzzle> CreatePuzzleAsync(string lichessId = "p1", int rating = 1600)
    {
        var p = new Puzzle { LichessId = lichessId, Fen = "fen", Moves = "e2e4", Rating = rating, Themes = "fork" };
        _db.Puzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private async Task FailPuzzleAsync(int userId, int puzzleId)
    {
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = userId, PuzzleId = puzzleId, Solved = false, TimeSpentSeconds = 10 });
        await _db.SaveChangesAsync();
    }

    // ---- Result ----

    [Fact]
    public async Task Result_CreatesNotification_WhenFriendsAndTargetFailed()
    {
        var avenger = await CreateUserAsync("avenger");
        var target = await CreateUserAsync("target");
        await MakeFriendsAsync(avenger.Id, target.Id);
        var puzzle = await CreatePuzzleAsync();
        await FailPuzzleAsync(target.Id, puzzle.Id);

        SetUser(avenger.Id);
        var result = await _controller.Result(new RevengeResultDto { TargetUserId = target.Id, PuzzleId = puzzle.Id, Solved = true });

        Assert.IsType<OkObjectResult>(result);
        var n = Assert.Single(_db.RevengeNotifications);
        Assert.Equal(avenger.Id, n.AvengerUserId);
        Assert.Equal(target.Id, n.TargetUserId);
        Assert.True(n.Solved);
        Assert.Null(n.SeenAt);
    }

    [Fact]
    public async Task Result_RecordsFailure_Too()
    {
        var avenger = await CreateUserAsync("avenger");
        var target = await CreateUserAsync("target");
        await MakeFriendsAsync(avenger.Id, target.Id);
        var puzzle = await CreatePuzzleAsync();
        await FailPuzzleAsync(target.Id, puzzle.Id);

        SetUser(avenger.Id);
        await _controller.Result(new RevengeResultDto { TargetUserId = target.Id, PuzzleId = puzzle.Id, Solved = false });

        var n = Assert.Single(_db.RevengeNotifications);
        Assert.False(n.Solved);
    }

    [Fact]
    public async Task Result_DoesNotCreate_WhenNotFriends()
    {
        var avenger = await CreateUserAsync("avenger");
        var target = await CreateUserAsync("target");
        var puzzle = await CreatePuzzleAsync();
        await FailPuzzleAsync(target.Id, puzzle.Id);

        SetUser(avenger.Id);
        await _controller.Result(new RevengeResultDto { TargetUserId = target.Id, PuzzleId = puzzle.Id, Solved = true });

        Assert.Empty(_db.RevengeNotifications);
    }

    [Fact]
    public async Task Result_DoesNotCreate_WhenTargetNeverFailedPuzzle()
    {
        var avenger = await CreateUserAsync("avenger");
        var target = await CreateUserAsync("target");
        await MakeFriendsAsync(avenger.Id, target.Id);
        var puzzle = await CreatePuzzleAsync();
        // target hat dieses Puzzle nie versucht → keine legitime Revanche.

        SetUser(avenger.Id);
        await _controller.Result(new RevengeResultDto { TargetUserId = target.Id, PuzzleId = puzzle.Id, Solved = true });

        Assert.Empty(_db.RevengeNotifications);
    }

    // ---- Notifications / Count / Seen ----

    [Fact]
    public async Task Notifications_ReturnsForTarget_NewestFirst()
    {
        var avenger = await CreateUserAsync("avenger");
        var target = await CreateUserAsync("target");
        var puzzle = await CreatePuzzleAsync();
        _db.RevengeNotifications.Add(new RevengeNotification { AvengerUserId = avenger.Id, TargetUserId = target.Id, PuzzleId = puzzle.Id, Solved = true, CreatedAt = new DateTime(2026, 6, 1) });
        _db.RevengeNotifications.Add(new RevengeNotification { AvengerUserId = avenger.Id, TargetUserId = target.Id, PuzzleId = puzzle.Id, Solved = false, CreatedAt = new DateTime(2026, 6, 10) });
        await _db.SaveChangesAsync();

        SetUser(target.Id);
        var result = await _controller.Notifications();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<RevengeNotificationDto>>(ok.Value);
        Assert.Equal(2, list.Count);
        Assert.False(list[0].Solved);              // neueste zuerst
        Assert.Equal("avenger", list[0].AvengerUsername);
        Assert.Equal(puzzle.Rating, list[0].Rating);
    }

    [Fact]
    public async Task UnseenCount_CountsOnlyUnseenForUser()
    {
        var avenger = await CreateUserAsync("avenger");
        var target = await CreateUserAsync("target");
        var puzzle = await CreatePuzzleAsync();
        _db.RevengeNotifications.Add(new RevengeNotification { AvengerUserId = avenger.Id, TargetUserId = target.Id, PuzzleId = puzzle.Id, Solved = true });
        _db.RevengeNotifications.Add(new RevengeNotification { AvengerUserId = avenger.Id, TargetUserId = target.Id, PuzzleId = puzzle.Id, Solved = false, SeenAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        SetUser(target.Id);
        var result = await _controller.UnseenCount();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, (int)ok.Value!.GetType().GetProperty("count")!.GetValue(ok.Value)!);
    }

    [Fact]
    public async Task MarkSeen_MarksAllUnseen()
    {
        var avenger = await CreateUserAsync("avenger");
        var target = await CreateUserAsync("target");
        var puzzle = await CreatePuzzleAsync();
        _db.RevengeNotifications.Add(new RevengeNotification { AvengerUserId = avenger.Id, TargetUserId = target.Id, PuzzleId = puzzle.Id, Solved = true });
        _db.RevengeNotifications.Add(new RevengeNotification { AvengerUserId = avenger.Id, TargetUserId = target.Id, PuzzleId = puzzle.Id, Solved = false });
        await _db.SaveChangesAsync();

        SetUser(target.Id);
        await _controller.MarkSeen();

        Assert.Equal(0, await _db.RevengeNotifications.CountAsync(n => n.TargetUserId == target.Id && n.SeenAt == null));
    }
}
