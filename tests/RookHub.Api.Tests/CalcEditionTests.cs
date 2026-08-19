using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class CalcEditionTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CalcEditionService _editions;
    private readonly CalculationService _calc;
    private const int OwnerId = 5, ViewerId = 6;

    public CalcEditionTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(opts);
        _editions = new CalcEditionService(_db);
        _calc = new CalculationService(_db);
    }
    public void Dispose() => _db.Dispose();

    private async Task<int> SeedBookAsync()
    {
        _db.AppUsers.Add(new AppUser { Id = OwnerId, Username = "owner", PasswordHash = "x" });
        _db.AppUsers.Add(new AppUser { Id = ViewerId, Username = "viewer", PasswordHash = "x" });
        var book = new Book { FileName = "noel.pgn", DisplayName = "Noel", IsCalculation = true, IsPublic = true, OwnerUserId = OwnerId };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        void Pos(string chapter, string round) => _db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = $"noel:{round}", BookFileName = "noel.pgn", BookId = book.Id, Round = round,
            Fen = "8/8/8/8/8/8/8/8 w - - 0 1", Moves = "", Chapter = chapter, IsInfoOnly = true,
        });
        Pos("Woche A", "001"); Pos("Woche A", "002");
        Pos("Woche B", "003"); Pos("Woche B", "004");
        await _db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task Upsert_CreatesThenUpdates_ByChapter()
    {
        var bookId = await SeedBookAsync();
        var future = DateTime.UtcNow.AddDays(2);
        var a = await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", VideoUrl = "https://yt/1", PublishAt = future });
        var b = await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", VideoUrl = "https://yt/2", PublishAt = future });
        Assert.Equal(a.Id, b.Id);                  // Upsert je Kapitel → dieselbe Ausgabe
        Assert.Equal("https://yt/2", b.VideoUrl);
        Assert.Equal(1, await _db.CalcEditions.CountAsync());
    }

    [Fact]
    public async Task ListVisible_OnlyReleased()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche A", PublishAt = DateTime.UtcNow.AddDays(-1) });
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(1) });
        var vis = await _editions.ListVisibleAsync(bookId);
        Assert.Single(vis);
        Assert.Equal("Woche A", vis[0].Chapter);
        Assert.True(vis[0].Released);
    }

    [Fact]
    public async Task GetBook_HidesFutureWeek_ForViewer_ButNotOwner()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(2) });

        var viewer = await _calc.GetBookAsync(ViewerId, bookId, isAdmin: false);
        Assert.DoesNotContain(viewer.Positions, p => p.Chapter == "Woche B");   // Entwurf ausgeblendet
        Assert.Contains(viewer.Positions, p => p.Chapter == "Woche A");         // freie Woche sichtbar

        var owner = await _calc.GetBookAsync(OwnerId, bookId, isAdmin: false);
        Assert.Contains(owner.Positions, p => p.Chapter == "Woche B");          // Besitzer sieht alles
    }

    [Fact]
    public async Task GetPublicBook_HidesFutureWeek()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(2) });
        var pub = await _calc.GetPublicBookAsync(bookId);
        Assert.DoesNotContain(pub.Positions, p => p.Chapter == "Woche B");
        Assert.Contains(pub.Positions, p => p.Chapter == "Woche A");
    }

    [Fact]
    public async Task GetPosition_HiddenWeek_NotFoundForViewer_OkForOwner()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(2) });
        var wb = await _db.BookPuzzles.FirstAsync(p => p.Chapter == "Woche B");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _calc.GetPositionAsync(ViewerId, wb.Id, isAdmin: false));
        var owner = await _calc.GetPositionAsync(OwnerId, wb.Id, isAdmin: false);
        Assert.Equal(wb.Id, owner.Id);
    }

    [Fact]
    public async Task CanManage_OwnerAndAdminOnly()
    {
        var bookId = await SeedBookAsync();
        Assert.True(await _editions.CanManageAsync(OwnerId, bookId, isAdmin: false));
        Assert.False(await _editions.CanManageAsync(ViewerId, bookId, isAdmin: false));
        Assert.True(await _editions.CanManageAsync(ViewerId, bookId, isAdmin: true));
    }
}
