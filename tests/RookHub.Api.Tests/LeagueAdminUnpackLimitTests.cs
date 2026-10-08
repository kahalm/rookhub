using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Services;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>F7-021: Die LeagueHub-Admin-Importe (admin/games, admin/mega-players; admin/import ist seit 08.10.2026 entfernt) entpacken gzip nur
/// noch bis zur Rumpf-Obergrenze. RequestSizeLimit zählt die komprimierten Bytes; eine gzip-Bombe entpackte bis
/// dahin ungebremst (admin/games sogar per ReadToEndAsync in EINEN String).</summary>
public class LeagueAdminUnpackLimitTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private const int SmallLimit = 64 * 1024;

    private static byte[] Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(text));
        return ms.ToArray();
    }

    /// <summary>Komprimiert winzig, entpackt 1 MB — weit über der Test-Grenze.</summary>
    private static byte[] Bomb(string prefix, char fill, string suffix = "")
        => Gzip(prefix + new string(fill, 1024 * 1024) + suffix);

    private LeagueController Controller(byte[] body, bool gzip = true)
    {
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(body);
        if (gzip) http.Request.Headers.ContentEncoding = "gzip";
        return new LeagueController(league, null!, null!)
        {
            CollectionUnpackedLimit = SmallLimit,
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static void Assert413(IActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, obj.StatusCode);
        // Codereview A10-006: einheitliche Fehlerform { message } (vorher { error }).
        var json = System.Text.Json.JsonSerializer.SerializeToElement(obj.Value);
        Assert.Contains("MB", json.GetProperty("message").GetString());
        Assert.False(json.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Games_GzipBomb_Returns413()
    {
        var bomb = Bomb("[Event \"x\"]\n[WhiteFideId \"1\"]\n\n1. e4 {", 'a', "} 1-0\n");
        Assert.True(bomb.Length < SmallLimit);                                     // komprimiert unter jeder Grenze
        Assert413(await Controller(bomb).ImportGames("Mega", default));
    }

    [Fact]
    public async Task MegaPlayers_GzipBomb_Returns413()
    {
        Assert413(await Controller(Bomb("", 'a')).ImportMegaPlayers(new LeagueMegaPlayers(_db), default));
    }

    [Fact]
    public async Task Games_GzipUnderTheLimit_StillImports()
    {
        var pgn = "[Event \"x\"]\n[White \"A\"]\n[Black \"B\"]\n[Result \"1-0\"]\n\n1. e4 e5 1-0\n";
        var result = await Controller(Gzip(pgn)).ImportGames("Mega", default);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task MegaPlayers_GzipUnderTheLimit_StillReplaces()
    {
        var tsv = "Carlsen, Magnus\t1503014\t3000\t2025\t2882\nCaruana, Fabiano\t2020009\t2500\t2025\t2844\n";
        var ok = Assert.IsType<OkObjectResult>(await Controller(Gzip(tsv)).ImportMegaPlayers(new LeagueMegaPlayers(_db), default));
        Assert.Equal(2, (int)ok.Value!.GetType().GetProperty("players")!.GetValue(ok.Value)!);
    }

    [Fact]
    public void RequestSizeLimits_StayWhereTheyWere()
    {
        Assert.Equal(400L * 1024 * 1024, LeagueController.CollectionMaxBytes);
    }
}

public class LimitedReadStreamTests
{
    private static byte[] Data(int n) => Enumerable.Range(0, n).Select(i => (byte)(i * 31)).ToArray();

    [Fact]
    public void Read_UpToTheLimit_PassesTheBytesThrough()
    {
        var data = Data(10_000);
        using var limited = new LimitedReadStream(new MemoryStream(data), data.Length);
        using var copy = new MemoryStream();
        limited.CopyTo(copy, 1000);
        Assert.Equal(data, copy.ToArray());
    }

    [Fact]
    public async Task ReadAsync_UpToTheLimit_PassesTheBytesThrough()
    {
        var data = Data(10_000);
        await using var limited = new LimitedReadStream(new MemoryStream(data), data.Length);
        using var copy = new MemoryStream();
        await limited.CopyToAsync(copy, 1000);
        Assert.Equal(data, copy.ToArray());
    }

    [Fact]
    public void Read_BeyondTheLimit_Throws()
    {
        using var limited = new LimitedReadStream(new MemoryStream(Data(10_000)), 9_999);
        var ex = Assert.Throws<LimitedReadStream.LimitExceededException>(() => limited.CopyTo(Stream.Null, 1000));
        Assert.Equal(9_999, ex.Limit);
    }

    [Fact]
    public async Task ReadAsync_BeyondTheLimit_Throws()
    {
        await using var limited = new LimitedReadStream(new MemoryStream(Data(10_000)), 9_999);
        await Assert.ThrowsAsync<LimitedReadStream.LimitExceededException>(() => limited.CopyToAsync(Stream.Null, 1000));
    }

    [Fact]
    public void Dispose_DisposesTheInnerStream()
    {
        var inner = new MemoryStream(Data(10));
        new LimitedReadStream(inner, 10).Dispose();
        Assert.False(inner.CanRead);
    }
}
