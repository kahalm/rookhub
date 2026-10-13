using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers.Ebooks;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbooksControllerTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    private readonly EbookAccessTokens _tokens = new(new EphemeralDataProtectionProvider());
    public void Dispose() => _kit.Dispose();

    private EbooksController Controller(int? userId, bool admin = false)
    {
        var c = new EbooksController(_kit.Service, _tokens, _kit.Db);
        var claims = new List<Claim>();
        if (userId != null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
            if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }
        var identity = userId != null ? new ClaimsIdentity(claims, "Test") : new ClaimsIdentity();
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return c;
    }

    [Fact]
    public void AccessToken_RoundTrips_AndRejectsGarbage()
    {
        var (token, expires) = _tokens.Issue(42);
        Assert.True(_tokens.TryRead(token, out var id));
        Assert.Equal(42, id);
        Assert.True(expires > DateTime.UtcNow.AddHours(11));
        Assert.False(_tokens.TryRead(token + "x", out _));
        Assert.False(_tokens.TryRead(null, out _));
        Assert.False(new EbookAccessTokens(new EphemeralDataProtectionProvider()).TryRead(token, out _));   // fremder Schlüsselbund
    }

    [Fact]
    public async Task Upload_ThroughController_ParsesCsvTagsAndGroups_AndReturns201()
    {
        var user = await _kit.UserAsync("u");
        var group = await _kit.GroupAsync("G", false, user.Id);
        var bytes = EbookTestKit.TinyPdf();
        var form = new EbookUploadForm
        {
            File = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "Buch.pdf"),
            Title = "Mein Buch", Tags = "Endspiel, Taktik", GroupIds = $"{group.Id},999",
        };

        var result = await Controller(user.Id).Upload(form, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<EbookDto>(created.Value);
        Assert.Equal("Mein Buch", dto.Title);
        Assert.Equal(["Endspiel", "Taktik"], dto.Tags);
        Assert.Equal([group.Id], dto.GroupIds!);
    }

    [Fact]
    public async Task Upload_WithoutFile_Is400()
    {
        var user = await _kit.UserAsync("u");
        var result = await Controller(user.Id).Upload(new EbookUploadForm(), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task File_NeedsBearerOrToken_ChecksVisibility_AndStreamsWithRanges()
    {
        var owner = await _kit.UserAsync("owner");
        var other = await _kit.UserAsync("other");
        var dto = await _kit.UploadAsync(owner.Id, false, EbookTestKit.TinyPdf(), "a.pdf", new EbookMetaDto { Title = "Buch: Teil 1/2" });

        Assert.IsType<UnauthorizedResult>(await Controller(null).GetFile(dto.Id, null, null, false, CancellationToken.None));
        Assert.IsType<UnauthorizedResult>(await Controller(null).GetFile(dto.Id, null, "kaputt", false, CancellationToken.None));

        var inline = Assert.IsType<PhysicalFileResult>(await Controller(owner.Id).GetFile(dto.Id, null, null, false, CancellationToken.None));
        Assert.True(inline.EnableRangeProcessing);
        Assert.Equal("application/pdf", inline.ContentType);
        Assert.True(string.IsNullOrEmpty(inline.FileDownloadName));

        var (token, _) = _tokens.Issue(owner.Id);
        var download = Assert.IsType<PhysicalFileResult>(await Controller(null).GetFile(dto.Id, null, token, true, CancellationToken.None));
        Assert.Equal("Buch_ Teil 1_2.pdf", download.FileDownloadName);

        var (foreignToken, _) = _tokens.Issue(other.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => Controller(null).GetFile(dto.Id, null, foreignToken, false, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => Controller(other.Id).GetFile(dto.Id, null, null, false, CancellationToken.None));
        // Admin über den Ausweis: IsAdmin kommt aus der Datenbank, nicht aus dem Ausweis.
        var admin = await _kit.UserAsync("admin", admin: true);
        var (adminToken, _) = _tokens.Issue(admin.Id);
        Assert.IsType<PhysicalFileResult>(await Controller(null).GetFile(dto.Id, null, adminToken, false, CancellationToken.None));
    }

    [Fact]
    public async Task Cover_Is404UntilRendered_Then200WithSniffedType()
    {
        var owner = await _kit.UserAsync("owner");
        var dto = await _kit.UploadAsync(owner.Id, false, EbookTestKit.TinyPdf(), "a.pdf");
        Assert.IsType<NotFoundResult>(await Controller(owner.Id).GetCover(dto.Id, null, CancellationToken.None));

        await File.WriteAllBytesAsync(_kit.Storage.CoverPath(dto.Id), [0xFF, 0xD8, 0xFF, 0xE0, 0, 0]);
        var book = await _kit.Db.Ebooks.SingleAsync();
        book.CoverState = EbookCoverState.Done;
        await _kit.Db.SaveChangesAsync();

        var ok = Assert.IsType<PhysicalFileResult>(await Controller(owner.Id).GetCover(dto.Id, null, CancellationToken.None));
        Assert.Equal("image/jpeg", ok.ContentType);
        Assert.IsType<UnauthorizedResult>(await Controller(null).GetCover(dto.Id, null, CancellationToken.None));
    }

    [Fact]
    public async Task Progress_And_Groups_Endpoints_Work()
    {
        var owner = await _kit.UserAsync("owner");
        var group = await _kit.GroupAsync("G", false, owner.Id);
        var dto = await _kit.UploadAsync(owner.Id, false, EbookTestKit.TinyPdf(), "a.pdf");
        var c = Controller(owner.Id);

        var set = await c.SetProgress(dto.Id, new EbookProgressUpdateDto { Page = 12, PageCount = 200 }, CancellationToken.None);
        Assert.Equal(12, Assert.IsType<EbookProgressDto>(Assert.IsType<OkObjectResult>(set.Result).Value).Page);

        var bulk = Assert.IsType<OkObjectResult>(await c.SetGroups(new SetEbookGroupsDto { EbookIds = [dto.Id], GroupIds = [group.Id] }, CancellationToken.None));
        Assert.Equal(1, (int)bulk.Value!.GetType().GetProperty("updated")!.GetValue(bulk.Value)!);

        var options = await c.GroupOptions(CancellationToken.None);
        Assert.Equal(["G"], Assert.IsType<List<EbookGroupOptionDto>>(Assert.IsType<OkObjectResult>(options.Result).Value).Select(o => o.Name));

        var token = c.AccessToken();
        Assert.True(_tokens.TryRead(Assert.IsType<EbookAccessTokenDto>(Assert.IsType<OkObjectResult>(token.Result).Value).Token, out var uid));
        Assert.Equal(owner.Id, uid);
    }

    [Fact]
    public void DownloadName_FallsBackToFileName_AndClipsLongTitles()
    {
        var file = new EbookFile { Format = "epub", FileName = "orig-name.epub" };
        Assert.Equal("orig-name.epub", EbooksController.DownloadName(new Ebook { Title = "  " }, file));
        var longName = EbooksController.DownloadName(new Ebook { Title = new string('a', 200) }, file);
        Assert.Equal(120 + ".epub".Length, longName.Length);
    }
}
