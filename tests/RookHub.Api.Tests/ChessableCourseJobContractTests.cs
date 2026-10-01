using RookHub.Api.Controllers;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Codereview I2-007: der Vertrag rookhub ↔ piratechess ↔ RepCheck an der Repo-Grenze, mit LITERALEN Werten (nie
/// die Gegenseite importieren — dann wandert ein Fehler mit). Die Gegenstücke:
/// piratechess <c>Services/CourseFetchJobStore.cs</c> (Statuswerte, Test <c>CourseFetchJobStoreTests</c>) und
/// <c>Services/BrowserCourseAssembler.cs</c> (<c>MaxOidsPerLookup</c>), RepCheck <c>extension/chessable-activity.js</c>
/// (<c>SHARED_CACHE_BATCH</c>). Wer einen dieser Werte ändert, ändert ihn auf allen Seiten.
/// </summary>
public class ChessableCourseJobContractTests
{
    /// <summary>piratechess <c>BrowserCourseAssembler.MaxOidsPerLookup</c> (Stand 2026-10-01).</summary>
    private const int PiratechessMaxOidsPerLookup = 10000;

    /// <summary>RepCheck <c>SHARED_CACHE_BATCH</c> in <c>chessable-activity.js</c> (Stand 2026-10-01).</summary>
    private const int RepCheckSharedCacheBatch = 5000;

    [Fact]
    public void JobStatuses_MatchPiratechessLiterally()
    {
        Assert.Equal("running", ChessableCourseJobStatus.Running);
        Assert.Equal("completed", ChessableCourseJobStatus.Completed);
        Assert.Equal("failed", ChessableCourseJobStatus.Failed);
        Assert.Equal("cancelled", ChessableCourseJobStatus.Cancelled);
    }

    /// <summary>Senkt piratechess sein Limit unter rookhubs, beantwortet es eine hier erlaubte Anfrage mit 400 —
    /// <c>GetCachedLineOidsAsync</c> liefert dann still die leere Menge, und RepCheck holt alle Linien selbst.</summary>
    [Fact]
    public void CachedLineLookupLimit_RepCheckBatch_FitsRookhub_FitsPiratechess()
    {
        Assert.Equal(10000, ExtensionController.MaxCachedLineLookup);
        Assert.True(RepCheckSharedCacheBatch <= ExtensionController.MaxCachedLineLookup);
        Assert.True(ExtensionController.MaxCachedLineLookup <= PiratechessMaxOidsPerLookup);
    }

    /// <summary>Golden-Antworten von piratechess <c>GET /api/chessable/direct/course/{jobId}</c>
    /// (<c>DirectCourseProgressResponse</c>, ASP.NET-Vorgabe camelCase) — so, wie piratechess sie schickt, nicht
    /// aus rookhubs eigenem DTO gebaut.</summary>
    [Theory]
    [InlineData("""{"status":"running","chaptersDone":2,"chaptersTotal":5,"linesDone":17,"linesTotal":40,"chapterCount":0,"lineCount":0,"courseName":"","pgn":null,"error":null}""", "running", null, null)]
    [InlineData("""{"status":"completed","chaptersDone":5,"chaptersTotal":5,"linesDone":40,"linesTotal":40,"chapterCount":5,"lineCount":40,"courseName":"Lifetime Repertoires","pgn":"[Event \"x\"]\n1. e4 *","error":null}""", "completed", "[Event \"x\"]\n1. e4 *", null)]
    [InlineData("""{"status":"failed","chaptersDone":1,"chaptersTotal":5,"linesDone":3,"linesTotal":40,"chapterCount":0,"lineCount":0,"courseName":"","pgn":null,"error":"Course has no chapters"}""", "failed", null, "Course has no chapters")]
    [InlineData("""{"status":"cancelled","chaptersDone":1,"chaptersTotal":5,"linesDone":3,"linesTotal":40,"chapterCount":0,"lineCount":0,"courseName":"","pgn":null,"error":null}""", "cancelled", null, null)]
    public async Task CourseProgress_GoldenPiratechessAnswers_AreRead(string json, string status, string? pgn, string? error)
    {
        var handler = new JsonHandler(json);
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") });

        var dto = await proxy.GetCourseProgressAsync("0f3c2a");

        Assert.Equal("/api/chessable/direct/course/0f3c2a", handler.Path);
        Assert.NotNull(dto);
        Assert.Equal(status, dto!.Status);
        Assert.Equal(pgn, dto.Pgn);
        Assert.Equal(error, dto.Error);
        Assert.Equal(5, dto.ChaptersTotal);
        Assert.Equal(40, dto.LinesTotal);
    }

    /// <summary>Golden-Antwort von <c>POST /api/chessable/direct/course/start</c> (<c>DirectCourseStartResponse</c>).</summary>
    [Fact]
    public async Task CourseStart_GoldenPiratechessAnswer_IsRead()
    {
        var handler = new JsonHandler("""{"jobId":"9b1deb4d3b7d4bad9bdd2b0d7b3dcb6d"}""");
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://pc:8080") });

        var dto = await proxy.StartCourseFetchAsync("bearer", "55720", "None");

        Assert.Equal("/api/chessable/direct/course/start", handler.Path);
        Assert.Equal("9b1deb4d3b7d4bad9bdd2b0d7b3dcb6d", dto.JobId);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        public string? Path;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
