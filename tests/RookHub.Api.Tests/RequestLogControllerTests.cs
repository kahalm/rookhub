using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

public class RequestLogControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RequestLogController _controller;

    public RequestLogControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _controller = new RequestLogController(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedLogsAsync()
    {
        _db.RequestLogs.AddRange(
            new RequestLog
            {
                Timestamp = DateTime.UtcNow.AddMinutes(-10),
                Method = "GET",
                Path = "/api/puzzles/random",
                UserName = "alice",
                StatusCode = 200,
                DurationMs = 50
            },
            new RequestLog
            {
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
                Method = "POST",
                Path = "/api/auth/login",
                UserName = null,
                StatusCode = 401,
                DurationMs = 20
            },
            new RequestLog
            {
                Timestamp = DateTime.UtcNow.AddMinutes(-1),
                Method = "GET",
                Path = "/api/friends",
                UserName = "bob",
                StatusCode = 200,
                DurationMs = 30
            }
        );
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetLogs_ReturnsAll()
    {
        await SeedLogsAsync();

        var result = await _controller.GetLogs(null, null, null, null, null, null, 1, 50) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var totalCount = (int)data.GetType().GetProperty("totalCount")!.GetValue(data)!;
        Assert.Equal(3, totalCount);
    }

    [Fact]
    public async Task GetLogs_FilterByPath()
    {
        await SeedLogsAsync();

        var result = await _controller.GetLogs(null, null, "puzzles", null, null, null, 1, 50) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var totalCount = (int)data.GetType().GetProperty("totalCount")!.GetValue(data)!;
        Assert.Equal(1, totalCount);
    }

    [Fact]
    public async Task GetLogs_FilterByMethod()
    {
        await SeedLogsAsync();

        var result = await _controller.GetLogs(null, null, null, "POST", null, null, 1, 50) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var totalCount = (int)data.GetType().GetProperty("totalCount")!.GetValue(data)!;
        Assert.Equal(1, totalCount);
    }

    [Fact]
    public async Task GetLogs_FilterByUserName()
    {
        await SeedLogsAsync();

        var result = await _controller.GetLogs(null, null, null, null, "alice", null, 1, 50) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var totalCount = (int)data.GetType().GetProperty("totalCount")!.GetValue(data)!;
        Assert.Equal(1, totalCount);
    }

    [Fact]
    public async Task GetLogs_FilterByMinStatus()
    {
        await SeedLogsAsync();

        var result = await _controller.GetLogs(null, null, null, null, null, 400, 1, 50) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var totalCount = (int)data.GetType().GetProperty("totalCount")!.GetValue(data)!;
        Assert.Equal(1, totalCount);
    }

    [Fact]
    public async Task GetLogs_FilterByDateRange()
    {
        await SeedLogsAsync();

        var from = DateTime.UtcNow.AddMinutes(-6);
        var to = DateTime.UtcNow;
        var result = await _controller.GetLogs(from, to, null, null, null, null, 1, 50) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var totalCount = (int)data.GetType().GetProperty("totalCount")!.GetValue(data)!;
        Assert.Equal(2, totalCount);
    }

    [Fact]
    public async Task GetLogs_Pagination()
    {
        await SeedLogsAsync();

        var result = await _controller.GetLogs(null, null, null, null, null, null, 1, 2) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var items = data.GetType().GetProperty("items")!.GetValue(data) as System.Collections.IList;
        Assert.Equal(2, items!.Count);
        var pageSize = (int)data.GetType().GetProperty("pageSize")!.GetValue(data)!;
        Assert.Equal(2, pageSize);
    }

    [Fact]
    public async Task GetLogs_ClampsPageSize()
    {
        await SeedLogsAsync();

        var result = await _controller.GetLogs(null, null, null, null, null, null, 1, 999) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var pageSize = (int)data.GetType().GetProperty("pageSize")!.GetValue(data)!;
        Assert.Equal(200, pageSize);
    }

    [Fact]
    public async Task GetLogs_ClampsNegativePage()
    {
        await SeedLogsAsync();

        var result = await _controller.GetLogs(null, null, null, null, null, null, -1, 50) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var page = (int)data.GetType().GetProperty("page")!.GetValue(data)!;
        Assert.Equal(1, page);
    }

    [Fact]
    public async Task GetLogs_TruncatesLongPath()
    {
        await SeedLogsAsync();
        var longPath = new string('x', 300);

        // Should not throw - path gets truncated internally
        var result = await _controller.GetLogs(null, null, longPath, null, null, null, 1, 50) as OkObjectResult;

        Assert.NotNull(result);
    }
}
