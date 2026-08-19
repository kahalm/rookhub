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
    private const int OwnerId = 5, ViewerId = 6, TesterId = 7;

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
        _db.AppUsers.Add(new AppUser { Id = TesterId, Username = "tester", PasswordHash = "x" });
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

    [Fact]
    public async Task Members_AddUpdateRemove_ByUsername()
    {
        var bookId = await SeedBookAsync();
        Assert.Null(await _editions.UpsertMemberAsync(bookId, "does-not-exist", isTester: false)); // unbekannt → null

        var added = await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        Assert.NotNull(added);
        Assert.False(added!.IsTester);

        var updated = await _editions.UpsertMemberAsync(bookId, "VIEWER", isTester: true); // case-insensitiv + Upsert
        Assert.Equal(ViewerId, updated!.UserId);
        Assert.True(updated.IsTester);
        Assert.Equal(1, await _db.CalcSeriesMembers.CountAsync(m => m.BookId == bookId)); // kein Duplikat

        var list = await _editions.ListMembersAsync(bookId);
        Assert.Single(list);
        Assert.Equal("viewer", list[0].Username);
        Assert.True(list[0].IsTester);

        Assert.True(await _editions.RemoveMemberAsync(bookId, ViewerId));
        Assert.False(await _editions.RemoveMemberAsync(bookId, ViewerId));   // schon weg
        Assert.Empty(await _editions.ListMembersAsync(bookId));
    }

    [Fact]
    public async Task Tester_SeesTesterPreviewWeek_PlainViewerDoesNot()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto
        {
            Chapter = "Woche B",
            PublishAt = DateTime.UtcNow.AddDays(5),          // öffentliche Freigabe noch fern
            TesterPreviewAt = DateTime.UtcNow.AddDays(-1),   // Tester-Vorschau schon offen
        });
        await _editions.UpsertMemberAsync(bookId, "tester", isTester: true);

        var tester = await _calc.GetBookAsync(TesterId, bookId, isAdmin: false);
        Assert.Contains(tester.Positions, p => p.Chapter == "Woche B");        // Tester sieht die Vorschau

        var viewer = await _calc.GetBookAsync(ViewerId, bookId, isAdmin: false);
        Assert.DoesNotContain(viewer.Positions, p => p.Chapter == "Woche B");  // Nicht-Tester noch nicht
    }

    [Fact]
    public async Task PrivateSeries_MemberHasAccess_NonMemberDoesNot()
    {
        var bookId = await SeedBookAsync();
        var book = await _db.Books.FirstAsync(b => b.Id == bookId);
        book.IsPublic = false;                               // Serie privat schalten
        await _db.SaveChangesAsync();

        // Nicht-Mitglied (kein Owner/Share/Gruppe): kein Zugriff → wie „nicht gefunden".
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _calc.GetBookAsync(ViewerId, bookId, isAdmin: false));

        // Verteiler-Mitglied: Zugriff.
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        var viewer = await _calc.GetBookAsync(ViewerId, bookId, isAdmin: false);
        Assert.NotEmpty(viewer.Positions);

        // Besitzer immer.
        var owner = await _calc.GetBookAsync(OwnerId, bookId, isAdmin: false);
        Assert.NotEmpty(owner.Positions);
    }

    [Fact]
    public async Task View_RecordedForMemberOnly_Idempotent()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche A", PublishAt = DateTime.UtcNow.AddDays(-1) }); // freigegeben
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        var wa = await _db.BookPuzzles.Where(p => p.Chapter == "Woche A").OrderBy(p => p.Id).ToListAsync();

        // Mitglied öffnet zwei Stellungen derselben Woche → genau EIN „gesehen"-Vermerk (je Ausgabe).
        await _calc.GetPositionAsync(ViewerId, wa[0].Id, isAdmin: false);
        await _calc.GetPositionAsync(ViewerId, wa[1].Id, isAdmin: false);
        Assert.Equal(1, await _db.CalcEditionViews.CountAsync());

        // Besitzer (kein Mitglied) zählt nicht.
        await _calc.GetPositionAsync(OwnerId, wa[0].Id, isAdmin: false);
        // Nicht-Mitglied mit Zugriff (Buch ist öffentlich) zählt nicht.
        await _calc.GetPositionAsync(TesterId, wa[0].Id, isAdmin: false);
        Assert.Equal(1, await _db.CalcEditionViews.CountAsync());

        var views = await _editions.ListViewsAsync(bookId);
        Assert.Single(views);
        Assert.Equal("viewer", views[0].Username);
        Assert.Equal("Woche A", views[0].Chapter);
    }

    [Fact]
    public async Task View_NotRecorded_ForHiddenWeek()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(3) }); // Entwurf
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        var wb = await _db.BookPuzzles.FirstAsync(p => p.Chapter == "Woche B");

        // Mitglied kann die noch nicht freigegebene Woche nicht öffnen → kein Vermerk.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _calc.GetPositionAsync(ViewerId, wb.Id, isAdmin: false));
        Assert.Equal(0, await _db.CalcEditionViews.CountAsync());
    }
}
