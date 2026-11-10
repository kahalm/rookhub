using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Scoresheet;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Tests.Scoresheet;

/// <summary>
/// Fotos einer wartenden Einlesung neu auf Seiten verteilen (Vorfall 2026-11-10, LeagueHub-Formular 113: zwei Seiten kamen als
/// „weiteres Foto derselben Seite" und verkehrt herum an). <c>POST /api/admin/scoresheets/{id}/layout</c>.
/// </summary>
public class ScoresheetLayoutTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ScoresheetScanService _service;

    public ScoresheetLayoutTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _service = new ScoresheetScanService(_db, new NoVision(), TestServices.SavedGames(_db), new NotificationService(_db),
            NullLogger<ScoresheetScanService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private sealed class NoVision : IScoresheetVisionClient
    {
        public bool IsConfigured => false;
        public string Model => "none";
        public Task<ScoresheetVisionResult> ReadAsync(byte[] jpeg, string instructions, int maxTokens,
            CancellationToken ct = default, ScoresheetReadMode mode = ScoresheetReadMode.Full) => throw new NotSupportedException();
    }

    private static ScoresheetPhotoRef R(int page, int view = 0) => new(page, view);

    private static List<List<ScoresheetPhotoRef>> Pages(params ScoresheetPhotoRef[][] pages) => pages.Select(p => p.ToList()).ToList();

    /// <summary>Wie Formular 113: eine Seite, Hauptfoto (1) = Fortsetzungsblatt, weiteres Foto (2) = Züge 1–60.</summary>
    private async Task<int> SeedAsync(ScoresheetScanStatus status = ScoresheetScanStatus.Pending, bool secondPage = false)
    {
        var scan = new ScoresheetScan
        {
            Photo = [1], ContentType = "image/png", FileName = "a.png", Status = status, PageCount = secondPage ? 2 : 1,
            Purpose = ScoresheetScan.PurposeLeague, CreatedAt = DateTime.UtcNow,
        };
        scan.Views.Add(new ScoresheetScanView { Page = 1, View = 1, Photo = [2], ContentType = "image/jpeg", FileName = "b.jpg" });
        if (secondPage) scan.Pages.Add(new ScoresheetScanPage { Page = 2, Photo = [3], ContentType = "image/webp", FileName = "c.webp" });
        _db.ScoresheetScans.Add(scan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return scan.Id;
    }

    private async Task<byte?> PhotoAsync(int id, int page, int view = 0) =>
        (await _service.PhotoForExternalAsync(id, page, default, view)) is { } p ? p.Data[0] : null;

    [Fact]
    public async Task Rearrange_ViewBecomesPageOne_MainBecomesPageTwo()
    {
        var id = await SeedAsync();

        var (layout, reason) = await _service.RearrangeLayoutAsync(id, Pages([R(1, 1)], [R(1, 0)]));

        Assert.Null(reason);
        Assert.Equal(2, layout!.PageCount);
        Assert.Equal(new[] { 0, 0 }, layout.ViewsPerPage);
        Assert.Equal((byte)2, await PhotoAsync(id, 1));
        Assert.Equal((byte)1, await PhotoAsync(id, 2));
        Assert.Null(await PhotoAsync(id, 1, 1));
        var scan = await _db.ScoresheetScans.AsNoTracking().FirstAsync(s => s.Id == id);
        Assert.Equal("image/jpeg", scan.ContentType);
        Assert.Equal("b.jpg", scan.FileName);
        var page2 = await _db.ScoresheetScanPages.AsNoTracking().SingleAsync(p => p.ScoresheetScanId == id);
        Assert.Equal(("image/png", "a.png"), (page2.ContentType, page2.FileName));
        Assert.False(await _db.ScoresheetScanViews.AnyAsync());

        var pending = Assert.Single(await _service.PendingForExternalAsync());
        Assert.Equal(2, pending.PageCount);
        Assert.Equal(new[] { 0, 0 }, pending.ViewsPerPage);
    }

    [Fact]
    public async Task Rearrange_PagesIntoViews_AndRunningIsAllowed()
    {
        var id = await SeedAsync(ScoresheetScanStatus.Running, secondPage: true);

        var (layout, reason) = await _service.RearrangeLayoutAsync(id, Pages([R(2), R(1), R(1, 1)]));

        Assert.Null(reason);
        Assert.Equal(1, layout!.PageCount);
        Assert.Equal(new[] { 2 }, layout.ViewsPerPage);
        Assert.Equal("running", layout.Status);
        Assert.Equal((byte)3, await PhotoAsync(id, 1));
        Assert.Equal((byte)1, await PhotoAsync(id, 1, 1));
        Assert.Equal((byte)2, await PhotoAsync(id, 1, 2));
        Assert.False(await _db.ScoresheetScanPages.AnyAsync());
    }

    [Fact]
    public async Task Pending_ListsViewsPerPage()
    {
        await SeedAsync(secondPage: true);
        var pending = Assert.Single(await _service.PendingForExternalAsync());
        Assert.Equal(new[] { 1, 0 }, pending.ViewsPerPage);
        Assert.Equal(1, pending.ViewCount);
    }

    public static TheoryData<string, List<List<ScoresheetPhotoRef>>?> Invalid => new()
    {
        { "emptyLayout", null },
        { "emptyLayout", [] },
        { "emptyPage", [[R(1), R(1, 1)], []] },
        { "unknownPhoto", [[R(1)], [R(3)]] },
        { "unknownPhoto", [[R(1)], [R(1, 2)]] },
        { "duplicatePhoto", [[R(1)], [R(1)], [R(1, 1)]] },
        { "missingPhoto", [[R(1, 1)]] },
        { "tooManyPages", [[R(1)], [R(1, 1)], [R(1)], [R(1)]] },
        { "tooManyViews", [[R(1), R(1, 1), R(1), R(1), R(1)]] },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Rearrange_Invalid_ReasonAndNothingChanged(string expected, List<List<ScoresheetPhotoRef>>? pages)
    {
        var id = await SeedAsync();

        var (layout, reason) = await _service.RearrangeLayoutAsync(id, pages);

        Assert.Null(layout);
        Assert.Equal(expected, reason);
        Assert.Equal((byte)1, await PhotoAsync(id, 1));
        Assert.Equal((byte)2, await PhotoAsync(id, 1, 1));
        Assert.Equal(1, (await _service.LayoutAsync(id))!.PageCount);
    }

    [Theory]
    [InlineData(ScoresheetScanStatus.Done)]
    [InlineData(ScoresheetScanStatus.Failed)]
    public async Task Rearrange_NotWaiting_NotPending(ScoresheetScanStatus status)
    {
        var id = await SeedAsync(status);
        Assert.Equal("notPending", (await _service.RearrangeLayoutAsync(id, Pages([R(1, 1)], [R(1)]))).Reason);
        Assert.Equal((byte)1, await PhotoAsync(id, 1));
    }

    [Fact]
    public async Task Rearrange_Unknown_NotFound() =>
        Assert.Equal("notFound", (await _service.RearrangeLayoutAsync(999, Pages([R(1)]))).Reason);

    // ── Controller ─────────────────────────────────────────────────────

    private AdminScoresheetController Controller() => new(_service);

    [Fact]
    public async Task Controller_Rearrange_OkWithLayout_ThenGetShowsIt()
    {
        var id = await SeedAsync();
        var c = Controller();

        var ok = Assert.IsType<OkObjectResult>(await c.Rearrange(id, new([[R(1, 1)], [R(1)]]), default));
        Assert.Equal(2, Assert.IsType<ScoresheetLayoutDto>(ok.Value).PageCount);

        var get = await c.Layout(id, default);
        Assert.Equal(new[] { 0, 0 }, Assert.IsType<OkObjectResult>(get.Result).Value is ScoresheetLayoutDto l ? l.ViewsPerPage : null);
        Assert.IsType<NotFoundResult>((await c.Layout(999, default)).Result);
    }

    [Fact]
    public async Task Controller_Rearrange_ErrorCodes()
    {
        var id = await SeedAsync();
        var done = await SeedAsync(ScoresheetScanStatus.Done);
        var c = Controller();

        var bad = Assert.IsType<BadRequestObjectResult>(await c.Rearrange(id, new([[R(1, 1)]]), default));
        Assert.Equal("missingPhoto", bad.Value!.GetType().GetProperty("reason")!.GetValue(bad.Value));
        Assert.IsType<BadRequestObjectResult>(await c.Rearrange(id, null, default));
        var conflict = Assert.IsType<ConflictObjectResult>(await c.Rearrange(done, new([[R(1, 1)], [R(1)]]), default));
        Assert.Equal("notPending", conflict.Value!.GetType().GetProperty("reason")!.GetValue(conflict.Value));
        Assert.IsType<NotFoundResult>(await c.Rearrange(999, new([[R(1)]]), default));
    }
}
