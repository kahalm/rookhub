using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Eine Fehlerform für die Antworten, die nicht aus einem Controller kommen (Codereview 2026-09-29, A10-006):
/// Validierungs-400 mit <c>message</c> (vorher nur <c>errors</c>, die eigene <c>ErrorMessage</c> kam im Frontend nie
/// an), globaler Handler mit dem <c>type</c> des Status (vorher immer der 500-Abschnitt) und mit <c>traceId</c>.
/// </summary>
public class ApiErrorResponsesTests
{
    private static async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers()
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiErrorResponses.InvalidModelState)
            .ConfigureApplicationPartManager(apm =>
            {
                apm.ApplicationParts.Clear();
                apm.ApplicationParts.Add(new AssemblyPart(typeof(ApiErrorProbeController).Assembly));
            });
        var app = builder.Build();
        app.UseExceptionHandler(error => error.Run(ApiErrorResponses.WriteUnhandledAsync));
        app.MapControllers();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task Validierung_400_traegtMessage_nebenErrorsUndTraceId()
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        var res = await client.PostAsJsonAsync("/error-probe/validate", new { key = "Nicht Gültig" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(ApiErrorProbeDto.KeyMessage, root.GetProperty("message").GetString());
        // Die bisherigen Felder bleiben — 7 Frontend-Stellen lesen errors.
        Assert.True(root.GetProperty("errors").TryGetProperty("Key", out _));
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Handler_500_traegtTraceId_undType15_6_1()
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        var res = await client.GetAsync("/error-probe/bug");

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("dictionary", body);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.6.1", root.GetProperty("type").GetString());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("message").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("too-large", 413, "https://tools.ietf.org/html/rfc9110#section-15.5.14", "Request body too large.")]
    [InlineData("malformed", 400, "https://tools.ietf.org/html/rfc9110#section-15.5.1", "Malformed request.")]
    public async Task Handler_KestrelStatus_bekommtSeinenType(string path, int status, string type, string message)
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        var res = await client.GetAsync($"/error-probe/{path}");

        Assert.Equal(status, (int)res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(type, root.GetProperty("type").GetString());
        Assert.Equal(message, root.GetProperty("message").GetString());
        Assert.Equal(status, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("traceId").GetString()));
    }

    /// <summary>Beides wirkt nur, weil Program.cs es einhängt — Program.cs startet im Test nicht (Migrate braucht
    /// MariaDB), deshalb eine Quelltext-Wache wie <see cref="DomainExceptionFilterTests"/>.</summary>
    [Fact]
    public void ProgramCs_HaengtBeideAntwortenEin()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "api", "RookHub.Api", "Program.cs"));
        Assert.Contains("InvalidModelStateResponseFactory = ApiErrorResponses.InvalidModelState", program);
        Assert.Contains("error.Run(ApiErrorResponses.WriteUnhandledAsync)", program);
    }

    /// <summary>Kein Controller antwortet mehr mit <c>{ error = … }</c> als Fehlerrumpf (League/TrainingGoal taten
    /// es, das Frontend las dort <c>error.error</c>, überall sonst <c>error.message</c>).</summary>
    [Fact]
    public void Controller_antwortenNichtMitErrorFeldAlsRumpf()
    {
        var dir = Path.Combine(RepoRoot(), "src", "api", "RookHub.Api", "Controllers");
        var hits = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, i)))
            .Where(x => Regex.IsMatch(x.line, @"new\s*\{\s*error\s*="))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.Empty(hits);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

public class ApiErrorProbeDto
{
    public const string KeyMessage = "Key darf nur Kleinbuchstaben enthalten.";

    [Required]
    [RegularExpression("^[a-z]+$", ErrorMessage = KeyMessage)]
    public string Key { get; set; } = string.Empty;
}

/// <summary>Sonde für <see cref="ApiErrorResponsesTests"/>; nur in deren Test-Host eingehängt.</summary>
[ApiController]
[Route("error-probe")]
public class ApiErrorProbeController : ControllerBase
{
    [HttpPost("validate")] public IActionResult Validate([FromBody] ApiErrorProbeDto dto) => Ok(dto);
    [HttpGet("bug")] public IActionResult Bug() => throw new KeyNotFoundException("The given key 'x' was not present in the dictionary.");
    [HttpGet("too-large")] public IActionResult TooLarge() => throw new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge);
    [HttpGet("malformed")] public IActionResult Malformed() => throw new BadHttpRequestException("bad", StatusCodes.Status400BadRequest);
}
