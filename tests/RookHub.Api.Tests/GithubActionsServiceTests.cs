using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>GitHub-Actions-Übersicht: Token-Gate, Parsing, Fehlerbehandlung, Cache.</summary>
public class GithubActionsServiceTests
{
    private const string RunsJson = """
    { "total_count": 1, "workflow_runs": [ {
        "id": 1, "name": "CI", "display_title": "fix things", "head_branch": "master",
        "event": "push", "status": "completed", "conclusion": "success", "run_number": 42,
        "created_at": "2026-07-01T10:00:00Z", "updated_at": "2026-07-01T10:05:00Z",
        "html_url": "https://github.com/kahalm/rookhub/actions/runs/1", "actor": { "login": "kahalm" } } ] }
    """;

    private static GithubActionsService Build(StubHandler handler, bool withToken = true)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var settings = new Dictionary<string, string?>
        {
            ["GitHub:Repos:0"] = "rookhub",
            ["GitHub:CacheSeconds"] = "60",
        };
        if (withToken) settings["GitHub:Token"] = "ghp_test";
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new GithubActionsService(http, config, new MemoryCache(new MemoryCacheOptions()),
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

        Assert.Equal(1, handler.Calls);   // 2. Poll trifft den Cache, nicht GitHub
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

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
