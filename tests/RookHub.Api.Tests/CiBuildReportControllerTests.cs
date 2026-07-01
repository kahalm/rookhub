using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Service-to-service Build-Report: nur mit korrektem Shared-Secret; sonst 401.</summary>
public class CiBuildReportControllerTests
{
    private const string Secret = "s3cret-report-key";

    private static CiBuildReportController Build()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CI:BuildReportSecret"] = Secret })
            .Build();
        var github = new GithubActionsService(
            new HttpClient(), new StubFactory(), config,
            new MemoryCache(new MemoryCacheOptions()), NullLogger<GithubActionsService>.Instance);
        return new CiBuildReportController(github, config);
    }

    private static readonly CiBuildReportDto Dto = new("log-watcher", "abc123", "v0.1.0");

    [Fact]
    public void Report_NoKey_Unauthorized()
        => Assert.IsType<UnauthorizedResult>(Build().Report(Dto, null));

    [Fact]
    public void Report_WrongKey_Unauthorized()
        => Assert.IsType<UnauthorizedResult>(Build().Report(Dto, "nope"));

    [Fact]
    public void Report_ValidKey_NoContent()
        => Assert.IsType<NoContentResult>(Build().Report(Dto, Secret));

    [Fact]
    public void Report_ValidKey_EmptyRepo_BadRequest()
        => Assert.IsType<BadRequestResult>(Build().Report(new CiBuildReportDto("", "s", "r"), Secret));

    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
