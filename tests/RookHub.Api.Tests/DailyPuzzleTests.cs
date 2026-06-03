using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Tests fuer den persistierten Daily-Puzzle-Mechanismus (Endpoint, Service, Race).</summary>
public class DailyPuzzleTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly BookPuzzleService _service;
    private readonly BookPuzzleController _controller;

    public DailyPuzzleTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue());
        _controller = new BookPuzzleController(_service);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> CreateDailyBookAsync(string name = "daily-book")
    {
        var book = new Book
        {
            FileName = name + ".pgn",
            DisplayName = name,
            ForDaily = true,
            ForRandom = false,
            ForBlind = false
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    private async Task<BookPuzzle> AddPuzzleAsync(Book book, string lineId, string round = "1")
    {
        var puzzle = new BookPuzzle
        {
            LineId = lineId,
            BookFileName = book.FileName,
            BookId = book.Id,
            Round = round,
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
            Moves = "e7e5",
        };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        return puzzle;
    }

    [Fact]
    public async Task GetOrAssignDailyAsync_AssignsAndPersists_FirstCall()
    {
        var book = await CreateDailyBookAsync();
        await AddPuzzleAsync(book, "d.pgn:1");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = await _service.GetOrAssignDailyAsync(today);

        Assert.NotEqual(0, dto.Id);
        var saved = await _db.DailyPuzzles.SingleAsync();
        Assert.Equal(today, saved.Date);
        Assert.Equal(dto.Id, saved.BookPuzzleId);
    }

    [Fact]
    public async Task GetOrAssignDailyAsync_StableAcrossCalls_SamePuzzleForSameDate()
    {
        var book = await CreateDailyBookAsync();
        await AddPuzzleAsync(book, "d.pgn:1");
        await AddPuzzleAsync(book, "d.pgn:2");
        await AddPuzzleAsync(book, "d.pgn:3");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = await _service.GetOrAssignDailyAsync(today);
        var second = await _service.GetOrAssignDailyAsync(today);
        var third = await _service.GetOrAssignDailyAsync(today);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Id, third.Id);
        Assert.Equal(1, await _db.DailyPuzzles.CountAsync());
    }

    [Fact]
    public async Task GetOrAssignDailyAsync_DifferentDates_DifferentRows()
    {
        var book = await CreateDailyBookAsync();
        await AddPuzzleAsync(book, "d.pgn:1");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);
        await _service.GetOrAssignDailyAsync(today);
        await _service.GetOrAssignDailyAsync(yesterday);

        Assert.Equal(2, await _db.DailyPuzzles.CountAsync());
    }

    [Fact]
    public async Task GetOrAssignDailyAsync_FutureDate_Throws()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.GetOrAssignDailyAsync(future));
    }

    [Fact]
    public async Task GetOrAssignDailyAsync_EmptyPool_Throws()
    {
        // Buch existiert, aber ForDaily=false → kein Daily-Pool.
        var book = new Book
        {
            FileName = "no-daily.pgn",
            DisplayName = "No Daily",
            ForDaily = false
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        await AddPuzzleAsync(book, "n.pgn:1");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.GetOrAssignDailyAsync(DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [Fact]
    public async Task GetOrAssignDailyAsync_OnlyPicksFromForDailyBooks()
    {
        var dailyBook = await CreateDailyBookAsync("only-daily");
        var dailyPuzzle = await AddPuzzleAsync(dailyBook, "d.pgn:1");

        var randomBook = new Book { FileName = "rand.pgn", DisplayName = "Rand", ForDaily = false, ForRandom = true };
        _db.Books.Add(randomBook);
        await _db.SaveChangesAsync();
        await AddPuzzleAsync(randomBook, "r.pgn:1");
        await AddPuzzleAsync(randomBook, "r.pgn:2");

        var dto = await _service.GetOrAssignDailyAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(dailyPuzzle.Id, dto.Id);
    }

    [Fact]
    public async Task GetDaily_Endpoint_ParsesYyyyMMdd_ReturnsDto()
    {
        var book = await CreateDailyBookAsync();
        await AddPuzzleAsync(book, "d.pgn:1");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dateStr = today.ToString("yyyyMMdd");

        var result = await _controller.GetDaily(dateStr) as OkObjectResult;
        Assert.NotNull(result);
        var dto = Assert.IsType<BookPuzzleDto>(result.Value);
        Assert.Equal("d.pgn:1", dto.LineId);
    }

    [Fact]
    public async Task GetDaily_Endpoint_TodayLiteral_Works()
    {
        var book = await CreateDailyBookAsync();
        await AddPuzzleAsync(book, "d.pgn:today");

        var result = await _controller.GetDaily("today") as OkObjectResult;
        Assert.NotNull(result);
        Assert.IsType<BookPuzzleDto>(result.Value);
    }

    [Fact]
    public async Task GetDaily_Endpoint_InvalidDate_Returns400()
    {
        var result = await _controller.GetDaily("not-a-date");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetDaily_Endpoint_FutureDate_Returns400()
    {
        var book = await CreateDailyBookAsync();
        await AddPuzzleAsync(book, "d.pgn:1");

        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7).ToString("yyyyMMdd");
        var result = await _controller.GetDaily(future);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetDaily_Endpoint_NoDailyPool_Returns404()
    {
        var book = new Book { FileName = "x.pgn", DisplayName = "X", ForDaily = false };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        await AddPuzzleAsync(book, "x.pgn:1");

        var result = await _controller.GetDaily("today");
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void DailyPuzzleScheduler_TimeUntilNextMidnightUtc_AlwaysWithin24h()
    {
        // 23:59:00 → ~01:01 (1 minute + Puffer)
        var d1 = DailyPuzzleScheduler.TimeUntilNextMidnightUtc(new DateTime(2026, 6, 3, 23, 59, 0, DateTimeKind.Utc));
        Assert.InRange(d1.TotalSeconds, 60, 65);

        // 00:00:30 → ~23h59m30s
        var d2 = DailyPuzzleScheduler.TimeUntilNextMidnightUtc(new DateTime(2026, 6, 3, 0, 0, 30, DateTimeKind.Utc));
        Assert.InRange(d2.TotalHours, 23.9, 24.1);

        // Mitternacht selbst → ~24h
        var d3 = DailyPuzzleScheduler.TimeUntilNextMidnightUtc(new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc));
        Assert.InRange(d3.TotalHours, 23.9, 24.1);
    }

    [Fact]
    public async Task GetRandomAsync_PoolDaily_RoutesThroughPersistence()
    {
        var book = await CreateDailyBookAsync();
        var only = await AddPuzzleAsync(book, "only.pgn:1");

        // pool=daily soll ueber GetOrAssignDailyAsync gehen → persistierter Eintrag.
        var dto = await _service.GetRandomAsync("daily", exclude: null, bookId: null);
        Assert.Equal(only.Id, dto.Id);
        Assert.Equal(1, await _db.DailyPuzzles.CountAsync());
    }
}
