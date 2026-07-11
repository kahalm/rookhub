using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Cache-Status-Abrufe des Chessable-Proxys: Fehler bleiben weich (false/leer = normaler
/// Download-Weg), müssen aber SICHTBAR geloggt werden — vorher schluckten nackte catches jede
/// Störung, ein down/fehlkonfigurierter piratechess ließ alle Kurse still ungecacht erscheinen
/// (Fast-Lane tot, keine Diagnose-Zeile).</summary>
public class ChessableProxyServiceTests
{
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("Connection refused");
    }

    [Fact]
    public async Task IsCourseCached_ProxyDown_ReturnsFalse_ButLogsWarning()
    {
        var log = new CapturingLogger<ChessableProxyService>();
        var proxy = new ChessableProxyService(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://pc:8080") }, log);

        Assert.False(await proxy.IsCourseCachedAsync("123"));
        Assert.Contains(log.Events, e => e.Message.Contains("Cache-Check"));
    }

    [Fact]
    public async Task GetCachedBids_ProxyDown_ReturnsEmpty_ButLogsWarning()
    {
        var log = new CapturingLogger<ChessableProxyService>();
        var proxy = new ChessableProxyService(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://pc:8080") }, log);

        Assert.Empty(await proxy.GetCachedBidsAsync());
        Assert.Contains(log.Events, e => e.Message.Contains("Batch-Cache"));
    }
}
