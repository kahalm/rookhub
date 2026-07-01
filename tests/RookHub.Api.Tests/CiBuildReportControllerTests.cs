using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Service-to-service Build-Report + GitHub-Webhook: nur mit korrektem Secret/Signatur.</summary>
// Gleiche Collection wie GithubActionsServiceTests → kein Parallellauf, da beide die statischen
// _reportedBuilds/_pushedRuns-Caches nutzen/zurücksetzen.
[Collection("CiStaticState")]
public class CiBuildReportControllerTests
{
    private const string Secret = "s3cret-report-key";

    private static CiBuildReportController Build()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CI:BuildReportSecret"] = Secret })
            .Build();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var github = new GithubActionsService(
            new HttpClient(), new StubFactory(), config,
            new MemoryCache(new MemoryCacheOptions()), NullLogger<GithubActionsService>.Instance, db);
        return new CiBuildReportController(github, config);
    }

    private static readonly CiBuildReportDto Dto = new("log-watcher", "abc123", "v0.1.0");

    [Fact]
    public async Task Report_NoKey_Unauthorized()
        => Assert.IsType<UnauthorizedResult>(await Build().Report(Dto, null));

    [Fact]
    public async Task Report_WrongKey_Unauthorized()
        => Assert.IsType<UnauthorizedResult>(await Build().Report(Dto, "nope"));

    [Fact]
    public async Task Report_ValidKey_NoContent()
        => Assert.IsType<NoContentResult>(await Build().Report(Dto, Secret));

    [Fact]
    public async Task Report_ValidKey_EmptyRepo_BadRequest()
        => Assert.IsType<BadRequestResult>(await Build().Report(new CiBuildReportDto("", "s", "r"), Secret));

    // ---- GitHub workflow_run-Webhook (Push-Modell) ----

    private const string WorkflowRunBody = """
    { "action": "completed", "repository": { "name": "log-watcher" },
      "workflow_run": { "id": 555, "name": "Build & Push Docker Image", "display_title": "b",
        "head_branch": "main", "event": "push", "status": "completed", "conclusion": "success",
        "run_number": 12, "created_at": "2026-07-01T10:00:00Z", "updated_at": "2026-07-01T10:03:00Z",
        "html_url": "https://github.com/kahalm/log-watcher/actions/runs/555", "head_sha": "abc",
        "actor": { "login": "kahalm" } } }
    """;

    private static string Sign(string body)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return "sha256=" + Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    private static CiBuildReportController WithRequest(string body, string? sig, string @event)
    {
        var c = Build();
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (sig != null) ctx.Request.Headers["X-Hub-Signature-256"] = sig;
        ctx.Request.Headers["X-GitHub-Event"] = @event;
        c.ControllerContext = new ControllerContext { HttpContext = ctx };
        return c;
    }

    [Fact]
    public async Task Webhook_ValidSignature_StoresRun()
    {
        GithubActionsService.ResetPushedRunsForTests();
        var res = await WithRequest(WorkflowRunBody, Sign(WorkflowRunBody), "workflow_run").GithubWebhook();
        Assert.IsType<NoContentResult>(res);
        var run = Assert.Single(GithubActionsService.PushedRunsForTests("log-watcher"));
        Assert.Equal(555, run.Id);
        Assert.Equal("completed", run.Status);
        Assert.Equal("success", run.Conclusion);
        Assert.Equal("Build & Push Docker Image", run.Name);
    }

    [Fact]
    public async Task Webhook_BadSignature_Unauthorized()
    {
        Assert.IsType<UnauthorizedResult>(
            await WithRequest(WorkflowRunBody, "sha256=deadbeef", "workflow_run").GithubWebhook());
    }

    [Fact]
    public async Task Webhook_NonWorkflowRunEvent_IgnoredNoStore()
    {
        GithubActionsService.ResetPushedRunsForTests();
        var res = await WithRequest("{}", Sign("{}"), "ping").GithubWebhook();
        Assert.IsType<NoContentResult>(res);
        Assert.Empty(GithubActionsService.PushedRunsForTests("log-watcher"));
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
