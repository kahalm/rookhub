using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>GitHub-Actions-Übersicht: Token-Gate, Parsing, Fehlerbehandlung, Cache.</summary>
// Teilt sich prozessweite statische Caches (_reportedBuilds/_pushedRuns) mit CiBuildReportControllerTests
// → gleiche Collection, damit die beiden Klassen NICHT parallel laufen und sich die Statics nicht leeren.
[Collection("CiStaticState")]
public class GithubActionsServiceTests
{
    // Der Reported-Builds-Cache ist prozessweit statisch → vor jedem Test leeren, damit eine in
    // einer anderen Testklasse gemeldete Build-SHA (z. B. CiBuildReportControllerTests „log-watcher")
    // nicht in den Overview-Tests eine 2. („nachgeladene") Run-Zeile erzeugt.
    public GithubActionsServiceTests()
    {
        GithubActionsService.ResetReportedBuildsForTests();
        GithubActionsService.ResetPushedRunsForTests();
    }

    private const string RunsJson = """
    { "total_count": 1, "workflow_runs": [ {
        "id": 1, "name": "CI", "display_title": "fix things", "head_branch": "master",
        "event": "push", "status": "completed", "conclusion": "success", "run_number": 42,
        "created_at": "2026-07-01T10:00:00Z", "updated_at": "2026-07-01T10:05:00Z",
        "head_sha": "abc1234def5678", "html_url": "https://github.com/kahalm/rookhub/actions/runs/1", "actor": { "login": "kahalm" } } ] }
    """;

    private static GithubActionsService Build(StubHandler handler, bool withToken = true,
        IDictionary<string, string?>? extraSettings = null, HttpMessageHandler? buildInfoHandler = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var settings = new Dictionary<string, string?>
        {
            ["GitHub:Repos:0"] = "rookhub",
            ["GitHub:CacheSeconds"] = "60",
        };
        if (withToken) settings["GitHub:Token"] = "ghp_test";
        if (extraSettings != null) foreach (var kv in extraSettings) settings[kv.Key] = kv.Value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var factory = new StubHttpClientFactory(buildInfoHandler ?? new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)));
        return new GithubActionsService(http, factory, config, new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GithubActionsService>.Instance);
    }

    [Fact]
    public async Task NoToken_ReturnsNotConfigured_WithoutCallingGithub()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var svc = Build(handler, withToken: false);

        var res = await svc.GetOverviewAsync();

        Assert.False(res.Configured);
        Assert.Empty(res.Repos);
        Assert.Equal(0, handler.Calls);   // knappes unauth. Limit nicht verbrannt
    }

    [Fact]
    public async Task ParsesRuns()
    {
        var handler = new StubHandler((_, _) => Json(RunsJson));
        var svc = Build(handler);

        var res = await svc.GetOverviewAsync();

        Assert.True(res.Configured);
        var repo = Assert.Single(res.Repos);
        Assert.Equal("rookhub", repo.Repo);
        Assert.Null(repo.Error);
        var run = Assert.Single(repo.Runs);
        Assert.Equal("CI", run.Name);
        Assert.Equal("fix things", run.Title);
        Assert.Equal("master", run.Branch);
        Assert.Equal("success", run.Conclusion);
        Assert.Equal(42, run.RunNumber);
        Assert.Equal("kahalm", run.Actor);
        Assert.Equal("abc1234def5678", run.HeadSha);
        Assert.Equal("master", run.Ref);
        Assert.False(run.IsTag);
    }

    [Fact]
    public async Task HttpError_YieldsRepoError_NoThrow()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        var svc = Build(handler);

        var res = await svc.GetOverviewAsync();

        var repo = Assert.Single(res.Repos);
        Assert.Equal("HTTP 404", repo.Error);
        Assert.Empty(repo.Runs);
    }

    [Fact]
    public async Task SecondCall_IsServedFromCache()
    {
        var handler = new StubHandler((_, _) => Json(RunsJson));
        var svc = Build(handler);

        await svc.GetOverviewAsync();
        await svc.GetOverviewAsync();

        // 1 Repo × (runs + tags) = 2 Calls beim 1. Mal; der 2. Poll trifft den Cache → keine weiteren.
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task MergesRunningBuild_FromStackBuildInfo()
    {
        var handler = new StubHandler((_, _) => Json(RunsJson));   // GitHub-Runs (head_sha abc1234def5678)
        // Crawler-build-info liefert dieselbe SHA + master → der Crawler-Run soll als „laufend" markierbar sein.
        var biHandler = new StubHandler((req, _) =>
            req.RequestUri!.AbsoluteUri.Contains("/api/health/build-info")
                ? Json("""{ "sha": "abc1234def5678", "ref": "master" }""")
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var svc = Build(handler, extraSettings: new Dictionary<string, string?>
        {
            ["GitHub:Repos:0"] = "chessresults_crawler",
            ["Crawler:BaseUrl"] = "http://crawler:8080",
            ["Crawler:ApiKey"] = "k",
        }, buildInfoHandler: biHandler);

        var res = await svc.GetOverviewAsync();

        var repo = Assert.Single(res.Repos);
        Assert.Equal("chessresults_crawler", repo.Repo);
        Assert.Equal("abc1234def5678", repo.RunningSha);
        Assert.Equal("master", repo.RunningRef);
    }

    [Fact]
    public async Task PushedBuildReport_SetsRunningBuild()
    {
        var handler = new StubHandler((_, _) => Json(RunsJson));   // head_sha abc1234def5678, branch master
        var svc = Build(handler, extraSettings: new Dictionary<string, string?>
        {
            ["GitHub:Repos:0"] = "chessresults_crawler",
        });
        // Stack meldet seine laufende SHA/Ref per Push (rookhub kann ihn nicht pollen).
        svc.ReportBuild("chessresults_crawler", "abc1234def5678", "master");

        var res = await svc.GetOverviewAsync();

        var repo = Assert.Single(res.Repos);
        Assert.Equal("abc1234def5678", repo.RunningSha);
        Assert.Equal("master", repo.RunningRef);
    }

    [Fact]
    public async Task NoStackConfigured_LeavesRunningBuildNull()
    {
        var handler = new StubHandler((_, _) => Json(RunsJson));
        var svc = Build(handler);   // nur rookhub, keine Stack-URLs

        var res = await svc.GetOverviewAsync();

        var repo = Assert.Single(res.Repos);
        Assert.Null(repo.RunningSha);
        Assert.Null(repo.RunningRef);
    }

    [Fact]
    public async Task LogWatcher_FiltersOutTestRuns_KeepsOnlyBuildAndPush()
    {
        const string mixed = """
        { "total_count": 2, "workflow_runs": [
          { "id": 1, "name": "Tests", "display_title": "t", "head_branch": "main", "event": "push",
            "status": "completed", "conclusion": "success", "run_number": 9,
            "created_at": "2026-07-01T10:00:00Z", "updated_at": "2026-07-01T10:02:00Z",
            "head_sha": "aaa", "html_url": "u", "actor": { "login": "kahalm" } },
          { "id": 2, "name": "Build & Push Docker Image", "display_title": "b", "head_branch": "main", "event": "push",
            "status": "completed", "conclusion": "success", "run_number": 10,
            "created_at": "2026-07-01T10:00:00Z", "updated_at": "2026-07-01T10:03:00Z",
            "head_sha": "bbb", "html_url": "u", "actor": { "login": "kahalm" } }
        ] }
        """;
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.AbsoluteUri.Contains("/actions/runs") ? Json(mixed) : Json("[]"));
        var svc = Build(handler, extraSettings: new Dictionary<string, string?> { ["GitHub:Repos:0"] = "log-watcher" });

        var res = await svc.GetOverviewAsync();

        var repo = Assert.Single(res.Repos);
        Assert.Equal("log-watcher", repo.Repo);
        var run = Assert.Single(repo.Runs);   // „Tests" raus, nur der Build-&-Push-Lauf bleibt
        Assert.Equal("Build & Push Docker Image", run.Name);
    }

    [Fact]
    public async Task GetRepoAsync_UnknownRepo_ReturnsNull()
    {
        var handler = new StubHandler((_, _) => Json(RunsJson));
        var svc = Build(handler);   // nur "rookhub" konfiguriert
        Assert.Null(await svc.GetRepoAsync("some-other-repo"));   // kein beliebiger Abruf
    }

    [Fact]
    public async Task GetRepoAsync_ConfiguredRepo_ReturnsFreshSingleRepo()
    {
        var handler = new StubHandler((_, _) => Json(RunsJson));
        var svc = Build(handler);

        var repo = await svc.GetRepoAsync("rookhub");

        Assert.NotNull(repo);
        Assert.Equal("rookhub", repo!.Repo);
        Assert.Null(repo.Error);
        Assert.Equal("CI", Assert.Single(repo.Runs).Name);
    }

    [Fact]
    public async Task PushedRun_OverridesGithubRun_BySameRunId()
    {
        var handler = new StubHandler((_, _) => Json(RunsJson));   // GitHub: Run-Id 1, completed/success
        var svc = Build(handler);
        // Webhook meldet denselben Run (Id 1), aber als laufend → Push gewinnt (frischer).
        svc.ReportRun("rookhub", new RookHub.Api.DTOs.CiRunDto(1, "CI", "building", "master", "push",
            "in_progress", null, 42, DateTime.UtcNow, DateTime.UtcNow, "u", "kahalm", "abc1234def5678", "master", false));

        var res = await svc.GetOverviewAsync();

        var run = Assert.Single(Assert.Single(res.Repos).Runs);
        Assert.Equal(1, run.Id);
        Assert.Equal("in_progress", run.Status);   // Push-Status überschreibt den GitHub-Stand
    }

    [Fact]
    public async Task PushedRun_NewRun_AppearsNewestFirst()
    {
        var handler = new StubHandler((_, _) => Json(RunsJson));   // GitHub-Run createdAt 10:00
        var svc = Build(handler);
        svc.ReportRun("rookhub", new RookHub.Api.DTOs.CiRunDto(999, "CI", "fresh", "master", "push",
            "in_progress", null, 43, new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc), "u", "kahalm", "def", "master", false));

        var res = await svc.GetOverviewAsync();

        var runs = Assert.Single(res.Repos).Runs;
        Assert.Equal(999, runs[0].Id);   // neuester (11:00) zuerst
        Assert.Contains(runs, r => r.Id == 1);   // GitHub-Run bleibt erhalten
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _reply;
        public int Calls { get; private set; }
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> reply) => _reply = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_reply(request, ct));
        }
    }
}
