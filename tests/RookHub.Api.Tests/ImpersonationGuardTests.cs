using System.Net;
using System.Reflection;
using System.Security.Claims;
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
using RookHub.Api.Authorization;
using RookHub.Api.Controllers;

namespace RookHub.Api.Tests;

/// <summary>
/// Impersonations-Sperre als EIN Attribut statt Opt-in-Zeile je Action (Codereview 2026-09-29, A1-013). Vorher stand
/// <c>if (IsImpersonating()) return …</c> an sechs Stellen — und fehlte an <c>push/subscribe</c>: die Subscription des
/// Admin-Browsers hing danach am Zielkonto. Die Liste unten ist die Pflichtliste der Actions mit dauerhafter Wirkung.
/// </summary>
public class ImpersonationGuardTests
{
    /// <summary>Actions mit dauerhafter Wirkung über die Sitzung hinaus (Zugangsdaten/-token, Identitätsbindungen,
    /// Kontolöschung, Anmelde-Übergabe). Neue Action dieser Art → hier eintragen UND das Attribut setzen.</summary>
    public static TheoryData<Type, string> MustDeny => new()
    {
        { typeof(AuthController), nameof(AuthController.Handoff) },
        { typeof(AuthController), nameof(AuthController.ChangePassword) },
        { typeof(ProfileController), nameof(ProfileController.CreateToken) },
        { typeof(ProfileController), nameof(ProfileController.LinkDiscord) },
        { typeof(ProfileController), nameof(ProfileController.UnlinkDiscord) },
        { typeof(ProfileController), nameof(ProfileController.DeleteAccount) },
        { typeof(ClubController), nameof(ClubController.Redeem) },
        { typeof(ClubController), nameof(ClubController.SelfUnlink) },
        { typeof(NotificationController), nameof(NotificationController.PushSubscribe) },
    };

    [Theory]
    [MemberData(nameof(MustDeny))]
    public void DurableAction_CarriesDenyWhileImpersonating(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!;
        Assert.True(method.IsDefined(typeof(DenyWhileImpersonatingAttribute), inherit: true)
                    || controller.IsDefined(typeof(DenyWhileImpersonatingAttribute), inherit: true),
            $"{controller.Name}.{action} hat dauerhafte Wirkung und braucht [DenyWhileImpersonating].");
    }

    /// <summary>Gegenrichtung: wer das Attribut setzt, trägt die Action auch in die Liste ein — so bleibt sie vollständig.</summary>
    [Fact]
    public void EveryAttributedAction_IsInTheList()
    {
        var listed = MustDeny.Select(row => $"{((Type)row[0]).Name}.{row[1]}").ToHashSet();
        var attributed = typeof(BaseApiController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.IsDefined(typeof(DenyWhileImpersonatingAttribute), true)
                            || t.IsDefined(typeof(DenyWhileImpersonatingAttribute), true))
                .Select(m => $"{t.Name}.{m.Name}"))
            .ToList();
        Assert.Empty(attributed.Where(a => !listed.Contains(a)));
    }

    [Fact]
    public void Filter_BlocksAnImpersonationToken_With403AndTheFormerMessage_AndLetsOwnSessionsThrough()
    {
        var blocked = Run(impersonatedBy: 990001);
        var result = Assert.IsType<ObjectResult>(blocked);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(new { message = "Not allowed while impersonating another user." }),
            System.Text.Json.JsonSerializer.Serialize(result.Value));

        Assert.Null(Run(impersonatedBy: null));
    }

    /// <summary>Über die echte MVC-Pipeline: dieselbe Antwort (Status, Content-Type, Rumpf) wie die frühere Inline-Zeile
    /// <c>return StatusCode(403, new { message = … })</c>, und die Action läuft nicht.</summary>
    [Fact]
    public async Task Pipeline_AnswerEqualsTheFormerInlineGuard_AndTheActionDoesNotRun()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().ConfigureApplicationPartManager(apm =>
        {
            apm.ApplicationParts.Clear();
            apm.ApplicationParts.Add(new AssemblyPart(typeof(ImpersonationProbeController).Assembly));
        });
        await using var app = builder.Build();
        app.Use((ctx, next) =>
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "7") };
            if (ctx.Request.Headers.ContainsKey("X-Imp")) claims.Add(new Claim("imp", "990001"));
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            return next();
        });
        app.MapControllers();
        await app.StartAsync();
        var client = app.GetTestClient();
        ImpersonationProbeController.Runs = 0;

        HttpRequestMessage Req(string path) => new(HttpMethod.Post, path) { Headers = { { "X-Imp", "1" } } };
        var manual = await client.SendAsync(Req("/probe-imp/manual"));
        var guarded = await client.SendAsync(Req("/probe-imp/guarded"));

        Assert.Equal(HttpStatusCode.Forbidden, guarded.StatusCode);
        Assert.Equal(manual.StatusCode, guarded.StatusCode);
        Assert.Equal(manual.Content.Headers.ContentType?.ToString(), guarded.Content.Headers.ContentType?.ToString());
        Assert.Equal(await manual.Content.ReadAsStringAsync(), await guarded.Content.ReadAsStringAsync());
        Assert.Equal(0, ImpersonationProbeController.Runs);

        var own = await client.PostAsync("/probe-imp/guarded", null);   // eigene Sitzung: Action läuft
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        Assert.Equal(1, ImpersonationProbeController.Runs);
    }

    private static IActionResult? Run(int? impersonatedBy)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "7") };
        if (impersonatedBy is int adminId) claims.Add(new Claim("imp", adminId.ToString()));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        var ctx = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());
        new DenyWhileImpersonatingAttribute().OnActionExecuting(ctx);
        return ctx.Result;
    }
}

/// <summary>Sonde für <see cref="ImpersonationGuardTests.Pipeline_AnswerEqualsTheFormerInlineGuard_AndTheActionDoesNotRun"/>.
/// Top-level und public, sonst findet MVC den Controller nicht; nur in Test-Hosts eingehängt.</summary>
[ApiController]
[Route("probe-imp")]
public class ImpersonationProbeController : ControllerBase
{
    public static int Runs;

    [HttpPost("manual")]
    public IActionResult Manual() => StatusCode(403, new { message = "Not allowed while impersonating another user." });

    [HttpPost("guarded")]
    [DenyWhileImpersonating]
    public IActionResult Guarded()
    {
        Runs++;
        return NoContent();
    }
}
