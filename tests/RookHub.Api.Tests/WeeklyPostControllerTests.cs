using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

public class WeeklyPostControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly WeeklyPostController _controller;

    public WeeklyPostControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _controller = new WeeklyPostController(_db);
    }

    public void Dispose() => _db.Dispose();

    private const string ValidPgn = "[Event \"Test\"]\n[White \"A\"]\n[Black \"B\"]\n\n1. e4 e5 2. Nf3 *";

    private static IFormFile MakePgnFile(string content, string name = "MyGame.pgn")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream",
        };
    }

    private static T Unwrap<T>(IActionResult result) where T : class =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(result).Value!);

    [Fact]
    public async Task GetAll_ReturnsPostsSortedByScheduledDesc()
    {
        await _controller.Create(MakePgnFile(ValidPgn, "alt.pgn"), new DateTime(2026, 6, 1, 19, 0, 0), null, default);
        await _controller.Create(MakePgnFile(ValidPgn, "neu.pgn"), new DateTime(2026, 6, 8, 19, 0, 0), null, default);

        var list = Unwrap<List<WeeklyPostDto>>(await _controller.GetAll());

        Assert.Equal(2, list.Count);
        Assert.Equal(new DateTime(2026, 6, 8, 19, 0, 0), list[0].ScheduledAt);   // neueste zuerst
        Assert.Equal(new DateTime(2026, 6, 1, 19, 0, 0), list[1].ScheduledAt);
    }

    [Fact]
    public async Task Create_ValidPgn_StoresWithDefaultTitleFromFileName()
    {
        var res = Unwrap<WeeklyPostDto>(
            await _controller.Create(MakePgnFile(ValidPgn, "Taktik_Woche_1.pgn"), new DateTime(2026, 6, 8, 19, 0, 0), null, default));

        Assert.Equal("Taktik Woche 1", res.Title);            // .pgn entfernt, _ -> Leerzeichen
        Assert.Equal(new DateTime(2026, 6, 8, 19, 0, 0), res.ScheduledAt);
        Assert.True(res.FileSize > 0);

        var detail = Unwrap<WeeklyPostDetailDto>(await _controller.GetById(res.Id));
        Assert.Contains("1. e4", detail.PgnContent);
    }

    [Fact]
    public async Task Create_ExplicitTitle_IsUsed()
    {
        var res = Unwrap<WeeklyPostDto>(
            await _controller.Create(MakePgnFile(ValidPgn), new DateTime(2026, 6, 8, 19, 0, 0), "Mein Titel", default));
        Assert.Equal("Mein Titel", res.Title);
    }

    [Fact]
    public async Task Create_InvalidPgn_ReturnsBadRequest()
    {
        var res = await _controller.Create(MakePgnFile("kein gueltiges pgn hier"), new DateTime(2026, 6, 8, 19, 0, 0), null, default);
        Assert.IsType<BadRequestObjectResult>(res);
        Assert.Equal(0, await _db.WeeklyPosts.CountAsync());
    }

    [Fact]
    public async Task Create_NoFile_ReturnsBadRequest()
    {
        var res = await _controller.Create(null!, new DateTime(2026, 6, 8, 19, 0, 0), null, default);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        Assert.IsType<NotFoundObjectResult>(await _controller.GetById(999));
    }

    [Fact]
    public async Task Update_ChangesTitleAndSchedule()
    {
        var created = Unwrap<WeeklyPostDto>(
            await _controller.Create(MakePgnFile(ValidPgn), new DateTime(2026, 6, 8, 19, 0, 0), null, default));

        var newDate = new DateTime(2026, 6, 15, 19, 0, 0);
        var updated = Unwrap<WeeklyPostDto>(
            await _controller.Update(created.Id, new UpdateWeeklyPostDto { Title = "Neu", ScheduledAt = newDate }));

        Assert.Equal("Neu", updated.Title);
        Assert.Equal(newDate, updated.ScheduledAt);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        Assert.IsType<NotFoundObjectResult>(
            await _controller.Update(999, new UpdateWeeklyPostDto { Title = "x" }));
    }

    [Fact]
    public async Task Delete_RemovesPost()
    {
        var created = Unwrap<WeeklyPostDto>(
            await _controller.Create(MakePgnFile(ValidPgn), new DateTime(2026, 6, 8, 19, 0, 0), null, default));

        Assert.IsType<NoContentResult>(await _controller.Delete(created.Id));
        Assert.Equal(0, await _db.WeeklyPosts.CountAsync());
        Assert.IsType<NotFoundObjectResult>(await _controller.Delete(created.Id));
    }
}
