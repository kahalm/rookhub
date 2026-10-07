using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;
using Uploader = RookHub.Api.Services.League.LeagueBatchUploadService.Uploader;

namespace RookHub.Api.Tests;

/// <summary>Stapel-Upload von Partieformular-Bildern (0.651.0): nur ablegen, Admins melden, ZIP, Deckel.</summary>
public class LeagueBatchUploadTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly LeagueBatchUploadService _svc;

    public LeagueBatchUploadTests()
    {
        var notifications = new NotificationService(_db);
        _svc = new LeagueBatchUploadService(_db, new AdminMessageService(_db, notifications), notifications);
        _db.AppUsers.AddRange(
            new AppUser { Id = 1, Username = "admin", PasswordHash = "x", IsAdmin = true },
            new AppUser { Id = 7, Username = "mitglied", PasswordHash = "x" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static byte[] Bytes(int n, byte v = 1) => Enumerable.Repeat(v, n).ToArray();

    [Fact]
    public async Task LoggedIn_StoresUnprocessed_AndWritesAnAdminMessageOnce()
    {
        var me = Uploader.User(7, TestClubs.HomeId);
        var b = await _svc.StartAsync(me, "  Runde 3 gegen Wörgl  ", default);
        Assert.Equal(32, b.Key.Length);
        Assert.NotNull((await _svc.AddFileAsync(me, b.Key, Bytes(100), "image/jpeg", "a.jpg", default)).State);
        var (s, _) = await _svc.AddFileAsync(me, b.Key, Bytes(50), "application/pdf", "b.pdf", default);
        Assert.Equal(2, s!.Files);
        Assert.Equal(150, s.Bytes);
        Assert.Equal(0, await _db.ScoresheetScans.CountAsync());                          // nichts eingelesen

        Assert.True((await _svc.FinishAsync(me, b.Key, default)).State!.Finished);
        await _svc.FinishAsync(me, b.Key, default);                                       // zweimal: nur eine Meldung
        var msg = await _db.AdminMessages.SingleAsync();
        Assert.Equal(7, msg.UserId);
        Assert.Contains("2 Bilder", msg.Body);
        Assert.Contains("/admin?tab=uploads", msg.Body);
        Assert.Contains("Kommentar: Runde 3 gegen Wörgl", msg.Body);
        Assert.Equal("finished", (await _svc.AddFileAsync(me, b.Key, Bytes(10), "image/png", "c.png", default)).Reason);
    }

    [Fact]
    public async Task Anonymous_RingsTheAdmins_WithoutAMessage_AndOnlyTheSameLinkMayAdd()
    {
        var anon = Uploader.Share("TOKEN-A", "ip1", TestClubs.HomeId);
        var b = await _svc.StartAsync(anon, null, default);
        await _svc.AddFileAsync(anon, b.Key, Bytes(10), "image/heic", "x.heic", default);
        Assert.Equal("notFound", (await _svc.AddFileAsync(Uploader.Share("TOKEN-B", "ip1", TestClubs.HomeId), b.Key, Bytes(10), "image/png", "y", default)).Reason);
        Assert.Equal("notFound", (await _svc.AddFileAsync(Uploader.User(7, TestClubs.HomeId), b.Key, Bytes(10), "image/png", "y", default)).Reason);
        await _svc.FinishAsync(anon, b.Key, default);
        Assert.Empty(_db.AdminMessages);
        var n = await _db.Notifications.SingleAsync();
        Assert.Equal(1, n.UserId);
        Assert.Equal(NotificationType.LeagueBatchUploaded, n.Type);
        Assert.Equal("/admin?tab=uploads", n.Link);
        Assert.Equal(LeagueClubService.ShareHashOf("TOKEN-A"), (await _db.LeagueBatchUploads.SingleAsync()).ShareHash);
    }

    [Fact]
    public async Task Refuses_NonImages_EmptyBatches()
    {
        var me = Uploader.User(7, TestClubs.HomeId);
        var b = await _svc.StartAsync(me, null, default);
        Assert.Equal("type", (await _svc.AddFileAsync(me, b.Key, Bytes(10), "text/plain", "a.txt", default)).Reason);
        Assert.Equal("empty", (await _svc.FinishAsync(me, b.Key, default)).Reason);
        Assert.Empty(_db.LeagueBatchUploads);                                             // leerer Stapel weg, keine Meldung
        Assert.Empty(_db.AdminMessages);
    }

    [Fact]
    public async Task Zip_HasEveryFile_DuplicateNamesNumbered_DeleteRemovesAll()
    {
        var me = Uploader.User(7, TestClubs.HomeId);
        var b = await _svc.StartAsync(me, null, default);
        await _svc.AddFileAsync(me, b.Key, Bytes(3, 1), "image/jpeg", "foto.jpg", default);
        await _svc.AddFileAsync(me, b.Key, Bytes(4, 2), "image/jpeg", "foto.jpg", default);
        var id = (await _db.LeagueBatchUploads.SingleAsync()).Id;

        using var ms = new MemoryStream();
        Assert.True(await _svc.WriteZipAsync(id, ms, default));
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Equal(new[] { "foto.jpg", "foto-2.jpg" }, zip.Entries.Select(e => e.FullName));
        Assert.Equal(4, zip.Entries[1].Length);

        var row = Assert.Single(await _svc.ListAsync(default));
        Assert.Equal("mitglied", row.User);
        Assert.False(row.ViaShare);
        Assert.Equal(2, row.Files);

        Assert.True(await _svc.DeleteAsync(id, default));
        Assert.Empty(_db.LeagueBatchUploadFiles);
        Assert.False(await _svc.WriteZipAsync(id, new MemoryStream(), default));
    }

    [Fact]
    public async Task Anonymous_DailyLimitPerAddress()
    {
        _db.LeagueBatchUploads.Add(new LeagueBatchUpload
        {
            Key = new string('a', 32), AnonIpHash = "ip9", ShareHash = "h", CreatedAt = DateTime.UtcNow.AddHours(-2),
            TotalBytes = LeagueBatchUploadService.AnonPerIpDailyBytes, FileCount = 1,
        });
        await _db.SaveChangesAsync();
        var anon = Uploader.Share("T", "ip9", TestClubs.HomeId);
        var b = await _svc.StartAsync(anon, null, default);
        Assert.Equal("dailyLimit", (await _svc.AddFileAsync(anon, b.Key, Bytes(10), "image/jpeg", "a.jpg", default)).Reason);
        var other = Uploader.Share("T", "ip10", TestClubs.HomeId);
        var b2 = await _svc.StartAsync(other, null, default);
        Assert.Null((await _svc.AddFileAsync(other, b2.Key, Bytes(10), "image/jpeg", "a.jpg", default)).Reason);
    }
}
