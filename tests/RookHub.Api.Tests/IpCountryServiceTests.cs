using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Land der Besucher-IP für die Startsprache von KidHub (<see cref="IpCountryService"/>): lokale
/// DB-IP-Liste, Download nur bei Bedarf, private Adressen ohne Land, Ausfall = kein Hinweis.
/// </summary>
public class IpCountryServiceTests : IDisposable
{
    // Auszug im Format von DB-IP „IP to Country Lite" (start,ende,land; v4 und v6 gemischt).
    private const string Csv =
        "0.0.0.0,0.255.255.255,ZZ\n" +
        "2.16.16.0,2.16.16.255,AT\n" +
        "2.16.20.0,2.16.23.255,HU\n" +
        "81.10.0.0,81.10.127.255,DE\n" +
        "kaputte,zeile\n" +
        "2a02:8388::,2a02:8388:ffff:ffff:ffff:ffff:ffff:ffff,AT\n" +
        "2a01:4f8::,2a01:4f8:ffff:ffff:ffff:ffff:ffff:ffff,DE\n";

    private readonly string _cachePath = Path.Combine(Path.GetTempPath(), $"ipcountry-test-{Guid.NewGuid():N}.csv.gz");

    public void Dispose()
    {
        if (File.Exists(_cachePath)) File.Delete(_cachePath);
    }

    private static byte[] Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(text));
        return ms.ToArray();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private (IpCountryService Service, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond,
        DateTime? now = null, bool enabled = true)
    {
        var handler = new FakeHandler(respond);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IpCountry:Enabled"] = enabled ? "true" : "false",
            ["IpCountry:CachePath"] = _cachePath,
            ["IpCountry:SourceUrlTemplate"] = "https://example.test/dbip-{0:yyyy-MM}.csv.gz",
        }).Build();
        var clock = now ?? new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        return (new IpCountryService(new FakeFactory(handler), config, NullLogger<IpCountryService>.Instance, () => clock), handler);
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Gzip(Csv)) };

    [Fact]
    public void Table_FindetV4UndV6UndIgnoriertKaputteZeilen()
    {
        var table = IpCountryTable.Parse(new StringReader(Csv));

        Assert.Equal(6, table.Count);
        Assert.Equal("AT", table.Lookup(IPAddress.Parse("2.16.16.0")));
        Assert.Equal("AT", table.Lookup(IPAddress.Parse("2.16.16.255")));
        Assert.Null(table.Lookup(IPAddress.Parse("2.16.17.0")));      // Lücke zwischen zwei Bereichen
        Assert.Equal("HU", table.Lookup(IPAddress.Parse("2.16.21.7")));
        Assert.Equal("DE", table.Lookup(IPAddress.Parse("2a01:4f8:c010::1")));
        Assert.Equal("AT", table.Lookup(IPAddress.Parse("2a02:8388:1:2::3")));
        Assert.Null(table.Lookup(IPAddress.Parse("0.1.2.3")));        // ZZ = reserviert
        Assert.Null(table.Lookup(IPAddress.Parse("1.1.1.1")));        // vor dem ersten Treffer / unbekannt
    }

    [Fact]
    public void Table_IPv4ImIPv6Gewand()
    {
        var table = IpCountryTable.Parse(new StringReader(Csv));
        Assert.Equal("DE", table.Lookup(IPAddress.Parse("::ffff:81.10.3.4")));
    }

    [Theory]
    [InlineData("10.24.12.12", false)]
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("100.100.1.1", false)]
    [InlineData("169.254.3.3", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("2.16.16.1", true)]
    [InlineData("2a02:8388::1", true)]
    public void NurOeffentlicheAdressenHabenEinLand(string ip, bool isPublic) =>
        Assert.Equal(isPublic, IpCountryService.IsPublic(IPAddress.Parse(ip)));

    [Fact]
    public async Task LaedtBeimErstenBedarfUndDannNichtMehr()
    {
        var (service, handler) = Create(_ => Ok());

        Assert.Equal("AT", await service.CountryOfAsync(IPAddress.Parse("2.16.16.9")));
        Assert.Equal("HU", await service.CountryOfAsync(IPAddress.Parse("2.16.22.1")));

        Assert.Equal(new[] { "https://example.test/dbip-2026-09.csv.gz" }, handler.Requests);
    }

    [Fact]
    public async Task PrivateAdresseLaedtGarNichts()
    {
        var (service, handler) = Create(_ => Ok());
        Assert.Null(await service.CountryOfAsync(IPAddress.Parse("10.24.12.12")));
        Assert.Null(await service.CountryOfAsync(null));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FehltDieDateiDesMonatsNimmtEsDieVorige()
    {
        var (service, handler) = Create(req => req.RequestUri!.ToString().Contains("2026-09")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Ok());

        Assert.Equal("AT", await service.CountryOfAsync(IPAddress.Parse("2.16.16.9")));
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("2026-08.csv.gz", handler.Requests[1]);
    }

    [Fact]
    public async Task AusfallGibtKeinenHinweisUndVersuchtEsErstSpaeterWieder()
    {
        var (service, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Assert.Null(await service.CountryOfAsync(IPAddress.Parse("2.16.16.9")));
        var afterFirst = handler.Requests.Count;
        Assert.Null(await service.CountryOfAsync(IPAddress.Parse("2.16.16.9")));
        Assert.Equal(afterFirst, handler.Requests.Count);   // innerhalb einer Stunde kein zweiter Versuch
    }

    [Fact]
    public async Task EineFrischeDateiImCacheBrauchtKeinenDownload()
    {
        await File.WriteAllBytesAsync(_cachePath, Gzip(Csv));
        File.SetLastWriteTimeUtc(_cachePath, new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
        var (service, handler) = Create(_ => throw new InvalidOperationException("kein Netz erwartet"));

        Assert.Equal("DE", await service.CountryOfAsync(IPAddress.Parse("81.10.1.1")));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AbgeschaltetFragtNiemanden()
    {
        var (service, handler) = Create(_ => Ok(), enabled: false);
        Assert.Null(await service.CountryOfAsync(IPAddress.Parse("2.16.16.9")));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("AT", "de")]
    [InlineData("ch", "de")]
    [InlineData("HR", "hr")]
    [InlineData("BA", "hr")]
    [InlineData("HU", "hu")]
    [InlineData("US", "en")]
    [InlineData("GB", "en")]
    [InlineData("FR", null)]
    [InlineData(null, null)]
    public void LandZuKindersprache(string? country, string? language) =>
        Assert.Equal(language, KidsLanguageHint.ForCountry(country));

    [Fact]
    public async Task Endpunkt_LiefertLandUndSpracheDerBesucherIp()
    {
        var (ipCountry, _) = Create(_ => Ok());
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var controller = new KidsController(new KidsPuzzleService(db), ipCountry: ipCountry);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("2.16.21.1");
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var result = await controller.GetLanguageHint(CancellationToken.None);

        var dto = Assert.IsType<KidsLanguageHintDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("HU", dto.Country);
        Assert.Equal("hu", dto.Language);
    }

    [Fact]
    public async Task Endpunkt_AusDemLanOhneHinweis()
    {
        var (ipCountry, _) = Create(_ => Ok());
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var controller = new KidsController(new KidsPuzzleService(db), ipCountry: ipCountry);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.24.12.12");
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var dto = Assert.IsType<KidsLanguageHintDto>(Assert.IsType<OkObjectResult>(
            (await controller.GetLanguageHint(CancellationToken.None)).Result).Value);
        Assert.Null(dto.Country);
        Assert.Null(dto.Language);
    }
}
