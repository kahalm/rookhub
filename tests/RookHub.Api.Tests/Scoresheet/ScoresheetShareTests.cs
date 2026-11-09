using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Controllers.Scoresheet;
using RookHub.Api.Services.Scoresheet;
using SkiaSharp;

namespace RookHub.Api.Tests.Scoresheet;

/// <summary>„Teilen → RookHub“ (0.883.0): geteilte Fotos landen kurz in der Ablage, der Browser geht per 303 auf die
/// Einlese-Seite, die Seite holt die Fotos angemeldet ab.</summary>
public class ScoresheetShareTests
{
    /// <summary>Ein echtes kleines Bild (<paramref name="size"/> × <paramref name="size"/> px; PNG für „image/png“, sonst JPEG) —
    /// seit R3-005 prüft der Endpunkt den Bildkopf, Zufallsbytes gelten als „unsupportedImage“.</summary>
    private static byte[] ImageBytes(string type, int size)
    {
        using var bmp = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.LightGray);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(type == "image/png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    private static IFormFile Photo(string type = "image/jpeg", int size = 100, string name = "sheet.jpg")
        => File(ImageBytes(type, size), type, name);

    private static IFormFile File(byte[] bytes, string type, string name)
    {
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
        Assert.Equal(ImageBytes("image/png", 50).Length, pages[1].Data.Length);

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
    public async Task Share_ContentTypeAloneIsNotEnough_HeaderIsChecked()
    {
        // R3-005: Zufallsbytes mit „image/jpeg“ kamen bisher in die Ablage.
        var inbox = new ScoresheetShareInbox();
        var c = Controller(inbox);
        var junk = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        Assert.Equal("/games/scoresheet?shareError=unsupportedImage", Location(c, await c.Share([File(junk, "image/jpeg", "x.jpg")])));
    }

    [Fact]
    public void Inbox_IsBoundedPerIp_OtherIpsStillGetIn()
    {
        // R3-005: von EINEM Anschluss höchstens drei gleichzeitig, andere Anschlüsse teilen weiter.
        var inbox = new ScoresheetShareInbox();
        for (var i = 0; i < ScoresheetShareInbox.MaxEntriesPerIp; i++)
            Assert.NotNull(inbox.Put([new ScoresheetUpload([1], "image/jpeg", null)], "203.0.113.7"));
        Assert.Null(inbox.Put([new ScoresheetUpload([1], "image/jpeg", null)], "203.0.113.7"));
        Assert.NotNull(inbox.Put([new ScoresheetUpload([1], "image/jpeg", null)], "198.51.100.1"));
    }

    [Fact]
    public async Task Share_FourthShareFromTheSameIp_IsBusy_ThenFreeAfterPickup()
    {
        var inbox = new ScoresheetShareInbox();
        var c = Controller(inbox);
        c.ControllerContext.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        var tokens = new List<string>();
        for (var i = 0; i < ScoresheetShareInbox.MaxEntriesPerIp; i++)
            tokens.Add(Location(c, await c.Share([Photo()]))["/games/scoresheet?share=".Length..]);
        Assert.Equal("/games/scoresheet?shareError=busy", Location(c, await c.Share([Photo()])));
        c.Remove(tokens[0]);   // abgeholt → wieder Platz
        Assert.StartsWith("/games/scoresheet?share=", Location(c, await c.Share([Photo()])));
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
