using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Social;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Upload durch jemanden ohne E-Book-Verwaltung → Glocke aller Admins (0.788.0, Wunsch 15.10.2026).</summary>
public class EbookUploadAdminNotificationTests : IDisposable
{
    private const string Anon = "0123456789abcdef0123456789abcdef";
    private readonly EbookTestKit _kit = new();
    private readonly EbookService _service;

    public EbookUploadAdminNotificationTests()
    {
        _service = new EbookService(_kit.Db, _kit.Storage, NullLogger<EbookService>.Instance,
            Microsoft.Extensions.Options.Options.Create(_kit.Options), new NotificationService(_kit.Db));
    }

    public void Dispose() => _kit.Dispose();

    private Task<RookHub.Api.DTOs.Ebooks.EbookDto> Upload(EbookCaller caller, bool manageAll, string name) =>
        _service.UploadAsync(caller, manageAll, new MemoryStream(EbookTestKit.TinyPdf()), name, EbookTestKit.TinyPdf().Length,
            new(), CancellationToken.None);

    [Fact]
    public async Task UserUpload_NotifiesEveryAdmin_WithTitleNameAndLink()
    {
        var admin1 = await _kit.UserAsync("admin1", admin: true);
        var admin2 = await _kit.UserAsync("admin2", admin: true);
        var user = await _kit.UserAsync("leser");

        var dto = await Upload(user.Id, false, "Mein_Buch.pdf");

        var notes = await _kit.Db.Notifications.ToListAsync();
        Assert.Equal(new[] { admin1.Id, admin2.Id }, notes.Select(n => n.UserId).OrderBy(i => i).ToArray());
        Assert.All(notes, n =>
        {
            Assert.Equal(NotificationType.EbookUploaded, n.Type);
            Assert.Equal($"/ebooks/{dto.Id}", n.Link);
            Assert.Contains("\"username\":\"leser\"", n.DataJson);
            Assert.Contains("\"title\":\"Mein Buch\"", n.DataJson);
        });
    }

    [Fact]
    public async Task AnonymousUpload_NotifiesAdmins_WithEmptyName()
    {
        await _kit.UserAsync("admin", admin: true);

        await Upload(EbookCaller.Anonymous(Anon), false, "Anonym.pdf");

        var note = await _kit.Db.Notifications.SingleAsync();
        Assert.Equal(NotificationType.EbookUploaded, note.Type);
        Assert.Contains("\"username\":\"\"", note.DataJson);
    }

    [Fact]
    public async Task ManagerUpload_NotifiesNobody()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        await _kit.UserAsync("admin2", admin: true);

        await Upload(admin.Id, true, "Eigenes.pdf");

        Assert.Empty(_kit.Db.Notifications);
    }
}
