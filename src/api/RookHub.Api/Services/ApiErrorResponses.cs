using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace RookHub.Api.Services;

/// <summary>
/// Die zwei Fehlerantworten, die nicht aus einem Controller kommen (Codereview 2026-09-29, A10-006): der globale
/// Ausnahme-Handler und die automatische Validierungs-400 von <c>[ApiController]</c>. Beide tragen jetzt — wie die
/// Controller-Antworten — ein <c>message</c>, dazu eine <c>traceId</c>.
///
/// <para><b>Warum:</b> Das Frontend liest fast überall <c>err.error?.message</c>. Die Validierungs-400 kam als
/// ValidationProblemDetails ohne <c>message</c> an; die eigens formulierten <c>ErrorMessage</c>s der DTOs erreichten
/// den Nutzer deshalb nie, er sah nur den allgemeinen Ersatztext. Der globale Handler schrieb für jeden Status den
/// <c>type</c> des 500-Abschnitts (auch bei 413/400) und keine <c>traceId</c> — ein Screenshot „An unexpected error
/// occurred." ließ sich keinem Log-Eintrag zuordnen.</para>
///
/// <para><b>traceId</b> wie bei den ProblemDetails des Frameworks: <c>Activity.Current?.Id</c> (W3C,
/// <c>00-&lt;trace.id&gt;-&lt;span.id&gt;-00</c> — der mittlere Teil ist das <c>trace.id</c>-Feld in Kibana), sonst
/// <c>HttpContext.TraceIdentifier</c> (= <c>RequestId</c> im Log).</para>
/// </summary>
public static class ApiErrorResponses
{
    /// <summary>RFC-9110-Abschnitt je Status; unbekannte 4xx fallen auf „400", alles andere auf „500".</summary>
    public static string ProblemType(int status) => status switch
    {
        StatusCodes.Status413PayloadTooLarge => "https://tools.ietf.org/html/rfc9110#section-15.5.14",
        >= 400 and < 500 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
    };

    public static string TraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;

    /// <summary>
    /// Rumpf des globalen Handlers (<c>app.UseExceptionHandler</c>). Domänen-Ausnahmen kommen hier nicht an
    /// (DomainExceptionFilter); was hier landet, ist ein echter Fehler → 500.
    /// </summary>
    public static async Task WriteUnhandledAsync(HttpContext context)
    {
        // Kestrel wirft für einen zu großen/kaputten Body eine BadHttpRequestException und trägt den
        // passenden Status (413 bzw. 400) selbst mit. Den durchreichen statt pauschal 500: ein zu großer
        // Chunk des Browser-Imports kam beim Nutzer sonst als nacktes „HTTP 500" an und sah nach einem
        // Serverfehler aus (am 2026-09-20 die halbe Fehlersuche gekostet).
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, title) = ex is BadHttpRequestException bad
            ? (bad.StatusCode, bad.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? "Request body too large."
                : "Malformed request.")
            : (StatusCodes.Status500InternalServerError, "An unexpected error occurred.");

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = ProblemType(status),
            title,
            status,
            message = title,
            traceId = TraceId(context),
        });
    }

    /// <summary>
    /// <c>ApiBehaviorOptions.InvalidModelStateResponseFactory</c>: dieselbe ValidationProblemDetails wie die
    /// Vorgabe des Frameworks (<c>errors</c>, <c>traceId</c>, Status, Content-Types), zusätzlich <c>message</c> =
    /// erste Validierungsmeldung — so kommt z. B. die eigene Meldung an <c>CreateRoleDto.Key</c> im Frontend an.
    /// </summary>
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState);
        problem.Extensions["message"] = problem.Errors.Values
            .SelectMany(messages => messages)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? problem.Title;

        ObjectResult result = problem.Status == StatusCodes.Status400BadRequest
            ? new BadRequestObjectResult(problem)
            : new ObjectResult(problem) { StatusCode = problem.Status };
        result.ContentTypes.Add("application/problem+json");
        result.ContentTypes.Add("application/problem+xml");
        return result;
    }
}
