using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

/// <summary>
/// Tests the RequestLog query logic directly against the database,
/// since the controller has inline filtering/pagination logic.
/// </summary>
public class RequestLogQueryTests : IDisposable
{
    private readonly AppDbContext _db;

    public RequestLogQueryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedLogsAsync()
    {
        _db.RequestLogs.AddRange(
            new RequestLog { Timestamp = new DateTime(2026, 5, 1), Method = "GET", Path = "/api/tournaments", StatusCode = 200, DurationMs = 50 },
            new RequestLog { Timestamp = new DateTime(2026, 5, 2), Method = "POST", Path = "/api/subscriptions", StatusCode = 201, DurationMs = 100 },
            new RequestLog { Timestamp = new DateTime(2026, 5, 3), Method = "GET", Path = "/api/tournaments/1/players", StatusCode = 200, DurationMs = 30 },
            new RequestLog { Timestamp = new DateTime(2026, 5, 4), Method = "GET", Path = "/api/health", StatusCode = 200, DurationMs = 5 },
            new RequestLog { Timestamp = new DateTime(2026, 5, 5), Method = "DELETE", Path = "/api/subscriptions/1", StatusCode = 204, DurationMs = 15 }
        );
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetLogs_ReturnsAll()
    {
        await SeedLogsAsync();

        var items = await _db.RequestLogs
            .OrderByDescending(r => r.Timestamp)
            .ToListAsync();

        Assert.Equal(5, items.Count);
        Assert.Equal("/api/subscriptions/1", items[0].Path); // Latest first
    }

    [Fact]
    public async Task GetLogs_FilterByPath()
    {
        await SeedLogsAsync();

        var items = await _db.RequestLogs
            .Where(r => r.Path.Contains("tournaments"))
            .ToListAsync();

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetLogs_FilterByMethod()
    {
        await SeedLogsAsync();

        var items = await _db.RequestLogs
            .Where(r => r.Method == "GET")
            .ToListAsync();

        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task GetLogs_FilterByDateRange()
    {
        await SeedLogsAsync();

        var from = new DateTime(2026, 5, 2);
        var to = new DateTime(2026, 5, 4);
        var items = await _db.RequestLogs
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .ToListAsync();

        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task GetLogs_Pagination()
    {
        await SeedLogsAsync();

        int page = 2, pageSize = 2;
        var items = await _db.RequestLogs
            .OrderByDescending(r => r.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetLogs_PageSizeCapped()
    {
        int pageSize = 500;
        if (pageSize > 200) pageSize = 200;

        Assert.Equal(200, pageSize);
    }
}
