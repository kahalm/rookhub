using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers.League;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Social;
using SkiaSharp;

namespace RookHub.Api.Tests.League;

/// <summary>Stapel direkt einlesen (0.901.0): aus den abgelegten Bildern wartende Liga-Einlesungen, Vermerk am Stapel,
/// kein Doppel ohne <c>force</c>; dazu die neuen ZIP-Dateinamen (Absender + Tag, Nummer bei mehreren).</summary>
public class LeagueBatchReadTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly LeagueBatchReadService _svc;

    public LeagueBatchReadTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Scoresheet:Reader"] = "external",
        }).Build();
        var scans = new ScoresheetScanService(_db, null!, TestServices.SavedGames(_db), new NotificationService(_db),
            NullLogger<ScoresheetScanService>.Instance, config);
        _svc = new LeagueBatchReadService(_db, scans);
        _db.AppUsers.AddRange(
            new AppUser { Id = 1, Username = "admin", PasswordHash = "x", IsAdmin = true },
            new AppUser { Id = 7, Username = "mitglied", PasswordHash = "x" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static byte[] Jpeg(int width = 40, int height = 30)
    {
        using var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    private async Task<int> BatchAsync(int? userId, int images, bool finished = true, int pdfs = 0)
    {
        var b = new LeagueBatchUpload
        {
            Key = Guid.NewGuid().ToString("N"), ClubId = TestClubs.HomeId, UserId = userId, CreatedAt = DateTime.UtcNow,
            FinishedAt = finished ? DateTime.UtcNow : null, FileCount = images + pdfs,
        };
        for (var i = 0; i < images; i++)
            b.Files.Add(new LeagueBatchUploadFile { FileName = $"blatt-{i + 1}.jpg", ContentType = "image/jpeg", Data = Jpeg(40 + i), Size = 1 });
        for (var i = 0; i < pdfs; i++)
            b.Files.Add(new LeagueBatchUploadFile { FileName = $"x{i}.pdf", ContentType = "application/pdf", Data = [1, 2, 3], Size = 3 });
        _db.LeagueBatchUploads.Add(b);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return b.Id;
    }

    [Fact]
    public async Task One_AllImagesBecomeOnePendingLeagueScan_InOrder_OwnedByTheUploader_AndTheBatchIsMarked()
    {
        var id = await BatchAsync(7, 2);

        var (result, reason) = await _svc.QueueAsync(id, 1, LeagueBatchReadService.ModeOne, false, default);

        Assert.Null(reason);
        var scan = await _db.ScoresheetScans.Include(s => s.Pages).SingleAsync();
        Assert.Equal([scan.Id], result!.ScanIds);
        Assert.Equal(7, scan.UserId);                                                   // der Hochladende, nicht der Admin
        Assert.Equal(ScoresheetScanStatus.Pending, scan.Status);
        Assert.Equal(ScoresheetScan.PurposeLeague, scan.Purpose);
        Assert.Equal(TestClubs.HomeId, scan.ClubId);
        Assert.Equal(2, scan.PageCount);
        Assert.Equal("blatt-1.jpg", scan.FileName);
        Assert.Equal("blatt-2.jpg", scan.Pages.Single().FileName);
        var batch = await _db.LeagueBatchUploads.AsNoTracking().SingleAsync();
        Assert.NotNull(batch.ReadQueuedAt);
        Assert.Equal(scan.Id.ToString(), batch.ReadScanIds);
        Assert.Equal(2, await _db.LeagueBatchUploadFiles.CountAsync());                  // nichts gelöscht
    }

    [Fact]
    public async Task Each_OneScanPerImage_SkipsPdfs_IgnoresOpenLimit_AnonymousBatchBelongsToTheAdmin()
    {
        var id = await BatchAsync(null, ScoresheetScanService.MaxOpenPerUser + 2, pdfs: 1);

        var (result, reason) = await _svc.QueueAsync(id, 1, LeagueBatchReadService.ModeEach, false, default);

        Assert.Null(reason);
        Assert.Equal(ScoresheetScanService.MaxOpenPerUser + 2, result!.ScanIds.Count);    // keine Bremse „höchstens drei offen"
        Assert.Equal(1, result.Skipped);
        var scans = await _db.ScoresheetScans.AsNoTracking().ToListAsync();
        Assert.All(scans, s => Assert.Equal((1, 1, ScoresheetScanStatus.Pending), (s.UserId!.Value, s.PageCount, s.Status)));
        // Der Leser von außen (Watcher) sieht sie als wartend
        var scanService = new ScoresheetScanService(_db, null!, TestServices.SavedGames(_db), new NotificationService(_db),
            NullLogger<ScoresheetScanService>.Instance);
        Assert.Equal(scans.Count, (await scanService.PendingForExternalAsync()).Count);
    }

    [Fact]
    public async Task SecondClick_IsRefusedWithThePreviousIds_UnlessForced_ThenTheIdsAccumulate()
    {
        var id = await BatchAsync(7, 1);
        var (first, _) = await _svc.QueueAsync(id, 1, null, false, default);

        var (again, reason) = await _svc.QueueAsync(id, 1, null, false, default);
        Assert.Equal("alreadyQueued", reason);
        Assert.Equal(first!.ScanIds, again!.PreviousScanIds);
        Assert.Equal(1, await _db.ScoresheetScans.CountAsync());

        var (forced, reason2) = await _svc.QueueAsync(id, 1, null, true, default);
        Assert.Null(reason2);
        Assert.Equal(2, await _db.ScoresheetScans.CountAsync());
        var batch = await _db.LeagueBatchUploads.AsNoTracking().SingleAsync();
        Assert.Equal($"{first.ScanIds[0]},{forced!.ScanIds[0]}", batch.ReadScanIds);
    }

    [Fact]
    public async Task Refuses_Unknown_Unfinished_TooManyPages_BadMode_AndUnreadable()
    {
        Assert.Equal("notFound", (await _svc.QueueAsync(999, 1, null, false, default)).Reason);
        Assert.Equal("notFinished", (await _svc.QueueAsync(await BatchAsync(7, 1, finished: false), 1, null, false, default)).Reason);
        Assert.Equal("tooManyPages", (await _svc.QueueAsync(await BatchAsync(7, ScoresheetScanService.MaxPages + 1), 1, "one", false, default)).Reason);
        Assert.Equal("invalidMode", (await _svc.QueueAsync(await BatchAsync(7, 1), 1, "pairs", false, default)).Reason);
        Assert.Equal("unsupportedImage", (await _svc.QueueAsync(await BatchAsync(7, 0, pdfs: 2), 1, "each", false, default)).Reason);
        Assert.Equal("unsupportedImage", (await _svc.QueueAsync(await BatchAsync(7, 1, pdfs: 1), 1, "one", false, default)).Reason);
        Assert.Equal(0, await _db.ScoresheetScans.CountAsync());
        Assert.All(await _db.LeagueBatchUploads.AsNoTracking().ToListAsync(), b => Assert.Null(b.ReadQueuedAt));
    }

    [Fact]
    public async Task Controller_MapsAlreadyQueuedTo409_AndIsAdminOnly()
    {
        var id = await BatchAsync(7, 1);
        var ctrl = new AdminLeagueUploadController(null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "test")),
                },
            },
        };
        var ok = Assert.IsType<OkObjectResult>(await ctrl.Read(id, new("one"), _svc, default));
        Assert.Single(Assert.IsType<LeagueBatchReadService.ReadResult>(ok.Value).ScanIds);
        Assert.IsType<ConflictObjectResult>(await ctrl.Read(id, new("one"), _svc, default));
        Assert.IsType<NotFoundResult>(await ctrl.Read(999, null, _svc, default));

        var perm = typeof(AdminLeagueUploadController).GetCustomAttribute<HasPermissionAttribute>();
        Assert.Equal(new HasPermissionAttribute(Permissions.MessagesAdmin).Policy, perm!.Policy);
        Assert.Null(typeof(AdminLeagueUploadController).GetMethod(nameof(AdminLeagueUploadController.Read))!
            .GetCustomAttribute<AllowAnonymousAttribute>());
    }

    // ── ZIP-Dateinamen ──

    [Theory]
    [InlineData("kahalm", "kahalm-2026-10-11")]
    [InlineData("claude-prod", "claude-prod-2026-10-11")]
    [InlineData(null, "anonym-2026-10-11")]
    [InlineData("Jörg Maß/ü", "Joerg-Mass-ue-2026-10-11")]
    [InlineData("  ", "anonym-2026-10-11")]
    public void DownloadNameOf_SenderAndDay_FileNameSafe(string? user, string expected) =>
        Assert.Equal(expected, LeagueBatchUploadService.DownloadNameOf(user, new DateOnly(2026, 10, 11)));

    [Fact]
    public void DownloadNames_NumberOnlyWhenTheSameSenderHasSeveralOnOneViennaDay()
    {
        var names = LeagueBatchUploadService.DownloadNames([
            (1, new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc), "kahalm"),
            (5, new DateTime(2026, 10, 11, 7, 0, 0, DateTimeKind.Utc), "kahalm"),      // früher hochgeladen → -1
            (3, new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc), "claude-prod"),
            (4, new DateTime(2026, 10, 11, 22, 30, 0, DateTimeKind.Utc), "kahalm"),    // Wien: schon der 12.
            (6, new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc), null),
        ]);
        Assert.Equal("kahalm-2026-10-11-2", names[1]);
        Assert.Equal("kahalm-2026-10-11-1", names[5]);
        Assert.Equal("claude-prod-2026-10-11", names[3]);
        Assert.Equal("kahalm-2026-10-12", names[4]);
        Assert.Equal("anonym-2026-10-11", names[6]);
    }

    [Fact]
    public async Task List_CarriesDownloadName_AndTheReadMark()
    {
        var id = await BatchAsync(7, 1);
        await _svc.QueueAsync(id, 1, null, false, default);
        var notifications = new NotificationService(_db);
        var list = new LeagueBatchUploadService(_db, new AdminMessageService(_db, notifications), notifications);

        var row = (await list.ListAsync(default)).Single();

        Assert.StartsWith("mitglied-", row.DownloadName);
        Assert.NotNull(row.ReadQueuedAt);
        Assert.Single(row.ReadScanIds!);
        Assert.Equal(row.DownloadName, await list.DownloadNameAsync(id, default));
    }
}
