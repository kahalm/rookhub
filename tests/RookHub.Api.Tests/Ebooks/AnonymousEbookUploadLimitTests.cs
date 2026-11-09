using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RookHub.Api.Filters;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>F3-009: anonyme Uploads bekommen ihre Rumpfgrenze VOR dem Lesen (<see cref="AnonymousEbookUploadLimitAttribute"/>).
/// Über die echte MVC-Pipeline (Filter-Reihenfolge nach <c>[RequestSizeLimit]</c>, vor dem Form-Binding).</summary>
public class AnonymousEbookUploadLimitTests
{
    private const string Visitor = "0123456789abcdef0123456789abcdef";
    private const long Declared400Mb = 400L * 1024 * 1024;

    private sealed class FakeBodyLimit : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; } = 30_000_000;   // Kestrel-Vorgabe
    }

    /// <summary>Rumpf, der beim ersten Lesen auffliegt — beweist „abgelehnt, ohne zu lesen“.</summary>
    private sealed class UnreadableBody : Stream
    {
        public bool Touched;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { Touched = true; throw new IOException("Rumpf gelesen"); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { Touched = true; throw new IOException("Rumpf gelesen"); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed record Run(HttpStatusCode Status, string Body, long? Limit, bool BodyTouched);

    private static async Task<Run> PostAsync(bool signedIn, string? visitor, bool hugeUnreadable, string? root = "/data/ebooks")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.Configure<EbookOptions>(o => o.Root = root);
        builder.Services.AddControllers().ConfigureApplicationPartManager(apm =>
        {
            apm.ApplicationParts.Clear();
            apm.ApplicationParts.Add(new AssemblyPart(typeof(EbookUploadLimitProbeController).Assembly));
        });
        await using var app = builder.Build();
        var feature = new FakeBodyLimit();
        var body = new UnreadableBody();
        app.Use((ctx, next) =>
        {
            ctx.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
            if (signedIn) ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "Test"));
            if (hugeUnreadable) { ctx.Request.Body = body; ctx.Request.ContentLength = Declared400Mb; }
            return next();
        });
        app.MapControllers();
        await app.StartAsync();

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(EbookTestKit.TinyPdf());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "a.pdf");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/probe-ebook-upload") { Content = form };
        if (visitor != null) req.Headers.Add("X-Visitor-Id", visitor);
        var res = await app.GetTestClient().SendAsync(req);
        return new Run(res.StatusCode, await res.Content.ReadAsStringAsync(), feature.MaxRequestBodySize, body.Touched);
    }

    [Fact]
    public async Task Anonymous_400Mb_Is413_WithoutReadingTheBody()
    {
        var r = await PostAsync(signedIn: false, Visitor, hugeUnreadable: true);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.Status);
        Assert.False(r.BodyTouched);
        using var json = JsonDocument.Parse(r.Body);
        Assert.Equal("ebook_file_too_large", json.RootElement.GetProperty("code").GetString());
        Assert.Contains("300 MB", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Anonymous_WithoutVisitorId_Is403_WithoutReadingTheBody()
    {
        var r = await PostAsync(signedIn: false, visitor: null, hugeUnreadable: true);
        Assert.Equal(HttpStatusCode.Forbidden, r.Status);
        Assert.False(r.BodyTouched);
        using var json = JsonDocument.Parse(r.Body);
        Assert.Equal("ebook_anonymous_upload_disabled", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(EbookService.AnonymousUploadDisabledMessage, json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Anonymous_SmallUpload_PassesWithTheLoweredBodyLimit()
    {
        var r = await PostAsync(signedIn: false, Visitor, hugeUnreadable: false);
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(300L * 1024 * 1024 + AnonymousEbookUploadLimitAttribute.MultipartSlackBytes, r.Limit);
    }

    [Fact]
    public async Task SignedIn_KeepsTheFullLimit_400MbIsNotRejectedByTheGate()
    {
        var small = await PostAsync(signedIn: true, visitor: null, hugeUnreadable: false);
        Assert.Equal(HttpStatusCode.OK, small.Status);
        Assert.Equal(EbookService.MaxUploadRequestBytes, small.Limit);
        // 400 MB angemeldet: der Filter lässt durch (das Binding liest den Rumpf — hier wirft die Sonde beim Lesen).
        var huge = await PostAsync(signedIn: true, visitor: null, hugeUnreadable: true);
        Assert.True(huge.BodyTouched);
        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, huge.Status);
    }

    [Fact]
    public async Task WithoutUploadRoot_TheServiceAnswersAsBefore()
    {
        var r = await PostAsync(signedIn: false, visitor: null, hugeUnreadable: false, root: null);
        Assert.Equal(HttpStatusCode.OK, r.Status);   // Sonde: der Dienst würde „not configured“ melden
        Assert.Equal(EbookService.MaxUploadRequestBytes, r.Limit);
    }

    [Fact]
    public void Limit_FollowsTheOptions()
    {
        Assert.Null(AnonymousEbookUploadLimitAttribute.AnonymousRequestLimit(new EbookOptions { AnonymousUploads = false }, Visitor));
        Assert.Null(AnonymousEbookUploadLimitAttribute.AnonymousRequestLimit(new EbookOptions(), "kaputt!"));
        Assert.Equal(10L * 1024 * 1024 + AnonymousEbookUploadLimitAttribute.MultipartSlackBytes,
            AnonymousEbookUploadLimitAttribute.AnonymousRequestLimit(new EbookOptions { MaxFileBytes = 10L * 1024 * 1024 }, Visitor));
    }

    [Fact]
    public void UploadEndpoint_CarriesTheGate()
    {
        var upload = typeof(RookHub.Api.Controllers.Ebooks.EbooksController).GetMethod("Upload")!;
        Assert.NotNull(upload.GetCustomAttributes(typeof(AnonymousEbookUploadLimitAttribute), false).SingleOrDefault());
    }
}

/// <summary>Sonde für <see cref="AnonymousEbookUploadLimitTests"/>: dieselben Grenzen wie <c>EbooksController.Upload</c>.</summary>
[ApiController]
[Route("probe-ebook-upload")]
public class EbookUploadLimitProbeController : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(EbookService.MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = EbookService.MaxUploadRequestBytes)]
    [AnonymousEbookUploadLimit]
    public IActionResult Upload(IFormFile file) => Ok(new { length = file.Length });
}
