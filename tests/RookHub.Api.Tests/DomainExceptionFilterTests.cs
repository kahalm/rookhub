using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RookHub.Api.Exceptions;
using RookHub.Api.Filters;

namespace RookHub.Api.Tests;

/// <summary>
/// Domänen-Ausnahmen → <c>{ message }</c> mit festem Status, alles andere bleibt unbehandelt
/// (Codereview 2026-09-29, A10-005). Bis dahin fingen die Controller die BCL-Typen selbst — auch jeden
/// echten Fehler, der zufällig KeyNotFound/InvalidOperation/Argument/UnauthorizedAccess war.
/// </summary>
public class DomainExceptionFilterTests
{
    private static ExceptionContext Run(Exception ex)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var ctx = new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = ex };
        new DomainExceptionFilter().OnException(ctx);
        return ctx;
    }

    public static TheoryData<Exception, int> DomainCases => new()
    {
        { new NotFoundException("Rolle nicht gefunden."), 404 },
        { new DomainValidationException("Unbekannte Permission: x"), 400 },
        { new ConflictException("Schon aufgelöst."), 409 },
        { new ForbiddenException("Nur der Empfänger."), 403 },
    };

    [Theory]
    [MemberData(nameof(DomainCases))]
    public void DomainException_WirdZuStatusUndMessage(Exception ex, int status)
    {
        var ctx = Run(ex);

        Assert.True(ctx.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(status, result.StatusCode);
        // Dieselbe Form wie das frühere NotFound(new { message = ex.Message }).
        Assert.Equal(JsonSerializer.Serialize(new { message = ex.Message }),
            JsonSerializer.Serialize(result.Value));
    }

    public static TheoryData<Exception> BugCases => new()
    {
        new KeyNotFoundException("The given key 'x' was not present in the dictionary."),
        new InvalidOperationException("Sequence contains no matching element"),
        new ObjectDisposedException("AppDbContext"),
        new ArgumentException("Value cannot be null."),
        new UnauthorizedAccessException("User ID claim is missing or invalid."),
    };

    /// <summary>Genau der Fund: die BCL-Basistypen sind KEINE Domänenfehler und müssen zum globalen
    /// Handler durch (500 + Error-Log), statt als 4xx mit Framework-Text beim Client zu landen.</summary>
    [Theory]
    [MemberData(nameof(BugCases))]
    public void BclException_BleibtUnbehandelt(Exception ex)
    {
        var ctx = Run(ex);

        Assert.False(ctx.ExceptionHandled);
        Assert.Null(ctx.Result);
    }

    /// <summary>Die Typen erben vom BCL-Typ, den die noch nicht umgestellten Controller fangen —
    /// sonst dürfte ein gemeinsam genutzter Dienst erst umgestellt werden, wenn ALLE Aufrufer es sind.</summary>
    [Fact]
    public void DomainExceptions_ErbenVomBisherGefangenenBclTyp()
    {
        Assert.IsAssignableFrom<KeyNotFoundException>(new NotFoundException("x"));
        Assert.IsAssignableFrom<InvalidOperationException>(new DomainValidationException("x"));
        Assert.IsAssignableFrom<InvalidOperationException>(new ConflictException("x"));
        Assert.IsAssignableFrom<UnauthorizedAccessException>(new ForbiddenException("x"));
    }

    /// <summary>
    /// Die Antwort über die echte MVC-Pipeline: eine geworfene Domänen-Ausnahme muss Byte für Byte so
    /// ankommen wie die frühere Hand-Abbildung (Status, Content-Type, Rumpf), ein echter Fehler als 500
    /// ohne seinen Text.
    /// </summary>
    [Fact]
    public async Task Pipeline_DomainAntwortGleichHandAbbildung_BugWird500()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(o => o.Filters.Add<DomainExceptionFilter>())
            .ConfigureApplicationPartManager(apm =>
            {
                apm.ApplicationParts.Clear();
                apm.ApplicationParts.Add(new AssemblyPart(typeof(DomainExceptionProbeController).Assembly));
            });
        await using var app = builder.Build();
        app.UseExceptionHandler(e => e.Run(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return ctx.Response.WriteAsJsonAsync(new { message = "An unexpected error occurred." });
        }));
        app.MapControllers();
        await app.StartAsync();
        var client = app.GetTestClient();

        foreach (var kind in new[] { "404", "400" })
        {
            var manual = await client.GetAsync($"/probe/manual-{kind}");
            var domain = await client.GetAsync($"/probe/domain-{kind}");
            Assert.Equal(manual.StatusCode, domain.StatusCode);
            Assert.Equal(manual.Content.Headers.ContentType?.ToString(), domain.Content.Headers.ContentType?.ToString());
            Assert.Equal(await manual.Content.ReadAsStringAsync(), await domain.Content.ReadAsStringAsync());
        }

        var bug = await client.GetAsync("/probe/bug");
        Assert.Equal(HttpStatusCode.InternalServerError, bug.StatusCode);
        Assert.DoesNotContain("dictionary", await bug.Content.ReadAsStringAsync());
    }

    /// <summary>Der Filter wirkt nur, weil Program.cs ihn global einhängt — Program.cs startet im Test nicht
    /// (Migrate braucht MariaDB), deshalb eine Quelltext-Wache wie <see cref="TransactionStrategyTests"/>.</summary>
    [Fact]
    public void ProgramCs_RegistriertDenFilterGlobal()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var program = File.ReadAllText(Path.Combine(dir!.FullName, "src", "api", "RookHub.Api", "Program.cs"));
        Assert.Contains("o.Filters.Add<RookHub.Api.Filters.DomainExceptionFilter>()", program);
    }
}

/// <summary>Sonde für <see cref="DomainExceptionFilterTests.Pipeline_DomainAntwortGleichHandAbbildung_BugWird500"/>.
/// Top-level und public, sonst findet MVC den Controller nicht; nur in dem Test-Host eingehängt.</summary>
[ApiController]
[Route("probe")]
public class DomainExceptionProbeController : ControllerBase
{
    [HttpGet("manual-404")] public IActionResult Manual404() => NotFound(new { message = "Rolle nicht gefunden." });
    [HttpGet("domain-404")] public IActionResult Domain404() => throw new NotFoundException("Rolle nicht gefunden.");
    [HttpGet("manual-400")] public IActionResult Manual400() => BadRequest(new { message = "Unbekannte Permission: x" });
    [HttpGet("domain-400")] public IActionResult Domain400() => throw new DomainValidationException("Unbekannte Permission: x");
    [HttpGet("bug")] public IActionResult Bug() => throw new KeyNotFoundException("The given key 'x' was not present in the dictionary.");
}
