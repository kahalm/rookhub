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
    public async Task GetCachedLinePgns_SendsOnlyOids_AndMapsTheParsedGamesByOid()
    {
        var pgn = "[Event \"x\"]\n[ChessableOid \"11\"]\n\n1. e4 *\n\n[Event \"x\"]\n[ChessableOid \"12\"]\n\n1. d4 *\n";
        var handler = new CapturingHandler(System.Text.Json.JsonSerializer.Serialize(new
        { bid = "1", name = "x", mode = "None", chapterCount = 1, lineCount = 2, pgn }));
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") });

        var truth = await proxy.GetCachedLinePgnsAsync("424242", new[] { "11", "12", "kaputt" });

        Assert.Equal("/api/chessable/direct/course/parse", handler.Path);
        // Der echte Kurs, kein Platzhalter: piratechess füllt nur Linien, die unter genau diesem Kurs liegen.
        Assert.Contains("\"bid\":\"424242\"", handler.Body);
        Assert.Contains("\"lines\":[null,null]", handler.Body);      // keine Inhalte → piratechess schreibt nichts in den Cache
        Assert.Contains("\"lineOids\":[\"11\",\"12\"]", handler.Body);
        Assert.Contains("1. e4", truth["11"]);
        Assert.Contains("1. d4", truth["12"]);
    }

    /// <summary>A3-013: oids gehen kanonisch an piratechess. Vorher wurden „011" und „11" zwei Eintraege, und
    /// <c>{"id":011}</c> ist keine gueltige JSON-Zahl, piratechess lehnte die ganze Abfrage ab.</summary>
    [Fact]
    public async Task GetCachedLinePgns_LeadingZeros_AskOnceInCanonicalForm()
    {
        var pgn = "[Event \"x\"]\n[ChessableOid \"11\"]\n\n1. e4 *\n";
        var handler = new CapturingHandler(System.Text.Json.JsonSerializer.Serialize(new
        { bid = "1", name = "x", mode = "None", chapterCount = 1, lineCount = 1, pgn }));
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") });

        var truth = await proxy.GetCachedLinePgnsAsync("424242", new[] { "011", "11" });

        using var body = System.Text.Json.JsonDocument.Parse(handler.Body!);
        var chapter = Assert.Single(body.RootElement.GetProperty("chapters").EnumerateArray());
        Assert.Equal(new[] { "11" },
            chapter.GetProperty("lineOids").EnumerateArray().Select(o => o.GetString()).ToArray());
        // Das Kapitel-JSON ist gueltiges JSON (mit „011" scheiterte schon das Parsen) mit genau EINER Linie id 11.
        using var list = System.Text.Json.JsonDocument.Parse(chapter.GetProperty("chapterJson").GetString()!);
        var entry = Assert.Single(list.RootElement.GetProperty("list").GetProperty("data").EnumerateArray());
        Assert.Equal(11, entry.GetProperty("id").GetInt32());
        Assert.Equal("{\"id\":11,\"name\":\"x\"}", entry.GetRawText());
        Assert.Contains("1. e4", truth["11"]);
    }

    [Fact]
    public async Task GetCachedLineOids_PostsToLinesCached_AndReturnsTheAnswer()
    {
        var handler = new CapturingHandler("{\"oids\":[\"11\"]}");
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") });

        var cached = await proxy.GetCachedLineOidsAsync("424242", new[] { "11", "12" });

        Assert.Equal("/api/chessable/direct/lines/cached", handler.Path);
        Assert.Contains("\"oids\":[\"11\",\"12\"]", handler.Body);
        Assert.Contains("\"bid\":\"424242\"", handler.Body);   // nur Linien, mit denen piratechess DIESEN Kurs füllt
        Assert.Equal(new[] { "11" }, cached);
    }

    [Fact]
    public async Task GetCachedLineOids_ProxyDown_ReturnsEmpty_ButLogsWarning()
    {
        var log = new CapturingLogger<ChessableProxyService>();
        var proxy = new ChessableProxyService(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://pc:8080") }, log);

        Assert.Empty(await proxy.GetCachedLineOidsAsync("424242", new[] { "11" }));
        Assert.Contains(log.Events, e => e.Message.Contains("Linien-Cache"));
    }

    // --- S2-008: Kurs-Abruf-Job bei piratechess abbrechen (best effort, blockiert nie) ---

    private sealed class ReplyHandler : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = new();
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _reply;
        public ReplyHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) => _reply = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return _reply(request, ct);
        }
    }

    [Fact]
    public async Task CancelCourseJob_SendsDeleteWithServiceKey_ToTheJobRoute()
    {
        var handler = new ReplyHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"cancelled\":true}", System.Text.Encoding.UTF8, "application/json")
        }));
        // Wie in Program.cs: der Dienst-Schlüssel hängt am typisierten Client, nicht am einzelnen Aufruf.
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") };
        client.DefaultRequestHeaders.Add("X-Service-Key", "svc-key");
        var proxy = new ChessableProxyService(client);

        Assert.True(await proxy.CancelCourseJobAsync("0f3a9c"));

        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, req.Method);
        Assert.Equal("/api/chessable/direct/course/0f3a9c", req.RequestUri!.AbsolutePath);
        Assert.Equal("svc-key", Assert.Single(req.Headers.GetValues("X-Service-Key")));
    }

    /// <summary>404 = Job schon weg, 405 = älterer piratechess ohne den Endpunkt: kein Fehler, keine Warnung.</summary>
    [Theory]
    [InlineData(System.Net.HttpStatusCode.NotFound)]
    [InlineData(System.Net.HttpStatusCode.MethodNotAllowed)]
    public async Task CancelCourseJob_UnknownJobOrOlderPiratechess_IsIgnored(System.Net.HttpStatusCode status)
    {
        var log = new CapturingLogger<ChessableProxyService>();
        var proxy = new ChessableProxyService(new HttpClient(new ReplyHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(status)))) { BaseAddress = new Uri("http://pc:8080") }, log);

        Assert.False(await proxy.CancelCourseJobAsync("job-1"));
        Assert.DoesNotContain(log.Events, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task CancelCourseJob_ServerErrorOrProxyDown_ReturnsFalse_ButLogsWarning()
    {
        var log = new CapturingLogger<ChessableProxyService>();
        var down = new ChessableProxyService(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://pc:8080") }, log);
        var broken = new ChessableProxyService(new HttpClient(new ReplyHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError))))
            { BaseAddress = new Uri("http://pc:8080") }, log);

        Assert.False(await down.CancelCourseJobAsync("job-1"));
        Assert.False(await broken.CancelCourseJobAsync("job-2"));
        Assert.Equal(2, log.Events.Count(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && e.Message.Contains("Abbruch von Kurs-Job")));
    }

    /// <summary>Ein hängender piratechess hält weder den Abbruch-Klick noch die Poll-Schleife auf (der Client selbst
    /// hat 15 min Timeout).</summary>
    [Fact]
    public async Task CancelCourseJob_HangingPiratechess_GivesUpAfterTheShortTimeout()
    {
        var proxy = new ChessableProxyService(new HttpClient(new ReplyHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        })) { BaseAddress = new Uri("http://pc:8080") })
        { CancelTimeout = TimeSpan.FromMilliseconds(50) };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(await proxy.CancelCourseJobAsync("job-1"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    /// <summary>Eine frei eingetragene Kurs-Id (Repertoire-Feld) ist keine Chessable-bid: dafür kennt der Cache keine
    /// Linie, also gar nicht erst fragen — sonst liefe z. B. die Repertoire-Bereinigung bei jedem Start in einen 400.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1234567890123")]
    public async Task CachedLineCalls_WithoutAValidCourseBid_AskNothing(string bid)
    {
        var handler = new CapturingHandler("{\"oids\":[\"11\"],\"pgn\":\"\"}");
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") });

        Assert.Empty(await proxy.GetCachedLineOidsAsync(bid, new[] { "11" }));
        Assert.Empty(await proxy.GetCachedLinePgnsAsync(bid, new[] { "11" }));
        Assert.Null(handler.Path);
    }
}
