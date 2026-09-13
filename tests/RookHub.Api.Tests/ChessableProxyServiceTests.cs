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

    /// <summary>Erfasst Request-URL + Body und liefert eine feste Antwort — für den Parse-Endpoint.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Path;
        public string? Body;
        private readonly string _responseJson;
        public CapturingHandler(string responseJson) => _responseJson = responseJson;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri?.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public async Task ParseCourse_PostsChaptersToParseEndpoint_AndReturnsPgn()
    {
        var handler = new CapturingHandler(
            "{\"bid\":\"424242\",\"name\":\"Course X\",\"mode\":\"None\",\"chapterCount\":1,\"lineCount\":2,\"pgn\":\"[Event \\\"x\\\"]\\n1. e4 *\"}");
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") });

        var chapters = new List<RookHub.Api.DTOs.ChessableIngestChapter>
        {
            new("{\"list\":{\"name\":\"Ch1\"}}", new List<string> { "{\"game\":{}}" })
        };
        var result = await proxy.ParseCourseAsync("424242", "None", chapters);

        Assert.Equal("/api/chessable/direct/course/parse", handler.Path);
        Assert.Contains("424242", handler.Body);
        Assert.Contains("Ch1", handler.Body);          // Kapitel-JSON durchgereicht
        Assert.Equal("Course X", result.Name);
        Assert.Equal(2, result.LineCount);
        Assert.Contains("e4", result.Pgn);
    }

    [Fact]
    public async Task ParseCourse_ForwardsLineOids_CourseJson_AndComplete()
    {
        var handler = new CapturingHandler(
            "{\"bid\":\"424242\",\"name\":\"C\",\"mode\":\"FirstKeyMove\",\"chapterCount\":1,\"lineCount\":1,\"pgn\":\"1. e4 *\"}");
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") });

        var chapters = new List<RookHub.Api.DTOs.ChessableIngestChapter>
        {
            new("{\"list\":{}}", new List<string> { null! }, new List<string> { "11" })
        };
        await proxy.ParseCourseAsync("424242", "FirstKeyMove", chapters, courseJson: "{\"course\":{}}", complete: true);

        Assert.Contains("\"lineOids\":[\"11\"]", handler.Body);
        Assert.Contains("\"lines\":[null]", handler.Body);   // null = Inhalt kommt aus dem geteilten Cache
        Assert.Contains("\"complete\":true", handler.Body);
        Assert.Contains("courseJson", handler.Body);
    }

    [Fact]
    public async Task GetCachedLineOids_PostsToLinesCached_AndReturnsTheAnswer()
    {
        var handler = new CapturingHandler("{\"oids\":[\"11\"]}");
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") });

        var cached = await proxy.GetCachedLineOidsAsync(new[] { "11", "12" });

        Assert.Equal("/api/chessable/direct/lines/cached", handler.Path);
        Assert.Contains("\"oids\":[\"11\",\"12\"]", handler.Body);
        Assert.Equal(new[] { "11" }, cached);
    }

    [Fact]
    public async Task GetCachedLineOids_ProxyDown_ReturnsEmpty_ButLogsWarning()
    {
        var log = new CapturingLogger<ChessableProxyService>();
        var proxy = new ChessableProxyService(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://pc:8080") }, log);

        Assert.Empty(await proxy.GetCachedLineOidsAsync(new[] { "11" }));
        Assert.Contains(log.Events, e => e.Message.Contains("Linien-Cache"));
    }
}
