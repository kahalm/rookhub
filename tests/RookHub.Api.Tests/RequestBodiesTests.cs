using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>R2-006: EINE Hilfe für „Rumpf gzip-entpacken und begrenzen“ (Liga-Admin-Importe und Spielervorbereitung) statt einer
/// eigenen Lese-Schleife im <c>PrepController</c>. Grenze, Text und BOM wie bisher.</summary>
public class RequestBodiesTests
{
    private static HttpRequest Request(byte[] body, bool gzip)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(body);
        if (gzip) ctx.Request.Headers.ContentEncoding = "gzip";
        return ctx.Request;
    }

    private static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(data);
        return ms.ToArray();
    }

    [Fact]
    public async Task ReadText_genauDieGrenze_geht_einByteDarueber_null_gepacktUndUngepackt()
    {
        var exact = Encoding.UTF8.GetBytes(new string('a', 100));
        var over = Encoding.UTF8.GetBytes(new string('a', 101));
        Assert.Equal(100, (await RequestBodies.ReadTextAsync(Request(exact, false), 100, default))!.Length);
        Assert.Equal(100, (await RequestBodies.ReadTextAsync(Request(Gzip(exact), true), 100, default))!.Length);
        Assert.Null(await RequestBodies.ReadTextAsync(Request(over, false), 100, default));
        Assert.Null(await RequestBodies.ReadTextAsync(Request(Gzip(over), true), 100, default));
    }

    [Fact]
    public async Task ReadText_laesstDieBytesWieGesendet_auchEinBom()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("1. e4 *")).ToArray();
        Assert.Equal(Encoding.UTF8.GetString(withBom), await RequestBodies.ReadTextAsync(Request(withBom, false), 1000, default));
    }

    [Fact]
    public async Task Unpacked_ungepackt_ohneGrenze_derRumpfSelbst()
    {
        var req = Request([1, 2, 3], false);
        Assert.Same(req.Body, RequestBodies.Unpacked(req, 1));
        await using var limited = RequestBodies.Unpacked(Request(Gzip([1, 2, 3]), true), 2);
        await Assert.ThrowsAsync<LimitedReadStream.LimitExceededException>(async () => await limited.ReadAsync(new byte[10]));
    }
}
