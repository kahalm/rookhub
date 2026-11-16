using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using Xunit;

namespace RookHub.Api.Tests.Accounts;

/// <summary>
/// Besucher-Herkunft: Google-Server und VPN-Austritte erkennen (<see cref="VisitorNetworkClassifier"/>) — über die
/// eingebettete Netzliste <c>Assets/visitor-networks.json</c> und die DB-IP-Liste „IP to ASN Lite“ (<see cref="IpAsnService"/>).
/// </summary>
public class VisitorNetworkClassifierTests : IDisposable
{
    private readonly string _cachePath = Path.Combine(Path.GetTempPath(), $"ipasn-test-{Guid.NewGuid():N}.csv.gz");
    public void Dispose()
    {
        if (File.Exists(_cachePath)) File.Delete(_cachePath);
    }

    private static VisitorNetworkClassifier Embedded(Func<IPAddress, uint?>? asnOf = null) =>
        new(VisitorNetworkList.Embedded.Value, asnOf ?? (_ => null));

    // Die Adressen aus dem Anlass (LeagueHub, November 2026): Google lädt geteilte Links nach, VPN-Austritte.
    [Theory]
    [InlineData("66.249.83.17")]
    [InlineData("66.102.8.40")]
    [InlineData("66.102.6.12")]
    [InlineData("142.250.32.5")]
    [InlineData("142.250.33.9")]
    [InlineData("74.125.212.100")]
    [InlineData("192.178.15.3")]
    [InlineData("2001:4860:4801::1")]
    public void EmbeddedList_KnowsGoogle_EvenWithoutTheAsnList(string ip) =>
        Assert.Equal(VisitorNetwork.Google, Embedded().Classify(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("187.15.156.20")]
    [InlineData("187.15.158.200")]
    [InlineData("187.13.132.7")]
    [InlineData("194.88.96.1")]
    [InlineData("194.88.103.254")]
    [InlineData("185.61.156.1")]
    [InlineData("185.61.159.250")]
    [InlineData("::ffff:187.15.158.9")]
    public void EmbeddedList_KnowsTheVpnNetworks_EvenWithoutTheAsnList(string ip) =>
        Assert.Equal(VisitorNetwork.Vpn, Embedded().Classify(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("212.56.161.10")]   // IKB Innsbruck — gewöhnlicher Anschluss
    [InlineData("81.223.10.20")]
    [InlineData("194.88.104.1")]    // knapp neben dem Hydra-/21
    [InlineData("2a02:8388::1")]
    public void OrdinaryAddresses_StayNormal(string ip) =>
        Assert.Equal(VisitorNetwork.Normal, Embedded().Classify(IPAddress.Parse(ip)));

    [Theory]
    [InlineData(15169u, VisitorNetwork.Google)]
    [InlineData(396982u, VisitorNetwork.Google)]
    [InlineData(136787u, VisitorNetwork.Vpn)]   // PacketHub
    [InlineData(212238u, VisitorNetwork.Vpn)]   // Datacamp
    [InlineData(25369u, VisitorNetwork.Vpn)]    // Hydra
    [InlineData(9009u, VisitorNetwork.Vpn)]     // M247
    [InlineData(62240u, VisitorNetwork.Vpn)]    // Clouvider
    [InlineData(31510u, VisitorNetwork.Normal)] // Innsbrucker Kommunalbetriebe
    [InlineData(8447u, VisitorNetwork.Normal)]  // A1 Telekom Austria
    [InlineData(16591u, VisitorNetwork.Normal)] // Google Fiber ist ein echter Anschluss
    public void Asn_DecidesOutsideTheFixedNetworks(uint asn, VisitorNetwork expected) =>
        Assert.Equal(expected, Embedded(_ => asn).Classify(IPAddress.Parse("203.0.113.9")));

    [Fact]
    public void EmbeddedList_IsDisjointAndNotEmpty()
    {
        var list = VisitorNetworkList.Embedded.Value;
        Assert.True(list.GoogleNetworks.Count >= 50, "Google-Netze fehlen — tools/visitor-networks/update_google.py");
        Assert.Contains(15169u, list.GoogleAsns);
        Assert.Contains(396982u, list.GoogleAsns);
        Assert.Empty(list.GoogleAsns.Intersect(list.VpnAsns));
        Assert.True(list.Ranges().Count > 50);   // wirft bei Überlappung
    }

    [Fact]
    public void OverlappingNetworks_AreRejected()
    {
        var list = new VisitorNetworkList([], [], ["10.0.0.0/8"], ["10.1.0.0/16"]);
        Assert.Throws<InvalidDataException>(() => list.Ranges());
    }

    // ── DB-IP „IP to ASN Lite“ ────────────────────────────────────────────────────────────────────────

    private const string AsnCsv =
        "1.0.0.0,1.0.0.255,13335,\"Cloudflare, Inc.\"\n" +
        "66.249.64.0,66.249.95.255,15169,\"Google LLC\"\n" +
        "187.15.158.0,187.15.158.255,136787,\"PacketHub S.A.\"\n" +
        "kaputt,zeile\n" +
        "5.6.7.0,5.6.7.255,keine-nummer,\"X\"\n" +
        "2a02:8388::,2a02:8388:ffff:ffff:ffff:ffff:ffff:ffff,8412,\"T-Mobile Austria GmbH\"\n";

    [Fact]
    public void AsnTable_ReadsNumbers_IgnoresCommasInTheNameAndBrokenLines()
    {
        var table = IpAsnTable.Parse(new StringReader(AsnCsv));
        Assert.Equal(4, table.Count);
        Assert.Equal(13335u, table.Lookup(IPAddress.Parse("1.0.0.1")));
        Assert.Equal(15169u, table.Lookup(IPAddress.Parse("66.249.83.17")));
        Assert.Equal(136787u, table.Lookup(IPAddress.Parse("::ffff:187.15.158.9")));
        Assert.Equal(8412u, table.Lookup(IPAddress.Parse("2a02:8388:1::2")));
        Assert.Null(table.Lookup(IPAddress.Parse("5.6.7.8")));
        Assert.Null(table.Lookup(IPAddress.Parse("9.9.9.9")));
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

    private static byte[] Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(text));
        return ms.ToArray();
    }

    [Fact]
    public async Task AsnService_DownloadsTheMonthlyListOnce_AndAnswersWithoutWaitingAfterwards()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Gzip(AsnCsv)) });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IpAsn:CachePath"] = _cachePath,
            ["IpAsn:SourceUrlTemplate"] = "https://example.test/asn-{0:yyyy-MM}.csv.gz",
        }).Build();
        var now = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var service = new IpAsnService(new FakeFactory(handler), config, NullLogger<IpAsnService>.Instance, () => now);

        Assert.Null(await service.AsnOfAsync(IPAddress.Parse("192.168.1.1")));      // privat: kein Download
        Assert.Empty(handler.Requests);
        Assert.Equal(15169u, await service.AsnOfAsync(IPAddress.Parse("66.249.83.17")));
        Assert.Equal(136787u, service.TryAsnOf(IPAddress.Parse("187.15.158.9")));
        Assert.Equal(new[] { "https://example.test/asn-2026-11.csv.gz" }, handler.Requests);
    }

    [Fact]
    public void AsnService_Disabled_AnswersNothing()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IpAsn:Enabled"] = "false",
        }).Build();
        var handler = new FakeHandler(_ => throw new InvalidOperationException("kein Download erwartet"));
        var service = new IpAsnService(new FakeFactory(handler), config, NullLogger<IpAsnService>.Instance);
        Assert.Null(service.TryAsnOf(IPAddress.Parse("66.249.83.17")));
        Assert.Empty(handler.Requests);
    }
}
