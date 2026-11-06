using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Controllers.Scoresheet;
using RookHub.Api.Services.Scoresheet;

namespace RookHub.Api.Tests.Scoresheet;

/// <summary>„Teilen → RookHub“ (0.883.0): geteilte Fotos landen kurz in der Ablage, der Browser geht per 303 auf die
/// Einlese-Seite, die Seite holt die Fotos angemeldet ab.</summary>
public class ScoresheetShareTests
{
    private static IFormFile Photo(string type = "image/jpeg", int size = 100, string name = "sheet.jpg")
    {
        var bytes = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(), ContentType = type,
        };
    }

    private static ScoresheetShareController Controller(ScoresheetShareInbox inbox) => new(inbox)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private static string Location(ScoresheetShareController c, IActionResult r)
    {
        Assert.Equal(StatusCodes.Status303SeeOther, Assert.IsType<StatusCodeResult>(r).StatusCode);
        return c.Response.Headers.Location.ToString();
    }

    [Fact]
    public async Task Share_StoresPhotos_AndRedirectsToUploadPageWithToken()
    {
        var inbox = new ScoresheetShareInbox();
        var c = Controller(inbox);

        var loc = Location(c, await c.Share([Photo(), Photo("image/png", 50, "p2.png")]));

        Assert.StartsWith("/games/scoresheet?share=", loc);
        var token = loc["/games/scoresheet?share=".Length..];
        Assert.Equal(32, token.Length);
        var pages = inbox.Get(token);
        Assert.NotNull(pages);
        Assert.Equal(2, pages!.Count);
        Assert.Equal("image/png", pages[1].ContentType);
        Assert.Equal(50, pages[1].Data.Length);

        Assert.IsType<OkObjectResult>(c.Info(token).Result);
        var file = Assert.IsType<FileContentResult>(c.Photo(token, 1));
        Assert.Equal("image/png", file.ContentType);
        Assert.IsType<NotFoundResult>(c.Photo(token, 2));

        Assert.IsType<NoContentResult>(c.Remove(token));
        Assert.Null(inbox.Get(token));
        Assert.IsType<NotFoundResult>(c.Info(token).Result);
    }

    [Theory]
    [InlineData(0, "image/jpeg", "noFile")]
    [InlineData(4, "image/jpeg", "tooManyPages")]
    [InlineData(1, "image/heic", "unsupportedImage")]
    [InlineData(1, "application/pdf", "unsupportedImage")]
    public async Task Share_RejectsBadInput_WithReasonOnTheUploadPage(int count, string type, string reason)
    {
        var inbox = new ScoresheetShareInbox();
        var c = Controller(inbox);

        var loc = Location(c, await c.Share(Enumerable.Range(0, count).Select(_ => Photo(type)).ToList()));

        Assert.Equal($"/games/scoresheet?shareError={reason}", loc);
    }

    [Fact]
    public void Inbox_ForgetsPhotosAfterLifetime()
    {
        var time = new ManualClock(DateTimeOffset.Parse("2026-11-06T12:00:00Z"));
        var inbox = new ScoresheetShareInbox(time);
        var token = inbox.Put([new ScoresheetUpload([1, 2, 3], "image/jpeg", "a.jpg")])!;

        time.Advance(ScoresheetShareInbox.Lifetime - TimeSpan.FromSeconds(1));
        Assert.NotNull(inbox.Get(token));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(inbox.Get(token));
    }

    [Fact]
    public void Inbox_IsBounded_SoTheOpenEndpointCannotFillMemory()
    {
        var inbox = new ScoresheetShareInbox();
        for (var i = 0; i < ScoresheetShareInbox.MaxEntries; i++)
            Assert.NotNull(inbox.Put([new ScoresheetUpload([1], "image/jpeg", null)]));

        Assert.Null(inbox.Put([new ScoresheetUpload([1], "image/jpeg", null)]));
    }

    [Fact]
    public async Task Share_FullInbox_RedirectsWithBusy()
    {
        var inbox = new ScoresheetShareInbox();
        for (var i = 0; i < ScoresheetShareInbox.MaxEntries; i++) inbox.Put([new ScoresheetUpload([1], "image/jpeg", null)]);
        var c = Controller(inbox);

        Assert.Equal("/games/scoresheet?shareError=busy", Location(c, await c.Share([Photo()])));
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
